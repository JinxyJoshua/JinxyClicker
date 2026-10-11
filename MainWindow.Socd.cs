using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// SOCD cleaner (Pro): a global keyboard hook on one opposing key pair. While
/// both keys are held it releases them so they don't fight the game; a single key
/// moves you as normal. This is the resolution Roblox honours from an external
/// app — the "move as the last key" variants need releasing a key you are still
/// holding, which the game rejects, so the cleaner does the one that works.
/// </summary>
public partial class MainWindow
{
    private readonly SocdHook _socd = new();
    private bool _socdEnabled;
    private string _socdLeft = "A", _socdRight = "D";
    // True from construction so the controls' own TextChanged events, which fire
    // while the XAML is still loading, don't run before LoadSocd sets things up.
    private bool _loadingSocd = true;

    private void LoadSocd(AppSettings s)
    {
        _loadingSocd = true;

        _socdEnabled = s.SocdEnabled;
        _socdLeft = KeyOr(s.SocdLeft, "A");
        _socdRight = KeyOr(s.SocdRight, "D");

        if (SocdEnabledCheck is not null) SocdEnabledCheck.IsChecked = _socdEnabled;
        if (SocdLeftKey is not null) SocdLeftKey.Text = _socdLeft;
        if (SocdRightKey is not null) SocdRightKey.Text = _socdRight;

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

    // Pushes the current keys into the hook and starts/stops it to match
    // "Pro and enabled".
    private void ApplySocdState()
    {
        _socd.Configure(Vk(_socdLeft), Vk(_socdRight));

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

    private void SocdKeys_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingSocd || SocdLeftKey is null || SocdRightKey is null) return;
        _socdLeft = SocdLeftKey.Text.Trim();
        _socdRight = SocdRightKey.Text.Trim();
        _settingsDirty = true;
        ApplySocdState();
    }
}
