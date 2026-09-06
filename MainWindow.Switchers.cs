using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The auto switcher page, which is a list rather than one card.
/// </summary>
/// <remarks>
/// There was a single switcher configured in place. People run more than one
/// setup — sword and bow in a fight, pickaxe and gumdrop while gathering — and
/// retyping the numbers between them is what this removes.
///
/// Several run at once because each resolves to its own engine name. The single
/// switcher relied on one reserved name, which is exactly the thing that had to
/// stop being true.
/// </remarks>
public partial class MainWindow
{
    private readonly List<SwitcherProfile> _switcherList = new();

    /// <summary>The switcher whose hotkey is being rebound, by name.</summary>
    private string? _rebindingSwitcher;

    /// <summary>The hotkey picked on the NEW SWITCHER form, before it is saved.</summary>
    private HotkeyBinding _pendingNewSwitcherHotkey = HotkeyBinding.Unbound;

    /// <summary>
    /// Loads the saved switchers, carrying the old single one across the first
    /// time.
    /// </summary>
    private void LoadSwitchers(AppSettings settings)
    {
        _switcherList.Clear();

        if (SwitcherStore.Exists())
        {
            _switcherList.AddRange(SwitcherStore.Load());
        }
        else
        {
            // The hotkey stays on the page rather than moving onto the entry.
            // It already toggles the switcher from in game, and taking a
            // working binding away in exchange for a feature is the trade this
            // migration exists to avoid.
            SwitcherProfile carried = SwitcherStore.FromSingle(settings, HotkeyBinding.Unbound);

            if (carried.IsUsable)
            {
                _switcherList.Add(carried);
                SwitcherStore.Save(_switcherList);
            }
        }

        BuildSwitcherCards();
    }

    private double ClickPeriodMs => _settings.Timing.PeriodMs;

    /// <summary>Whether a switcher is allowed to run right now.</summary>
    private bool SwitcherLive(SwitcherProfile profile) =>
        profile.Enabled && HotkeysEnabledToggle?.IsChecked != false;

    private bool IsSwitcherRunning(SwitcherProfile profile) =>
        _macros.IsRunning(profile.EngineName);

    // ---- running ----

    private void StartSwitcher(SwitcherProfile profile)
    {
        KeyMacro? macro = profile.ToMacro(ClickPeriodMs);

        if (macro == null) return;

        _macros.Start(macro);
        _switcherStartedAt = _macros.Sent;
        _macroTicker.Start();
    }

    private void StopSwitcher(SwitcherProfile profile) => _macros.Stop(profile.EngineName);

    /// <summary>Stops every switcher, leaving the macros alone.</summary>
    private void StopAllSwitchers()
    {
        foreach (SwitcherProfile profile in _switcherList) StopSwitcher(profile);
    }

    private void ToggleSwitcher(SwitcherProfile profile)
    {
        if (IsSwitcherRunning(profile)) StopSwitcher(profile);
        else if (SwitcherLive(profile)) StartSwitcher(profile);

        BuildSwitcherCards();
    }

    /// <summary>Answers a switcher's own hotkey.</summary>
    private void OnSwitcherProfileHotkey(string name)
    {
        SwitcherProfile? profile = _switcherList
            .FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile == null || !SwitcherLive(profile)) return;

