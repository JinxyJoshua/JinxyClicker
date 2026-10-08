using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// A small banner naming the region of the Roblox server the player just joined.
/// </summary>
/// <remarks>
/// It reads nothing from the game. The region comes from Roblox's own log, which
/// records the server's relay address, geolocated once per server (see
/// <see cref="RobloxServerLog"/> and <see cref="IpRegionLookup"/>). So it reports
/// where the server is reached, not an exact host, and it shows no ping — a real
/// ping to a Roblox server cannot be measured from outside the game, and a made-up
/// one would be worse than none.
///
/// Click-through and never focused, like <see cref="ClickOverlay"/>, so it cannot
/// swallow a click or steal the foreground mid-game. Streamer mode hides it from
/// capture the same way.
/// </remarks>
public sealed class ServerOverlay : Window
{
    private readonly TextBlock _headline;
    private readonly TextBlock _detail;
    private readonly DispatcherTimer _hideTimer;
    private bool _streamerMode;

    private static readonly Brush Quiet = new SolidColorBrush(Color.FromRgb(0xC9, 0xC2, 0xE0));

    public ServerOverlay()
    {
        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        IsHitTestVisible = false;

        _headline = new TextBlock
        {
            Text = "—",
            FontSize = 20,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _detail = new TextBlock
        {
            Text = "",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Quiet
        };

        var body = new StackPanel();
        body.Children.Add(_headline);
        body.Children.Add(_detail);

        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x16)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(16, 9, 16, 10),
            Child = body
        };

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };
    }

    public bool StreamerMode
    {
        get => _streamerMode;
        set
        {
            if (_streamerMode == value) return;

            _streamerMode = value;
            ApplyCaptureVisibility();
        }
    }

    /// <summary>
    /// Names the server's region, and — unless <paramref name="autoHideSeconds"/>
    /// is zero — takes the banner back down after that many seconds.
    /// </summary>
    /// <remarks>
    /// The countdown starts here rather than at <see cref="ShowLooking"/>, so a
    /// timed banner is shown for its full length after the region is known, not
    /// with the lookup time eaten out of it. Zero means "keep it on" — the banner
    /// stays until the server changes or Roblox closes.
    /// </remarks>
    public void ShowRegion(ServerRegion region, int autoHideSeconds)
    {
        _headline.Text = region.Headline;

        string detail = region.Detail;
        _detail.Text = detail;
        _detail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        Place();
        ScheduleHide(autoHideSeconds);
    }

    /// <summary>Says the region is being worked out, while a lookup is in flight.</summary>
    public void ShowLooking()
    {
        // No hide scheduled here — this is a placeholder that ShowRegion replaces,
        // and the timer belongs to the region the user actually came to read.
        _hideTimer.Stop();

        _headline.Text = "Locating server…";
        _detail.Visibility = Visibility.Collapsed;
        Place();
    }

    private void ScheduleHide(int seconds)
    {
        _hideTimer.Stop();

        if (seconds <= 0) return; // "keep it on"

        _hideTimer.Interval = TimeSpan.FromSeconds(seconds);
        _hideTimer.Start();
    }

    /// <summary>
    /// Applies a changed auto-hide length to a banner already on screen, so
    /// picking "keep it on" stops a running countdown and picking a shorter time
    /// takes effect at once rather than only on the next join.
    /// </summary>
    public void ReapplyAutoHide(int seconds)
    {
        if (IsVisible) ScheduleHide(seconds);
    }

    /// <summary>Hides the banner and cancels any pending auto-hide.</summary>
    public new void Hide()
    {
        _hideTimer.Stop();
        base.Hide();
    }

    private void Place()
    {
        if (!IsVisible) Show();

        UpdateLayout();

        // Top right, as asked for. The click readout can also sit here while the
        // clicker runs, but the server banner is usually a brief pop on joining,
        // so the two rarely share the corner at once.
        const int margin = 24;

        Left = SystemParameters.WorkArea.Right - ActualWidth - margin;
        Top = SystemParameters.WorkArea.Top + margin;
    }

    private void ApplyCaptureVisibility()
    {
        try
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;

            SetWindowDisplayAffinity(
                handle, _streamerMode ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }
        catch
        {
            // Older than Windows 10 2004. It simply appears in captures, which is
            // the default behaviour anyway.
        }
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
        catch
        {
            // An overlay that cannot be styled is still a usable overlay.
        }
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
