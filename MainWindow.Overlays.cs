using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The Overlays page: a live preview that shows exactly the overlays you've
/// switched on, and a grid of simple on/off cards. CPS, keystrokes and server
/// drive their existing controls so there is one source of truth for each; FPS,
/// system, mouse, clock, watermark and music are owned here. Nothing reads the
/// game beyond what those widgets already read.
/// </summary>
public partial class MainWindow
{
    private FpsOverlay? _fpsOverlay;
    private SystemOverlay? _systemOverlay;
    private MusicOverlay? _musicOverlay;
    private MouseOverlay? _mouseOverlay;
    private ClockOverlay? _clockOverlay;
    private WatermarkOverlay? _watermarkOverlay;

    private bool _ovFps, _ovSystem, _ovMusic, _ovMouse, _ovClock, _ovWatermark, _ovScore;
    private bool _overlayShowAlways;   // true = over any window, false = only while Roblox is front
    private bool _applyingOverlayShow;
    private string _watermarkText = "Jinxy";
    private ScoreLeague _scoreLeague = ScoreLeague.CollegeFootball;
    private readonly List<string> _scoreFollowed = new();                 // event ids shown on screen
    private readonly Dictionary<string, ScoreOverlay> _scoreOverlays = new();
    private readonly Dictionary<string, (double X, double Y)> _scorePos = new(); // dragged positions (session)
    private List<GameInfo> _scoreGames = new();                           // games shown in the list (Top 25 for CFB)
    private List<GameInfo> _allGames = new();                             // the full scoreboard, for the picker's matchups
    private bool _scoreLiveOnly;                                          // filter: only in-progress games
    private readonly HashSet<string> _favorites = new();                 // favorite team abbreviations
    private readonly Dictionary<ScoreLeague, List<TeamInfo>> _teamCache = new();   // full team lists per league
    private List<TeamInfo> _teams = new();                               // current league's teams
    private static readonly SolidColorBrush FavYellow = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xDD, 0x4A)));

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    private DispatcherTimer? _scoreTimer;
    private bool _scoreLoading;
    private bool _applyingScoreUi;       // suppresses control handlers while we populate them

    private static readonly Color OvGold = Color.FromRgb(0xFF, 0xD1, 0x6A);

    private bool _overlaysBuilt;
    private bool _syncingOverlayToggles;
    private bool _overlayMoveMode;
    private bool _scoreMoveMode;   // the Scoreboard's own move mode, independent of the other overlays
    private bool _cpsMovedHooked;

    // Dragged positions; null means "use the default corner".
    private double? _cpsX, _cpsY, _fpsX, _fpsY, _sysX, _sysY, _musX, _musY,
                    _mouseX, _mouseY, _clockX, _clockY, _wmX, _wmY, _scoreX, _scoreY;

    private DispatcherTimer? _infoOverlayTimer;
    private readonly Dictionary<string, CheckBox> _overlayToggles = new();
    private readonly Dictionary<string, FrameworkElement> _previewItems = new();

    // The draggable layer of the preview, and the item being dragged in it.
    private Canvas? _previewCanvas;
    private FrameworkElement? _dragItem;
    private Point _dragStart;
    private double _dragStartLeft, _dragStartTop;

    private string? _musicTitle;
    private long _lastMusicProbe;

    private static readonly Color OvPurple = Color.FromRgb(0xA7, 0x8B, 0xFA);
    private static readonly Color OvGreen = Color.FromRgb(0x7A, 0xE5, 0x8A);
    private static readonly Color OvBlue = Color.FromRgb(0x6A, 0xB7, 0xFF);
    private static readonly Color OvRed = Color.FromRgb(0xFF, 0x6B, 0x74);

    // key, icon glyph, short name, the card's sample text, accent, Plus-only.
    private static readonly (string Key, string Glyph, string Name, string Sample, Color Accent, bool Plus)[] OverlayCatalog =
    {
        ("cps",        "", "CPS",        "142",         OvPurple, false),
        ("fps",        "", "FPS",        "146 FPS",     OvGreen,  false),
        ("system",     "", "System",     "CPU 18%",     OvBlue,   false),
        ("mouse",      "", "Mouse",      "L M R 4 5",   OvBlue,   false),
        ("clock",      "", "Clock",      "9:41",        OvPurple, false),
        ("watermark",  "", "Watermark",  "Your name",   OvPurple, false),
        ("keystrokes", "", "Keystrokes", "W A S D",     OvRed,    true),
        ("music",      "", "Music",      "Now playing", OvPurple, false),
        ("server",     "", "Server",     "EU West",     OvBlue,   false),
    };

    private void NavOverlays_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavOverlays, PageOverlays, "Overlays", "Pick what shows over your game");
        BuildOverlaysPage();
        RefreshOverlayToggles();
        ApplyKeystrokesPro();
        UpdateInfoOverlays();
    }

    private async void NavScore_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavScore, PageScore, "Scoreboard", "Follow live game scores on screen");
        PopulateLeagueCombo();
        SyncScoreShowToggle();
        if (ScoreLiveOnlyCheck != null) ScoreLiveOnlyCheck.IsChecked = _scoreLiveOnly;
        await LoadScoreGamesAsync();   // always refresh the full scoreboard (feeds the picker's matchups)
        await LoadTeamsAsync();
    }

    private void LoadOverlaySettings(AppSettings s)
    {
        _ovFps = s.OverlayFps;
        _ovSystem = s.OverlaySystem;
        _ovMusic = s.OverlayMusic;
        _ovMouse = s.OverlayMouse;
        _ovClock = s.OverlayClock;
        _ovWatermark = s.OverlayWatermark;
        _ovScore = s.OverlayScore;
        _overlayShowAlways = s.OverlayShowAlways;
        _watermarkText = string.IsNullOrWhiteSpace(s.OverlayWatermarkText) ? "Jinxy" : s.OverlayWatermarkText;
        _scoreLeague = ParseLeague(s.OverlayScoreLeague);
        _scoreLiveOnly = s.OverlayScoreLiveOnly;
        _favorites.Clear();
        if (!string.IsNullOrWhiteSpace(s.OverlayScoreFavorites))
            foreach (string t in s.OverlayScoreFavorites.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                _favorites.Add(t.ToUpperInvariant());
        _scoreFollowed.Clear();
        if (!string.IsNullOrWhiteSpace(s.OverlayScoreGameId))
            foreach (string id in s.OverlayScoreGameId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!_scoreFollowed.Contains(id)) _scoreFollowed.Add(id);

        _applyingScoreUi = true;
        PopulateLeagueCombo();
        if (ScoreLiveOnlyCheck != null) ScoreLiveOnlyCheck.IsChecked = _scoreLiveOnly;
        _applyingScoreUi = false;

        // Re-follow the saved games (loads today's list, then re-attaches overlays).
        if (_scoreFollowed.Count > 0) _ = RestoreScoreSelectionAsync();

        _applyingOverlayShow = true;
        if (OverlayShowEverywhere != null) OverlayShowEverywhere.IsChecked = _overlayShowAlways;
        if (OverlayShowRoblox != null) OverlayShowRoblox.IsChecked = !_overlayShowAlways;
        _applyingOverlayShow = false;

        _cpsX = s.OverlayCpsX; _cpsY = s.OverlayCpsY;
        _fpsX = s.OverlayFpsX; _fpsY = s.OverlayFpsY;
        _sysX = s.OverlaySystemX; _sysY = s.OverlaySystemY;
        _musX = s.OverlayMusicX; _musY = s.OverlayMusicY;
        _mouseX = s.OverlayMouseX; _mouseY = s.OverlayMouseY;
        _clockX = s.OverlayClockX; _clockY = s.OverlayClockY;
        _wmX = s.OverlayWatermarkX; _wmY = s.OverlayWatermarkY;
        _scoreX = s.OverlayScoreX; _scoreY = s.OverlayScoreY;

        _overlay?.SetSavedPosition(_cpsX, _cpsY);
        ApplyInfoOverlays();
        if (_overlaysBuilt) RefreshOverlayToggles();
    }

    // ---- page build (once) ----

    private void BuildOverlaysPage()
    {
        if (_overlaysBuilt) return;
        _overlaysBuilt = true;
        BuildOverlayPreview();
        BuildOverlayToggleGrid();
    }

    private void BuildOverlayPreview()
    {
        Grid host = OverlayPreviewHost;
        host.Children.Clear();
        _previewItems.Clear();

        // The game-like backdrop.
        host.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x2A, 0x3B, 0x55), Color.FromRgb(0x10, 0x16, 0x24), 90)
        });
        host.Children.Add(new Rectangle
        {
            Height = 86, VerticalAlignment = VerticalAlignment.Bottom,
            Fill = new SolidColorBrush(Color.FromRgb(0x24, 0x40, 0x2E))
        });
        var blocks = new Canvas();
        AddBlock(blocks, 70, 152, 40, 46, Color.FromRgb(0xC2, 0x45, 0x45));
        AddBlock(blocks, 150, 136, 44, 62, Color.FromRgb(0x45, 0x7A, 0xC2));
        AddBlock(blocks, 240, 150, 38, 48, Color.FromRgb(0x8A, 0x55, 0xC2));
        host.Children.Add(blocks);
        host.Children.Add(new TextBlock
        {
            Text = "drag an overlay to place it", Opacity = 0.3, Foreground = Brushes.White,
            FontSize = 12, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });

        // The draggable layer on top of the scene.
        _previewCanvas = new Canvas();
        host.Children.Add(_previewCanvas);

        AddReplica("server", PreviewPill("", "EU West", OvBlue), draggable: false);
        AddReplica("music", PreviewPill("", "Now playing", OvPurple), true);
        AddReplica("clock", PreviewPill("", "9:41", OvPurple), true);
        AddReplica("keystrokes", PreviewKeys(), true);
        AddReplica("mouse", PreviewPill("", "L M R 4 5", OvBlue), true);
        AddReplica("watermark", PreviewPill("", _watermarkText, OvPurple), true);
        AddReplica("cps", PreviewCps(), true);
        AddReplica("fps", PreviewPill("", "146 FPS", OvGreen), true);
        AddReplica("system", PreviewSystem(), true);

        _previewCanvas.SizeChanged += (_, _) => LayoutPreviewItems();
        RefreshPreview();
    }

    private void AddReplica(string key, FrameworkElement el, bool draggable)
    {
        el.Tag = key;
        _previewItems[key] = el;
        _previewCanvas!.Children.Add(el);
        if (draggable)
        {
            el.Cursor = Cursors.SizeAll;
            el.MouseLeftButtonDown += PreviewItem_Down;
            el.MouseMove += PreviewItem_Move;
            el.MouseLeftButtonUp += PreviewItem_Up;
        }
    }

    private void PreviewItem_Down(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || _previewCanvas == null) return;
        _dragItem = fe;
        _dragStart = e.GetPosition(_previewCanvas);
        _dragStartLeft = double.IsNaN(Canvas.GetLeft(fe)) ? 0 : Canvas.GetLeft(fe);
        _dragStartTop = double.IsNaN(Canvas.GetTop(fe)) ? 0 : Canvas.GetTop(fe);
        fe.CaptureMouse();
        e.Handled = true;
    }

    private void PreviewItem_Move(object sender, MouseEventArgs e)
    {
        if (_dragItem == null || _previewCanvas == null || e.LeftButton != MouseButtonState.Pressed) return;
        Point p = e.GetPosition(_previewCanvas);
        double w = _previewCanvas.ActualWidth, h = _previewCanvas.ActualHeight;
        double nl = Math.Max(0, Math.Min(_dragStartLeft + (p.X - _dragStart.X), w - _dragItem.ActualWidth));
        double nt = Math.Max(0, Math.Min(_dragStartTop + (p.Y - _dragStart.Y), h - _dragItem.ActualHeight));
        Canvas.SetLeft(_dragItem, nl);
        Canvas.SetTop(_dragItem, nt);
    }

    private void PreviewItem_Up(object sender, MouseButtonEventArgs e)
    {
        if (_dragItem is { Tag: string key } fe && _previewCanvas is { } cv)
        {
            fe.ReleaseMouseCapture();
            double w = cv.ActualWidth, h = cv.ActualHeight;
            if (w > 0 && h > 0)
            {
                double fx = Canvas.GetLeft(fe) / w;
                double fy = Canvas.GetTop(fe) / h;
                Rect wa = SystemParameters.WorkArea;
                SetOverlayPos(key, wa.Left + fx * wa.Width, wa.Top + fy * wa.Height);
            }
        }
        _dragItem = null;
        e.Handled = true;
    }

    // Positions every replica from its saved spot (mapped back from the screen),
    // or its default corner when it has never been dragged.
    private void LayoutPreviewItems()
    {
        if (_previewCanvas is not { } cv) return;
        double w = cv.ActualWidth, h = cv.ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (KeyValuePair<string, FrameworkElement> item in _previewItems)
        {
            FrameworkElement fe = item.Value;
            if (ReferenceEquals(fe, _dragItem)) continue;
            if (fe.ActualWidth <= 0) fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point pos = SavedToCanvas(item.Key, fe, w, h) ?? DefaultCanvasPos(item.Key, fe, w, h);
            Canvas.SetLeft(fe, pos.X);
            Canvas.SetTop(fe, pos.Y);
        }
    }

    private Point? SavedToCanvas(string key, FrameworkElement fe, double w, double h)
    {
        (double? sx, double? sy) = SavedPos(key);
        if (sx is not { } x || sy is not { } y) return null;
        Rect wa = SystemParameters.WorkArea;
        double rw = ReplicaWidth(fe), rh = ReplicaHeight(fe);
        double px = Math.Max(0, Math.Min((x - wa.Left) / wa.Width * w, w - rw));
        double py = Math.Max(0, Math.Min((y - wa.Top) / wa.Height * h, h - rh));
        return new Point(px, py);
    }

    private static Point DefaultCanvasPos(string key, FrameworkElement fe, double w, double h)
    {
        double rw = ReplicaWidth(fe), rh = ReplicaHeight(fe);
        const double m = 14;
        return key switch
        {
            "server" => new Point(m, m),
            "music" => new Point((w - rw) / 2, m),
            "clock" => new Point((w - rw) / 2, m + 44),
            "score" => new Point((w - rw) / 2, m + 88),
            "cps" => new Point(w - rw - m, m),
            "fps" => new Point(w - rw - m, m + 78),
            "system" => new Point(w - rw - m, m + 118),
            "mouse" => new Point(m, h - rh - m),
            "watermark" => new Point(w - rw - m, h - rh - m),
            "keystrokes" => new Point(m, (h - rh) / 2),
            _ => new Point(m, m)
        };
    }

    private static double ReplicaWidth(FrameworkElement fe) => fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width;
    private static double ReplicaHeight(FrameworkElement fe) => fe.ActualHeight > 0 ? fe.ActualHeight : fe.DesiredSize.Height;

    private (double? X, double? Y) SavedPos(string key) => key switch
    {
        "cps" => (_cpsX, _cpsY),
        "fps" => (_fpsX, _fpsY),
        "system" => (_sysX, _sysY),
        "mouse" => (_mouseX, _mouseY),
        "clock" => (_clockX, _clockY),
        "watermark" => (_wmX, _wmY),
        "score" => (_scoreX, _scoreY),
        "music" => (_musX, _musY),
        "keystrokes" => (_keystrokesX, _keystrokesY),
        _ => (null, null)
    };

    // A drop in the preview writes the overlay's real on-screen spot and applies
    // it to the live window right away.
    private void SetOverlayPos(string key, double x, double y)
    {
        switch (key)
        {
            case "cps": _cpsX = x; _cpsY = y; _overlay?.SetSavedPosition(x, y); break;
            case "fps": _fpsX = x; _fpsY = y; break;
            case "system": _sysX = x; _sysY = y; break;
            case "mouse": _mouseX = x; _mouseY = y; break;
            case "clock": _clockX = x; _clockY = y; break;
            case "watermark": _wmX = x; _wmY = y; break;
            case "score": _scoreX = x; _scoreY = y; break;
            case "music": _musX = x; _musY = y; break;
            case "keystrokes":
                _keystrokesX = x; _keystrokesY = y;
                if (_keystrokes != null) { _keystrokes.Left = x; _keystrokes.Top = y; }
                break;
            default: return;
        }
        SaveAppSettings();
    }

    /// <summary>Shows only the overlays that are on, and re-lays out the preview.</summary>
    private void RefreshPreview()
    {
        foreach (KeyValuePair<string, FrameworkElement> item in _previewItems)
            item.Value.Visibility = IsOverlayOn(item.Key) ? Visibility.Visible : Visibility.Collapsed;

        if (_previewItems.TryGetValue("watermark", out FrameworkElement? wm) && wm is Border { Child: StackPanel sp }
            && sp.Children.Count > 1 && sp.Children[1] is TextBlock t)
            t.Text = _watermarkText;

        LayoutPreviewItems();
    }

    private bool IsOverlayOn(string key) => key switch
    {
        "cps" => OverlayCheck?.IsChecked == true,
        "keystrokes" => KeystrokesCheck?.IsChecked == true,
        "server" => ServerRegionCheck?.IsChecked == true,
        "fps" => _ovFps,
        "system" => _ovSystem,
        "mouse" => _ovMouse,
        "clock" => _ovClock,
        "watermark" => _ovWatermark,
        "score" => _ovScore,
        "music" => _ovMusic,
        _ => false
    };

    private static FrameworkElement Anchor(FrameworkElement el, HorizontalAlignment h, VerticalAlignment v, Thickness margin)
    {
        el.HorizontalAlignment = h;
        el.VerticalAlignment = v;
        el.Margin = margin;
        return el;
    }

    private static FrameworkElement Stacked(FrameworkElement el)
    {
        el.HorizontalAlignment = HorizontalAlignment.Right;
        el.Margin = new Thickness(0, 8, 0, 0);
        return el;
    }

    private static Border GlassBox(UIElement child, double radius, Thickness? pad = null) => new()
    {
        CornerRadius = new CornerRadius(radius),
        Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x12, 0x12, 0x1C)),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        Padding = pad ?? new Thickness(10, 6, 12, 7),
        Child = child
    };

    private static TextBlock Glyph(string glyph, Color color, double size = 12, double rightMargin = 7) => new()
    {
        Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
        Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, rightMargin, 0)
    };

    private static TextBlock Strong(string text, Color color, double size = 12) => new()
    {
        Text = text, FontSize = size, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Muted(string text, double left = 0) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA8)),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 0, 0)
    };

    private static Border PreviewPill(string glyph, string text, Color accent)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Glyph(glyph, accent));
        sp.Children.Add(Strong(text, Colors.White));
        return GlassBox(sp, 11);
    }

    private static Border PreviewSystem()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(Glyph("", OvBlue));
        sp.Children.Add(Muted("CPU"));
        sp.Children.Add(Strong(" 18%", OvBlue));
        sp.Children.Add(Muted("RAM", 10));
        sp.Children.Add(Strong(" 63%", OvGreen));
        return GlassBox(sp, 11);
    }

    private static Border PreviewCps()
    {
        var head = new DockPanel { LastChildFill = false, Width = 128 };
        TextBlock label = Muted("CPS");
        TextBlock val = Strong("142", Colors.White, 20);
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(val, Dock.Right);
        head.Children.Add(label);
        head.Children.Add(val);

        var line = new Polyline
        {
            Stroke = new SolidColorBrush(OvPurple), StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(0, 6, 0, 0),
            Points = new PointCollection
            {
                new Point(0, 24), new Point(20, 14), new Point(40, 20),
                new Point(62, 8), new Point(84, 18), new Point(106, 6), new Point(128, 12)
            }
        };

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(line);
        return GlassBox(stack, 10, new Thickness(12, 9, 12, 11));
    }

    private static Border PreviewKeys()
    {
        var keys = new StackPanel();
        keys.Children.Add(KeyRow(Cap("W", false)));
        keys.Children.Add(KeyRow(Cap("A", false), Cap("S", true), Cap("D", false)));
        return GlassBox(keys, 10, new Thickness(9));
    }

    private static StackPanel KeyRow(params UIElement[] caps)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (UIElement c in caps) row.Children.Add(c);
        return row;
    }

    private static Border Cap(string text, bool lit) => new()
    {
        Width = 26, Height = 24, Margin = new Thickness(2),
        CornerRadius = new CornerRadius(5),
        Background = lit ? new SolidColorBrush(OvRed) : new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2E, 0x3C)),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        Child = new TextBlock
        {
            Text = text, FontSize = 12, FontWeight = FontWeights.Bold,
            Foreground = lit ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xA6, 0xAD, 0xBF)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        }
    };

    // ---- toggle grid ----

    private void BuildOverlayToggleGrid()
    {
        OverlayToggleGrid.Children.Clear();
        _overlayToggles.Clear();
        foreach (var o in OverlayCatalog)
            OverlayToggleGrid.Children.Add(BuildOverlayCard(o));
    }

    private Border BuildOverlayCard((string Key, string Glyph, string Name, string Sample, Color Accent, bool Plus) o)
    {
        var sample = PreviewPill(o.Glyph, o.Sample, o.Accent);
        sample.HorizontalAlignment = HorizontalAlignment.Center;
        sample.VerticalAlignment = VerticalAlignment.Center;
        var strip = new Border
        {
            Height = 46, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1E)),
            Margin = new Thickness(0, 0, 0, 10),
            Child = sample
        };

        var name = new TextBlock
        {
            Text = o.Name, FontWeight = FontWeights.Bold, FontSize = 13,
            Foreground = Brush("TextBright"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };

        FrameworkElement header;
        if (o.Plus)
        {
            var dp = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(name, Dock.Left);
            var plus = new Border
            {
                Background = Brush("Accent"), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "PLUS", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
            };
            DockPanel.SetDock(plus, Dock.Right);
            dp.Children.Add(name);
            dp.Children.Add(plus);
            header = dp;
        }
        else header = name;

        var toggle = new CheckBox { Tag = o.Key, Margin = new Thickness(2, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Checked += OverlayToggle_Changed;
        toggle.Unchecked += OverlayToggle_Changed;
        _overlayToggles[o.Key] = toggle;

        var content = new StackPanel();
        content.Children.Add(strip);
        content.Children.Add(header);
        content.Children.Add(toggle);

        if (o.Key == "watermark")
        {
            var box = new TextBox
            {
                Text = _watermarkText, FontSize = 12, MaxLength = 24,
                Margin = new Thickness(2, 10, 0, 0), Padding = new Thickness(7, 3, 7, 3),
                Background = Brush("Panel"), Foreground = Brush("TextBright"),
                BorderBrush = Brush("Outline"), ToolTip = "The text shown on screen"
            };
            box.TextChanged += (_, _) =>
            {
                _watermarkText = box.Text;
                _watermarkOverlay?.SetText(_watermarkText);
                RefreshPreview();
                SaveAppSettings();
            };
            content.Children.Add(box);
        }

        return new Border
        {
            Width = 168, Padding = new Thickness(11),
            CornerRadius = new CornerRadius(8), Background = Brush("Panel2"),
            BorderBrush = Brush("Outline"), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 10), Child = content
        };
    }

    private void OverlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingOverlayToggles) return;
        if (sender is not CheckBox { Tag: string key } cb) return;
        bool on = cb.IsChecked == true;

        switch (key)
        {
            case "cps": if (OverlayCheck != null) OverlayCheck.IsChecked = on; break;
            case "server": if (ServerRegionCheck != null) ServerRegionCheck.IsChecked = on; break;
            case "keystrokes":
                if (!IsPro)
                {
                    _syncingOverlayToggles = true; cb.IsChecked = false; _syncingOverlayToggles = false;
                    NavPro_Click(cb, e);
                    return;
                }
                if (KeystrokesCheck != null) KeystrokesCheck.IsChecked = on;
                break;
            case "fps": _ovFps = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "system": _ovSystem = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "mouse": _ovMouse = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "clock": _ovClock = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "watermark": _ovWatermark = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "music": _ovMusic = on; ApplyInfoOverlays(); SaveAppSettings(); break;
        }

        RefreshPreview();
    }

    /// <summary>Puts each toggle in step with the authoritative source for it.</summary>
    private void RefreshOverlayToggles()
    {
        if (!_overlaysBuilt) return;
        _syncingOverlayToggles = true;
        SetToggle("cps", OverlayCheck?.IsChecked == true);
        SetToggle("keystrokes", KeystrokesCheck?.IsChecked == true);
        SetToggle("server", ServerRegionCheck?.IsChecked == true);
        SetToggle("fps", _ovFps);
        SetToggle("system", _ovSystem);
        SetToggle("mouse", _ovMouse);
        SetToggle("clock", _ovClock);
        SetToggle("watermark", _ovWatermark);
        SetToggle("music", _ovMusic);
        _syncingOverlayToggles = false;
        RefreshPreview();
    }

    private void SetToggle(string key, bool on)
    {
        if (_overlayToggles.TryGetValue(key, out CheckBox? cb)) cb.IsChecked = on;
    }

    /// <summary>"Only Roblox" vs "Windows too" — where the overlays are allowed to show.</summary>
    private void OverlayShowMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        _overlayShowAlways = tag == "always";
        if (_applyingOverlayShow) return;
        ApplyInfoOverlays();
        SaveAppSettings();
    }

    // ---- the real pills ----

    private void ApplyInfoOverlays()
    {
        bool any = _ovFps || _ovSystem || _ovMusic || _ovMouse || _ovClock || _ovWatermark || _ovScore
                   || (IsPro && _keystrokesEnabled);
        if (any)
        {
            _infoOverlayTimer ??= CreateInfoOverlayTimer();
            if (!_infoOverlayTimer.IsEnabled) _infoOverlayTimer.Start();
        }
        UpdateInfoOverlays();
        if (!any) _infoOverlayTimer?.Stop();
    }

    private DispatcherTimer CreateInfoOverlayTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        t.Tick += (_, _) => UpdateInfoOverlays();
        return t;
    }

    private void UpdateInfoOverlays()
    {
        // Move mode always shows them (you're placing them). Otherwise the Show
        // switch decides: "Windows too" shows everywhere, "Only Roblox" shows only
        // while Roblox is the front window.
        bool front = _overlayMoveMode || _overlayShowAlways || RobloxForeground.IsFront();
        bool streamer = StreamerModeCheck?.IsChecked == true;

        if (_ovFps)
        {
            if (_fpsOverlay == null) { _fpsOverlay = new FpsOverlay(); _fpsOverlay.Moved += SaveFpsPos; }
            _fpsOverlay.StreamerMode = streamer;
            _fpsOverlay.SetMoveMode(_overlayMoveMode);
            if (front)
            {
                _fpsOverlay.SetFps(FpsShared.ReadFps());
                if (!_fpsOverlay.IsVisible) _fpsOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_fpsOverlay, _fpsX, _fpsY, _fpsOverlay.PlaceDefault);
            }
            else _fpsOverlay.Hide();
        }
        else _fpsOverlay?.Hide();

        if (_ovSystem)
        {
            if (_systemOverlay == null) { _systemOverlay = new SystemOverlay(); _systemOverlay.Moved += SaveSystemPos; }
            _systemOverlay.StreamerMode = streamer;
            _systemOverlay.SetMoveMode(_overlayMoveMode);
            if (front)
            {
                double? cpu = ReadSystemCpuPercent();
                double? used = MemoryUsage.GigabytesInUse();
                double? total = MemoryUsage.TotalGigabytes();
                double? ramPct = used.HasValue && total is > 0 ? used.Value / total.Value * 100 : null;
                _systemOverlay.SetStats(cpu, ramPct);
                if (!_systemOverlay.IsVisible) _systemOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_systemOverlay, _sysX, _sysY, _systemOverlay.PlaceDefault);
            }
            else _systemOverlay.Hide();
        }
        else _systemOverlay?.Hide();

        if (_ovMouse)
        {
            if (_mouseOverlay == null) { _mouseOverlay = new MouseOverlay(); _mouseOverlay.Moved += SaveMousePos; }
            _mouseOverlay.StreamerMode = streamer;
            _mouseOverlay.SetAccent(AccentColorValue());
            _mouseOverlay.SetMoveMode(_overlayMoveMode);
            if (front)
            {
                if (!_mouseOverlay.IsVisible) _mouseOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_mouseOverlay, _mouseX, _mouseY, _mouseOverlay.PlaceDefault);
            }
            else _mouseOverlay.Hide();
        }
        else _mouseOverlay?.Hide();

        if (_ovClock)
        {
            if (_clockOverlay == null) { _clockOverlay = new ClockOverlay(); _clockOverlay.Moved += SaveClockPos; }
            _clockOverlay.StreamerMode = streamer;
            _clockOverlay.SetMoveMode(_overlayMoveMode);
            if (front)
            {
                _clockOverlay.SetTime(DateTime.Now.ToString("h:mm tt", CultureInfo.CurrentCulture));
                if (!_clockOverlay.IsVisible) _clockOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_clockOverlay, _clockX, _clockY, _clockOverlay.PlaceDefault);
            }
            else _clockOverlay.Hide();
        }
        else _clockOverlay?.Hide();

        if (_ovWatermark)
        {
            if (_watermarkOverlay == null) { _watermarkOverlay = new WatermarkOverlay(); _watermarkOverlay.Moved += SaveWmPos; }
            _watermarkOverlay.StreamerMode = streamer;
            _watermarkOverlay.SetText(_watermarkText);
            _watermarkOverlay.SetMoveMode(_overlayMoveMode);
            if (front)
            {
                if (!_watermarkOverlay.IsVisible) _watermarkOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_watermarkOverlay, _wmX, _wmY, _watermarkOverlay.PlaceDefault);
            }
            else _watermarkOverlay.Hide();
        }
        else _watermarkOverlay?.Hide();

        // Score: one overlay per followed game. Close any whose game is no longer followed.
        foreach (string id in new List<string>(_scoreOverlays.Keys))
            if (!_ovScore || !_scoreFollowed.Contains(id))
            {
                try { _scoreOverlays[id].Hide(); _scoreOverlays[id].Close(); } catch { }
                _scoreOverlays.Remove(id);
            }

        // The live scoreboards stay up whenever Score is on, not only over Roblox, so
        // clicking a game opens its bug right away even while you're in the app.
        if (_ovScore)
        {
            for (int i = 0; i < _scoreFollowed.Count; i++)
            {
                string id = _scoreFollowed[i];
                if (!_scoreOverlays.TryGetValue(id, out ScoreOverlay? ov))
                {
                    ov = new ScoreOverlay();
                    string gid = id;
                    ov.Moved += () => SaveScoreOverlayPos(gid);
                    _scoreOverlays[id] = ov;
                }
                ov.StreamerMode = streamer;
                GameInfo? g = _scoreGames.Find(x => x.Id == id);
                if (g != null) ov.SetGame(g, _favorites.Contains(g.AwayAbbr), _favorites.Contains(g.HomeAbbr));
                else ov.SetMessage(_scoreLoading ? "Loading score…" : "Game not on today");
                ov.SetMoveMode(_scoreMoveMode);
                if (!ov.IsVisible) ov.Show();
                if (!_scoreMoveMode) PlaceScoreOverlay(ov, id, i);
            }
        }

        if (_ovMusic)
        {
            if (_musicOverlay == null) { _musicOverlay = new MusicOverlay(); _musicOverlay.Moved += SaveMusicPos; }
            _musicOverlay.StreamerMode = streamer;
            _musicOverlay.SetMoveMode(_overlayMoveMode);

            long now = Environment.TickCount64;
            if (now - _lastMusicProbe > 2000) { _lastMusicProbe = now; _ = ProbeMusicTitleAsync(); }

            if (front)
            {
                _musicOverlay.SetTitle(_musicTitle);
                if (!_musicOverlay.IsVisible) _musicOverlay.Show();
                if (!_overlayMoveMode) PlacePill(_musicOverlay, _musX, _musY, _musicOverlay.PlaceDefault);
            }
            else _musicOverlay.Hide();
        }
        else _musicOverlay?.Hide();

        // The CPS card has a run-driven life of its own; show it here too while
        // configuring (page open or move mode) so it can be arranged like the rest.
        bool clicking = _overlayTimer?.IsEnabled == true;
        bool configView = _overlayMoveMode || _overlayShowAlways;
        if (OverlayCheck?.IsChecked == true && configView && !clicking)
        {
            _overlay ??= new ClickOverlay { StreamerMode = streamer };
            if (!_cpsMovedHooked) { _overlay.Moved += SaveCpsPos; _cpsMovedHooked = true; }
            _overlay.StreamerMode = streamer;
            _overlay.SetSavedPosition(_cpsX, _cpsY);
            _overlay.SetMoveMode(_overlayMoveMode);
            _overlay.Update(ClickDiagnostics.Current());
        }
        else if (!clicking && !configView && _overlay is { IsVisible: true })
        {
            _overlay.SetMoveMode(false);
            _overlay.Hide();
        }

        // Keystrokes follows the same Show switch as the rest.
        if (_keystrokes is { } ks && IsPro && _keystrokesEnabled)
        {
            if (front && !ks.IsVisible) ks.Show();
            else if (!front && ks.IsVisible) ks.Hide();
        }
    }

    private void SaveFpsPos() { if (_fpsOverlay != null) { _fpsX = _fpsOverlay.Left; _fpsY = _fpsOverlay.Top; SaveAppSettings(); } }
    private void SaveSystemPos() { if (_systemOverlay != null) { _sysX = _systemOverlay.Left; _sysY = _systemOverlay.Top; SaveAppSettings(); } }
    private void SaveMusicPos() { if (_musicOverlay != null) { _musX = _musicOverlay.Left; _musY = _musicOverlay.Top; SaveAppSettings(); } }
    private void SaveMousePos() { if (_mouseOverlay != null) { _mouseX = _mouseOverlay.Left; _mouseY = _mouseOverlay.Top; SaveAppSettings(); } }
    private void SaveClockPos() { if (_clockOverlay != null) { _clockX = _clockOverlay.Left; _clockY = _clockOverlay.Top; SaveAppSettings(); } }
    private void SaveWmPos() { if (_watermarkOverlay != null) { _wmX = _watermarkOverlay.Left; _wmY = _watermarkOverlay.Top; SaveAppSettings(); } }
    // A dragged score bug remembers its spot for the session (games change daily, so
    // positions aren't persisted); the first one also seeds the saved stack anchor.
    private void SaveScoreOverlayPos(string id)
    {
        if (!_scoreOverlays.TryGetValue(id, out ScoreOverlay? ov)) return;
        _scorePos[id] = (ov.Left, ov.Top);
        if (_scoreFollowed.Count > 0 && _scoreFollowed[0] == id) { _scoreX = ov.Left; _scoreY = ov.Top; SaveAppSettings(); }
    }

    private void PlaceScoreOverlay(ScoreOverlay ov, string id, int index)
    {
        if (_scorePos.TryGetValue(id, out (double X, double Y) p)) { ov.Left = p.X; ov.Top = p.Y; return; }

        // Default: stack from the saved anchor (or top-centre), one bug under another.
        ov.UpdateLayout();
        double baseX = _scoreX ?? SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - ov.ActualWidth) / 2;
        double baseY = _scoreY ?? SystemParameters.WorkArea.Top + 120;
        ov.Left = baseX;
        ov.Top = baseY + index * (ov.ActualHeight + 8);
    }

    // ---- Scoreboard tab: follow a real game's live score ----

    private static ScoreLeague ParseLeague(string? s) =>
        Enum.TryParse(s, out ScoreLeague l) ? l : ScoreLeague.CollegeFootball;

    private void PopulateLeagueCombo()
    {
        if (ScoreLeagueCombo == null) return;
        if (ScoreLeagueCombo.Items.Count == 0)
            foreach (ScoreLeague l in Enum.GetValues<ScoreLeague>())
                ScoreLeagueCombo.Items.Add(new ComboBoxItem { Content = SportsScores.DisplayName(l), Tag = l });
        foreach (ComboBoxItem it in ScoreLeagueCombo.Items)
            if (it.Tag is ScoreLeague l && l == _scoreLeague) { ScoreLeagueCombo.SelectedItem = it; break; }
    }

    private async void ScoreLeague_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingScoreUi) return;
        if (ScoreLeagueCombo?.SelectedItem is ComboBoxItem { Tag: ScoreLeague l }) _scoreLeague = l;
        SaveAppSettings();
        await LoadScoreGamesAsync();
        await LoadTeamsAsync();
    }

    private async void ScoreRefresh_Click(object sender, RoutedEventArgs e) => await LoadScoreGamesAsync();

    private void ScoreLiveOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_applyingScoreUi) return;
        _scoreLiveOnly = ScoreLiveOnlyCheck?.IsChecked == true;
        BuildScoreList();
        int shown = VisibleScoreGames().Count;
        SetScoreStatus(_scoreLiveOnly
            ? (shown == 0 ? "No games are live right now." : $"{shown} live now — click to add.")
            : $"{_scoreGames.Count} game{(_scoreGames.Count == 1 ? "" : "s")} — click to add.");
        SaveAppSettings();
    }

    /// <summary>Fetches today's games (Top 25 for college football, else all) and fills the
    /// clickable list; every followed game keeps its own overlay.</summary>
    private async System.Threading.Tasks.Task LoadScoreGamesAsync()
    {
        _scoreLoading = true;
        SetScoreStatus("Loading games…");
        if (ScoreRefreshButton != null) ScoreRefreshButton.IsEnabled = false;

        List<GameInfo> all = await SportsScores.GetGamesAsync(_scoreLeague);

        _scoreLoading = false;
        if (ScoreRefreshButton != null) ScoreRefreshButton.IsEnabled = true;
        _allGames = all;
        _scoreGames = _scoreLeague == ScoreLeague.CollegeFootball ? Top25(all) : all;
        List<GameInfo> games = _scoreGames;
        BuildScoreList();
        BuildTeamList();       // matchups in the picker depend on the full scoreboard
        ApplyInfoOverlays();   // refresh any followed bugs with the new data

        if (games.Count == 0)
        {
            SetScoreStatus(_scoreLeague == ScoreLeague.CollegeFootball
                ? "No ranked games on right now."
                : "No games found today for this league.");
            return;
        }
        int shown = VisibleScoreGames().Count;
        SetScoreStatus($"{shown} game{(shown == 1 ? "" : "s")} — click to add to your screen.");
    }

    /// <summary>The games shown in the list: live-only filter applied, favourite-team
    /// games pulled to the top (order preserved within each group).</summary>
    private List<GameInfo> VisibleScoreGames()
    {
        List<GameInfo> src = _scoreLiveOnly ? _scoreGames.FindAll(g => g.InProgress) : _scoreGames;
        if (_favorites.Count == 0) return src;
        var fav = new List<GameInfo>();
        var rest = new List<GameInfo>();
        foreach (GameInfo g in src) (HasFavorite(g) ? fav : rest).Add(g);
        fav.AddRange(rest);
        return fav;
    }

    private bool HasFavorite(GameInfo g) => _favorites.Contains(g.AwayAbbr) || _favorites.Contains(g.HomeAbbr);

    /// <summary>Builds the clickable game rows: rank, team logos, abbreviations, score, status.</summary>
    private void BuildScoreList()
    {
        if (ScoreGameList == null) return;
        ScoreGameList.Children.Clear();
        foreach (GameInfo g in VisibleScoreGames())
            ScoreGameList.Children.Add(BuildScoreRow(g));
        HighlightScoreRow();
    }

    private Border BuildScoreRow(GameInfo g)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(FavStar(g.AwayAbbr));
        row.Children.Add(RankBadge(g.AwayRank));
        row.Children.Add(RowLogo(g.AwayLogo));
        row.Children.Add(TeamText(g.AwayAbbr));
        row.Children.Add(RowText(g.State == "pre" ? "" : g.AwayScore.ToString(), 28, true));
        row.Children.Add(RowText("@", 18, false));
        row.Children.Add(FavStar(g.HomeAbbr));
        row.Children.Add(RankBadge(g.HomeRank));
        row.Children.Add(RowLogo(g.HomeLogo));
        row.Children.Add(TeamText(g.HomeAbbr));
        row.Children.Add(RowText(g.State == "pre" ? "" : g.HomeScore.ToString(), 28, true));
        row.Children.Add(new TextBlock
        {
            Text = g.Final ? "FINAL" : g.StatusText, FontSize = 11, Foreground = Brush("TextMuted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var border = new Border
        {
            Background = Brush("Panel2"), CornerRadius = new CornerRadius(7),
            BorderBrush = Brush("Outline"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 7),
            Cursor = System.Windows.Input.Cursors.Hand, Tag = g.Id, Child = row
        };
        border.MouseLeftButtonUp += (_, _) => OnScoreRowClick(g.Id);
        return border;
    }

    private static Image RowLogo(string? url)
    {
        var img = new Image
        {
            Width = 22, Height = 22, Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
        };
        BitmapImage? bmp = ScoreOverlay.LoadLogo(url);
        if (bmp != null) img.Source = bmp;
        return img;
    }

    private TextBlock RowText(string text, double width, bool bright) => new()
    {
        Text = text, Width = width, FontSize = 13, FontWeight = bright ? FontWeights.Bold : FontWeights.Normal,
        Foreground = bright ? Brush("TextBright") : Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A team's abbreviation, in yellow when it is a favorite.</summary>
    private TextBlock TeamText(string abbr) => new()
    {
        Text = abbr, Width = 46, FontSize = 13, FontWeight = FontWeights.Bold,
        Foreground = _favorites.Contains(abbr) ? FavYellow : Brush("TextBright"),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>A star toggle that favorites/unfavorites a team without following the game.</summary>
    private TextBlock FavStar(string abbr)
    {
        bool fav = _favorites.Contains(abbr);
        var t = new TextBlock
        {
            Text = fav ? "★" : "☆",   // ★ filled / ☆ hollow
            Width = 20, FontSize = 13, TextAlignment = TextAlignment.Center,
            Foreground = fav ? FavYellow : Brush("TextMuted"), VerticalAlignment = VerticalAlignment.Center,
            Cursor = System.Windows.Input.Cursors.Hand, ToolTip = (fav ? "Unfavorite " : "Favorite ") + abbr
        };
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleFavorite(abbr); };
        return t;
    }

    /// <summary>Toggles a favorite team: re-sorts the list (favorites first) and recolours.</summary>
    private void ToggleFavorite(string abbr)
    {
        if (string.IsNullOrWhiteSpace(abbr)) return;
        abbr = abbr.ToUpperInvariant();
        if (!_favorites.Remove(abbr)) _favorites.Add(abbr);
        SaveAppSettings();
        BuildScoreList();
        BuildTeamList();       // update the picker's stars
        ApplyInfoOverlays();   // recolour any open bugs
    }

    // ---- full team picker (favorite any team, even one not playing today) ----

    private async System.Threading.Tasks.Task LoadTeamsAsync()
    {
        if (!_teamCache.TryGetValue(_scoreLeague, out List<TeamInfo>? teams))
        {
            teams = await SportsScores.GetTeamsAsync(_scoreLeague);
            if (teams.Count > 0) _teamCache[_scoreLeague] = teams;
        }
        _teams = teams ?? new List<TeamInfo>();
        BuildTeamList();
    }

    private void ScoreTeamSearch_Changed(object sender, TextChangedEventArgs e) => BuildTeamList();

    /// <summary>Shows current favorites when the search is empty, else matching teams.</summary>
    private void BuildTeamList()
    {
        if (ScoreTeamList == null) return;
        ScoreTeamList.Children.Clear();

        string q = (ScoreTeamSearch?.Text ?? "").Trim();
        List<TeamInfo> show;
        if (q.Length == 0)
        {
            show = _teams.FindAll(t => _favorites.Contains(t.Abbr));
            if (show.Count == 0)
            {
                ScoreTeamList.Children.Add(TeamHint(_teams.Count == 0
                    ? "Loading teams…"
                    : $"Type to search {_teams.Count} teams, then tap ★ to favorite."));
                return;
            }
        }
        else
        {
            show = _teams.FindAll(t =>
                t.Abbr.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (show.Count == 0) { ScoreTeamList.Children.Add(TeamHint($"No teams match “{q}”.")); return; }
            if (show.Count > 60) show = show.GetRange(0, 60);
        }

        foreach (TeamInfo t in show)
            ScoreTeamList.Children.Add(BuildTeamRow(t));
    }

    private TextBlock TeamHint(string text) => new()
    {
        Text = text, Foreground = Brush("TextMuted"), FontSize = 12, Margin = new Thickness(2, 4, 0, 0)
    };

    private Border BuildTeamRow(TeamInfo t)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(FavStar(t.Abbr));
        row.Children.Add(RowLogo(t.Logo));
        row.Children.Add(new TextBlock
        {
            Text = t.Abbr, Width = 58, FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = _favorites.Contains(t.Abbr) ? FavYellow : Brush("TextBright"),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = t.Name, Width = 180, FontSize = 12, Foreground = Brush("TextSoft"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Show who this team is playing today (click the matchup to add that game).
        GameInfo? game = _allGames.Find(g => g.AwayAbbr == t.Abbr || g.HomeAbbr == t.Abbr);
        var m = new TextBlock
        {
            Text = game != null ? MatchupText(t.Abbr, game) : "not playing today",
            FontSize = 11, Foreground = game != null ? Brush("TextSoft") : Brush("TextMuted"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (game != null)
        {
            m.Cursor = System.Windows.Input.Cursors.Hand;
            m.ToolTip = "Show this game on screen";
            m.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnScoreRowClick(game.Id); };
        }
        row.Children.Add(m);

        var border = new Border
        {
            Background = Brush("Panel2"), CornerRadius = new CornerRadius(7),
            BorderBrush = Brush("Outline"), BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6),
            Cursor = System.Windows.Input.Cursors.Hand, Child = row
        };
        border.MouseLeftButtonUp += (_, _) => ToggleFavorite(t.Abbr);
        return border;
    }

    /// <summary>"@ UGA · 3:30 PM", or with the score once it is underway.</summary>
    private static string MatchupText(string abbr, GameInfo g)
    {
        bool away = g.AwayAbbr == abbr;
        string opp = away ? g.HomeAbbr : g.AwayAbbr;
        string head = (away ? "@ " : "vs ") + opp;
        string status = g.Final ? "Final" : g.StatusText;
        if (g.State == "pre")
            return $"{head}  ·  {status}";
        int me = away ? g.AwayScore : g.HomeScore;
        int them = away ? g.HomeScore : g.AwayScore;
        return $"{head}  {me}-{them}  ·  {status}";
    }

    /// <summary>A small right-aligned rank number before a team, blank when unranked.</summary>
    private TextBlock RankBadge(int rank) => new()
    {
        Text = rank is >= 1 and <= 25 ? rank.ToString() : "",
        Width = 22, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brush("Accent"),
        TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 5, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void HighlightScoreRow()
    {
        if (ScoreGameList == null) return;
        foreach (object child in ScoreGameList.Children)
            if (child is Border { Tag: string id } b)
            {
                bool on = _scoreFollowed.Contains(id);
                b.BorderBrush = on ? Brush("Accent") : Brush("Outline");
                b.BorderThickness = new Thickness(on ? 2 : 1);
            }
    }

    /// <summary>Clicking a game toggles its bug — adds an overlay for it, or removes it if
    /// it was already on. Score is on whenever at least one game is followed.</summary>
    private void OnScoreRowClick(string id)
    {
        if (_scoreFollowed.Contains(id)) { _scoreFollowed.Remove(id); _scorePos.Remove(id); }
        else _scoreFollowed.Add(id);

        _ovScore = _scoreFollowed.Count > 0;
        SyncScoreShowToggle();
        HighlightScoreRow();
        EnsureScoreTimer();
        ApplyInfoOverlays();
        RefreshPreview();
        SaveAppSettings();

        GameInfo? g = _scoreGames.Find(x => x.Id == id);
        SetScoreStatus(_scoreFollowed.Count == 0
            ? "No games on screen — click one to add it."
            : $"Showing {_scoreFollowed.Count} game{(_scoreFollowed.Count == 1 ? "" : "s")}" + (g != null ? "  ·  " + g.ShortName : ""));
    }

    private void ScoreShow_Changed(object sender, RoutedEventArgs e)
    {
        _ovScore = ScoreShowCheck?.IsChecked == true;
        ApplyInfoOverlays();
        RefreshPreview();
        SaveAppSettings();
    }

    /// <summary>Lets the bugs be dragged on screen (shared overlay move mode).</summary>
    private void ScoreMove_Click(object sender, RoutedEventArgs e)
    {
        _scoreMoveMode = !_scoreMoveMode;
        if (ScoreMoveButton != null) ScoreMoveButton.Content = _scoreMoveMode ? "Done moving" : "Move on screen";
        UpdateInfoOverlays();
    }

    private void SyncScoreShowToggle()
    {
        if (ScoreShowCheck != null) ScoreShowCheck.IsChecked = _ovScore;
    }

    private void EnsureScoreTimer()
    {
        if (_scoreTimer != null) { _scoreTimer.Start(); return; }
        _scoreTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _scoreTimer.Tick += async (_, _) => await RefreshScoreAsync();
        _scoreTimer.Start();
    }

    /// <summary>Re-reads the list so the list and every followed bug stay live.</summary>
    private async System.Threading.Tasks.Task RefreshScoreAsync()
    {
        List<GameInfo> all = await SportsScores.GetGamesAsync(_scoreLeague);
        if (all.Count == 0) return;
        _allGames = all;
        _scoreGames = _scoreLeague == ScoreLeague.CollegeFootball ? Top25(all) : all;
        BuildScoreList();
        BuildTeamList();
        ApplyInfoOverlays();   // push fresh scores into every open bug
    }

    /// <summary>The ranked (AP Top 25) games from a full scoreboard, best rank first.</summary>
    private static List<GameInfo> Top25(List<GameInfo> all)
    {
        List<GameInfo> ranked = all.FindAll(g => g.BestRank <= 25);
        ranked.Sort((a, b) => a.BestRank.CompareTo(b.BestRank));
        return ranked;
    }

    private async System.Threading.Tasks.Task RestoreScoreSelectionAsync() => await LoadScoreGamesAsync();

    private void SetScoreStatus(string text) { if (ScoreStatusText != null) ScoreStatusText.Text = text; }

    // Sets a window to a saved spot (clamped on-screen), or its default corner.
    private static void PlacePill(Window w, double? x, double? y, Action placeDefault)
    {
        if (x is { } px && y is { } py)
        {
            w.UpdateLayout();
            Rect wa = SystemParameters.WorkArea;
            w.Left = Math.Max(wa.Left, Math.Min(px, wa.Right - w.ActualWidth));
            w.Top = Math.Max(wa.Top, Math.Min(py, wa.Bottom - w.ActualHeight));
        }
        else placeDefault();
    }

    /// <summary>
    /// Flips drag mode on every movable overlay. While on, the enabled overlays
    /// sit on screen regardless of whether Roblox is in front and can be dragged;
    /// clicking Done restores click-through and remembers where each one landed.
    /// </summary>
    private void OverlayMove_Click(object sender, RoutedEventArgs e)
    {
        _overlayMoveMode = !_overlayMoveMode;

        // The keys panel drags with the rest; CPS and the pills are handled in
        // UpdateInfoOverlays, which runs below.
        _keystrokes?.SetMoveMode(_overlayMoveMode);
        if (_overlayMoveMode) StartInfoOverlayTimerForMove();
        else ApplyInfoOverlays();

        UpdateInfoOverlays();
        if (OverlayMoveButton != null)
            OverlayMoveButton.Content = _overlayMoveMode ? "Done moving" : "Move overlays";
        if (OverlayMoveHint != null)
            OverlayMoveHint.Visibility = _overlayMoveMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCpsPos() { if (_overlay != null) { _cpsX = _overlay.Left; _cpsY = _overlay.Top; SaveAppSettings(); } }

    private void StartInfoOverlayTimerForMove()
    {
        _infoOverlayTimer ??= CreateInfoOverlayTimer();
        if (!_infoOverlayTimer.IsEnabled) _infoOverlayTimer.Start();
    }

    private async Task ProbeMusicTitleAsync()
    {
        try
        {
            var web = MusicWeb;
            if (web?.CoreWebView2 == null) { _musicTitle = null; return; }

            string raw = await web.CoreWebView2.ExecuteScriptAsync("document.title");
            string? title = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
            _musicTitle = MusicNowPlaying.Clean(title);
        }
        catch
        {
            _musicTitle = null;
        }
    }

    /// <summary>Closed with the app so leftover pills can't linger on screen.</summary>
    private void CloseInfoOverlays()
    {
        _infoOverlayTimer?.Stop();
        try { _fpsOverlay?.Close(); } catch { }
        try { _systemOverlay?.Close(); } catch { }
        try { _musicOverlay?.Close(); } catch { }
        try { _mouseOverlay?.Close(); } catch { }
        try { _clockOverlay?.Close(); } catch { }
        try { _watermarkOverlay?.Close(); } catch { }
        foreach (ScoreOverlay ov in _scoreOverlays.Values)
            try { ov.Close(); } catch { }
        _scoreOverlays.Clear();
        _scoreTimer?.Stop();
    }
}
