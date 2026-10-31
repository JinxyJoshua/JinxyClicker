using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

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

    /// <summary>Pins the pill to the bottom-left corner.</summary>
    protected void PlaceBottomLeft(int margin)
    {
        UpdateLayout();
        Left = SystemParameters.WorkArea.Left + margin;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - margin;
    }

    /// <summary>Pins the pill to the bottom-right corner.</summary>
    protected void PlaceBottomRight(int margin)
    {
        UpdateLayout();
        Left = SystemParameters.WorkArea.Right - ActualWidth - margin;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - margin;
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

/// <summary>
/// A row of mouse-button keycaps — left, middle, right and the two side buttons —
/// that light up in the accent colour as they're pressed, including the clicker's
/// own clicks. Nothing is read from the game; it polls the button states only.
/// </summary>
public sealed class MouseOverlay : PillOverlayBase
{
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;

    private sealed class Cap
    {
        public int Vk;
        public Border Border = null!;
        public TextBlock Letter = null!;
        public TextBlock Count = null!;
        public bool Down;
        public int Clicks;
    }

    private static readonly Color Rest = Color.FromArgb(0x55, 0x2A, 0x2E, 0x3C);
    private static readonly Color RestText = Color.FromRgb(0xA6, 0xAD, 0xBF);

    private readonly List<Cap> _caps = new();
    private readonly DispatcherTimer _poll;
    private Color _accent = Color.FromRgb(0x6A, 0xB7, 0xFF);

    public MouseOverlay() : base("", Color.FromRgb(0x6A, 0xB7, 0xFF))
    {
        AddCap("L", VK_LBUTTON);
        AddCap("M", VK_MBUTTON);
        AddCap("R", VK_RBUTTON);
        AddCap("4", VK_XBUTTON1);
        AddCap("5", VK_XBUTTON2);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _poll.Tick += (_, _) => Poll();
    }

    /// <summary>The colour a button lights up in while held.</summary>
    public void SetAccent(Color accent) => _accent = accent;

    private void AddCap(string text, int vk)
    {
        var letter = new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(RestText),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var count = new TextBlock
        {
            Text = "0", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(RestText),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0)
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(letter);
        stack.Children.Add(count);

        var border = new Border
        {
            Width = 34, Height = 38, Margin = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Rest),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = stack
        };
        _caps.Add(new Cap { Vk = vk, Border = border, Letter = letter, Count = count });
        Body.Children.Add(border);
    }

    private void Poll()
    {
        foreach (Cap c in _caps)
        {
            bool down = (GetAsyncKeyState(c.Vk) & 0x8000) != 0;
            if (down == c.Down) continue;
            c.Down = down;

            if (down)
            {
                c.Clicks++;
                c.Count.Text = c.Clicks.ToString();
                c.Border.Background = new SolidColorBrush(_accent);
                c.Letter.Foreground = Brushes.White;
                c.Count.Foreground = Brushes.White;
            }
            else
            {
                c.Border.Background = new SolidColorBrush(Rest);
                c.Letter.Foreground = new SolidColorBrush(RestText);
                c.Count.Foreground = new SolidColorBrush(RestText);
            }
        }
    }

    public new void Show() { base.Show(); _poll.Start(); }
    public new void Hide() { _poll.Stop(); base.Hide(); }

    public void PlaceDefault() => PlaceBottomLeft(28);

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
}

/// <summary>A minimal clock pill — the current time, for recordings and streams.</summary>
public sealed class ClockOverlay : PillOverlayBase
{
    private readonly TextBlock _time;

    public ClockOverlay() : base("", Color.FromRgb(0xA7, 0x8B, 0xFA))
    {
        _time = new TextBlock
        {
            Text = "--:--", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        };
        Body.Children.Add(_time);
    }

    public void SetTime(string text)
    {
        if (_time.Text != text) _time.Text = text;
    }

    public void PlaceDefault() => PlaceTopCentre(72);
}

/// <summary>A custom text badge — a watermark / username over the game.</summary>
public sealed class WatermarkOverlay : PillOverlayBase
{
    private readonly TextBlock _text;

    public WatermarkOverlay() : base("", Color.FromRgb(0xA7, 0x8B, 0xFA))
    {
        _text = new TextBlock
        {
            Text = "Jinxy", FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260
        };
        Body.Children.Add(_text);
    }

    public void SetText(string? text)
    {
        string t = string.IsNullOrWhiteSpace(text) ? "Jinxy" : text!.Trim();
        if (_text.Text != t) _text.Text = t;
    }

    public void PlaceDefault() => PlaceBottomRight(28);
}
