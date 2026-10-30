using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace JinxyClicker;

/// <summary>
/// The App Targeting page: pick which apps the clicker may click over. The
/// engine side lives in <see cref="AppTargeting"/> and the ClickLoop guard; this
/// is only the page — mode, the editable list for that mode, a one-click tick
/// list of running apps, and a live readout of what's under the pointer.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _appTargetTimer;

    private void NavAppTargeting_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(NavAppTargeting, PageAppTargeting, "App Targeting",
                 "Click only where you want — pick which apps the clicker clicks over");

        RefreshRunningApps();
        RefreshAppTargetingUi();

        // A light live readout of the app under the pointer, only while the page
        // is up. Lazily created, and it no-ops whenever the page is hidden.
        _appTargetTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _appTargetTimer.Tick -= AppTargetPointer_Tick;
        _appTargetTimer.Tick += AppTargetPointer_Tick;
        _appTargetTimer.Start();
    }

    private void LoadAppTargeting(AppSettings s)
    {
        _clickTargetMode = (ClickTargetMode)Math.Clamp(s.ClickTargetMode, 0, 2);
        _clickWhitelist = s.ClickWhitelist ?? new List<string>();
        _clickBlacklist = s.ClickBlacklist ?? new List<string>();

        // Reflect the saved mode on the segment without re-saving.
        RadioButton target = _clickTargetMode switch
        {
            ClickTargetMode.Whitelist => AppTargetWhitelist,
            ClickTargetMode.Blacklist => AppTargetBlacklist,
            _ => AppTargetOff
        };
        if (target != null) { _loadingAppTarget = true; target.IsChecked = true; _loadingAppTarget = false; }
    }

    private bool _loadingAppTarget;

    private void AppTargetMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        _clickTargetMode = tag switch
        {
            "Whitelist" => ClickTargetMode.Whitelist,
            "Blacklist" => ClickTargetMode.Blacklist,
            _ => ClickTargetMode.Off
        };

        RefreshAppTargetingUi();
        if (_loadingAppTarget) return;   // applying a saved value, not a user change

        SaveAppSettings();
        UpdateEngineSettings();
    }

    /// <summary>The list the current mode edits, or null when Off.</summary>
    private List<string>? ActiveTargetList => _clickTargetMode switch
    {
        ClickTargetMode.Whitelist => _clickWhitelist,
        ClickTargetMode.Blacklist => _clickBlacklist,
        _ => null
    };

    private void AppTargetAdd_Click(object sender, RoutedEventArgs e) => AddTypedApp();

    private void AppTargetAddBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { AddTypedApp(); e.Handled = true; }
    }

    private void AddTypedApp()
    {
        if (AppTargetAddBox == null) return;
        string? name = AppTargeting.Normalize(AppTargetAddBox.Text);
        List<string>? list = ActiveTargetList;
        if (name == null || list == null) return;

        if (!list.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
            list.Add(name);

        AppTargetAddBox.Text = "";
        CommitAppTargeting();
    }

    private void RemoveApp(string exe)
    {
        ActiveTargetList?.RemoveAll(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
        CommitAppTargeting();
    }

    private void ToggleApp(string exe, bool on)
    {
        List<string>? list = ActiveTargetList;
        if (list == null) return;

        bool has = list.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
        if (on && !has) list.Add(exe);
        else if (!on && has) list.RemoveAll(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));

        CommitAppTargeting();
    }

    // Save, push to the engine, then redraw the chips and running-app ticks.
    private void CommitAppTargeting()
    {
        SaveAppSettings();
        UpdateEngineSettings();
        RefreshAppTargetingUi();
        RefreshRunningTicks();
    }

    private void AppTargetRefresh_Click(object sender, RoutedEventArgs e) => RefreshRunningApps();

    // ---- drawing ----

    private void RefreshAppTargetingUi()
    {
        if (AppTargetExplain == null) return;

        List<string>? list = ActiveTargetList;
        bool off = list == null;

        AppTargetExplain.Text = _clickTargetMode switch
        {
            ClickTargetMode.Whitelist => "Whitelist — the clicker clicks only while the pointer is over one of these apps.",
            ClickTargetMode.Blacklist => "Blacklist — the clicker clicks everywhere except while the pointer is over one of these apps.",
            _ => "Off — the clicker clicks everywhere, over any app."
        };

        AppTargetActiveCard.IsEnabled = !off;
        AppTargetActiveCard.Opacity = off ? 0.55 : 1.0;
        AppTargetActiveHeading.Text = _clickTargetMode == ClickTargetMode.Blacklist ? "BLACKLIST"
            : _clickTargetMode == ClickTargetMode.Whitelist ? "WHITELIST" : "APPS";

        int n = list?.Count ?? 0;
        AppTargetActiveCount.Text = n == 1 ? "1 app" : $"{n} apps";
        AppTargetEmpty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;

        AppTargetChips.Children.Clear();
        if (list != null)
            foreach (string exe in list)
                AppTargetChips.Children.Add(BuildChip(exe));

        RefreshRunningTicks();
    }

    private UIElement BuildChip(string exe)
    {
        var text = new TextBlock { Text = exe, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var remove = new Button
        {
            Content = "✕", FontSize = 11, Width = 20, Height = 20, Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Remove"
        };
        remove.Click += (_, _) => RemoveApp(exe);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(text);
        row.Children.Add(remove);

        return new Border
        {
            Background = (Brush)FindResource("Control"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(10, 5, 8, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Child = row
        };
    }

    // Snapshot of apps that currently own a visible window, deduped by exe.
    private List<string> _runningApps = new();

    private void RefreshRunningApps()
    {
        string self = AppTargeting.Normalize(Process.GetCurrentProcess().ProcessName + ".exe") ?? "";
        var seen = new HashSet<string>();
        var apps = new List<string>();

        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                string? exe = AppTargeting.Normalize(p.ProcessName + ".exe");
                if (exe == null || exe == self) continue;
                if (seen.Add(exe)) apps.Add(exe);
            }
            catch { /* process exited or access denied — skip it */ }
            finally { p.Dispose(); }
        }

        apps.Sort(StringComparer.OrdinalIgnoreCase);
        _runningApps = apps;
        DrawRunningApps();
    }

    private void DrawRunningApps()
    {
        if (RunningAppsList == null) return;
        RunningAppsList.Children.Clear();

        if (_runningApps.Count == 0)
        {
            RunningAppsList.Children.Add(new TextBlock
            {
                Text = "No windowed apps found. Press Refresh after opening Roblox.",
                Foreground = (Brush)FindResource("TextMuted"), FontSize = 12, TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        List<string>? list = ActiveTargetList;
        foreach (string exe in _runningApps)
        {
            var cb = new CheckBox
            {
                Content = exe,
                Margin = new Thickness(0, 4, 0, 4),
                IsEnabled = list != null,
                IsChecked = list != null && list.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)),
                Tag = exe
            };
            cb.Checked += RunningApp_Toggled;
            cb.Unchecked += RunningApp_Toggled;
            RunningAppsList.Children.Add(cb);
        }
    }

    private bool _settingRunningTicks;

    private void RunningApp_Toggled(object sender, RoutedEventArgs e)
    {
        if (_settingRunningTicks) return;
        if (sender is CheckBox { Tag: string exe } cb)
            ToggleApp(exe, cb.IsChecked == true);
    }

    // Re-sync the running-app checkboxes with the list without firing handlers.
    private void RefreshRunningTicks()
    {
        if (RunningAppsList == null) return;
        List<string>? list = ActiveTargetList;

        _settingRunningTicks = true;
        foreach (object child in RunningAppsList.Children)
            if (child is CheckBox { Tag: string exe } cb)
            {
                cb.IsEnabled = list != null;
                cb.IsChecked = list != null && list.Any(x => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase));
            }
        _settingRunningTicks = false;
    }

    private void AppTargetPointer_Tick(object? sender, EventArgs e)
    {
        if (PageAppTargeting is not { IsVisible: true })
        {
            _appTargetTimer?.Stop();
            return;
        }

        string? exe = PointerApp.ExeUnderCursor();
        bool allow = AppTargeting.Allows(_clickTargetMode,
            _clickTargetMode == ClickTargetMode.Whitelist ? AppTargeting.Set(_clickWhitelist)
            : _clickTargetMode == ClickTargetMode.Blacklist ? AppTargeting.Set(_clickBlacklist)
            : EmptyApps, exe);

        string verdict = _clickTargetMode == ClickTargetMode.Off ? "clicks here"
            : allow ? "would click here" : "won't click here";
        AppTargetPointer.Text = $"Under the pointer: {exe ?? "unknown"}  —  {verdict}";
    }
}
