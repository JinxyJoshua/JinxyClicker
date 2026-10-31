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
/// The Overlays page: a styled preview of every on-screen widget, plus a grid of
/// toggles. The three new pills (FPS, system, music) are owned here; the CPS,
/// keystrokes and server cards drive their existing controls so there is one
/// source of truth for each. Nothing here reads the game beyond what those
/// widgets already read.
/// </summary>
public partial class MainWindow
{
    private FpsOverlay? _fpsOverlay;
    private SystemOverlay? _systemOverlay;
    private MusicOverlay? _musicOverlay;

    private bool _ovFps, _ovSystem, _ovMusic;
    private bool _overlaysBuilt;
    private bool _syncingOverlayToggles;

    private DispatcherTimer? _infoOverlayTimer;
    private readonly Dictionary<string, CheckBox> _overlayToggles = new();

    private string? _musicTitle;
    private long _lastMusicProbe;

    // Accent purple shared with the real overlays, so the preview matches.
    private static readonly Color OvPurple = Color.FromRgb(0xA7, 0x8B, 0xFA);
    private static readonly Color OvGreen = Color.FromRgb(0x7A, 0xE5, 0x8A);
    private static readonly Color OvBlue = Color.FromRgb(0x6A, 0xB7, 0xFF);
    private static readonly Color OvRed = Color.FromRgb(0xFF, 0x6B, 0x74);

    private static readonly (string Key, string Glyph, string Name, string Blurb, string Sample, Color Accent)[] OverlayCatalog =
    {
        ("cps",        "", "CPS",        "Clicks per second, with a live graph",   "142",       OvPurple),
        ("keystrokes", "", "Keystrokes", "WASD + mouse that light up as you play",  "W A S D",   OvRed),
        ("fps",        "", "FPS",        "Roblox's real frame rate",                "146 FPS",   OvGreen),
        ("system",     "", "System",     "CPU and RAM load, machine-wide",          "CPU 18%",   OvBlue),
        ("music",      "", "Music",      "The track playing on the Music page",     "Now playing", OvPurple),
        ("server",     "", "Server",     "The region of your Roblox server",        "EU West",   OvBlue),
    };

    private void NavOverlays_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavOverlays, PageOverlays, "Overlays", "The widgets you show on screen");
        BuildOverlaysPage();
        RefreshOverlayToggles();
    }

    /// <summary>Reads the saved on/off state for the three pills and applies it.</summary>
    private void LoadOverlaySettings(AppSettings s)
    {
        _ovFps = s.OverlayFps;
        _ovSystem = s.OverlaySystem;
        _ovMusic = s.OverlayMusic;
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

        // The overlays, placed roughly where they appear in-game.
        host.Children.Add(Anchor(PreviewPill("", "EU West", OvBlue), HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(14, 14, 0, 0)));
        host.Children.Add(Anchor(PreviewPill("", "Now playing", OvPurple), HorizontalAlignment.Center, VerticalAlignment.Top, new Thickness(0, 14, 0, 0)));
        host.Children.Add(Anchor(PreviewKeys(), HorizontalAlignment.Left, VerticalAlignment.Center, new Thickness(14, 0, 0, 0)));

        var right = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 14, 0)
        };
        right.Children.Add(PreviewCps());
        right.Children.Add(Stacked(PreviewPill("", "146 FPS", OvGreen)));
        right.Children.Add(Stacked(PreviewSystem()));
        host.Children.Add(right);
    }

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

    private Border BuildOverlayCard((string Key, string Glyph, string Name, string Blurb, string Sample, Color Accent) o)
    {
        var strip = new Border
        {
            Height = 44, CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x1E)),
            Margin = new Thickness(0, 0, 0, 9),
            Child = PreviewPill(o.Glyph, o.Sample, o.Accent)
        };
        if (strip.Child is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        var name = new TextBlock
        {
            Text = o.Name, FontWeight = FontWeights.Bold, FontSize = 13,
            Foreground = Brush("TextBright"), Margin = new Thickness(2, 0, 0, 0)
        };
        var blurb = new TextBlock
        {
            Text = o.Blurb, FontSize = 11, Foreground = Brush("TextMuted"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0), MinHeight = 30,
            VerticalAlignment = VerticalAlignment.Top
        };

        var toggle = new CheckBox { Tag = o.Key, Margin = new Thickness(2, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Checked += OverlayToggle_Changed;
        toggle.Unchecked += OverlayToggle_Changed;
        _overlayToggles[o.Key] = toggle;

        var content = new StackPanel();
        content.Children.Add(strip);
        content.Children.Add(name);
        content.Children.Add(blurb);
        content.Children.Add(toggle);

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
            case "keystrokes": if (KeystrokesCheck != null) KeystrokesCheck.IsChecked = on; break;
            case "server": if (ServerRegionCheck != null) ServerRegionCheck.IsChecked = on; break;
            case "fps": _ovFps = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "system": _ovSystem = on; ApplyInfoOverlays(); SaveAppSettings(); break;
            case "music": _ovMusic = on; ApplyInfoOverlays(); SaveAppSettings(); break;
        }
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
        SetToggle("music", _ovMusic);
        _syncingOverlayToggles = false;
    }

    private void SetToggle(string key, bool on)
    {
        if (_overlayToggles.TryGetValue(key, out CheckBox? cb)) cb.IsChecked = on;
    }

    // ---- the real pills ----

    private void ApplyInfoOverlays()
    {
        bool any = _ovFps || _ovSystem || _ovMusic;
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
        bool front = RobloxForeground.IsFront();
        bool streamer = StreamerModeCheck?.IsChecked == true;

        if (_ovFps)
        {
            _fpsOverlay ??= new FpsOverlay();
            _fpsOverlay.StreamerMode = streamer;
            if (front)
            {
                _fpsOverlay.SetFps(FpsShared.ReadFps());
                if (!_fpsOverlay.IsVisible) _fpsOverlay.Show();
                _fpsOverlay.PlaceDefault();
            }
            else _fpsOverlay.Hide();
        }
        else _fpsOverlay?.Hide();

        if (_ovSystem)
        {
            _systemOverlay ??= new SystemOverlay();
            _systemOverlay.StreamerMode = streamer;
            if (front)
            {
                double? cpu = ReadSystemCpuPercent();
                double? used = MemoryUsage.GigabytesInUse();
                double? total = MemoryUsage.TotalGigabytes();
                double? ramPct = used.HasValue && total is > 0 ? used.Value / total.Value * 100 : null;
                _systemOverlay.SetStats(cpu, ramPct);
                if (!_systemOverlay.IsVisible) _systemOverlay.Show();
                _systemOverlay.PlaceDefault();
            }
            else _systemOverlay.Hide();
        }
        else _systemOverlay?.Hide();

        if (_ovMusic)
        {
            _musicOverlay ??= new MusicOverlay();
            _musicOverlay.StreamerMode = streamer;

            long now = Environment.TickCount64;
            if (now - _lastMusicProbe > 2000) { _lastMusicProbe = now; _ = ProbeMusicTitleAsync(); }

            if (front)
            {
                _musicOverlay.SetTitle(_musicTitle);
                if (!_musicOverlay.IsVisible) _musicOverlay.Show();
                _musicOverlay.PlaceDefault();
            }
            else _musicOverlay.Hide();
        }
        else _musicOverlay?.Hide();
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
    }
}
