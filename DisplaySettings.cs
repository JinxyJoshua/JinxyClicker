using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>A display mode: resolution and refresh rate.</summary>
public readonly record struct DisplayMode(int Width, int Height, int Refresh)
{
    public override string ToString() => $"{Width} x {Height}  @ {Refresh}Hz";
    public string Label => $"{Width} x {Height}";
}

/// <summary>
/// Reads the monitor's supported modes and switches resolution / refresh rate, via
/// ChangeDisplaySettingsEx. Changes use CDS_FULLSCREEN, so they are temporary: they
/// revert when this process exits (a crash can't strand you), and <see cref="Revert"/>
/// puts the saved desktop mode back on demand.
/// </summary>
public static class DisplaySettings
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int CDS_FULLSCREEN = 0x00000004;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint DM_PELSWIDTH = 0x00080000, DM_PELSHEIGHT = 0x00100000, DM_DISPLAYFREQUENCY = 0x00400000;

    /// <summary>Distinct modes the primary monitor reports, widest first.</summary>
    public static List<DisplayMode> SupportedModes()
    {
        var modes = new List<DisplayMode>();
        var dm = NewDevMode();
        for (int i = 0; EnumDisplaySettings(null, i, ref dm); i++)
        {
            if (dm.dmDisplayFrequency <= 1 || dm.dmBitsPerPel < 24) continue;   // skip 0/1Hz and low colour
            modes.Add(new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency));
            dm = NewDevMode();
        }
        return modes
            .Distinct()
            .OrderByDescending(m => m.Width).ThenByDescending(m => m.Height).ThenByDescending(m => m.Refresh)
            .ToList();
    }

    public static DisplayMode? Current()
    {
        var dm = NewDevMode();
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)
            ? new DisplayMode((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency)
            : null;
    }

    /// <summary>Switches to the mode (temporarily). Returns false if the driver refused it.</summary>
    public static bool Apply(DisplayMode mode)
    {
        var dm = NewDevMode();
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return false;

        dm.dmPelsWidth = (uint)mode.Width;
        dm.dmPelsHeight = (uint)mode.Height;
        dm.dmDisplayFrequency = (uint)mode.Refresh;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;

        return ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_FULLSCREEN, IntPtr.Zero) == DISP_CHANGE_SUCCESSFUL;
    }

    /// <summary>Restores the saved desktop mode.</summary>
    public static void Revert() => ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

    private static DEVMODE NewDevMode() => new() { dmDeviceName = new string('\0', 32), dmFormName = new string('\0', 32), dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? deviceName, IntPtr devMode, IntPtr hwnd, int flags, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public uint dmPanningWidth, dmPanningHeight;
    }
}