        // The same guard the click keys carry: bound to a letter or digit, this
        // would otherwise fire while its own boxes are being typed into.
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        ToggleSwitcher(profile);
    }

    // ---- the list ----

    private void SaveSwitchers()
    {
        SwitcherStore.Save(_switcherList);
        BuildSwitcherCards();
    }

    private void BuildSwitcherCards()
    {
        if (SwitcherList == null) return;

        SwitcherList.Children.Clear();

        foreach (SwitcherProfile profile in _switcherList)
            SwitcherList.Children.Add(SwitcherCard(profile));

        if (SwitcherEmptyText != null)
            SwitcherEmptyText.Visibility =
                _switcherList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshDisableAllSwitchers();
    }

    /// <summary>
    /// One switcher card: what it swaps between, its timing, and a switch.
    /// </summary>
    /// <remarks>
    /// Built in code rather than as a template, like the macro cards, because
    /// whether one is running is a fact about the process rather than a
    /// property of the profile.
    /// </remarks>
    private Border SwitcherCard(SwitcherProfile profile)
    {
        bool hotkeysOn = HotkeysEnabledToggle?.IsChecked != false;
        bool live = hotkeysOn && profile.Enabled;

        var body = new StackPanel { Opacity = live ? 1.0 : 0.45 };

        var header = new DockPanel { LastChildFill = true };

        var toggle = new CheckBox
        {
            IsChecked = IsSwitcherRunning(profile),
            IsEnabled = live && profile.IsUsable,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };

        toggle.Checked += (_, _) => ToggleSwitcher(profile);
        toggle.Unchecked += (_, _) => ToggleSwitcher(profile);

        DockPanel.SetDock(toggle, Dock.Right);
        header.Children.Add(toggle);

        header.Children.Add(new TextBlock
        {
            Text = profile.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        body.Children.Add(header);

        body.Children.Add(new TextBlock
        {
            Text = profile.Summary(),
            FontSize = 11,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = (Brush)FindResource("Accent")
        });

        // The raise is shown rather than applied silently: a hold shorter than
        // the equip animation draws the weapon and swaps away before it fires.
        int effective = profile.EffectiveFirstHoldMs(ClickPeriodMs);

        if (profile.IsUsable && effective > profile.HoldFirstMs)
        {
            body.Children.Add(new TextBlock
            {
                Text = $"First hold raised to {effective} ms so a click still lands.",
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("TextMuted")
            });
        }

        string? problem = profile.Problem();

        if (problem != null)
        {
            body.Children.Add(new TextBlock
            {
                Text = problem,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = (Brush)FindResource("Accent")
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = "HOTKEY",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 12, 0, 4),
            Foreground = (Brush)FindResource("TextMuted")
        });

        // The key and its cross on one row. The cross only exists when there is
        // a key to take off, the same as a macro card - a switcher with no
        // hotkey shows the button alone rather than a cross that does nothing.
        var hotkeyRow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Left };

        if (profile.Hotkey.IsValid)
        {
            var clear = new Button
            {
                Style = (Style)FindResource("ClearBindingButton"),
                Opacity = live ? 1.0 : 0.45,
                ToolTip = $"Unbind {profile.Hotkey.Name} from {profile.Name}"
            };

            clear.Click += (_, _) =>
            {
                // A rebind left armed elsewhere would swallow the next key.
                if (_rebinding != RebindTarget.None) CancelRebind();

                ApplySwitcherBinding(profile.Name, HotkeyBinding.Unbound);
            };

            DockPanel.SetDock(clear, Dock.Right);
            hotkeyRow.Children.Add(clear);
        }

        var hotkey = new Button
        {
            Content = profile.Hotkey.IsValid ? profile.Hotkey.Name : "Not set",
            Height = 30,
            MinWidth = 88,
            Padding = new Thickness(10, 0, 10, 0),
            FontWeight = FontWeights.Bold
        };

        hotkey.Click += (_, _) => BeginSwitcherRebind(profile, hotkey);

        hotkeyRow.Children.Add(hotkey);
        body.Children.Add(hotkeyRow);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };

        var enable = new Button
        {
            Content = profile.Enabled ? "Disable" : "Enable",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            Margin = new Thickness(0, 0, 10, 0)
        };

        enable.Click += (_, _) => SetSwitcherEnabled(profile, !profile.Enabled);
        actions.Children.Add(enable);

        var delete = new Button
        {
            Content = "Delete",
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11
        };
        delete.Click += (_, _) => DeleteSwitcher(profile);
        actions.Children.Add(delete);

        body.Children.Add(actions);

        var card = new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Outline"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 12, 12),
            Width = 300,
            Child = body
        };

        return card;
    }

    // ---- editing ----

    private void SetSwitcherEnabled(SwitcherProfile profile, bool enabled)
    {
        int at = _switcherList.IndexOf(profile);
        if (at < 0) return;

        HotkeyBinding hotkey = profile.Hotkey;
        HotkeyClaim? taken = null;

        // Waking onto a key something else took while this slept would have one
        // press fire two things, so the one waking gives the key up.
        if (enabled && hotkey.IsValid)
        {
            taken = HotkeyClaims.WouldCollideOnWaking(
                HotkeyClaims.Except(Claims(), profile.Name), hotkey.VirtualKey);

            if (taken != null) hotkey = HotkeyBinding.Unbound;
        }

        if (!enabled) StopSwitcher(profile);

        _switcherList[at] = profile with { Enabled = enabled, Hotkey = hotkey };

        SaveSwitchers();

        if (taken != null)
            ShowMacroKeyLost(profile.Name, profile.Hotkey.Name, taken.Value.Name);
    }

    private void DeleteSwitcher(SwitcherProfile profile)
    {
        StopSwitcher(profile);
        _switcherList.Remove(profile);
        SaveSwitchers();
    }

    private void SaveSwitcher_Click(object sender, RoutedEventArgs e)
    {
        string name = NewSwitcherName.Text.Trim();

        if (name.Length == 0) name = SwitcherStore.UnusedName(_switcherList);

        var profile = new SwitcherProfile(
            name,
            NewSwitcherSlotA.Text.Trim(),
            NewSwitcherSlotB.Text.Trim(),
            MacroStore.ParseInterval(NewSwitcherHoldA.Text) ?? 0,
            MacroStore.ParseInterval(NewSwitcherHoldB.Text) ?? 0,
            MacroStore.ParseInterval(NewSwitcherEquip.Text) ?? KeyMacro.DefaultEquipMs,
            _pendingNewSwitcherHotkey);

        string? problem = profile.Problem();

        if (problem != null)
        {
            NewSwitcherStatus.Text = problem;
            return;
        }

        SwitcherStore.Upsert(_switcherList, profile);
        SaveSwitchers();

        NewSwitcherStatus.Text = $"Saved {profile.Name}.";

        NewSwitcherName.Clear();
        NewSwitcherSlotA.Clear();
        NewSwitcherSlotB.Clear();

        ApplySwitcherBinding(null, HotkeyBinding.Unbound);
    }

    /// <summary>Takes the pending key off the NEW SWITCHER form.</summary>
    private void ClearNewSwitcherKey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding != RebindTarget.None) CancelRebind();

        ApplySwitcherBinding(null, HotkeyBinding.Unbound);
    }

    // ---- the page-level switch ----

    private void RefreshDisableAllSwitchers()
    {
        if (SwitcherHotkeyKill == null || SwitcherHotkeyKillNote == null) return;

        bool any = _switcherList.Any(s => s.Enabled);

        SwitcherHotkeyKill.Content = any ? "DISABLE ALL" : "ENABLE ALL";
        SwitcherHotkeyKill.IsEnabled = _switcherList.Count > 0;

        SwitcherHotkeyKillNote.Text = _switcherList.Count == 0
            ? "No switchers yet. Add one below."
            : any
                ? "Switch every auto switcher off at once, without losing what it is set to."
                : "Every auto switcher is switched off. Their keys are free for anything else to use.";

        if (any) SwitcherHotkeyKill.ClearValue(ForegroundProperty);
        else SwitcherHotkeyKill.SetResourceReference(ForegroundProperty, "Accent");
    }

    /// <summary>Switches every auto switcher off, or back on again.</summary>
    private void DisableAllSwitchers_Click(object sender, RoutedEventArgs e)
    {
        if (_switcherList.Count == 0) return;

        bool turningOff = _switcherList.Any(s => s.Enabled);
        var lost = new List<string>();

        for (int i = 0; i < _switcherList.Count; i++)
        {
            SwitcherProfile profile = _switcherList[i];

            if (profile.Enabled == !turningOff) continue;

            HotkeyBinding hotkey = profile.Hotkey;

            if (!turningOff && hotkey.IsValid)
            {
                HotkeyClaim? taken = HotkeyClaims.WouldCollideOnWaking(
                    HotkeyClaims.Except(Claims(), profile.Name), hotkey.VirtualKey);

                if (taken != null)
                {
                    lost.Add($"{profile.Name} lost {hotkey.Name} to {taken.Value.Name}");
                    hotkey = HotkeyBinding.Unbound;
                }
            }

            _switcherList[i] = profile with { Enabled = !turningOff, Hotkey = hotkey };
        }

        if (turningOff) StopAllSwitchers();

        SaveSwitchers();

        if (lost.Count > 0)
        {
            AppDialog.Show(this, "Hotkeys taken",
                "These were claimed while their switchers were switched off:\n\n"
                + string.Join("\n", lost)
                + "\n\nGive them new keys, or take the old ones back.");
        }
    }

    /// <summary>The global keybind on this page: starts and stops all of them.</summary>
    private void OnSwitcherHotkey()
    {
        if (IsActive && Keyboard.FocusedElement is TextBox) return;

        bool anyRunning = _switcherList.Any(IsSwitcherRunning);

        if (anyRunning)
        {
            StopAllSwitchers();
        }
        else
        {
            foreach (SwitcherProfile profile in _switcherList.Where(SwitcherLive))
                StartSwitcher(profile);
        }

        BuildSwitcherCards();
    }

    // ---- rebinding ----

    private void BeginSwitcherRebind(SwitcherProfile profile, Button button)
    {
        if (_rebinding == RebindTarget.SwitcherProfile && _rebindingSwitcher == profile.Name)
        {
            CancelRebind();
            return;
        }

        _rebindingSwitcher = profile.Name;
        _rebindingMacroButton = button;

        BeginRebind(RebindTarget.SwitcherProfile);
    }

    private void NewSwitcherHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_rebinding == RebindTarget.SwitcherProfile && _rebindingSwitcher == null)
        {
            CancelRebind();
            return;
        }

        _rebindingSwitcher = null;
        _rebindingMacroButton = NewSwitcherHotkeyButton;

        BeginRebind(RebindTarget.SwitcherProfile);
    }

    /// <summary>Puts a captured key on the switcher that was being rebound.</summary>
    private void ApplySwitcherBinding(string? name, HotkeyBinding binding)
    {
        if (name == null)
        {
            _pendingNewSwitcherHotkey = binding;
            NewSwitcherHotkeyButton.Content = binding.IsValid ? binding.Name : "Not set";

            ClearNewSwitcherKey.Visibility =
                binding.IsValid ? Visibility.Visible : Visibility.Collapsed;

            return;
        }

        int at = _switcherList.FindIndex(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        if (at < 0) return;

        _switcherList[at] = _switcherList[at] with { Hotkey = binding };
        SaveSwitchers();
    }

    /// <summary>What the switchers currently claim, for the collision check.</summary>
    private IEnumerable<HotkeyClaim> SwitcherClaims() =>
        _switcherList
            .Where(s => s.Hotkey.IsValid)
            .Select(s => new HotkeyClaim(s.Name, s.Hotkey.VirtualKey, s.Enabled));

    /// <summary>The switcher hotkeys the poll thread has to watch.</summary>
    private (string Name, int VirtualKey)[] SwitcherHotkeys() =>
        _switcherList
            .Where(s => s.Enabled && s.Hotkey.IsValid)
            .Select(s => (s.Name, s.Hotkey.VirtualKey))
            .ToArray();

    /// <summary>The text under the list while something is running.</summary>
    private void RefreshSwitcherCount()
    {
        if (SwitcherCountText == null) return;

        int running = _switcherList.Count(IsSwitcherRunning);

        if (running == 0)
        {
            SwitcherCountText.Text = "";
            return;
        }

        long swaps = _macros.Sent - _switcherStartedAt;

        SwitcherCountText.Text = swaps == 0
            ? "Waiting — nothing sent yet. Switch to the game and it starts."
            : $"{swaps:N0} presses sent"
              + (running > 1 ? $", {running} switchers running." : ".");
    }
}
