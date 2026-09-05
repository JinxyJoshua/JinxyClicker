using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// A small readout on screen while the clicker runs.
/// </summary>
/// <remarks>
/// The app is behind the game whenever it matters, so everything it knows about
/// its own output is invisible at exactly the moment that output is being used.
/// This puts the two figures worth watching where they can actually be read.
///
/// It reports what the app <em>sends</em>, never what the game does with it.
/// Nothing here reads Roblox — no memory, no rendering, no game state at all —
/// so there is no hit count and no percentage, because either would have to be
/// invented. See <see cref="OverlayReadout"/>.
///
/// Click-through and never focused, so it cannot swallow a click or pull the
/// foreground away mid-fight. Streamer mode hides it from capture the same way
/// it hides the macro badge.
/// </remarks>
public sealed class ClickOverlay : Window
{
    private readonly TextBlock _headline;
    private readonly TextBlock _detail;
    private readonly TextBlock _note;
    private readonly Border _frame;
    private bool _streamerMode;

    private static readonly Brush Calm = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
    private static readonly Brush Rough = new SolidColorBrush(Color.FromRgb(0xFF, 0x9C, 0x9C));

    public ClickOverlay()
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

        _headline = new TextBlock
        {
            Text = "—",
            FontSize = 22,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White
        };

        _detail = new TextBlock
        {
            Text = "waiting for the first clicks",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = Calm
        };

        _note = new TextBlock
        {
            Text = "",
            FontSize = 10,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC2, 0xE0))
        };

        var body = new StackPanel();
        body.Children.Add(_headline);
        body.Children.Add(_detail);
        body.Children.Add(_note);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x10, 0x10, 0x16)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(14, 9, 16, 10),
            Child = body
        };

        Content = _frame;

        SourceInitialized += (_, _) =>
        {
            MakeClickThrough();
            ApplyCaptureVisibility();
        };
    }

    /// <summary>Hides it from capture while leaving it on the monitor.</summary>
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

    /// <summary>Puts the current figures on screen.</summary>
    public void Update(TimingStats stats)
    {
        _headline.Text = OverlayReadout.Headline(stats);
        _detail.Text = OverlayReadout.Detail(stats);
        _detail.Foreground = OverlayReadout.IsWarning(stats) ? Rough : Calm;

        string note = OverlayReadout.RateNote(stats.DeliveredCps);

        _note.Text = note;
        _note.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (!IsVisible) Show();

        UpdateLayout();
        PlaceTopRight();
    }

    /// <summary>
    /// Top right, opposite the macro badge.
    /// </summary>
    /// <remarks>
    /// The badge already occupies the top left, and two overlays stacked in one
    /// corner would obscure each other whenever a macro is running — which is
    /// most of the time this is on.
    /// </remarks>
    private void PlaceTopRight()
    {
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
            // Older than Windows 10 2004. It simply appears in captures, which
            // is what it does by default anyway.
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
