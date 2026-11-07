using System;
using System.Globalization;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The Mouse &amp; Sensitivity controls (Plus, on the MOD page): the Windows pointer
/// speed and acceleration (what people loosely call "DPI"), and the Roblox in-game
/// sensitivity. Windows settings are applied instantly; Roblox sens is written to
/// its settings file and takes effect next launch. Reads the live values when the
/// MOD page opens so the controls always reflect reality.
/// </summary>
public partial class MainWindow
{
    // Starts true so the slider's initial XAML value (10) firing ValueChanged at
    // construction does NOT push the Windows pointer speed to 10 on every launch.
    // RefreshMouseSens() runs when the MOD page first opens, reads the live value
    // and releases this — only user drags from then on actually change the speed.
    private bool _loadingMouseSens = true;

    private void RefreshMouseSens()
    {
        _loadingMouseSens = true;
        try
        {
            if (MouseSpeedSlider is not null) MouseSpeedSlider.Value = MouseSettings.GetPointerSpeed();
            if (MouseAccelCheck is not null) MouseAccelCheck.IsChecked = MouseSettings.GetAcceleration();
            if (RobloxSensSlider is not null && RobloxSens.Read() is { } s)
                RobloxSensSlider.Value = Math.Clamp(s, RobloxSensSlider.Minimum, RobloxSensSlider.Maximum);
            UpdateMouseSensLabels();
        }
        catch { /* no Roblox / restricted — leave the defaults */ }
        _loadingMouseSens = false;
    }

    private void UpdateMouseSensLabels()
    {
        if (MouseSpeedValue is not null && MouseSpeedSlider is not null)
            MouseSpeedValue.Text = ((int)MouseSpeedSlider.Value).ToString();
        if (RobloxSensValue is not null && RobloxSensSlider is not null)
            RobloxSensValue.Text = RobloxSensSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void MouseSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MouseSpeedValue is not null && MouseSpeedSlider is not null)
            MouseSpeedValue.Text = ((int)MouseSpeedSlider.Value).ToString();
        if (_loadingMouseSens || MouseSpeedSlider is null) return;
        try { MouseSettings.SetPointerSpeed((int)MouseSpeedSlider.Value); } catch { }
    }

    private void MouseAccel_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingMouseSens || MouseAccelCheck is null) return;
        try { MouseSettings.SetAcceleration(MouseAccelCheck.IsChecked == true); } catch { }
    }

    private void RobloxSens_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RobloxSensValue is not null && RobloxSensSlider is not null)
            RobloxSensValue.Text = RobloxSensSlider.Value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void RobloxSensApply_Click(object sender, RoutedEventArgs e)
    {
        if (RobloxSensSlider is null) return;
        bool ok = false;
        try { ok = RobloxSens.Write((float)RobloxSensSlider.Value); } catch { }
        if (MouseSensStatus is not null)
            MouseSensStatus.Text = ok
                ? "Roblox sensitivity set — restart Roblox to apply it."
                : "Couldn't find Roblox's settings. Open Roblox once, then try again.";
    }

    private void MouseReset_Click(object sender, RoutedEventArgs e)
    {
        _loadingMouseSens = true;
        try
        {
            MouseSettings.SetPointerSpeed(10);
            MouseSettings.SetAcceleration(false);
            if (MouseSpeedSlider is not null) MouseSpeedSlider.Value = 10;
            if (MouseAccelCheck is not null) MouseAccelCheck.IsChecked = false;
            UpdateMouseSensLabels();
            if (MouseSensStatus is not null)
                MouseSensStatus.Text = "Mouse reset to 1:1 speed with acceleration off.";
        }
        catch { }
        _loadingMouseSens = false;
    }
}
