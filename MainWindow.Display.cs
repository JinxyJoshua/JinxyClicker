using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// Display switching (Plus, on its own tab): pick a resolution / refresh rate and
/// it is applied while Roblox is open and reverted when it closes. A background
/// watcher drives the lifecycle, so it works even when you're on another page, and
/// the change is reverted on app exit too (and, because it's a temporary mode, by
/// Windows itself if the app ever dies).
/// </summary>
public partial class MainWindow
{
    private bool _displayEnabled;
    private DisplayMode? _displayMode;
    private bool _displayApplied;
    private bool _loadingDisplay;
    private List<DisplayMode> _supportedModes = new();
    private readonly DispatcherTimer _displayWatch = new() { Interval = TimeSpan.FromSeconds(2) };

    private void InitDisplay(AppSettings s)
    {
        _displayEnabled = s.DisplayEnabled;
        if (s.DisplayWidth > 0 && s.DisplayHeight > 0 && s.DisplayRefresh > 0)
            _displayMode = new DisplayMode(s.DisplayWidth, s.DisplayHeight, s.DisplayRefresh);

        _displayWatch.Tick -= DisplayWatchTick;
        _displayWatch.Tick += DisplayWatchTick;
        _displayWatch.Start();
    }

    private void RefreshDisplayOnShow()
    {
        if (DisplayResBox is null || DisplayRefreshBox is null) return;
        _loadingDisplay = true;
        try
        {
            _supportedModes = DisplaySettings.SupportedModes();

            DisplayResBox.Items.Clear();
            foreach (Resolution r in DisplaySettings.ResolutionsOf(_supportedModes))
                DisplayResBox.Items.Add(r);

            // Pick the saved resolution, else the current desktop one, else the widest.
            DisplayMode? want = _displayMode ?? DisplaySettings.Current();
            Resolution wantRes = want is { } w ? new Resolution(w.Width, w.Height) : default;
            DisplayResBox.SelectedItem = DisplayResBox.Items.Cast<Resolution>().FirstOrDefault(r => r == wantRes);
            if (DisplayResBox.SelectedItem is null && DisplayResBox.Items.Count > 0)
                DisplayResBox.SelectedIndex = 0;

            PopulateRefreshBox(want?.Refresh);

            if (DisplayEnableCheck is not null) DisplayEnableCheck.IsChecked = _displayEnabled;

            // First visit with nothing saved: seed a mode from the boxes so enabling works.
            if (_displayMode is null
                && DisplayResBox.SelectedItem is Resolution r0 && DisplayRefreshBox.SelectedItem is int hz0)
                _displayMode = new DisplayMode(r0.Width, r0.Height, hz0);

            UpdateDisplaySummary();
            BuildDisplayPresets();
            UpdateDisplayRobloxStatus();
        }
        catch { }
        _loadingDisplay = false;
        SetDisplayStatus();
    }

    // One-click Roblox resolution presets, built from what the monitor supports.
    private void BuildDisplayPresets()
    {
        if (DisplayPresets is null) return;
        DisplayPresets.Children.Clear();
        foreach ((Resolution res, string label, string why) in DisplaySettings.PresetsFor(_supportedModes))
            DisplayPresets.Children.Add(MakePresetChip(res, label, why));
    }

