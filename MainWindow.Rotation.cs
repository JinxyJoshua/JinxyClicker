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
/// The Auto Switcher page: one rotation chain of key+delay slots, cycled by the
/// page's hotkey. A Toggle/Hold trigger and an optional "stop after N rotations"
/// limit. Replaces the old two-slot swap and the multi-profile list.
/// </summary>
/// <remarks>
/// The rotation runs as a single <see cref="KeyMacro"/> (the slot keys with their
/// per-slot dwells), started and stopped by the page's global switcher hotkey
/// (<c>_hotkeySettings.SwitcherHotkey</c>) — so it shares the key-sending engine,
/// the hotkey poll, the collision check and the rebind flow with the macros.
/// </remarks>
public partial class MainWindow
{
    private readonly List<RotationSlot> _rotationSlots = new();

    // Starts true so the Trigger radio's IsChecked="True" and the field Text, set
    // during XAML parse before LoadRotation runs, do not save an empty chain over
    // the stored one. LoadRotation reads the stored chain and then releases this.
    private bool _loadingRotation = true;

    // ---- load / save ----

    private void LoadRotation(AppSettings s)
    {
        _loadingRotation = true;
        try
        {
            RotationChain chain = RotationChainStore.Exists()
                ? RotationChainStore.Load()
                : MigrateOldSwitcher(s);

            _rotationSlots.Clear();
            _rotationSlots.AddRange(chain.Slots);
            if (_rotationSlots.Count == 0) _rotationSlots.Add(new RotationSlot("", 100));

            if (RotationTriggerHold != null) RotationTriggerHold.IsChecked = chain.Trigger == SwitchTrigger.Hold;
            if (RotationTriggerToggle != null) RotationTriggerToggle.IsChecked = chain.Trigger != SwitchTrigger.Hold;
            if (RotationStopAfter != null)
                RotationStopAfter.Text = chain.StopAfterRotations.ToString(CultureInfo.InvariantCulture);

            BuildRotationSlots();
        }
        finally
        {
            _loadingRotation = false;
        }

        // Persist the migrated chain so next launch loads it rather than migrating again.
        if (!RotationChainStore.Exists()) SaveRotation();
        RefreshRotation();
    }

    /// <summary>The old two-slot swap, as a two-slot rotation, on first run.</summary>
    private static RotationChain MigrateOldSwitcher(AppSettings s)
    {
        var slots = new List<RotationSlot>
        {
            new(s.SwitcherSlotA ?? "", ClampDelay(s.SwitcherIntervalMs)),
            new(s.SwitcherSlotB ?? "", ClampDelay(s.SwitcherIntervalBMs)),
        };
        return new RotationChain(slots, SwitchTrigger.Toggle, 0, Enabled: !s.SwitcherDisabled);
    }

    private static int ClampDelay(int ms) =>
        Math.Clamp(ms, KeyMacro.MinIntervalMs, KeyMacro.MaxIntervalMs);

    private RotationChain CurrentChain()
    {
        SwitchTrigger trigger = RotationTriggerHold?.IsChecked == true ? SwitchTrigger.Hold : SwitchTrigger.Toggle;
        int stop = int.TryParse(RotationStopAfter?.Text?.Trim(), out int n) ? Math.Max(0, n) : 0;
        return new RotationChain(_rotationSlots.ToList(), trigger, stop, Enabled: !_switcherDisabled);
    }

    private void SaveRotation()
    {
        if (_loadingRotation) return;
        _rotation = CurrentChain();
        RotationChainStore.Save(_rotation);
        RefreshRotation();
    }

    private RotationChain _rotation = RotationChain.Empty;

    // ---- the slot rows ----

    private void BuildRotationSlots()
    {
        if (RotationSlots == null) return;

        RotationSlots.Children.Clear();
        for (int i = 0; i < _rotationSlots.Count; i++)
            RotationSlots.Children.Add(RotationSlotRow(i));
    }

