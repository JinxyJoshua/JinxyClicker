using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// SOCD cleaner (Plus): a global keyboard hook on one or two opposing key pairs —
/// a Custom pair, or the WASD profile that cleans A/D and W/S together. While both
/// keys in a pair are held it releases the one pressed first and keeps the most
/// recent, so you snap to the latest direction instead of standing still; a single
/// key moves you as normal. It only acts while Roblox is the foreground window.
/// </summary>
public partial class MainWindow
{
    private readonly SocdHook _socd = new();
    private bool _socdEnabled;
    private string _socdProfile = "Custom";   // "WASD" (cleans A/D and W/S) or "Custom"
    private string _socdLeft = "A", _socdRight = "D";
    // True from construction so the controls' own TextChanged events, which fire
    // while the XAML is still loading, don't run before LoadSocd sets things up.
    private bool _loadingSocd = true;

    private void LoadSocd(AppSettings s)
    {
        _loadingSocd = true;

        _socdEnabled = s.SocdEnabled;
        _socdProfile = KeyOr(s.SocdProfile, "Custom") == "WASD" ? "WASD" : "Custom";
        _socdLeft = KeyOr(s.SocdLeft, "A");
        _socdRight = KeyOr(s.SocdRight, "D");

        if (SocdEnabledCheck is not null) SocdEnabledCheck.IsChecked = _socdEnabled;
        if (SocdProfileWasd is not null) SocdProfileWasd.IsChecked = _socdProfile == "WASD";
        if (SocdProfileCustom is not null) SocdProfileCustom.IsChecked = _socdProfile != "WASD";
        if (SocdLeftKey is not null) SocdLeftKey.Text = _socdLeft;
        if (SocdRightKey is not null) SocdRightKey.Text = _socdRight;
        UpdateSocdProfileUi();

        _loadingSocd = false;
        ApplySocdPro();
    }

    private static string KeyOr(string? v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v!.Trim();

    // A typed key character to its virtual-key code, or 0 for blank/invalid.
    private static int Vk(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var parsed = MacroStore.ParseKeys(key.Trim());
        return parsed.HasValue && parsed.Value.Keys.Length > 0 ? parsed.Value.Keys[0] : 0;
    }

    private void NavSocd_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        ShowPage(NavSocd, PageSocd, "SOCD", "Clean opposing movement keys so holding both doesn't fight");
        ApplySocdPro();
    }

    private void ApplySocdPro()
    {
        bool pro = IsPro;

        // The tab stays visible so Free users can see the feature exists, but its
        // content is never shown — clicking it (or a pass expiring here) sends them
        // to the key page instead.
        if (SocdLockNote is not null) SocdLockNote.Visibility = Visibility.Collapsed;
        if (SocdProPanel is not null) SocdProPanel.IsEnabled = pro;

        if (!pro && PageSocd is { Visibility: Visibility.Visible })
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");

        ApplySocdState();
    }

    // Pushes the current pair(s) into the hook and starts/stops it to match
    // "Pro and enabled". WASD cleans both strafe axes; Custom cleans one pair.
    private void ApplySocdState()
    {
        if (_socdProfile == "WASD")
            _socd.Configure((Vk("A"), Vk("D")), (Vk("W"), Vk("S")));
        else
            _socd.Configure((Vk(_socdLeft), Vk(_socdRight)));

        bool shouldRun = IsPro && _socdEnabled;
        if (shouldRun && !_socd.IsRunning) _socd.Start();
        else if (!shouldRun && _socd.IsRunning) _socd.Stop();
    }

    private void SocdEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSocd) return;
        _socdEnabled = SocdEnabledCheck.IsChecked == true;
        _settingsDirty = true;
        ApplySocdState();
    }

    private void SocdProfile_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSocd) return;
        if (sender is RadioButton { Tag: string tag }) _socdProfile = tag == "WASD" ? "WASD" : "Custom";
        _settingsDirty = true;
        UpdateSocdProfileUi();
        ApplySocdState();
    }

    // The custom key pickers only matter for the Custom profile.
    private void UpdateSocdProfileUi()
    {
        if (SocdCustomKeys is not null)
            SocdCustomKeys.Visibility = _socdProfile == "WASD" ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SocdKeys_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingSocd || SocdLeftKey is null || SocdRightKey is null) return;
        _socdLeft = SocdLeftKey.Text.Trim();
        _socdRight = SocdRightKey.Text.Trim();
        _settingsDirty = true;
        ApplySocdState();
    }
}
