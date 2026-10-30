using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// Screen Tint on the Mod page (Plus): a translucent colour laid over the screen,
/// optionally only while Roblox is in front.
/// </summary>
/// <remarks>
/// The overlay is created only when the tint is first switched on. A half-second
/// timer runs while it is on, so turning on "only while Roblox is in front" shows
/// and hides the veil as the player tabs in and out. It is click-through and
/// never focused, so it can never swallow a click or steal the game's foreground.
/// </remarks>
public partial class MainWindow
{
    private TintOverlay? _tint;
    private DispatcherTimer? _tintTimer;
    private bool _tintButtonsBuilt;

    private bool _tintEnabled;
    private string _tintPresetId = "warm";
    private double _tintIntensity = 50;

    private void LoadTint(AppSettings s)
    {
        _tintEnabled = s.TintEnabled;
        _tintPresetId = TintStyles.ById(s.TintPreset)?.Id ?? "warm";
        _tintIntensity = Math.Clamp(s.TintIntensity, 0, 100);

        BuildTintButtons();

        if (TintToggle != null) TintToggle.IsChecked = _tintEnabled;
        if (TintIntensitySlider != null) TintIntensitySlider.Value = _tintIntensity;
        LightTintPreset();

        if (_tintEnabled) StartTintTimer();
        UpdateTintNow();
    }

    private void BuildTintButtons()
    {
        if (_tintButtonsBuilt || TintPresetRow == null) return;
        _tintButtonsBuilt = true;

        foreach (TintPreset p in TintStyles.All)
        {
            var swatch = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(p.R, p.G, p.B)),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var b = new Button
            {
                Tag = p.Id,
                Padding = new Thickness(10, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 11,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { swatch, new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center } }
                }
            };
            b.Click += TintPreset_Click;
            TintPresetRow.Children.Add(b);
        }
    }

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

    private void TintPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id }) _tintPresetId = id;
        LightTintPreset();
        UpdateTintNow();
        SaveAppSettings();
    }

    private void TintIntensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _tintIntensity = e.NewValue;
        UpdateTintNow();   // live
    }

    private void TintIntensity_DragCompleted(object sender, DragCompletedEventArgs e) => SaveAppSettings();

    // Lights the active preset's button.
    private void LightTintPreset()
    {
        if (TintPresetRow == null) return;
        foreach (object child in TintPresetRow.Children)
            if (child is Button b && b.Tag is string id)
                b.FontWeight = id == _tintPresetId ? FontWeights.Bold : FontWeights.Normal;
    }

    /// <summary>Closed with the app so a leftover veil can't keep it alive or linger on screen.</summary>
    private void CloseTint()
    {
        StopTintTimer();
        try { _tint?.Close(); } catch { }
    }
}
