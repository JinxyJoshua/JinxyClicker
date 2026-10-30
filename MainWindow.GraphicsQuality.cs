using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The Graphics Quality slider on the Mod page: one dial from full quality to
/// maximum FPS, with Looks/FPS meters, preset anchors and a readout of exactly
/// what it will write — all of it Roblox's own allowlisted graphics flags.
/// </summary>
/// <remarks>
/// The slider only sets the pending level and updates the readout; the flags are
/// written to Roblox on Apply, like Roblox's own graphics settings, and cleared
/// on Restore. See <see cref="GraphicsQuality"/> for the level → flag mapping.
/// </remarks>
public partial class MainWindow
{
    private int _qualityLevel = GraphicsQuality.MaxLevel;
    private bool _qualityMetersBuilt;
    private bool _syncingQualityPreset;
    private const int QualityMeterSegments = 10;

    private static readonly Brush QualityLooksOn = (Brush)new BrushConverter().ConvertFrom("#A86BFF")!;
    private static readonly Brush QualityFpsOn = (Brush)new BrushConverter().ConvertFrom("#4ED38A")!;
    private static readonly Brush QualityMeterOff = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    private void LoadGraphicsQuality(AppSettings s)
    {
        _qualityLevel = Math.Clamp(s.GraphicsQualityLevel, GraphicsQuality.MinLevel, GraphicsQuality.MaxLevel);

        BuildQualityMeters();
        if (QualitySlider != null) QualitySlider.Value = _qualityLevel;   // raises ValueChanged → readout
        UpdateQualityReadout();
        RefreshQualityState();
    }

    private void BuildQualityMeters()
    {
        if (_qualityMetersBuilt || QualityLooksMeter == null || QualityFpsMeter == null) return;
        _qualityMetersBuilt = true;

        for (int i = 0; i < QualityMeterSegments; i++)
        {
            QualityLooksMeter.Children.Add(MakeMeterSegment());
            QualityFpsMeter.Children.Add(MakeMeterSegment());
        }
    }

    private static Border MakeMeterSegment() => new()
    {
        Width = 13, Height = 9, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 3, 0),
        Background = QualityMeterOff
    };

    private static void FillMeter(Panel meter, int filled, Brush on)
    {
        for (int i = 0; i < meter.Children.Count; i++)
            if (meter.Children[i] is Border seg)
                seg.Background = i < filled ? on : QualityMeterOff;
    }

    private void QualitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _qualityLevel = (int)Math.Round(e.NewValue);
        UpdateQualityReadout();
    }

    // A preset anchor was clicked — move the slider to it, which refreshes everything.
    private void QualityPreset_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingQualityPreset) return;
        if (sender is RadioButton { Tag: string t } && int.TryParse(t, out int level) && QualitySlider != null)
            QualitySlider.Value = level;
    }

    private void UpdateQualityReadout()
    {
        bool full = _qualityLevel >= GraphicsQuality.MaxLevel;
        GraphicsPreset p = GraphicsQuality.PresetFor(_qualityLevel);

        if (QualityBadgeText != null) QualityBadgeText.Text = TierName(_qualityLevel);

        if (QualitySummary != null)
            QualitySummary.Text = full
                ? "Full quality — Roblox renders everything, nothing overridden."
                : $"{TextureWord(p.TextureQuality)} textures, {AaWord(p.Msaa)}, " +
                  $"{(p.KillGrass ? "no grass" : "grass on")}, {(p.GraySky ? "grey sky" : "normal sky")}.";

        // Looks grows with the level; FPS is its mirror.
        FillMeter(QualityLooksMeter, (int)Math.Round(_qualityLevel / 10.0 * QualityMeterSegments), QualityLooksOn);
        FillMeter(QualityFpsMeter, (int)Math.Round((11 - _qualityLevel) / 10.0 * QualityMeterSegments), QualityFpsOn);

        if (QualGraphicsLevel != null) QualGraphicsLevel.Text = $"{_qualityLevel} of 10";
        if (QualFpsCap != null) QualFpsCap.Text = FpsCapWord();
        if (QualTextures != null) QualTextures.Text = full ? "High" : TextureWord(p.TextureQuality);
        if (QualAA != null) QualAA.Text = full ? "4× MSAA" : AaWord(p.Msaa);
        if (QualDetail != null) QualDetail.Text = $"{(full ? 21 : p.FrmLevel)} of 21";
        if (QualGrass != null) QualGrass.Text = full || !p.KillGrass ? "On" : "Off";
        if (QualSky != null) QualSky.Text = full || !p.GraySky ? "Normal" : "Grey";
        if (QualArt != null) QualArt.Text = $"{(int)Math.Round(_qualityLevel / 10.0 * 100)}%";

        LightQualityPreset();
    }

    // Checks the preset anchor that matches the current level, or none when the
    // slider sits between anchors. Guarded so it does not re-drive the slider.
    private void LightQualityPreset()
    {
        if (QualityPresetRow == null) return;

        _syncingQualityPreset = true;
        try
        {
            foreach (object child in QualityPresetRow.Children)
                if (child is RadioButton rb && rb.Tag is string t && int.TryParse(t, out int level))
                    rb.IsChecked = level == _qualityLevel;
        }
        finally
        {
            _syncingQualityPreset = false;
        }
    }

    private static string TierName(int level) =>
        level >= 9 ? "Quality" : level >= 5 ? "Balanced" : level >= 2 ? "Performance" : "Max FPS";

    private static string TextureWord(int quality) => quality switch
    {
        <= 0 => "Lowest",
        1 => "Low",
        2 => "Medium",
        _ => "High"
    };

    private static string AaWord(int msaa) => msaa <= 0 ? "No AA" : $"{msaa}× MSAA";

    private static string FpsCapWord()
    {
        int? cap = FastFlagStore.CurrentFpsCap();
        if (cap is null or <= 60) return "60 (default)";
        return cap >= FastFlagStore.UnlimitedFps ? "Uncapped" : $"{cap} FPS";
    }

    private void ApplyQuality_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GraphicsQuality.Apply(_qualityLevel);
            SaveAppSettings();
            RefreshFlags();   // the FPS-boost flags share some of these; keep that list honest
            RefreshQualityState(applied: _qualityLevel < GraphicsQuality.MaxLevel);
        }
        catch (DirectoryNotFoundException)
        {
            if (QualityStateText != null) QualityStateText.Text = "Roblox not found";
        }
        catch
        {
            if (QualityStateText != null) QualityStateText.Text = "Could not apply";
        }
    }

    private void RestoreQuality_Click(object sender, RoutedEventArgs e)
    {
        GraphicsQuality.Restore();
        _qualityLevel = GraphicsQuality.MaxLevel;
        if (QualitySlider != null) QualitySlider.Value = _qualityLevel;
        SaveAppSettings();
        RefreshFlags();
        RefreshQualityState(applied: false);
    }

    private void RefreshQualityState(bool? applied = null)
    {
        if (QualityStateText == null) return;

        bool isApplied = applied ?? GraphicsQuality.IsApplied();
        QualityStateText.Text = isApplied ? "Applied — restart Roblox" : "Roblox's own settings now";
    }
}
