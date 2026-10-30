using System;
using System.IO;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The Graphics Quality slider on the Tweaks page: one dial from full quality to
/// maximum FPS, written as Roblox's own allowlisted graphics flags.
/// </summary>
/// <remarks>
/// The slider only sets the pending level and updates the readout; the flags are
/// written to Roblox on Apply, like Roblox's own graphics settings, and cleared
/// on Restore. See <see cref="GraphicsQuality"/> for the level → flag mapping.
/// </remarks>
public partial class MainWindow
{
    private int _qualityLevel = GraphicsQuality.MaxLevel;

    private void LoadGraphicsQuality(AppSettings s)
    {
        _qualityLevel = Math.Clamp(s.GraphicsQualityLevel, GraphicsQuality.MinLevel, GraphicsQuality.MaxLevel);

        if (QualitySlider != null) QualitySlider.Value = _qualityLevel;   // raises ValueChanged → readout
        UpdateQualityReadout();
        RefreshQualityState();
    }

    private void QualitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        _qualityLevel = (int)Math.Round(e.NewValue);
        UpdateQualityReadout();
    }

    private void UpdateQualityReadout()
    {
        if (QualityReadout == null) return;

        if (_qualityLevel >= GraphicsQuality.MaxLevel)
        {
            QualityReadout.Text = "Full quality — no overrides, Roblox renders everything.";
            return;
        }

        GraphicsPreset p = GraphicsQuality.PresetFor(_qualityLevel);
        QualityReadout.Text =
            $"Render level {p.FrmLevel}/21   ·   textures {p.TextureQuality}/3   ·   {p.Msaa}× anti-aliasing   ·   " +
            $"{(p.KillGrass ? "no grass" : "grass on")}   ·   {(p.GraySky ? "flat grey sky" : "normal sky")}";
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
        QualityStateText.Text = isApplied ? "Applied — restart Roblox" : "Roblox's own settings";
    }
}