    private System.Windows.UIElement MakePresetChip(Resolution res, string label, string why)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(), FontSize = 8, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("Accent"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = res.ToString(), FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBright"), Margin = new Thickness(0, 2, 0, 0),
        });
        var btn = new Button
        {
            Content = stack,
            Style = (Style)FindResource("PresetChip"),
            Margin = new Thickness(0, 0, 8, 8),
            ToolTip = why,
            Tag = res,
        };
        btn.Click += DisplayPreset_Click;
        return btn;
    }

    // Picking a preset selects its resolution and turns the switch on, so it actually
    // takes effect in Roblox with one click.
    private void DisplayPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not Resolution res || DisplayResBox is null) return;

        DisplayResBox.SelectedItem = DisplayResBox.Items.Cast<Resolution>().FirstOrDefault(r => r == res);
        if (DisplayEnableCheck is { IsChecked: not true }) DisplayEnableCheck.IsChecked = true;
    }

    // Live "this drives Roblox" line: open/closed, whether the switch is active, and
    // the in-game FPS when the overlay's FPS reader is running.
    private void UpdateDisplayRobloxStatus()
    {
        if (DisplayRobloxStatus is null) return;

        bool roblox = RobloxIsRunning();
        int? fps = FpsShared.ReadFps();
        string fpsPart = fps is int f && f > 0 ? $"  ·  {f} FPS" : "";

        DisplayRobloxStatus.Text = !roblox ? "Roblox is closed — your picks apply the moment it opens."
            : _displayApplied ? $"Roblox is open — display switched{fpsPart}."
            : _displayEnabled ? $"Roblox is open — switching when you join{fpsPart}."
            : $"Roblox is open{fpsPart}.";

        if (DisplayRobloxDot is not null)
            DisplayRobloxDot.Foreground = (Brush)FindResource(roblox ? "Accent" : "TextMuted");
    }

    // Fills the refresh-rate box with the rates the selected resolution supports,
    // keeping the preferred rate if it's still available, else the highest.
    private void PopulateRefreshBox(int? preferred)
    {
        if (DisplayRefreshBox is null || DisplayResBox?.SelectedItem is not Resolution res) return;
        DisplayRefreshBox.Items.Clear();
        foreach (int hz in DisplaySettings.RefreshesOf(_supportedModes, res.Width, res.Height))
            DisplayRefreshBox.Items.Add(hz);

        if (preferred is int p && DisplayRefreshBox.Items.Contains(p))
            DisplayRefreshBox.SelectedItem = p;
        else if (DisplayRefreshBox.Items.Count > 0)
            DisplayRefreshBox.SelectedIndex = 0;
    }

    // The "your display" hero: the live resolution and a row of stat chips.
    private void UpdateDisplaySummary()
    {
        DisplayMode? cur = DisplaySettings.Current();
        if (DisplayCurrentText is not null)
            DisplayCurrentText.Text = cur is { } c ? $"{c.Width} × {c.Height}" : "—";

        if (DisplayChips is null) return;
        DisplayChips.Children.Clear();
        if (cur is { } m) AddDisplayChip("REFRESH", m.Refresh + " Hz");
        AddDisplayChip("SCALING", DisplaySettings.ScalingPercent() + "%");
        int bits = DisplaySettings.CurrentBits();
        if (bits > 0) AddDisplayChip("COLOUR", bits + "-bit");
        int mon = 0;
        try { mon = Displays.All().Count; } catch { }
        if (mon > 0) AddDisplayChip("MONITORS", mon.ToString());
    }

    private void AddDisplayChip(string label, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 9, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextMuted"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
        });
        row.Children.Add(new TextBlock
        {
            Text = value, FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBright"), VerticalAlignment = VerticalAlignment.Center,
        });
        DisplayChips.Children.Add(new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Outline"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(9, 4, 9, 4),
            Margin = new Thickness(0, 0, 7, 7), Child = row,
        });
    }

    // Re-reads the monitor's modes and the current desktop mode (e.g. after plugging
    // in a monitor or changing the resolution in Windows).
    private void DisplayRefresh_Click(object sender, RoutedEventArgs e) => RefreshDisplayOnShow();

    private void DisplayEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingDisplay || DisplayEnableCheck is null) return;
        _displayEnabled = DisplayEnableCheck.IsChecked == true;
        _settingsDirty = true;
        if (!_displayEnabled && _displayApplied) { DisplaySettings.Revert(); _displayApplied = false; }
        SetDisplayStatus();
    }

    private void DisplayRes_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDisplay) return;
        _loadingDisplay = true;
        PopulateRefreshBox(_displayMode?.Refresh);   // keep the rate if this resolution still has it
        _loadingDisplay = false;
        SyncDisplayModeFromBoxes();
    }

    private void DisplayRate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDisplay) return;
        SyncDisplayModeFromBoxes();
    }

    // Combines the two dropdowns into the mode to apply, and applies it live if Roblox
    // is already running under an active switch.
    private void SyncDisplayModeFromBoxes()
    {
        if (DisplayResBox?.SelectedItem is not Resolution r || DisplayRefreshBox?.SelectedItem is not int hz) return;
        _displayMode = new DisplayMode(r.Width, r.Height, hz);
        _settingsDirty = true;
        if (_displayApplied) DisplaySettings.Apply(_displayMode.Value);
        SetDisplayStatus();
    }

    private void DisplayWatchTick(object? sender, EventArgs e)
    {
        bool roblox = RobloxIsRunning();
        if (_displayEnabled && _displayMode is { } m && roblox && !_displayApplied)
        {
            _displayApplied = DisplaySettings.Apply(m);
        }
        else if (_displayApplied && (!roblox || !_displayEnabled))
        {
            DisplaySettings.Revert();
            _displayApplied = false;
        }

        UpdateDisplayRobloxStatus();   // keep the live Roblox line current
    }

    private static bool RobloxIsRunning()
    {
        try { return System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Length > 0; }
        catch { return false; }
    }

    private void SetDisplayStatus()
    {
        if (DisplayStatus is null) return;
        DisplayStatus.Text = !_displayEnabled ? "Off — your screen stays as it is."
            : _displayApplied ? "Active now — reverts when Roblox closes."
            : "On — switches the moment Roblox opens.";
    }

    /// <summary>Called on app exit so a resolution change never outlives the app.</summary>
    private void RevertDisplayOnExit()
    {
        if (!_displayApplied) return;
        try { DisplaySettings.Revert(); } catch { }
        _displayApplied = false;
    }
}
