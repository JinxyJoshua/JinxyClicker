using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JinxyClicker;

/// <summary>
/// Shared base for the small glass "pill" overlays — FPS, system stats and
/// now-playing music. Each is a click-through, never-focused, topmost window in
/// the same dark glass as <see cref="ClickOverlay"/>, so the whole set reads as
/// one family on screen. Subclasses fill <see cref="Body"/> with their own text.
/// </summary>
public abstract class PillOverlayBase : Window
{
    /// <summary>The horizontal content row, already holding the leading icon.</summary>
    protected readonly StackPanel Body;

    private bool _streamerMode;
    private bool _moveMode;

    /// <summary>Raised after the user finishes dragging the pill, so its new spot can be saved.</summary>
    public event Action? Moved;

    protected PillOverlayBase(string glyph, Color accent)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        var icon = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };

        Body = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Body.Children.Add(icon);

        Content = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xE6, 0x16, 0x16, 0x20),
                Color.FromArgb(0xE6, 0x0C, 0x0C, 0x12),
                new Point(0, 0), new Point(0, 1)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(13, 7, 15, 8),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.5 },
            Child = Body
        };

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };

        // Only grabbable while in move mode; otherwise the window is click-through
        // and never receives the button down at all.
        MouseLeftButtonDown += (_, _) =>
        {
            if (!_moveMode) return;
            try { DragMove(); } catch { }
            Moved?.Invoke();
        };
    }

    /// <summary>
    /// Drops click-through so the pill can be dragged, or restores it. While on,
    /// the pill stays put rather than snapping back to its default corner.
    /// </summary>
    public void SetMoveMode(bool on)
    {
        _moveMode = on;
        IsHitTestVisible = on;
        if (on) RemoveClickThrough(); else MakeClickThrough();
    }

    /// <summary>Hides it from capture while leaving it on the monitor.</summary>
    public bool StreamerMode
    {
        get => _streamerMode;
        set { if (_streamerMode == value) return; _streamerMode = value; ApplyCaptureVisibility(); }
    }

    // ---- shared text helpers so subclasses stay tiny ----

    protected static TextBlock Value(Color color, double size = 15) => new()
    {
        Text = "—",
        FontSize = size,
        FontWeight = FontWeights.Black,
        Foreground = new SolidColorBrush(color),
        VerticalAlignment = VerticalAlignment.Center
    };

    protected static TextBlock Label(string text, double leftMargin = 4) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA8)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(leftMargin, 0, 0, 0)
    };

    /// <summary>Pins the pill to the top-right, dropped <paramref name="y"/> px down.</summary>
    protected void PlaceTopRight(int y)
    {
        const int margin = 24;
        UpdateLayout();
        Left = SystemParameters.WorkArea.Right - ActualWidth - margin;
        Top = SystemParameters.WorkArea.Top + y;
    }

    /// <summary>Pins the pill centred along the top edge.</summary>
    protected void PlaceTopCentre(int y)
    {
        UpdateLayout();
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ActualWidth) / 2;
        Top = SystemParameters.WorkArea.Top + y;
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
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private void RemoveClickThrough()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
}

/// <summary>Roblox's real FPS, read through ETW (see <see cref="FpsShared"/>).</summary>
public sealed class FpsOverlay : PillOverlayBase
{
    private readonly TextBlock _value;

    public FpsOverlay() : base("", Color.FromRgb(0x7A, 0xE5, 0x8A))
    {
        _value = Value(Colors.White, 17);
        _value.Margin = new Thickness(0, 0, 0, 0);
        Body.Children.Add(_value);
        Body.Children.Add(Label("FPS", 6));
    }

    public void SetFps(int? fps)
    {
        string t = fps is { } f && f > 0 ? f.ToString(CultureInfo.CurrentCulture) : "—";
        if (_value.Text != t) _value.Text = t;
    }

    public void PlaceDefault() => PlaceTopRight(176);
}

/// <summary>CPU load and RAM in use, machine-wide — the same figures the Clicker page shows.</summary>
public sealed class SystemOverlay : PillOverlayBase
{
    private readonly TextBlock _cpu;
    private readonly TextBlock _ram;

    public SystemOverlay() : base("", Color.FromRgb(0x6A, 0xB7, 0xFF))
    {
        _cpu = Value(Color.FromRgb(0x6A, 0xB7, 0xFF), 14);
        _cpu.Margin = new Thickness(0, 0, 0, 0);
        _ram = Value(Color.FromRgb(0x7A, 0xE5, 0x8A), 14);
        _ram.Margin = new Thickness(0, 0, 0, 0);

        Body.Children.Add(Label("CPU", 0));
        Body.Children.Add(Spacer(_cpu));
        Body.Children.Add(Label("RAM", 12));
        Body.Children.Add(Spacer(_ram));
    }

    private static UIElement Spacer(TextBlock value)
    {
        value.Margin = new Thickness(5, 0, 0, 0);
        return value;
    }

    public void SetStats(double? cpuPercent, double? ramPercent)
    {
        _cpu.Text = cpuPercent is { } c ? c.ToString("0", CultureInfo.CurrentCulture) + "%" : "—";
        _ram.Text = ramPercent is { } r ? r.ToString("0", CultureInfo.CurrentCulture) + "%" : "—";
    }

    public void PlaceDefault() => PlaceTopRight(226);
}

/// <summary>The current track title, scraped best-effort from the Music page's player.</summary>
public sealed class MusicOverlay : PillOverlayBase
{
    private readonly TextBlock _title;

    public MusicOverlay() : base("", Color.FromRgb(0xA7, 0x8B, 0xFA))
    {
        _title = new TextBlock
        {
            Text = "Nothing playing",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 240
        };
        Body.Children.Add(_title);
    }

    public void SetTitle(string? title)
    {
        string t = string.IsNullOrWhiteSpace(title) ? "Nothing playing" : title!.Trim();
        if (_title.Text != t) _title.Text = t;
    }

    public void PlaceDefault() => PlaceTopCentre(24);
}
