using System;
using System.Windows;
using System.Windows.Controls;

namespace JinxyClicker;

/// <summary>
/// SOCD / auto-strafe (Pro): a global keyboard hook on two opposing key pairs.
/// Strafe mode alternates them side to side while both are held; last-input and
/// neutral are the standard SOCD resolutions.
/// </summary>
public partial class MainWindow
{
    private readonly SocdHook _socd = new();
    private bool _socdEnabled;
    private SocdMode _socdMode = SocdMode.Strafe;
    private int _socdSpeedMs = 40;
    private string _socdLeft = "A", _socdRight = "D", _socdUp = "W", _socdDown = "S";
    private bool _loadingSocd;

    private void LoadSocd(AppSettings s)
    {
        _loadingSocd = true;

        _socdEnabled = s.SocdEnabled;
        _socdMode = ParseMode(s.SocdMode);
        _socdSpeedMs = s.SocdSpeedMs is >= SocdHook.MinStrafeMs and <= SocdHook.MaxStrafeMs ? s.SocdSpeedMs : 40;
        _socdLeft = KeyOr(s.SocdLeft, "A");
        _socdRight = KeyOr(s.SocdRight, "D");
        _socdUp = KeyOr(s.SocdUp, "W");
        _socdDown = KeyOr(s.SocdDown, "S");

        if (SocdEnabledCheck is not null) SocdEnabledCheck.IsChecked = _socdEnabled;
        if (SocdModeStrafe is not null) SocdModeStrafe.IsChecked = _socdMode == SocdMode.Strafe;
        if (SocdModeLast is not null) SocdModeLast.IsChecked = _socdMode == SocdMode.LastInputWins;
        if (SocdModeNeutral is not null) SocdModeNeutral.IsChecked = _socdMode == SocdMode.Neutral;
        if (SocdSpeedBox is not null) SocdSpeedBox.Text = _socdSpeedMs.ToString();
        if (SocdLeftKey is not null) SocdLeftKey.Text = _socdLeft;
        if (SocdRightKey is not null) SocdRightKey.Text = _socdRight;
        if (SocdUpKey is not null) SocdUpKey.Text = _socdUp;
        if (SocdDownKey is not null) SocdDownKey.Text = _socdDown;

        _loadingSocd = false;
        ApplySocdPro();
    }

    private static string KeyOr(string? v, string fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v!.Trim();

    private static SocdMode ParseMode(string? m) => m switch
    {
        "LastInputWins" => SocdMode.LastInputWins,
        "Neutral" => SocdMode.Neutral,
        _ => SocdMode.Strafe
    };

    // A typed key character to its virtual-key code, or 0 for blank/invalid.
    private static int Vk(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        var parsed = MacroStore.ParseKeys(key.Trim());
        return parsed.HasValue && parsed.Value.Keys.Length > 0 ? parsed.Value.Keys[0] : 0;
    }

    private void NavSocd_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavSocd, PageSocd, "SOCD", "Strafe or null-cancel opposing movement keys");
        ApplySocdPro();
    }

    private void ApplySocdPro()
    {
        bool pro = IsPro;

        // The whole tab is Pro-only: it's hidden from the sidebar unless licensed.
        if (NavSocd is not null) NavSocd.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (SocdLockNote is not null) SocdLockNote.Visibility = Visibility.Collapsed;
        if (SocdProPanel is not null) SocdProPanel.IsEnabled = pro;

        if (!pro && PageSocd is { Visibility: Visibility.Visible })
            ShowPage(NavClicker, PageClicker, "Clicker", "Configure your own click engine");

        ApplySocdState();
    }

    // Pushes the current settings into the hook and starts/stops it to match
    // "Pro and enabled".
    private void ApplySocdState()
    {
        _socd.Mode = _socdMode;
        _socd.SetStrafeInterval(_socdSpeedMs);
        _socd.Configure(Vk(_socdLeft), Vk(_socdRight), Vk(_socdUp), Vk(_socdDown));

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

    private void SocdMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingSocd) return;
        if (sender is RadioButton { Tag: string tag }) _socdMode = ParseMode(tag);
        _settingsDirty = true;
        ApplySocdState();
    }

    private void SocdSpeed_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingSocd || SocdSpeedBox is null) return;
        if (int.TryParse(SocdSpeedBox.Text, out int ms))
        {
            _socdSpeedMs = Math.Clamp(ms, SocdHook.MinStrafeMs, SocdHook.MaxStrafeMs);
            _settingsDirty = true;
            ApplySocdState();
        }
    }

    private void SocdKeys_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loadingSocd || SocdLeftKey is null) return;
        _socdLeft = SocdLeftKey.Text.Trim();
        _socdRight = SocdRightKey.Text.Trim();
        _socdUp = SocdUpKey.Text.Trim();
        _socdDown = SocdDownKey.Text.Trim();
        _settingsDirty = true;
        ApplySocdState();
    }
}
