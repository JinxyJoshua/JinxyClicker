using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        if (IsPro && RecentGamesPanel is not null && RecentGamesPanel.Children.Count == 0)
            _ = LoadRecentGamesAsync();
    }

    private void ApplySetupsPro()
    {
        bool pro = IsPro;
        if (SetupsLockNote is not null) SetupsLockNote.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
        if (SetupsProPanel is not null) SetupsProPanel.IsEnabled = pro;
        if (SetupsListCard is not null) SetupsListCard.IsEnabled = pro;
        if (RecentGamesCard is not null) RecentGamesCard.IsEnabled = pro;
    }

    // ---- recently played games ----

    private void RefreshRecent_Click(object sender, RoutedEventArgs e)
    {
        if (RecentGamesPanel is not null) RecentGamesPanel.Children.Clear();
        _ = LoadRecentGamesAsync();
    }

    private async System.Threading.Tasks.Task LoadRecentGamesAsync()
    {
        if (RecentGamesPanel is null) return;
        if (RecentGamesStatus is not null) RecentGamesStatus.Text = "Reading your recent games…";

        IReadOnlyList<RecentGame> games;
        try { games = await RecentGames.LoadAsync(); }
        catch { games = Array.Empty<RecentGame>(); }

        RecentGamesPanel.Children.Clear();
        foreach (RecentGame g in games) RecentGamesPanel.Children.Add(BuildGameTile(g));

        if (RecentGamesStatus is not null)
            RecentGamesStatus.Text = games.Count == 0
                ? "No recent games found yet — join one in Roblox, then hit Refresh."
                : "";
    }

    private FrameworkElement BuildGameTile(RecentGame game)
    {
        var thumb = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("Sidebar"),
            ClipToBounds = true
        };
        if (!string.IsNullOrEmpty(game.IconUrl))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(game.IconUrl);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 152;
                bmp.EndInit();
                thumb.Background = new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
            }
            catch { /* leave the placeholder background */ }
        }

        var name = new TextBlock
        {
            Text = game.Name,
            Foreground = (Brush)FindResource("TextBright"),
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 30,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var stack = new StackPanel { Width = 92, Children = { thumb, name } };

        var tile = new Border
        {
            Padding = new Thickness(6, 8, 6, 8),
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("Outline"),
            Background = (Brush)FindResource("Panel"),
            Cursor = Cursors.Hand,
            Child = stack,
            ToolTip = $"Play {game.Name}"
        };
        tile.MouseEnter += (_, _) => tile.BorderBrush = (Brush)FindResource("Accent");
        tile.MouseLeave += (_, _) => tile.BorderBrush = (Brush)FindResource("Outline");
        tile.MouseLeftButtonUp += (_, _) =>
        {
            if (PlayUrlBox is not null) PlayUrlBox.Text = game.PlaceId.ToString();
            RunPlay();
        };
        return tile;
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
        // Only switch crosshair if the setup's one still exists — a deleted one is
        // left alone rather than silently swapped for a fallback.
        if (CrosshairNameExists(setup.CrosshairName))
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

    private void Play_Click(object sender, RoutedEventArgs e) => RunPlay();

    /// <summary>True when the app was launched from the "Jinxy Play" desktop shortcut.</summary>
    private static bool LaunchedWithPlay() =>
        Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--play", StringComparison.OrdinalIgnoreCase));

    private void RunPlay()
    {
        if (!IsPro) { SetSetupsStatus("Play is a Pro feature — activate a key on the PRO page."); return; }

        SetupProfile? active = SetupProfiles.Find(_setups, _activeSetup);
        if (active is not null) ApplySetup(active);

        // Write the crosshair into Roblox's cursor files (flags already wrote to
        // disk in ApplySetup). Best-effort — a missing install must not block this.
        try { if (!IsDefaultSelected) ApplyCurrentCrosshair(); } catch { }

        // Launch straight into the game via Roblox's universal deep link, which the
        // desktop client resolves with its own signed-in session. Needs the Roblox
        // app signed in; no login handling here.
        string? launch = SetupProfiles.ResolvePlayLaunch(PlayUrlBox?.Text);
        if (launch is null)
        {
            SetSetupsStatus(active is not null
                ? $"Applied “{active.Name}”. Add your game link or ID above to launch straight in."
                : "Applied your current look. Add your game link or ID above to launch straight in.");
            return;
        }

        try { Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true }); }
        catch { SetSetupsStatus("Applied your setup, but couldn't launch Roblox — open it yourself."); return; }

        SetSetupsStatus(active is not null
            ? $"Applied “{active.Name}” and launched your game. (Sign into the Roblox app for it to join.)"
            : "Applied your look and launched your game. (Sign into the Roblox app for it to join.)");
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

    /// <summary>
    /// A later launch reached the running copy: bring this window to the front, and
    /// if it came from the Play shortcut, run Play.
    /// </summary>
    public void OnSecondInstance(string message)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;   // nudge above whatever had focus, then release
        Topmost = false;
        Focus();

        if (string.Equals(message, "play", StringComparison.OrdinalIgnoreCase)) RunPlay();
    }

    // Writes a "Jinxy Play" shortcut to the desktop that relaunches the app with
    // --play, so a double-click applies the active setup and opens the game. Uses
    // the Windows Script Host COM object so no extra package is needed.
    private void CreatePlayShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) return;

        try
        {
            string exe = Process.GetCurrentProcess().MainModule!.FileName;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = System.IO.Path.Combine(desktop, "Jinxy Play.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) { SetSetupsStatus("Couldn't create the shortcut on this system."); return; }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            shortcut.TargetPath = exe;
            shortcut.Arguments = "--play";
            shortcut.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "Launch Jinxy and play with your active setup";
            shortcut.Save();

            SetSetupsStatus("Added “Jinxy Play” to your desktop. Double-click it to apply your setup and open your game.");
        }
        catch (Exception ex)
        {
            SetSetupsStatus("Couldn't create the shortcut: " + ex.Message);
        }
    }
}
