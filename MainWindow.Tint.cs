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

    // A small sample scene (sky, ground, a few blocks) with the look's tint and
    // vignette laid over it, so each card shows what the look does.
    private UIElement BuildLookPreview(ColorLook look)
    {
        const double w = 136, h = 78;
        var grid = new Grid
        {
            Width = w, Height = h,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), 6, 6)
        };

        grid.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x8F, 0xC6, 0xFF), Color.FromRgb(0xDD, 0xEE, 0xFF), 90)
        });
        grid.Children.Add(new Ellipse
        {
            Width = 20, Height = 20, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0xB2)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 16, 0)
        });
        grid.Children.Add(new Rectangle
        {
            Height = 28, VerticalAlignment = VerticalAlignment.Bottom,
            Fill = new SolidColorBrush(Color.FromRgb(0x4E, 0x9A, 0x43))
        });

        var blocks = new Canvas();
        AddBlock(blocks, 18, 40, 16, 20, Color.FromRgb(0xE0, 0x4F, 0x4F));
        AddBlock(blocks, 52, 32, 18, 28, Color.FromRgb(0x4F, 0x8B, 0xE0));
        AddBlock(blocks, 92, 36, 15, 24, Color.FromRgb(0x9B, 0x5F, 0xD6));
        grid.Children.Add(blocks);

        // Tint veil — a touch stronger than the live value so even gentle looks read.
        TintPreset preset = TintStyles.ById(look.TintId) ?? TintStyles.All[0];
        byte a = TintStyles.AlphaFor(Math.Max(look.Intensity, 24));
        grid.Children.Add(new Rectangle
        {
            Fill = new SolidColorBrush(Color.FromArgb(a, preset.R, preset.G, preset.B))
        });

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
    /// Shows, hides or recolours the veil. Only ever shown while Roblox is the
    /// foreground window, so it never tints the rest of the desktop.
    /// </summary>
    private void UpdateTintNow()
    {
        bool shouldShow = _tintEnabled && RobloxForeground.IsFront();

        if (!shouldShow)
        {
            _tint?.Hide();
            return;
        }

        _tint ??= new TintOverlay { StreamerMode = StreamerModeCheck?.IsChecked == true };

        TintPreset preset = TintStyles.ById(_tintPresetId) ?? TintStyles.All[0];
        _tint.SetTint(preset, _tintIntensity);
        _tint.SetVignette(_tintVignette);
        if (!_tint.IsVisible) _tint.Show();
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

        _tintLookId = null;   // nudged off the look
        LightTintPreset();
        UpdateTintNow();   // live
    }

    private void TintIntensity_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    private void TintVignette_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tintVignette = e.NewValue;
        UpdateTintValues();
        if (_applyingLook) return;

        _tintLookId = null;
        LightTintPreset();
        UpdateTintNow();   // live
    }

    private void TintVignette_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    /// <summary>Closed with the app so a leftover veil can't keep it alive or linger on screen.</summary>
    private void CloseTint()
    {
        StopTintTimer();
        try { _tint?.Close(); } catch { }
    }
}
