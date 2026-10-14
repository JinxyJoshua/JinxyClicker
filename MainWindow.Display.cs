using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
        if (DisplayModeBox is null) return;
        _loadingDisplay = true;
        try
        {
            if (DisplayModeBox.Items.Count == 0)
                foreach (DisplayMode m in DisplaySettings.SupportedModes())
                    DisplayModeBox.Items.Add(m);

            DisplayMode? want = _displayMode ?? DisplaySettings.Current();
            if (want is { } w)
                DisplayModeBox.SelectedItem = DisplayModeBox.Items.Cast<DisplayMode>().FirstOrDefault(m => m == w);
            if (DisplayModeBox.SelectedItem is null && DisplayModeBox.Items.Count > 0)
                DisplayModeBox.SelectedIndex = 0;

            if (DisplayEnableCheck is not null) DisplayEnableCheck.IsChecked = _displayEnabled;

            // "Your display" summary: the current desktop mode and monitor count.
            DisplayMode? cur = DisplaySettings.Current();
            if (DisplayCurrentText is not null) DisplayCurrentText.Text = cur?.ToString() ?? "—";
            if (DisplayMonitorsText is not null)
            {
                int monitors = 0;
                try { monitors = Displays.All().Count; } catch { }
                DisplayMonitorsText.Text = monitors <= 0 ? ""
                    : monitors == 1 ? "1 monitor connected" : monitors + " monitors connected";
            }
        }
        catch { }
        _loadingDisplay = false;
        SetDisplayStatus();
    }

    // Re-reads the monitor's modes and the current desktop mode (e.g. after plugging
    // in a monitor or changing the resolution in Windows).
    private void DisplayRefresh_Click(object sender, RoutedEventArgs e)
    {
        DisplayModeBox?.Items.Clear();   // force a re-enumerate in RefreshDisplayOnShow
        RefreshDisplayOnShow();
    }

    private void DisplayEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingDisplay || DisplayEnableCheck is null) return;
        _displayEnabled = DisplayEnableCheck.IsChecked == true;
        _settingsDirty = true;
        if (!_displayEnabled && _displayApplied) { DisplaySettings.Revert(); _displayApplied = false; }
        SetDisplayStatus();
    }

    private void DisplayMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingDisplay || DisplayModeBox?.SelectedItem is not DisplayMode m) return;
        _displayMode = m;
        _settingsDirty = true;
        if (_displayApplied) DisplaySettings.Apply(m);   // Roblox is open — switch live
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
