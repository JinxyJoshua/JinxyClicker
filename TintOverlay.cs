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
        Background = new SolidColorBrush(Color.FromArgb(a, preset.R, preset.G, preset.B));
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
