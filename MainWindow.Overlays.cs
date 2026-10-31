using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private bool _ovFps, _ovSystem, _ovMusic, _ovMouse, _ovClock, _ovWatermark;
    private string _watermarkText = "Jinxy";

    private bool _overlaysBuilt;
    private bool _syncingOverlayToggles;
    private bool _overlayMoveMode;
    private bool _cpsMovedHooked;

    // Dragged positions; null means "use the default corner".
    private double? _cpsX, _cpsY, _fpsX, _fpsY, _sysX, _sysY, _musX, _musY,
                    _mouseX, _mouseY, _clockX, _clockY, _wmX, _wmY;

    private DispatcherTimer? _infoOverlayTimer;
    private readonly Dictionary<string, CheckBox> _overlayToggles = new();
    private readonly Dictionary<string, FrameworkElement> _previewItems = new();

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
    }

    private void LoadOverlaySettings(AppSettings s)
    {
        _ovFps = s.OverlayFps;
        _ovSystem = s.OverlaySystem;
        _ovMusic = s.OverlayMusic;
        _ovMouse = s.OverlayMouse;
        _ovClock = s.OverlayClock;
        _ovWatermark = s.OverlayWatermark;
        _watermarkText = string.IsNullOrWhiteSpace(s.OverlayWatermarkText) ? "Jinxy" : s.OverlayWatermarkText;

        _cpsX = s.OverlayCpsX; _cpsY = s.OverlayCpsY;
        _fpsX = s.OverlayFpsX; _fpsY = s.OverlayFpsY;
        _sysX = s.OverlaySystemX; _sysY = s.OverlaySystemY;
        _musX = s.OverlayMusicX; _musY = s.OverlayMusicY;
        _mouseX = s.OverlayMouseX; _mouseY = s.OverlayMouseY;
        _clockX = s.OverlayClockX; _clockY = s.OverlayClockY;
        _wmX = s.OverlayWatermarkX; _wmY = s.OverlayWatermarkY;

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

        // A sample scene so the overlays have something game-like to sit over.
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
            Text = "your game", Opacity = 0.22, Foreground = Brushes.White,
            FontSize = 13, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });

        // Each overlay roughly where it appears in-game; stored by key so the
        // preview can show only the ones that are switched on.
        AddPreview(host, "server", PreviewPill("", "EU West", OvBlue), HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(14, 14, 0, 0));
        AddPreview(host, "keystrokes", PreviewKeys(), HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(14, 0, 0, 0));
        AddPreview(host, "mouse", PreviewPill("", "L M R 4 5", OvBlue), HorizontalAlignment.Left, VerticalAlignment.Bottom, new Thickness(14, 0, 0, 14));
        AddPreview(host, "watermark", PreviewPill("", _watermarkText, OvPurple), HorizontalAlignment.Right, VerticalAlignment.Bottom, new Thickness(0, 0, 14, 14));

        var topCentre = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0) };
        FrameworkElement music = PreviewPill("", "Now playing", OvPurple);
        music.HorizontalAlignment = HorizontalAlignment.Center;
        FrameworkElement clock = PreviewPill("", "9:41", OvPurple);
        clock.HorizontalAlignment = HorizontalAlignment.Center;
        clock.Margin = new Thickness(0, 8, 0, 0);
        _previewItems["music"] = music;
        _previewItems["clock"] = clock;
        topCentre.Children.Add(music);
        topCentre.Children.Add(clock);
        host.Children.Add(topCentre);

        var right = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 14, 0)
        };
        FrameworkElement cps = PreviewCps();
        FrameworkElement fps = Stacked(PreviewPill("", "146 FPS", OvGreen));
        FrameworkElement sys = Stacked(PreviewSystem());
        _previewItems["cps"] = cps;
        _previewItems["fps"] = fps;
        _previewItems["system"] = sys;
        right.Children.Add(cps);
        right.Children.Add(fps);
        right.Children.Add(sys);
        host.Children.Add(right);

        RefreshPreview();
    }

    private void AddPreview(Grid host, string key, FrameworkElement el, HorizontalAlignment h, VerticalAlignment v, Thickness margin)
    {
        Anchor(el, h, v, margin);
        _previewItems[key] = el;
        host.Children.Add(el);
    }

    /// <summary>Shows in the preview only the overlays that are currently on.</summary>
    private void RefreshPreview()
    {
        foreach (KeyValuePair<string, FrameworkElement> item in _previewItems)
            item.Value.Visibility = IsOverlayOn(item.Key) ? Visibility.Visible : Visibility.Collapsed;

        if (_previewItems.TryGetValue("watermark", out FrameworkElement? wm) && wm is Border { Child: StackPanel sp }
            && sp.Children.Count > 1 && sp.Children[1] is TextBlock t)
            t.Text = _watermarkText;
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

    // ---- the real pills ----

    private void ApplyInfoOverlays()
    {
        bool any = _ovFps || _ovSystem || _ovMusic || _ovMouse || _ovClock || _ovWatermark;
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
        // In move mode the pills stay on screen (and where they were dragged) even
        // when Roblox isn't in front, so they can be arranged against the desktop.
        bool front = _overlayMoveMode || RobloxForeground.IsFront();
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
    }

    private void SaveFpsPos() { if (_fpsOverlay != null) { _fpsX = _fpsOverlay.Left; _fpsY = _fpsOverlay.Top; SaveAppSettings(); } }
    private void SaveSystemPos() { if (_systemOverlay != null) { _sysX = _systemOverlay.Left; _sysY = _systemOverlay.Top; SaveAppSettings(); } }
    private void SaveMusicPos() { if (_musicOverlay != null) { _musX = _musicOverlay.Left; _musY = _musicOverlay.Top; SaveAppSettings(); } }
    private void SaveMousePos() { if (_mouseOverlay != null) { _mouseX = _mouseOverlay.Left; _mouseY = _mouseOverlay.Top; SaveAppSettings(); } }
    private void SaveClockPos() { if (_clockOverlay != null) { _clockX = _clockOverlay.Left; _clockY = _clockOverlay.Top; SaveAppSettings(); } }
    private void SaveWmPos() { if (_watermarkOverlay != null) { _wmX = _watermarkOverlay.Left; _wmY = _watermarkOverlay.Top; SaveAppSettings(); } }

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

        if (_overlayMoveMode)
        {
            if (OverlayCheck?.IsChecked == true)
            {
                _overlay ??= new ClickOverlay { StreamerMode = StreamerModeCheck?.IsChecked == true };
                if (!_cpsMovedHooked) { _overlay.Moved += SaveCpsPos; _cpsMovedHooked = true; }
                _overlay.SetSavedPosition(_cpsX, _cpsY);
                _overlay.Update(ClickDiagnostics.Current());
                _overlay.SetMoveMode(true);
            }
            _keystrokes?.SetMoveMode(true);   // the keys panel drags with the rest
            StartInfoOverlayTimerForMove();
        }
        else
        {
            if (_overlay != null)
            {
                _overlay.SetMoveMode(false);
                if (_overlayTimer?.IsEnabled != true) _overlay.Hide();
            }
            _keystrokes?.SetMoveMode(false);
            ApplyInfoOverlays();
        }

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
    }
}
