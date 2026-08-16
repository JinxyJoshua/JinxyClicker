using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// A brief on-screen notice that recording started or a clip was saved.
/// </summary>
/// <remarks>
/// Excluded from screen capture, which is the whole reason this is a window
/// rather than something drawn on the app's own page. A plain topmost window
/// would sit in the corner of every clip announcing "Recording" — the notice
/// exists for the person at the keyboard, not for whoever watches the video
/// later. SetWindowDisplayAffinity leaves it visible on the monitor while the
/// capture stack sees nothing there.
///
/// It is also click-through and never takes focus, so it cannot steal a click
/// mid-game or pull the foreground away from Roblox.
/// </remarks>
public sealed class CaptureToast : Window
{
    private readonly TextBlock _label;
    private readonly DispatcherTimer _hideTimer;

    public CaptureToast()
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
        Opacity = 0;

        _label = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(18, 10, 18, 10)
        };

        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x16)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Child = _label
        };

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); FadeOut(); };

        SourceInitialized += (_, _) => MakeInvisibleToCaptureAndClicks();
    }

    /// <summary>
    /// Shows a notice for a moment at the top-left of the captured monitor.
    /// </summary>
    /// <param name="text">What happened — "Recording" or "Saved".</param>
    /// <param name="accent">Dot colour, so the two states read apart at a glance.</param>
    /// <param name="display">The monitor being captured, or null for the primary.</param>
    /// <param name="seconds">How long it stays before fading.</param>
    public void Notify(string text, Color accent, DisplayInfo? display, double seconds = 2.0)
    {
        _label.Text = text;
        _label.Foreground = new SolidColorBrush(accent);

        // Shown before positioning so SizeToContent has measured it; a window
        // with no size yet cannot be placed against its own width.
        if (!IsVisible) Show();

        UpdateLayout();
        PlaceTopLeft(display);

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.5, seconds));
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PlaceTopLeft(DisplayInfo? display)
    {
        const int margin = 24;

        // Physical pixels from the monitor enumeration, WPF units here, so the
        // scale factor has to come out or the notice drifts off a scaled screen.
        double scaleX = 1, scaleY = 1;

        if (PresentationSource.FromVisual(this)?.CompositionTarget is CompositionTarget target)
        {
            scaleX = target.TransformToDevice.M11;
            scaleY = target.TransformToDevice.M22;
            if (scaleX <= 0) scaleX = 1;
            if (scaleY <= 0) scaleY = 1;
        }

        if (display != null)
        {
            Left = display.X / scaleX + margin;
            Top = display.Y / scaleY + margin;
            return;
        }

        Left = SystemParameters.WorkArea.Left + margin;
        Top = SystemParameters.WorkArea.Top + margin;
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) => Hide();
        BeginAnimation(OpacityProperty, fade);
    }

    private void MakeInvisibleToCaptureAndClicks()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            // Fails on builds older than Windows 10 2004, where the notice simply
            // appears in the clip. Not worth refusing to show it over.
            SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);

            int style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch
        {
            // A notice that cannot be styled is still a usable notice.
        }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
