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

        FishingReading reading = FindBar(bgra, screenW, bandHeight, stride, out int needleX);

        bool barSeen = reading.BarPresent;
        if (barSeen != _lastBarSeen)
        {
            _lastBarSeen = barSeen;
            BarSeenChanged?.Invoke(barSeen);
        }

        return FishingBar.Decide(reading, needleX, DeadzonePx, HoldPushesRight);
    }

    /// <summary>
    /// Finds the bar in the grabbed band: the row with the most zone-green that
    /// is split by the needle's gap.
    /// </summary>
    /// <remarks>
    /// Every few rows rather than every one — the bar is tens of pixels tall, so
    /// sampling a fraction of the rows finds it for a fraction of the work.
    /// </remarks>
    private static FishingReading FindBar(
        byte[] bgra, int width, int height, int stride, out int needleX)
    {
        needleX = 0;

        var mask = new bool[width];
        FishingReading best = FishingReading.None;
        int bestNeedle = 0;
        int bestSpan = 0;

        for (int y = 0; y < height; y += 3)
        {
            int rowStart = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = rowStart + x * 4;
                mask[x] = IsZoneGreen(bgra[i + 2], bgra[i + 1], bgra[i]); // R,G,B
            }

            FishingReading r = FishingBar.Read(mask);
            if (!r.BarPresent) continue;

            int span = r.ZoneRight - r.ZoneLeft;
            if (span <= bestSpan) continue;

            best = r;
            bestSpan = span;
            bestNeedle = NeedleGap(mask, r.ZoneLeft, r.ZoneRight);
        }

        needleX = bestNeedle > 0 ? bestNeedle : (best.ZoneLeft + best.ZoneRight) / 2;
        return best;
    }

    /// <summary>
    /// The needle's screen x: the middle of the grey gap the needle cuts in the
    /// green. Zero when the zone has no gap yet (needle at an edge).
    /// </summary>
    private static int NeedleGap(bool[] mask, int left, int right)
    {
        int gapStart = -1;
        for (int x = left; x <= right; x++)
        {
            if (!mask[x] && gapStart < 0) gapStart = x;
            else if (mask[x] && gapStart >= 0)
            {
                // The needle is a thin column; a wide gap is not it.
                if (x - gapStart <= 40) return (gapStart + x) / 2;
                gapStart = -1;
            }
        }
        return 0;
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
