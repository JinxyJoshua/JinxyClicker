using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// Screen Tint on the Mod page (Plus): a translucent colour look laid over the
/// screen while Roblox is in front. Presented like Lyre's Shaders page — a grid
/// of Look cards, each showing a live preview of the look on a sample scene, plus
/// Strength and Vignette sliders to fine-tune the chosen look.
/// </summary>
public partial class MainWindow
{
    private TintOverlay? _tint;
    private ScreenGrade? _grade;
    private DispatcherTimer? _tintTimer;
    private bool _tintCardsBuilt;

    private bool _tintEnabled;
    private string _tintPresetId = "warm";
    private double _tintIntensity = 50;
    private double _tintVignette;
    private string? _tintLookId;

    // The card Border + its checkmark, by look id, for highlighting the selection.
    private readonly Dictionary<string, (Border Card, UIElement Check)> _lookCards = new();

    // True while a Look click is pushing values to the sliders, so their change
    // handlers don't read it as the user nudging off the look.
    private bool _applyingLook;

    private void LoadTint(AppSettings s)
    {
        _tintEnabled = s.TintEnabled;
        _tintPresetId = TintStyles.ById(s.TintPreset)?.Id ?? "warm";
        _tintIntensity = Math.Clamp(s.TintIntensity, 0, 100);
        _tintVignette = Math.Clamp(s.TintVignette, 0, 100);
        _tintLookId = ColorLooks.ById(s.TintLook)?.Id;

        BuildLookCards();

        if (TintToggle != null) TintToggle.IsChecked = _tintEnabled;
        _applyingLook = true;
        if (TintIntensitySlider != null) TintIntensitySlider.Value = _tintIntensity;
        if (TintVignetteSlider != null) TintVignetteSlider.Value = _tintVignette;
        _applyingLook = false;

        UpdateTintValues();
        LightTintPreset();

        if (_tintEnabled) StartTintTimer();
        UpdateTintNow();
    }

    // ---- Look cards ----

    private void BuildLookCards()
    {
        if (_tintCardsBuilt || TintLookGrid == null) return;
        _tintCardsBuilt = true;

        foreach (ColorLook look in ColorLooks.All)
            TintLookGrid.Children.Add(BuildLookCard(look));
    }