    private UIElement RotationSlotRow(int index)
    {
        RotationSlot slot = _rotationSlots[index];

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        row.Children.Add(new TextBlock
        {
            Text = (index + 1) + ".",
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMuted")
        });

        var key = new TextBox
        {
            Text = slot.Key,
            Width = 64,
            MaxLength = 1,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Height = 34
        };
        key.TextChanged += (_, _) =>
        {
            _rotationSlots[index] = _rotationSlots[index] with { Key = key.Text };
            SaveRotation();
        };
        row.Children.Add(key);

        row.Children.Add(new TextBlock
        {
            Text = "Delay ms",
            Margin = new Thickness(14, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextMuted")
        });

        var delay = new TextBox
        {
            Text = slot.DelayMs.ToString(CultureInfo.InvariantCulture),
            Width = 96,
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 34
        };
        delay.TextChanged += (_, _) =>
        {
            int d = int.TryParse(delay.Text?.Trim(), out int v) ? v : _rotationSlots[index].DelayMs;
            _rotationSlots[index] = _rotationSlots[index] with { DelayMs = d };
            SaveRotation();
        };
        row.Children.Add(delay);

        var remove = new Button
        {
            Content = "✕",
            Width = 34,
            Height = 34,
            Margin = new Thickness(14, 0, 0, 0),
            FontWeight = FontWeights.Bold,
            ToolTip = "Remove this slot"
        };
        remove.Click += (_, _) => RemoveSlot(index);
        row.Children.Add(remove);

        return row;
    }

    private void RotationAddSlot_Click(object sender, RoutedEventArgs e)
    {
        _rotationSlots.Add(new RotationSlot("", 100));
        BuildRotationSlots();
        SaveRotation();
    }

    private void RemoveSlot(int index)
    {
        if (index < 0 || index >= _rotationSlots.Count) return;
        _rotationSlots.RemoveAt(index);
        BuildRotationSlots();
        SaveRotation();
    }

    private void RotationTrigger_Changed(object sender, RoutedEventArgs e) => SaveRotation();

    private void RotationStopAfter_Changed(object sender, TextChangedEventArgs e) => SaveRotation();

    // ---- running ----

    private bool IsRotationRunning => _macros.IsRunning(RotationChain.EngineName);

    private void StartRotation()
    {
        RotationChain chain = CurrentChain();
        KeyMacro? macro = chain.ToMacro();

        if (macro == null)
        {
            if (RotationStatus != null) RotationStatus.Text = chain.Problem();
            return;
        }

        _macros.Start(macro);
        _switcherStartedAt = _macros.Sent;
        _macroTicker.Start();
        RefreshRotation();
    }

    private void StopRotation()
    {
        _macros.Stop(RotationChain.EngineName);
        RefreshRotation();
    }

    private void RotationToggle_Click(object sender, RoutedEventArgs e)
    {
        if (IsRotationRunning) StopRotation();
        else StartRotation();
    }

    /// <summary>The page hotkey: toggles on a press, or runs while held in Hold mode.</summary>
    private void OnSwitcherHotkey(bool down)
    {
        // The same guard the click keys carry: bound to a letter or digit, this
        // would otherwise fire while its own boxes are being typed into.
        if (IsActive && Keyboard.FocusedElement is TextBox) return;
        if (_switcherDisabled) return;

        if (CurrentChain().Trigger == SwitchTrigger.Hold)
        {
            if (down) StartRotation(); else StopRotation();
        }
        else if (down)   // Toggle: act on the press only
        {
            if (IsRotationRunning) StopRotation(); else StartRotation();
        }
    }

    private void RefreshRotation()
    {
        bool running = IsRotationRunning;

        if (RotationStatusText != null) RotationStatusText.Text = running ? "Running" : "Idle";
        if (RotationToggle != null) RotationToggle.Content = running ? "ON" : "OFF";

        if (RotationStatus != null)
            RotationStatus.Text = running ? "" : (CurrentChain().Problem() ?? "");

        if (RotationCountText != null)
        {
            if (!running)
            {
                RotationCountText.Text = "";
            }
            else
            {
                long sent = _macros.Sent - _switcherStartedAt;
                RotationCountText.Text = sent == 0
                    ? "Waiting — switch to the game and it starts."
                    : $"{sent:N0} presses sent.";
            }
        }
    }

    // ---- shims: the hotkey / rebind / ticker code calls these by their old
    // names; for one rotation they map onto the single chain. ----

    /// <summary>The page hotkey stop path, and the "switcher switched off" stop.</summary>
    private void StopAllSwitchers() => StopRotation();

    private void RefreshDisableAllSwitchers() => RefreshRotation();

    private void RefreshSwitcherCount() => RefreshRotation();

    /// <summary>
    /// The rotation claims no hotkey of its own: it runs off the global switcher
    /// hotkey, which the fixed-hotkey claims already cover. So nothing extra here.
    /// </summary>
    private static IEnumerable<HotkeyClaim> SwitcherClaims() => Enumerable.Empty<HotkeyClaim>();
}
