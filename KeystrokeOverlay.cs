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
    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_SPACE = 0x20;

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
    private Border _frame = null!;
    private bool _background = true;

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

        // ---- keyboard cluster: Shift / W / A S D / Space + Ctrl, then the mouse ----
        var keys = new StackPanel();
        keys.Children.Add(Row(MakeCap("W", VK_W, 38)));
        keys.Children.Add(Row(MakeCap("A", VK_A, 38), MakeCap("S", VK_S, 38), MakeCap("D", VK_D, 38)));

        StackPanel mouseRow = Row(MakeCap("LMB", VK_LBUTTON, 58), MakeCap("RMB", VK_RBUTTON, 58));
        mouseRow.Margin = new Thickness(0, 4, 0, 0);
        keys.Children.Add(mouseRow);

        // ---- CPS row ----
        var divider = new Border
        {
            Height = 2,
            Margin = new Thickness(5, 8, 5, 7),
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
        };

        var cpsLabel = new TextBlock
        {
            Text = "CPS",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA2, 0xB4)),
            VerticalAlignment = VerticalAlignment.Center
        };
        _cpsValue = new TextBlock
        {
            Text = "0",
            FontSize = 15,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var cpsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        cpsRow.Children.Add(cpsLabel);
        cpsRow.Children.Add(_cpsValue);

        var body = new StackPanel();
        body.Children.Add(keys);
        body.Children.Add(divider);
        body.Children.Add(cpsRow);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(10),
            Child = body
        };
        ApplyBackground();   // panel look or Medal-style, per _background

        Content = _frame;

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

    // One centred row of keycaps.
    private static StackPanel Row(params Border[] caps)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (Border cap in caps) row.Children.Add(cap);
        return row;
    }

    // Builds one keycap, registers it for polling, and hands back its Border so
    // the caller can place it in a row.
    private Border MakeCap(string text, int vk, double w)
    {
        var scale = new ScaleTransform(1, 1);
        var label = new TextBlock
        {
            Text = text,
            FontSize = text.Length > 1 ? 11 : 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xBF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Width = w,
            Height = 32,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2E, 0x3C)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = label,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale
        };

        _caps.Add(new Cap { Vk = vk, Border = border, Label = label, Scale = scale });
        return border;
    }

    public void SetAccent(Color accent) => _accent = accent;

    /// <summary>
    /// Panel look (the keys sit on a dark card) versus Medal-style, where the card
    /// is gone and the keys float on their own. The keycaps keep their own fill
    /// either way, so they stay readable over the game without the card.
    /// </summary>
    public void SetBackground(bool on)
    {
        _background = on;
        ApplyBackground();
    }

    private void ApplyBackground()
    {
        if (_frame is null) return;

        if (_background)
        {
            _frame.Padding = new Thickness(10);
            _frame.Background = new LinearGradientBrush(
                Color.FromArgb(0xE6, 0x16, 0x16, 0x20),
                Color.FromArgb(0xE6, 0x0C, 0x0C, 0x12),
                new Point(0, 0), new Point(0, 1));
            _frame.BorderThickness = new Thickness(1);
            _frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            _frame.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 };
        }
        else
        {
            _frame.Padding = new Thickness(4);
            _frame.Background = Brushes.Transparent;
            _frame.BorderThickness = new Thickness(0);
            _frame.BorderBrush = Brushes.Transparent;
            _frame.Effect = null;
        }
    }

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

    public void StartPolling() { if (IsVisible) { _poll.Start(); InstallMouseHook(); } }

    public new void Show()
    {
        base.Show();
        _poll.Start();
        InstallMouseHook();
    }

    public new void Hide()
    {
        _poll.Stop();
        RemoveMouseHook();
        base.Hide();
    }

    /// <summary>
    /// Optional second opinion on whether a key is down, for a key another hook is
    /// swallowing. SOCD blocks its pair while both are held, and a blocked key no
    /// longer registers in GetAsyncKeyState — so without this the overlay would show
    /// the keys you're holding as up.
    /// </summary>
    public Func<int, bool>? KeyStateProbe { get; set; }

    private void PollKeys()
    {
        foreach (Cap cap in _caps)
        {
            bool down = (GetAsyncKeyState(cap.Vk) & 0x8000) != 0
                        || (KeyStateProbe?.Invoke(cap.Vk) ?? false);
            if (down == cap.Down) continue;
            cap.Down = down;
            Paint(cap, down);
        }

        // CPS: left-clicks seen in the last second (counted by the mouse hook, which
        // catches fast clicks the 16 ms poll above would miss).
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

    // ---- accurate CPS via a low-level mouse hook ----
    //
    // Counting clicks by polling GetAsyncKeyState misses anything faster than the
    // 16 ms poll, so fast clicking (or the autoclicker) read far too low — 6 CPS for
    // a real 70. A WH_MOUSE_LL hook catches every left-button-down, physical and
    // injected, so the CPS is the real number the game receives.
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelMouseProc? _mouseProc;   // kept alive so the GC can't collect it
    private IntPtr _mouseHookId = IntPtr.Zero;

    private void InstallMouseHook()
    {
        if (_mouseHookId != IntPtr.Zero) return;
        _mouseProc = MouseHookCallback;
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookId == IntPtr.Zero) return;
        UnhookWindowsHookEx(_mouseHookId);
        _mouseHookId = IntPtr.Zero;
        _mouseProc = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_LBUTTONDOWN)
            _clickTimes.Add(Environment.TickCount64);
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
}
