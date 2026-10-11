using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// An on-screen keystrokes overlay (Pro): WASD + mouse buttons that light up in
/// the accent colour as they're pressed, with a live CPS readout. Click-through
/// and topmost like <see cref="ClickOverlay"/>, so it sits over the game without
/// swallowing input.
/// </summary>
public sealed class KeystrokeOverlay : Window
{
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;
    private const int VK_A = 0x41, VK_D = 0x44, VK_S = 0x53, VK_W = 0x57;

    private sealed class Cap
    {
        public int Vk;
        public Border Border = null!;
        public TextBlock Label = null!;
        public ScaleTransform Scale = null!;
        public bool Down;
    }

    private readonly List<Cap> _caps = new();
    private readonly List<long> _clickTimes = new();   // left-click moments in the last second
    private readonly TextBlock _cpsValue;
    private readonly DispatcherTimer _poll;
    private Color _accent = Color.FromRgb(0xFF, 0x4B, 0x52);
    private bool _streamerMode;
    private bool _moveMode;

    public KeystrokeOverlay()
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

        // ---- build the keycap grid: W on top, A S D, then LMB / RMB ----
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Add(grid, "W", VK_W, row: 0, col: 1, w: 46);
        Add(grid, "A", VK_A, row: 1, col: 0, w: 46);
        Add(grid, "S", VK_S, row: 1, col: 1, w: 46);
        Add(grid, "D", VK_D, row: 1, col: 2, w: 46);
        Add(grid, "LMB", VK_LBUTTON, row: 2, col: 0, w: 70, colSpan: 2);
        Add(grid, "RMB", VK_RBUTTON, row: 2, col: 1, w: 70, colSpan: 2, shiftRight: true);

        // ---- CPS row ----
        var divider = new Border
        {
            Height = 2,
            Margin = new Thickness(6, 12, 6, 10),
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
        };

        var cpsLabel = new TextBlock
        {
            Text = "CPS",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _cpsValue = new TextBlock
        {
            Text = "0",
            FontSize = 20,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var cpsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        cpsRow.Children.Add(cpsLabel);
        cpsRow.Children.Add(_cpsValue);

        var body = new StackPanel();
        body.Children.Add(grid);
        body.Children.Add(divider);
        body.Children.Add(cpsRow);

        var frame = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xE6, 0x16, 0x16, 0x20),
                Color.FromArgb(0xE6, 0x0C, 0x0C, 0x12),
                new Point(0, 0), new Point(0, 1)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 },
            Child = body
        };

        Content = frame;

        Left = 48;
        Top = Math.Max(48, SystemParameters.WorkArea.Height / 2 - 120);

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _poll.Tick += (_, _) => PollKeys();

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };

        // Repositioning (only works while in move mode, which drops click-through).
        MouseLeftButtonDown += (_, _) => { if (_moveMode) DragMove(); };
    }

    private void Add(Grid grid, string text, int vk, int row, int col, double w, int colSpan = 1, bool shiftRight = false)
    {
        var scale = new ScaleTransform(1, 1);
        var label = new TextBlock
        {
            Text = text,
            FontSize = text.Length > 1 ? 13 : 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xBF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Width = w,
            Height = 40,
            Margin = new Thickness(3),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2E, 0x3C)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = label,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            HorizontalAlignment = shiftRight ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        if (colSpan > 1) Grid.SetColumnSpan(border, colSpan);

        _caps.Add(new Cap { Vk = vk, Border = border, Label = label, Scale = scale });
        grid.Children.Add(border);
    }

    public void SetAccent(Color accent) => _accent = accent;

    public bool StreamerMode
    {
        get => _streamerMode;
        set { if (_streamerMode == value) return; _streamerMode = value; ApplyCaptureVisibility(); }
    }

    /// <summary>Drops click-through so the overlay can be dragged to a new spot.</summary>
    public void SetMoveMode(bool on)
    {
        _moveMode = on;
        IsHitTestVisible = on;
        if (on) RemoveClickThrough(); else MakeClickThrough();
    }

    public void StartPolling() { if (IsVisible) _poll.Start(); }

    public new void Show()
    {
        base.Show();
        _poll.Start();
    }

    public new void Hide()
    {
        _poll.Stop();
        base.Hide();
    }

    private void PollKeys()
    {
        foreach (Cap cap in _caps)
        {
            bool down = (GetAsyncKeyState(cap.Vk) & 0x8000) != 0;
            if (down == cap.Down) continue;
            cap.Down = down;
            Paint(cap, down);

            if (cap.Vk == VK_LBUTTON && down) _clickTimes.Add(Environment.TickCount64);
        }

        // CPS: left-clicks seen in the last second.
        long cutoff = Environment.TickCount64 - 1000;
        _clickTimes.RemoveAll(t => t < cutoff);
        string cps = _clickTimes.Count.ToString();
        if (_cpsValue.Text != cps) _cpsValue.Text = cps;
    }

    private void Paint(Cap cap, bool down)
    {
        if (down)
        {
            cap.Border.Background = new SolidColorBrush(_accent);
            cap.Border.BorderBrush = new SolidColorBrush(Lighten(_accent));
            cap.Border.Effect = new DropShadowEffect { Color = _accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
            cap.Label.Foreground = Brushes.White;
            Pop(cap.Scale);
        }
        else
        {
            cap.Border.Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2E, 0x3C));
            cap.Border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            cap.Border.Effect = null;
            cap.Label.Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xBF));
        }
    }

    private static void Pop(ScaleTransform scale)
    {
        var anim = new DoubleAnimation(1.14, 1.0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static Color Lighten(Color c) => Color.FromRgb(
        (byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60));

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

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
}
