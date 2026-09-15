using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// Watches the fishing minigame on screen and holds the mouse to keep the fish
/// on the line.
/// </summary>
/// <remarks>
/// The decision is <see cref="FishingBar"/>, tested against a real recording.
/// This is the parts that cannot be tested without a screen: grabbing the
/// pixels, and pressing the button. It grabs a band across the middle of the
/// screen — where the bar sits — finds the bar in it, and asks FishingBar what
/// to do, thirty-odd times a second.
///
/// Nothing about the bar's position is hard-coded. The needle draws a fixed
/// grey gap in the sliding green, so both the zone and the target come out of
/// the same row scan; the bot works at any resolution and needs no calibration.
///
/// The mouse itself is pressed through delegates the window hands in, so the
/// press goes through the same input lock the clicker uses and this file never
/// learns SendInput.
/// </remarks>
public sealed class FishingBot
{
    private readonly Action _press;
    private readonly Action _release;
    private Thread? _thread;
    private volatile bool _running;
    private bool _holding;

    /// <summary>Which way holding slides the green zone. Flipped if a game runs backwards.</summary>
    public bool HoldPushesRight { get; set; } = true;

    /// <summary>How far off the needle is tolerated before acting, in pixels.</summary>
    public int DeadzonePx { get; set; } = 14;

    /// <summary>Raised on the worker thread whenever a bar is found or lost.</summary>
    public event Action<bool>? BarSeenChanged;

    private bool _lastBarSeen;

