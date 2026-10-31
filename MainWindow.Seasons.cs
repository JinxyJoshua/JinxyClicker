using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// Seasonal themes: each season recolours the accent and drifts a light ambient
/// effect over the whole app — snow for winter, falling leaves and a few bats for a
/// spooky fall, blossoms for spring, warm sparkles for summer. The effect is a
/// click-through <c>SeasonCanvas</c> of emoji particles animated on a timer.
/// </summary>
public partial class MainWindow
{
    private enum Season { None, Fall, Winter, Spring, Summer }

    private Season _season = Season.None;
    private DispatcherTimer? _seasonTimer;
    private readonly List<SeasonParticle> _particles = new();
    private readonly Random _seasonRng = new();

    private sealed class SeasonParticle
    {
        public TextBlock El = null!;
        public double X, Y, Vy, Vx, Sway, SwayPhase, Rot, Spin;
    }

    /// <summary>Applies a season's accent colour and starts (or clears) its effect.</summary>
    private void ApplySeason(Season s)
    {
        _season = s;
        StopSeasonFx();

        if (s == Season.None)
        {
            // Hand the accent back to whatever swatch the user had chosen.
            if (TryParseColor(_accentHex, out Color a)) ApplyAccent(a);
            return;
        }

        ApplyAccent(SeasonAccent(s));
        StartSeasonFx(s);
    }

    private static Color SeasonAccent(Season s) => s switch
    {
        Season.Fall => Color.FromRgb(0xE8, 0x6A, 0x17),    // pumpkin orange
        Season.Winter => Color.FromRgb(0x5C, 0xC6, 0xFF),  // icy blue
        Season.Spring => Color.FromRgb(0x5A, 0xD1, 0x7A),  // fresh green
        Season.Summer => Color.FromRgb(0xFF, 0xC4, 0x3C),  // warm gold
        _ => Color.FromRgb(0xA7, 0x8B, 0xFA)
    };

    // glyphs, how many, base font size, downward speed.
    private static (string[] Glyphs, int Count, double Size, double Fall) SeasonSpec(Season s) => s switch
    {
        Season.Winter => (new[] { "❄", "❅", "❆", "•" }, 70, 15, 1.1),
        Season.Fall => (new[] { "\U0001F341", "\U0001F342", "\U0001F341", "\U0001F342", "\U0001F987" }, 34, 22, 1.3),
        Season.Spring => (new[] { "\U0001F338", "\U0001F33C", "\U0001F337" }, 40, 18, 0.8),
        Season.Summer => (new[] { "✨", "☀", "✨" }, 28, 16, 0.5),
        _ => (Array.Empty<string>(), 0, 14, 1)
    };

    private void StartSeasonFx(Season s)
    {
        if (SeasonCanvas == null) return;
        SeasonCanvas.Children.Clear();
        _particles.Clear();

        (string[] glyphs, int count, double size, double fall) = SeasonSpec(s);
        if (glyphs.Length == 0) return;

        double w = Math.Max(600, SeasonCanvas.ActualWidth);
        double h = Math.Max(400, SeasonCanvas.ActualHeight);
        for (int i = 0; i < count; i++)
            _particles.Add(MakeParticle(s, glyphs, size, fall, w, h, spread: true));

        _seasonTimer ??= MakeSeasonTimer();
        _seasonTimer.Start();
    }