    private Border BuildLookCard(ColorLook look)
    {
        var name = new TextBlock
        {
            Text = look.Name, FontWeight = FontWeights.Bold, FontSize = 12,
            Foreground = Brush("TextBright"), Margin = new Thickness(2, 8, 0, 0)
        };
        var blurb = new TextBlock
        {
            Text = look.Blurb, FontSize = 10, Foreground = Brush("TextMuted"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 1, 0, 0)
        };

        var content = new StackPanel();
        content.Children.Add(BuildLookPreview(look));
        content.Children.Add(name);
        content.Children.Add(blurb);

        var check = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = Brush("Accent"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 6, 0), Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var inner = new Grid();
        inner.Children.Add(content);
        inner.Children.Add(check);

        var card = new Border
        {
            Tag = look.Id, Width = 152, Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8), Background = Brush("Panel2"),
            BorderBrush = Brush("Outline"), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 10, 10), Cursor = Cursors.Hand, Child = inner
        };
        card.MouseLeftButtonUp += (_, _) => ApplyLook(look.Id);

        _lookCards[look.Id] = (card, check);
        return card;
    }

    // A neon-dusk skyline sample scene with the look's real grade applied to the
    // colours, so each card shows exactly what the look does. The dusk gradient
    // exercises the grade across a sweep of hues, the low sun gives a warm
    // highlight, the building silhouettes give deep darks for contrast/vignette,
    // and the lit windows spread saturated hues (cyan/amber/magenta/green/blue/
    // purple) so colour shifts are obvious at a glance.
    private UIElement BuildLookPreview(ColorLook look)
    {
        const double w = 136, h = 78;
        float[] m = GradeLooks.For(look.Id);
        var grid = new Grid
        {
            Width = w, Height = h,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 6, 6)
        };

        // Dusk sky: indigo up top, through magenta, to a warm band at the horizon.
        var sky = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        sky.GradientStops.Add(new GradientStop(Graded(m, 0x2A, 0x1E, 0x5A), 0.0));
        sky.GradientStops.Add(new GradientStop(Graded(m, 0xB8, 0x3C, 0x8E), 0.58));
        sky.GradientStops.Add(new GradientStop(Graded(m, 0xFF, 0xAC, 0x5C), 1.0));
        grid.Children.Add(new Rectangle { Fill = sky });

        // A low sun sitting behind the skyline.
        grid.Children.Add(new Ellipse
        {
            Width = 34, Height = 34, Fill = new SolidColorBrush(Graded(m, 0xFF, 0xE1, 0x8E)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var city = new Canvas();
        Color dark = Graded(m, 0x17, 0x12, 0x2E);
        void Building(double left, double bh, double bw) => AddBlock(city, left, h - bh, bw, bh, dark);
        Building(0, 34, 22);
        Building(20, 26, 16);
        Building(34, 46, 20);
        Building(52, 30, 14);
        Building(64, 52, 24);
        Building(86, 34, 18);
        Building(102, 44, 20);
        Building(120, 28, 18);

        // Lit windows, one per hue, scattered across the silhouettes.
        AddBlock(city, 70, 32, 3, 3, Graded(m, 0x3C, 0xE0, 0xD8));   // cyan
        AddBlock(city, 78, 32, 3, 3, Graded(m, 0xFF, 0xC5, 0x3D));   // amber
        AddBlock(city, 70, 42, 3, 3, Graded(m, 0xE0, 0x4F, 0xB0));   // magenta
        AddBlock(city, 78, 42, 3, 3, Graded(m, 0x6F, 0xD6, 0x5F));   // green
        AddBlock(city, 40, 40, 3, 3, Graded(m, 0x4F, 0x8B, 0xE0));   // blue
        AddBlock(city, 40, 48, 3, 3, Graded(m, 0x9B, 0x5F, 0xD6));   // purple
        AddBlock(city, 108, 42, 3, 3, Graded(m, 0xFF, 0xC5, 0x3D));  // amber
        AddBlock(city, 108, 50, 3, 3, Graded(m, 0x3C, 0xE0, 0xD8));  // cyan
        AddBlock(city, 6, 52, 3, 3, Graded(m, 0xE0, 0x4F, 0xB0));    // magenta
        grid.Children.Add(city);

        byte v = TintStyles.VignetteAlphaFor(look.Vignette);
        if (v > 0)
        {
            var vb = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5),
                RadiusX = 0.85, RadiusY = 0.85
            };
            vb.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
            vb.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(v / 3), 0, 0, 0), 0.78));
            vb.GradientStops.Add(new GradientStop(Color.FromArgb(v, 0, 0, 0), 1.0));
            grid.Children.Add(new Rectangle { Fill = vb });
        }

        return new Border { CornerRadius = new CornerRadius(6), Child = grid };
    }

    private static void AddBlock(Canvas canvas, double left, double top, double w, double h, Color color)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color) };
        Canvas.SetLeft(r, left);
        Canvas.SetTop(r, top);
        canvas.Children.Add(r);
    }

    // Runs one sample colour through a look's grade, for the preview thumbnails.
    private static Color Graded(float[] m, byte r, byte g, byte b)
    {
        var (gr, gg, gb) = GradeMatrix.ApplyToColor(m, r / 255.0, g / 255.0, b / 255.0);
        return Color.FromRgb((byte)Math.Round(gr * 255), (byte)Math.Round(gg * 255), (byte)Math.Round(gb * 255));
    }

    private void ApplyLook(string id)
    {
        ColorLook? look = ColorLooks.ById(id);
        if (look == null) return;

        _tintLookId = id;
        _tintPresetId = look.TintId;
        _tintIntensity = look.Intensity;
        _tintVignette = look.Vignette;

        _applyingLook = true;
        if (TintIntensitySlider != null) TintIntensitySlider.Value = _tintIntensity;
        if (TintVignetteSlider != null) TintVignetteSlider.Value = _tintVignette;
        _applyingLook = false;

        UpdateTintValues();
        LightTintPreset();

        // Turn the tint on when a look is picked — picking a look and seeing
        // nothing happen reads as broken. Toggling it on saves and repaints the
        // overlay; otherwise do both here.
        if (!_tintEnabled && TintToggle != null)
        {
            TintToggle.IsChecked = true;
        }
        else
        {
            UpdateTintNow();
            SaveAppSettings();
        }
    }

    // Highlights the selected look's card, or none when the sliders have been
    // nudged off any look.
    private void LightTintPreset()
    {
        foreach ((string id, (Border card, UIElement check)) in _lookCards)
        {
            bool on = id == _tintLookId;
            card.BorderBrush = on ? Brush("Accent") : Brush("Outline");
            card.BorderThickness = new Thickness(on ? 2 : 1);
            check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateTintValues()
    {
        if (TintStrengthValue != null)
            TintStrengthValue.Text = _tintIntensity.ToString("0", CultureInfo.CurrentCulture) + "%";
        if (TintVignetteValue != null)
            TintVignetteValue.Text = _tintVignette < 1
                ? "Off"
                : _tintVignette.ToString("0", CultureInfo.CurrentCulture) + "%";
    }

    // ---- overlay plumbing ----

    private void StartTintTimer()
    {
        if (_tintTimer == null)
        {
            _tintTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _tintTimer.Tick += (_, _) => UpdateTintNow();
        }
        _tintTimer.Start();
    }

    private void StopTintTimer() => _tintTimer?.Stop();

    /// <summary>
    /// Applies the colour grade and vignette, or clears them. Only ever on while
    /// Roblox is the foreground window — the grade covers the whole screen, so it
    /// is lifted the moment you tab away from the game.
    /// </summary>
    private void UpdateTintNow()
    {
        bool shouldShow = _tintEnabled && RobloxForeground.IsFront();

        if (!shouldShow)
        {
            _grade?.Reset();
            _tint?.Hide();
            return;
        }

        // The real colour grade for the chosen look, scaled by Strength.
        _grade ??= new ScreenGrade();
        _grade.Apply(GradeLooks.Lerp(_tintLookId, _tintIntensity / 100.0));

        // Vignette darkens the edges, which a colour matrix can't do, so it stays a
        // light overlay. The old flat colour wash is gone — the grade does colour now.
        if (_tintVignette >= 1)
        {
            _tint ??= new TintOverlay { StreamerMode = StreamerModeCheck?.IsChecked == true };
            _tint.SetTint(TintStyles.All[0], 0);
            _tint.SetVignette(_tintVignette);
            if (!_tint.IsVisible) _tint.Show();
        }
        else _tint?.Hide();
    }

    private void TintToggle_Changed(object sender, RoutedEventArgs e)
    {
        _tintEnabled = TintToggle?.IsChecked == true;
        if (_tintEnabled) StartTintTimer();
        else { StopTintTimer(); _tint?.Hide(); }
        UpdateTintNow();
        SaveAppSettings();
    }

    private void TintIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tintIntensity = e.NewValue;
        UpdateTintValues();
        if (_applyingLook) return;

        // Strength scales the selected look's grade — it stays selected.
        UpdateTintNow();   // live
    }

    private void TintIntensity_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    private void TintVignette_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tintVignette = e.NewValue;
        UpdateTintValues();
        if (_applyingLook) return;

        UpdateTintNow();   // live
    }

    private void TintVignette_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    /// <summary>Closed with the app so a leftover veil can't keep it alive or linger on screen.</summary>
    private void CloseTint()
    {
        StopTintTimer();
        try { _grade?.Dispose(); } catch { }   // clears the screen grade on exit
        try { _tint?.Close(); } catch { }
    }
}
