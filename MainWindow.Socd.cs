using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// SOCD cleaning (Pro): a global keyboard hook that null-cancels the A/D and W/S
/// pairs so opposing movement keys resolve instantly instead of fighting.
/// </summary>
public partial class MainWindow
{
    private readonly SocdHook _socd = new();
    private bool _socdEnabled;
    private bool _socdNeutral;

    private void LoadSocd(AppSettings s)
    {
        _socdEnabled = s.SocdEnabled;
        _socdNeutral = s.SocdNeutral;
        if (SocdEnabledCheck is not null) SocdEnabledCheck.IsChecked = _socdEnabled;
        if (SocdNeutralCheck is not null) SocdNeutralCheck.IsChecked = _socdNeutral;
        ApplySocdPro();
    }

    private void NavSocd_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavSocd, PageSocd, "SOCD", "Null-cancel opposing movement keys");
        ApplySocdPro();
    }

    private void ApplySocdPro()
    {
        bool pro = IsPro;

        // The whole tab is Pro-only: it's hidden from the sidebar unless licensed.
        if (NavSocd is not null) NavSocd.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (SocdLockNote is not null) SocdLockNote.Visibility = Visibility.Collapsed;
        if (SocdProPanel is not null) SocdProPanel.IsEnabled = pro;

        // If a pass expired while this page was open, leave it.
        if (!pro && PageSocd is { Visibility: Visibility.Visible })
            ShowPage(NavClicker, PageClicker, "Clicker", "Configure your own click engine");

        ApplySocdState();   // reconcile the hook with the current Pro + toggle state
    }

    // Starts or stops the hook so it matches "Pro and enabled", and keeps the mode current.
    private void ApplySocdState()
    {
        _socd.Mode = _socdNeutral ? SocdMode.Neutral : SocdMode.LastInputWins;

        bool shouldRun = IsPro && _socdEnabled;
        if (shouldRun && !_socd.IsRunning) _socd.Start();
        else if (!shouldRun && _socd.IsRunning) _socd.Stop();
    }

    private void SocdEnabled_Changed(object sender, RoutedEventArgs e)
    {
        _socdEnabled = SocdEnabledCheck.IsChecked == true;
        _settingsDirty = true;
        ApplySocdState();
    }

    private void SocdNeutral_Changed(object sender, RoutedEventArgs e)
    {
        _socdNeutral = SocdNeutralCheck.IsChecked == true;
        _settingsDirty = true;
        ApplySocdState();   // mode change takes effect live
    }
}
