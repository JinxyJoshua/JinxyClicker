using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>
/// One monitor, in the physical pixel coordinates gdigrab crops against.
/// </summary>
/// <remarks>
/// <see cref="DeviceName"/> is what gets stored rather than the position in the
/// list: enumeration order changes when a monitor is unplugged, so a stored
/// index would quietly start pointing at a different screen.
/// </remarks>
public sealed record DisplayInfo(
    string DeviceName, int Number, int X, int Y, int Width, int Height, bool IsPrimary, int RefreshHz = 0)
{
    /// <summary>
    /// Refresh rate, or 60 when Windows would not say.
    /// </summary>
    /// <remarks>
    /// A ceiling on what is worth capturing. Asking a 60 Hz screen for 144
    /// frames a second does not produce 144 different frames — Desktop
    /// Duplication has 60 to give and the rest are duplicates — so it buys
    /// nothing and costs the encoder every one of them.
    /// </remarks>
    public int EffectiveRefreshHz => RefreshHz > 0 ? RefreshHz : 60;

    /// <summary>
    /// Dimensions rounded down to even. yuv420p subsamples chroma by two, and
    /// x264 rejects an odd width or height outright — a monitor reporting one
    /// would otherwise fail the recording with an ffmpeg error nobody can read.
    /// </summary>
    public int EvenWidth => Width & ~1;

    public int EvenHeight => Height & ~1;

    /// <summary>
    /// Zero-based index for Desktop Duplication's output_idx.
    /// </summary>
    /// <remarks>
    /// DXGI enumerates outputs in its own order, which is not promised to match
    /// EnumDisplayMonitors. In practice they agree on a normal setup, and being
    /// wrong shows up immediately as the other monitor in the clip rather than
    /// as a silent failure — so this stays a simple offset rather than a
    /// separate DXGI enumeration for a case that may never arise.
    /// </remarks>
    public int OutputIndex => Number - 1;

    public string Label => IsPrimary
        ? $"Display {Number} · {Width}×{Height} · primary"
        : $"Display {Number} · {Width}×{Height}";
}

public static class Displays
{
    /// <summary>
    /// Every attached monitor, primary first in the enumeration Windows gives.
    /// Returns an empty list if the enumeration fails, which callers read as
    /// "record the whole desktop".
    /// </summary>
    public static List<DisplayInfo> All()
    {
        var found = new List<DisplayInfo>();

        try
        {
            bool Callback(IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data)
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };

                if (GetMonitorInfo(monitor, ref info))
                {
                    found.Add(new DisplayInfo(
                        info.szDevice,
                        found.Count + 1,
                        info.rcMonitor.Left,
                        info.rcMonitor.Top,
                        info.rcMonitor.Right - info.rcMonitor.Left,
                        info.rcMonitor.Bottom - info.rcMonitor.Top,
                        (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        RefreshHzOf(info.szDevice)));
                }

                return true;
            }

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        }
        catch
        {
            // Nothing here is worth taking the recorder down for.
            return new List<DisplayInfo>();
        }

        return found;
    }

    /// <summary>
    /// How large the whole virtual desktop is, for the "all displays" capture.
    /// </summary>
    /// <remarks>
    /// The bounding box rather than the sum of the widths: monitors can be
    /// stacked as well as side by side, and gdigrab captures the rectangle that
    /// encloses them either way.
    /// </remarks>
    public static (int Width, int Height) VirtualDesktopSize()
    {
        List<DisplayInfo> all = All();

        if (all.Count == 0) return (1920, 1080);

        int left = all.Min(d => d.X);
        int top = all.Min(d => d.Y);
        int right = all.Max(d => d.X + d.Width);
        int bottom = all.Max(d => d.Y + d.Height);

        return (right - left, bottom - top);
    }

    /// <summary>
    /// The stored monitor, or the primary when it is gone — unplugging a screen
    /// should fall back to recording something rather than failing.
    /// </summary>
    public static DisplayInfo? Match(IReadOnlyList<DisplayInfo> displays, string? deviceName)
    {
        if (displays.Count == 0) return null;

        return displays.FirstOrDefault(d =>
                   string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
               ?? displays.FirstOrDefault(d => d.IsPrimary)
               ?? displays[0];
    }

    /// <summary>
    /// The monitor's refresh rate, or 0 when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Asked per device rather than taken from the video adapter: a machine with
    /// two monitors has one adapter and frequently two different refresh rates,
    /// and the one that matters is the screen being captured.
    /// </remarks>
    private static int RefreshHzOf(string deviceName)
    {
        try
        {
            var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

            return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode)
                ? mode.dmDisplayFrequency
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE mode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    private const uint MONITORINFOF_PRIMARY = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