    private DispatcherTimer MakeSeasonTimer()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };   // ~30fps
        t.Tick += (_, _) => SeasonTick();
        return t;
    }

    private SeasonParticle MakeParticle(Season s, string[] glyphs, double size, double fall,
        double w, double h, bool spread)
    {
        bool rise = s == Season.Summer;
        double fs = size * (0.6 + _seasonRng.NextDouble() * 0.9);
        string glyph = glyphs[_seasonRng.Next(glyphs.Length)];

        var el = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = fs,
            // WPF draws these glyphs monochrome, so a per-season foreground colours them.
            Foreground = SeasonBrush(s, glyph),
            Opacity = 0.45 + _seasonRng.NextDouble() * 0.45,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(0)
        };

        var p = new SeasonParticle
        {
            El = el,
            X = _seasonRng.NextDouble() * w,
            Y = spread ? _seasonRng.NextDouble() * h : (rise ? h + 20 : -20),
            Vy = (rise ? -1 : 1) * fall * (0.5 + _seasonRng.NextDouble()),
            Vx = (_seasonRng.NextDouble() - 0.5) * 0.6,
            Sway = 0.4 + _seasonRng.NextDouble() * 1.4,
            SwayPhase = _seasonRng.NextDouble() * Math.PI * 2,
            Spin = (s == Season.Fall || s == Season.Spring) ? (_seasonRng.NextDouble() - 0.5) * 3.0 : 0
        };

        Canvas.SetLeft(el, p.X);
        Canvas.SetTop(el, p.Y);
        SeasonCanvas!.Children.Add(el);
        return p;
    }

    private SolidColorBrush SeasonBrush(Season s, string glyph)
    {
        Color c = s switch
        {
            Season.Winter => Color.FromRgb(0xEA, 0xF4, 0xFF),                               // icy white
            Season.Fall => glyph == "\U0001F987"
                ? Color.FromRgb(0x9A, 0x8A, 0xB0)                                           // bats: pale slate (visible on dark)
                : FallLeafColor(),                                                          // leaves: warm
            Season.Spring => _seasonRng.Next(2) == 0
                ? Color.FromRgb(0xFF, 0x9E, 0xC9) : Color.FromRgb(0x9B, 0xDE, 0x8A),        // blossom pink / fresh green
            Season.Summer => Color.FromRgb(0xFF, 0xD6, 0x4A),                               // warm gold
            _ => Colors.White
        };
        return new SolidColorBrush(c);
    }

    private Color FallLeafColor()
    {
        Color[] leaves =
        {
            Color.FromRgb(0xE8, 0x82, 0x2E),   // orange
            Color.FromRgb(0xD1, 0x47, 0x2B),   // red
            Color.FromRgb(0xE8, 0xA3, 0x3D)    // gold
        };
        return leaves[_seasonRng.Next(leaves.Length)];
    }

    private void SeasonTick()
    {
        if (SeasonCanvas == null || _particles.Count == 0) return;
        double w = SeasonCanvas.ActualWidth, h = SeasonCanvas.ActualHeight;
        if (w < 1 || h < 1) return;
        bool rise = _season == Season.Summer;

        foreach (SeasonParticle p in _particles)
        {
            p.SwayPhase += 0.05;
            p.Y += p.Vy;
            p.X += p.Vx + Math.Sin(p.SwayPhase) * p.Sway * 0.4;
            p.Rot += p.Spin;

            if (!rise && p.Y > h + 24) { p.Y = -24; p.X = _seasonRng.NextDouble() * w; }
            else if (rise && p.Y < -24) { p.Y = h + 24; p.X = _seasonRng.NextDouble() * w; }
            if (p.X < -30) p.X = w + 15; else if (p.X > w + 30) p.X = -15;

            Canvas.SetLeft(p.El, p.X);
            Canvas.SetTop(p.El, p.Y);
            if (p.Spin != 0 && p.El.RenderTransform is RotateTransform rt) rt.Angle = p.Rot;
        }
    }

    private void StopSeasonFx()
    {
        _seasonTimer?.Stop();
        SeasonCanvas?.Children.Clear();
        _particles.Clear();
    }

    // ---- Theme-page controls ----

    private bool _applyingSeasonUi;

    /// <summary>A season segment was chosen (RadioButton Checked).</summary>
    private void SeasonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_applyingSeasonUi) return;
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse(tag, out Season s))
        {
            ApplySeason(s);
            _settingsDirty = true;
        }
    }

    /// <summary>Restores the saved season at startup and checks its segment.</summary>
    private void LoadSeason(string? saved)
    {
        if (!Enum.TryParse(saved, out Season s)) s = Season.None;

        _applyingSeasonUi = true;
        if (SeasonButtons != null)
            foreach (object child in SeasonButtons.Children)
                if (child is RadioButton { Tag: string tag } rb)
                    rb.IsChecked = string.Equals(tag, s.ToString(), StringComparison.OrdinalIgnoreCase);
        _applyingSeasonUi = false;

        ApplySeason(s);
    }
}
