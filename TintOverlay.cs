using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>One screen-tint look: a name and the colour of the veil.</summary>
public sealed record TintPreset(string Id, string Name, byte R, byte G, byte B);

/// <summary>
/// The lightweight "shaders" — a translucent colour laid over the screen. Honest
/// about what it is: a veil, not a grade. It can warm, cool, dim or colour-cast
/// the picture, but it cannot desaturate or boost contrast the way a real
/// post-process would; that needs reprocessing every frame, which a game does
/// not want. No capture, no game data — just a coloured sheet of glass.
/// </summary>
public static class TintStyles
{
    public static readonly IReadOnlyList<TintPreset> All = new[]
    {
        new TintPreset("warm",  "Warm",  255, 150, 40),   // cosy amber, less blue light
        new TintPreset("cool",  "Cool",  60, 140, 255),   // cool blue cast
        new TintPreset("night", "Night", 120, 55, 10),    // dark amber for late play
        new TintPreset("dim",   "Dim",   0, 0, 0),         // neutral darken
        new TintPreset("dusk",  "Dusk",  150, 70, 200)     // purple cast
    };

    public static TintPreset? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>Capped so the veil is never fully opaque — you can always see the game.</summary>
    public const byte MaxAlpha = 140;

    /// <summary>Turns a 0–100 intensity into the veil's alpha.</summary>
    public static byte AlphaFor(double intensity) =>
        (byte)Math.Clamp(Math.Round(intensity / 100.0 * MaxAlpha), 0, MaxAlpha);

    /// <summary>
    /// How dark the vignette's edges get at full strength. Higher than the tint's
    /// cap because it only darkens the corners, where it reads as depth rather
    /// than as a veil over the whole picture.
    /// </summary>
    public const byte MaxVignetteAlpha = 215;

    /// <summary>Turns a 0–100 vignette strength into the edge alpha.</summary>
    public static byte VignetteAlphaFor(double strength) =>
        (byte)Math.Clamp(Math.Round(strength / 100.0 * MaxVignetteAlpha), 0, MaxVignetteAlpha);
}

/// <summary>
/// A one-tap "look": a tint colour, its strength and a vignette amount applied
/// together, with a short name and blurb for its card. The pieces are the plain
/// tint and vignette controls — a look is just a named starting point a player
/// can then nudge with the sliders.
/// </summary>
public sealed record ColorLook(string Id, string Name, string Blurb, string TintId, double Intensity, double Vignette);

/// <summary>The built-in looks shown as preview cards on the Screen tab.</summary>
public static class ColorLooks
{
    public static readonly IReadOnlyList<ColorLook> All = new[]
    {
        new ColorLook("punch",   "Punch",     "Colours cranked all the way up.",        "warm", 90, 0),
        new ColorLook("crisp",   "Crisp",     "Bright and clean for spotting players.", "cool", 80, 0),
        new ColorLook("theater", "Theater",   "Moody contrast with a warm tilt.",       "warm", 85, 30),
        new ColorLook("amber",   "Amber",     "Warm golden-hour glow.",                 "warm", 80, 20),
        new ColorLook("frost",   "Frost",     "Cool, icy blues.",                       "cool", 80, 12),
        new ColorLook("faded",   "Faded",     "Washed film with warm light.",           "dusk", 80, 30),
        new ColorLook("ink",     "Ink",       "Black and white, hard shadows.",         "dim",  90, 40),
        new ColorLook("haze",    "Haze",      "Soft pastel dream.",                     "dusk", 75, 20),
        new ColorLook("owl",     "Night Owl", "Lifts the shadows so dark maps read.",   "dim",  85, 0),
        new ColorLook("toxic",   "Toxic",     "Eerie radioactive green.",               "cool", 85, 25),
        new ColorLook("vapor",   "Vapor",     "Hot pink and cyan, dialled up.",         "dusk", 90, 20),
        new ColorLook("crimson", "Crimson",   "Deep red and moody.",                    "warm", 85, 30)
    };

    public static ColorLook? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(l => l.Id == id);
}

/// <summary>Whether Roblox owns the foreground window right now.</summary>
internal static class RobloxForeground
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    public static bool IsFront()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return false;

            using Process p = Process.GetProcessById((int)pid);
            return p.ProcessName.Contains("roblox", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// A full-screen, click-through, always-on-top coloured veil. Reuses the overlay
/// recipe from <see cref="ServerOverlay"/>: transparent, never activated, never
/// hit-tested, and excluded from capture under streamer mode.
/// </summary>
public sealed class TintOverlay : Window
{
    private bool _streamerMode;

    // Two stacked layers: the flat colour veil, then the vignette on top of it.
    private readonly System.Windows.Controls.Border _tintLayer = new();
    private readonly System.Windows.Controls.Border _vignetteLayer = new() { IsHitTestVisible = false };

    public TintOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;

        var grid = new System.Windows.Controls.Grid();
        grid.Children.Add(_tintLayer);
        grid.Children.Add(_vignetteLayer);
        Content = grid;

        // Cover every monitor; the "only while Roblox is in front" gate decides
        // when it shows, not where.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };
    }

    /// <summary>Sets the veil colour and strength.</summary>
    public void SetTint(TintPreset preset, double intensity)
    {
        byte a = TintStyles.AlphaFor(intensity);
        _tintLayer.Background = a == 0 ? null : new SolidColorBrush(Color.FromArgb(a, preset.R, preset.G, preset.B));
    }

    /// <summary>
    /// Sets the vignette: a radial darkening that is clear in the middle and
    /// deepens toward the edges, so the centre of the screen stays bright. 0
    /// clears it.
    /// </summary>
    public void SetVignette(double strength)
    {
        byte a = TintStyles.VignetteAlphaFor(strength);
        if (a == 0) { _vignetteLayer.Background = null; return; }

        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.85, RadiusY = 0.85
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a / 3), 0, 0, 0), 0.78));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(a, 0, 0, 0), 1.0));
        _vignetteLayer.Background = brush;
    }

    public bool StreamerMode
    {
        get => _streamerMode;
        set { if (_streamerMode == value) return; _streamerMode = value; ApplyCaptureVisibility(); }
    }

    private void ApplyCaptureVisibility()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            SetWindowDisplayAffinity(handle, _streamerMode ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
        catch { }
    }

    private void MakeClickThrough()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            int style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE,
                style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