    public FishingBot(Action press, Action release)
    {
        _press = press;
        _release = release;
    }

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;

        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "FishingBot",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        // Never leave the button stuck down when switched off mid-hold.
        if (_holding) { _release(); _holding = false; }
    }

    private void Loop()
    {
        try
        {
            while (_running)
            {
                FishAction action = LookOnce();

                bool wantHold = action == FishAction.Hold;

                // Idle (no bar) releases too, so a lost bar never leaves the
                // button held. Only the edges send an event.
                if (wantHold && !_holding) { _press(); _holding = true; }
                else if (!wantHold && _holding) { _release(); _holding = false; }

                Thread.Sleep(PollMs);
            }
        }
        catch
        {
            // A failed grab must not take the thread down. The loop simply ends;
            // the button is released in the finally.
        }
        finally
        {
            if (_holding) { _release(); _holding = false; }
        }
    }

    /// <summary>Milliseconds between looks. ~33 a second, fast enough to balance a needle.</summary>
    private const int PollMs = 30;

    /// <summary>
    /// One grab and one decision.
    /// </summary>
    private FishAction LookOnce()
    {
        int screenW = GetSystemMetrics(SM_CXSCREEN);
        int screenH = GetSystemMetrics(SM_CYSCREEN);
        if (screenW <= 0 || screenH <= 0) return FishAction.Idle;

        // The bar lives across the lower middle. Grabbing a band rather than the
        // whole screen keeps each look cheap.
        int bandTop = (int)(screenH * 0.55);
        int bandHeight = (int)(screenH * 0.30);

        byte[]? bgra = GrabBand(0, bandTop, screenW, bandHeight, out int stride);
        if (bgra == null) return FishAction.Idle;

        FishingReading reading = FindBar(bgra, screenW, bandHeight, stride);

        bool barSeen = reading.BarPresent;
        if (barSeen != _lastBarSeen)
        {
            _lastBarSeen = barSeen;
            BarSeenChanged?.Invoke(barSeen);
        }

        return FishingBar.Decide(reading, DeadzonePx, HoldPushesRight);
    }

    /// <summary>
    /// Finds the bar in the grabbed band: the widest green zone, and the thin
    /// green needle that is taller than it.
    /// </summary>
    /// <remarks>
    /// The zone is a wide horizontal run of green; the needle is a thin vertical
    /// marker of the same green that stands a little taller than the zone, so it
    /// shows above the zone's top edge whether it is inside the zone or beside
    /// it. The first build looked for the needle as a gap inside the green and so
    /// could not see it at all once it left the zone — which is when steering it
    /// back matters most.
    ///
    /// So the zone is found on the row with the widest green, and the needle is
    /// counted a few rows above that as the column green across the most of them.
    /// </remarks>
    private static FishingReading FindBar(byte[] bgra, int width, int height, int stride)
    {
        var mask = new bool[width];

        // Pass one: the zone. The row with the widest green run.
        (int Left, int Right)? zone = null;
        int zoneRow = -1;

        for (int y = 0; y < height; y += 2)
        {
            FillGreenMask(bgra, y, width, stride, mask);
            var z = FishingBar.FindZone(mask);
            if (z == null) continue;

            if (zone == null || z.Value.Right - z.Value.Left > zone.Value.Right - zone.Value.Left)
            {
                zone = z;
                zoneRow = y;
            }
        }

        if (zone == null) return FishingReading.None;

        // Pass two: the needle. In the twelve rows above the zone, the only green
        // is the needle sticking up; the column green in the most of them is its
        // centre. Falls back to the zone centre if the needle is not above it.
        int top = Math.Max(0, zoneRow - 14);
        var votes = new int[width];

        for (int y = top; y < zoneRow - 2; y++)
        {
            FillGreenMask(bgra, y, width, stride, mask);
            for (int x = 0; x < width; x++) if (mask[x]) votes[x]++;
        }

        int needleX = zone.Value.Left + (zone.Value.Right - zone.Value.Left) / 2;
        int bestVotes = 0, sum = 0, count = 0;

        for (int x = 0; x < width; x++)
        {
            if (votes[x] > bestVotes) { bestVotes = votes[x]; sum = x; count = 1; }
            else if (votes[x] == bestVotes && bestVotes > 0) { sum += x; count++; }
        }

        if (bestVotes >= 3 && count > 0) needleX = sum / count;

        return new FishingReading(true, zone.Value.Left, zone.Value.Right, needleX);
    }

    /// <summary>Fills the mask with which pixels of one row are zone-green.</summary>
    private static void FillGreenMask(byte[] bgra, int y, int width, int stride, bool[] mask)
    {
        int rowStart = y * stride;
        for (int x = 0; x < width; x++)
        {
            int i = rowStart + x * 4;
            mask[x] = IsZoneGreen(bgra[i + 2], bgra[i + 1], bgra[i]); // R, G, B
        }
    }

    /// <summary>
    /// The zone's green: bright, clearly greener than it is red or blue. Read off
    /// the recording, where the zone measured around (120, 210, 120).
    /// </summary>
    private static bool IsZoneGreen(byte r, byte g, byte b) =>
        g > 150 && g - r > 40 && g - b > 40;

    // ---- grabbing pixels ----

    /// <summary>
    /// Copies a rectangle of the screen into a BGRA byte array.
    /// </summary>
    /// <remarks>
    /// Straight GDI so nothing is added to the project: the screen DC into a
    /// memory bitmap, then GetDIBits into managed bytes. Every handle is freed
    /// each call — a leak here would bleed GDI objects a thousand a minute.
    /// </remarks>
    private static byte[]? GrabBand(int x, int y, int width, int height, out int stride)
    {
        stride = width * 4;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero, bmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
        try
        {
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;

            bmp = CreateCompatibleBitmap(screenDc, width, height);
            if (bmp == IntPtr.Zero) return null;

            oldBmp = SelectObject(memDc, bmp);

            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
                return null;

            var info = new BITMAPINFO
            {
                biSize = Marshal.SizeOf<BITMAPINFO>(),
                biWidth = width,
                // Negative: top-down, so row 0 is the top of the band.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            var buffer = new byte[stride * height];
            int scanned = GetDIBits(memDc, bmp, 0, (uint)height, buffer, ref info, 0);
            return scanned == 0 ? null : buffer;
        }
        finally
        {
            if (oldBmp != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (bmp != IntPtr.Zero) DeleteObject(bmp);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr dest, int xd, int yd, int w, int h,
        IntPtr src, int xs, int ys, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines,
        byte[] bits, ref BITMAPINFO info, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
        // Space for the colour table GetDIBits may touch on some drivers.
        public int colours;
    }
}
