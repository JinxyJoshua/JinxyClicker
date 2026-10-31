using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace JinxyClicker;

/// <summary>
/// A small readout on screen while the clicker runs.
/// </summary>
/// <remarks>
/// The app is behind the game whenever it matters, so everything it knows about
/// its own output is invisible at exactly the moment that output is being used.
/// This puts the figures worth watching where they can actually be read: a big
/// live CPS number and a rolling line graph of the last few seconds, in a dark
/// glass card that matches the keystrokes overlay.
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
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private readonly TextBlock _note;
    private readonly Border _frame;

    // The rolling line graph.
    private const double GraphWidth = 180;
    private const double GraphHeight = 44;
    private const int GraphSamples = 56;
    private readonly Polyline _line;
    private readonly Polygon _fill;
    private readonly Queue<double> _history = new();

    private bool _streamerMode;

    // Calm accent while clicking is tidy; a warm red when it isn't — the same
    // two-state colouring the detail line used, now carried by the graph too.
    private static readonly Color CalmColor = Color.FromRgb(0xA7, 0x8B, 0xFA);
    private static readonly Color RoughColor = Color.FromRgb(0xFF, 0x6B, 0x74);
    private static readonly Brush Calm = new SolidColorBrush(CalmColor);
    private static readonly Brush Rough = new SolidColorBrush(RoughColor);

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

        // ---- header: "CPS" label on the left, the live number on the right ----
        var label = new TextBlock
        {
            Text = "CPS",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA8)),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 0, 2)
        };
        _value = new TextBlock
        {
            Text = "—",
            FontSize = 27,
            FontWeight = FontWeights.Black,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        var header = new DockPanel { LastChildFill = false, Width = GraphWidth };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(_value, Dock.Right);
        header.Children.Add(label);
        header.Children.Add(_value);

        // ---- the rolling graph ----
        _fill = new Polygon { Fill = new SolidColorBrush(Color.FromArgb(0x33, CalmColor.R, CalmColor.G, CalmColor.B)) };
        _line = new Polyline
        {
            Stroke = Calm,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        var graph = new Grid
        {
            Width = GraphWidth,
            Height = GraphHeight,
            Margin = new Thickness(0, 9, 0, 0),
            ClipToBounds = true
        };
        // A faint baseline, so an empty or flat graph still reads as a graph.
        graph.Children.Add(new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF))
        });
        graph.Children.Add(_fill);
        graph.Children.Add(_line);

        _detail = new TextBlock
        {
            Text = "waiting for the first clicks",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(1, 7, 0, 0),
            Foreground = Calm
        };

        _note = new TextBlock
        {
            Text = "",
            FontSize = 10,
            Margin = new Thickness(1, 3, 0, 0),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = GraphWidth,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xC2, 0xE0))
        };

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(graph);
        body.Children.Add(_detail);
        body.Children.Add(_note);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush(
                Color.FromArgb(0xE6, 0x16, 0x16, 0x20),
                Color.FromArgb(0xE6, 0x0C, 0x0C, 0x12),
                new Point(0, 0), new Point(0, 1)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(15, 11, 15, 12),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 },
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
        bool have = stats.Samples > 0;
        bool warn = OverlayReadout.IsWarning(stats);

        _value.Text = have
            ? stats.DeliveredCps.ToString("0.0", CultureInfo.CurrentCulture)
            : "—";

        _detail.Text = OverlayReadout.Detail(stats);
        _detail.Foreground = warn ? Rough : Calm;

        string note = OverlayReadout.RateNote(stats.DeliveredCps);
        _note.Text = note;
        _note.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        // Feed the graph and recolour it to match the detail line.
        if (have)
        {
            _history.Enqueue(stats.DeliveredCps);
            while (_history.Count > GraphSamples) _history.Dequeue();
        }
        Color c = warn ? RoughColor : CalmColor;
        _line.Stroke = new SolidColorBrush(c);
        _fill.Fill = new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
        RedrawGraph();

        if (!IsVisible) Show();

        UpdateLayout();
        PlaceTopRight();
    }

    // Maps the rolling CPS history onto the graph area. The vertical scale floors
    // at a small value so a steady low rate draws a calm flat line near the bottom
    // rather than a jumpy full-height one.
    private void RedrawGraph()
    {
        _line.Points.Clear();
        _fill.Points.Clear();
        if (_history.Count < 2) return;

        double max = 10;
        foreach (double v in _history)
            if (v > max) max = v;

        const double top = 2, bottom = GraphHeight - 1;
        double usable = bottom - top;

        var pts = new PointCollection();
        int n = _history.Count, i = 0;
        foreach (double v in _history)
        {
            double x = i / (double)(n - 1) * GraphWidth;
            double y = bottom - v / max * usable;
            pts.Add(new Point(x, y));
            i++;
        }
        _line.Points = pts;

        var fill = new PointCollection { new Point(pts[0].X, bottom) };
        foreach (Point p in pts) fill.Add(p);
        fill.Add(new Point(pts[^1].X, bottom));
        _fill.Points = fill;
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
