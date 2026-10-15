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

/// <summary>A screen resolution (width × height), without a refresh rate.</summary>
public readonly record struct Resolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
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

    /// <summary>Distinct resolutions from a set of modes, widest first. Pure, testable.</summary>
    public static List<Resolution> ResolutionsOf(IEnumerable<DisplayMode> modes) =>
        modes.Select(m => new Resolution(m.Width, m.Height))
             .Distinct()
             .OrderByDescending(r => r.Width).ThenByDescending(r => r.Height)
             .ToList();

    /// <summary>The refresh rates a given resolution supports, highest first. Pure, testable.</summary>
    public static List<int> RefreshesOf(IEnumerable<DisplayMode> modes, int width, int height) =>
        modes.Where(m => m.Width == width && m.Height == height)
             .Select(m => m.Refresh)
             .Distinct()
             .OrderByDescending(r => r)
             .ToList();

    // Resolutions Roblox players reach for: 16:9 steps down for more frames, and the
    // 4:3 / 5:4 ones that stretch to fill a widescreen so models look bigger and the
    // view is wider — the classic BedWars "stretched res" look.
    private static readonly (int W, int H, string Label, string Why)[] CuratedPresets =
    {
        (1600, 900,  "Balanced",  "A step down for more frames"),
        (1366, 768,  "Balanced",  "16:9, lighter on the GPU"),
        (1280, 1024, "Stretched", "5:4 — wider, bigger models"),
        (1280, 960,  "Stretched", "4:3 — the classic stretched look"),
        (1152, 864,  "Stretched", "4:3 — bigger models, more FPS"),
        (1024, 768,  "Low",       "Lowest — most frames, chunkiest models"),
    };

    /// <summary>
    /// One-click resolution presets for Roblox, filtered to what the monitor actually
    /// reports: the native resolution first, then the supported curated ones. Pure,
    /// testable.
    /// </summary>
    public static List<(Resolution Res, string Label, string Why)> PresetsFor(IEnumerable<DisplayMode> modes)
    {
        List<Resolution> res = ResolutionsOf(modes);
        var result = new List<(Resolution, string, string)>();
        if (res.Count == 0) return result;

        result.Add((res[0], "Native", "Your monitor's real resolution"));

        var have = new HashSet<Resolution>(res);
        foreach ((int w, int h, string label, string why) in CuratedPresets)
        {
            var r = new Resolution(w, h);
            if (r != res[0] && have.Contains(r)) result.Add((r, label, why));
        }
        return result;
    }

    /// <summary>The current desktop colour depth in bits per pixel (0 if unknown).</summary>
    public static int CurrentBits()
    {
        var dm = NewDevMode();
        return EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) ? (int)dm.dmBitsPerPel : 0;
    }

    /// <summary>System display scaling as a percentage (100 = 1:1), e.g. 125.</summary>
    public static int ScalingPercent()
    {
        try { uint dpi = GetDpiForSystem(); return dpi > 0 ? (int)Math.Round(dpi * 100.0 / 96.0) : 100; }
        catch { return 100; }
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

    // ---- Scaling (DPI) ----
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
}
