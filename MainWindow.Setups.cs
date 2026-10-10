using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// Setups (Pro): a named bundle of the app's whole look-and-feel — crosshair,
/// click preset, FastFlags, theme — that can be saved and re-applied in one tap,
/// plus a Play button that applies the active setup and launches the game.
/// </summary>
public partial class MainWindow
{
    private List<SetupProfile> _setups = new();
    private string? _activeSetup;

    private void LoadSetups(AppSettings s)
    {
        _setups = s.SetupProfiles ?? new List<SetupProfile>();
        _activeSetup = s.ActiveSetup;
        if (PlayUrlBox is not null) PlayUrlBox.Text = s.PlayGameUrl ?? "";
        RefreshSetupsList();
    }

    private void NavSetups_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavSetups, PageSetups, "Setups", "Save and switch your whole configuration");
        ApplySetupsPro();
        RefreshSetupsList();
    }

    private void ApplySetupsPro()
    {
        bool pro = IsPro;
        if (SetupsLockNote is not null) SetupsLockNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        if (SetupsProPanel is not null) SetupsProPanel.IsEnabled = pro;
        if (SetupsListCard is not null) SetupsListCard.IsEnabled = pro;
    }

    private void PlayUrl_Changed(object sender, TextChangedEventArgs e) => _settingsDirty = true;

    /// <summary>A setup capturing the app's current crosshair, clicker, flags, and theme.</summary>
    private SetupProfile CurrentSetup(string name) => new()
    {
        Name = name,
        CrosshairName = _crosshairName,
        ClickPresetName = _selectedPreset?.Name,
        FastFlagsOn = FastFlagStore.IsApplied(FastFlagStore.FpsBoost),
        ThemeMode = ThemeLight?.IsChecked == true ? "Light" : "Dark",
        AccentColor = _accentHex
    };

    private void SetupSave_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        string? name = SetupProfiles.CleanName(SetupNameBox?.Text);
        if (name is null) { SetSetupsStatus("Type a name for this setup first."); return; }

        SetupProfiles.Save(_setups, CurrentSetup(name));
        _activeSetup = name;
        if (SetupNameBox is not null) SetupNameBox.Text = "";
        SaveAppSettings();
        RefreshSetupsList();
        SetSetupsStatus($"Saved “{name}”.");
    }

    // Re-drives each subsystem to match the setup. A piece that no longer exists
    // (a deleted crosshair or preset) is simply skipped.
    private void ApplySetup(SetupProfile setup)
    {
        if (!string.IsNullOrWhiteSpace(setup.CrosshairName))
            SelectCrosshair(setup.CrosshairName!, switched: false);

        if (!string.IsNullOrWhiteSpace(setup.ClickPresetName))
        {
            ClickPreset? preset = _clickPresets.FirstOrDefault(p =>
                string.Equals(p.Name, setup.ClickPresetName, StringComparison.OrdinalIgnoreCase));
            if (preset is not null) { ApplyProfile(preset); _selectedPreset = preset; RefreshAppliedPreset(); }
        }

        if (setup.ThemeMode is not null && ThemeLight is not null && ThemeDark is not null)
        {
            if (string.Equals(setup.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase)) ThemeLight.IsChecked = true;
            else ThemeDark.IsChecked = true;
        }
        if (!string.IsNullOrWhiteSpace(setup.AccentColor)) SelectAccent(setup.AccentColor);

        try
        {
            if (setup.FastFlagsOn) { FastFlagStore.Backup(); FastFlagStore.Apply(FastFlagStore.FpsBoost); }
            else FastFlagStore.Reset(FastFlagStore.FpsBoost);
            RefreshFlags();
        }
        catch { /* flags are best-effort; a missing Roblox install is not fatal */ }

        _activeSetup = setup.Name;
        SaveAppSettings();
        RefreshSetupsList();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        SetupProfile? active = SetupProfiles.Find(_setups, _activeSetup);
        if (active is not null) ApplySetup(active);

        // Write the crosshair into Roblox's cursor files (flags already wrote to
        // disk in ApplySetup). Best-effort — a missing install must not block Play.
        try { if (!IsDefaultSelected) ApplyCurrentCrosshair(); } catch { }

        string url = string.IsNullOrWhiteSpace(PlayUrlBox?.Text)
            ? "https://www.roblox.com/games"
            : PlayUrlBox!.Text.Trim();
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { SetSetupsStatus("Couldn't open that game link — check it's a valid URL."); return; }

        SetSetupsStatus(active is not null
            ? $"Applied “{active.Name}” and opened your game. If Roblox was already open, fully close and relaunch it."
            : "Applied your current look and opened your game.");
    }

    private void RefreshSetupsList()
    {
        if (SetupsList is null) return;

        SetupsList.Items.Clear();
        foreach (SetupProfile setup in _setups) SetupsList.Items.Add(BuildSetupRow(setup));

        if (_setups.Count == 0)
            SetSetupsStatus("No setups yet. Set your crosshair, clicker, flags, and theme how you like, then Save current.");
    }

    private FrameworkElement BuildSetupRow(SetupProfile setup)
    {
        bool active = string.Equals(setup.Name, _activeSetup, StringComparison.OrdinalIgnoreCase);
        var dock = new DockPanel { LastChildFill = false };

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = setup.Name + (active ? "   • active" : ""),
            Foreground = (Brush)FindResource(active ? "Accent" : "TextBright"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold
        });
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(setup.CrosshairName)) parts.Add(setup.CrosshairName!);
        if (!string.IsNullOrWhiteSpace(setup.ClickPresetName)) parts.Add(setup.ClickPresetName!);
        parts.Add(setup.FastFlagsOn ? "FPS flags on" : "FPS flags off");
        parts.Add(setup.ThemeMode ?? "Dark");
        info.Children.Add(new TextBlock
        {
            Text = string.Join("  •  ", parts),
            Foreground = (Brush)FindResource("TextMuted"),
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0)
        });
        DockPanel.SetDock(info, Dock.Left);
        dock.Children.Add(info);

        var del = new Button
        {
            Content = "Delete",
            Style = (Style)FindResource("ChipButton"),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(8, 0, 0, 0)
        };
        del.Click += (_, _) =>
        {
            SetupProfiles.Remove(_setups, setup.Name);
            if (string.Equals(_activeSetup, setup.Name, StringComparison.OrdinalIgnoreCase)) _activeSetup = null;
            SaveAppSettings();
            RefreshSetupsList();
            SetSetupsStatus($"Deleted “{setup.Name}”.");
        };
        DockPanel.SetDock(del, Dock.Right);
        dock.Children.Add(del);

        var apply = new Button
        {
            Content = "Apply",
            Style = (Style)FindResource("AccentButton"),
            Padding = new Thickness(16, 0, 16, 0),
            Height = 30,
            FontWeight = FontWeights.Bold
        };
        apply.Click += (_, _) => { ApplySetup(setup); SetSetupsStatus($"Applied “{setup.Name}”."); };
        DockPanel.SetDock(apply, Dock.Right);
        dock.Children.Add(apply);

        return new Border
        {
            Background = (Brush)FindResource("Sidebar"),
            BorderBrush = (Brush)FindResource(active ? "Accent" : "Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = dock
        };
    }

    private void SetSetupsStatus(string text) { if (SetupsStatus is not null) SetupsStatus.Text = text; }
}
