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
    string DeviceName, int Number, int X, int Y, int Width, int Height, bool IsPrimary)
{
    /// <summary>
    /// Dimensions rounded down to even. yuv420p subsamples chroma by two, and
    /// x264 rejects an odd width or height outright — a monitor reporting one
    /// would otherwise fail the recording with an ffmpeg error nobody can read.
    /// </summary>
    public int EvenWidth => Width & ~1;

    public int EvenHeight => Height & ~1;

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
                        (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
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
