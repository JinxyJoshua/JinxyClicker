using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The Lower Ping page (Plus): a one-switch network-latency bundle plus the same
/// tweaks as individual toggles.
/// </summary>
/// <remarks>
/// Mirrors <see cref="GamingMode"/>'s one switch exactly. The switch holds its
/// OWN <see cref="LowerPing"/> tweak instances and runs the registry/powercfg
/// work off the UI thread; the cards on the page bind to a SEPARATE set, so
/// nothing on screen is mutated from the background thread. It remembers which
/// tweaks it turned on and reverts only those, leaving anything the user set by
/// hand alone.
/// </remarks>
public partial class MainWindow
{
    private readonly LowerPing _lowerPing = new();
    private readonly ObservableCollection<PcTweak> _lowerPingTweaks = new(LowerPing.Curated());

    private bool _lowerPingBusy;
    private bool _lowerPingOn;
    private List<string> _lowerPingApplied = new();

    /// <summary>Refreshes the Lower Ping toggles from live system state. Called
    /// when the Lower Ping sub-tab of the Performance page is opened.</summary>
    private void RefreshLowerPingTweaks()
    {
        foreach (PcTweak tweak in _lowerPingTweaks) tweak.Refresh();
    }

    // Lower Ping is a Plus sub-tab of the Performance page. If a pass lapses while
    // that sub-tab is showing, send the user to the key page. Called from RefreshLicense.
    private void ApplyLowerPingPro()
    {
        // IsVisible, not Visibility: the sub-panel keeps its own Visibility even
        // when its parent Performance page is collapsed, so a Visibility check
        // would yank a lapsed user off whatever page they are on, every re-check.
        if (!IsPro && PagePerf_lowerping is { IsVisible: true })
        {
            if (PerfTabOptimizations is not null) PerfTabOptimizations.IsChecked = true;   // back to a free sub-tab
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");
        }
    }

    private void LoadLowerPing(AppSettings s)
    {
        _lowerPingOn = s.LowerPingOn;
        _lowerPingApplied = s.LowerPingAppliedTweaks ?? new List<string>();

        if (LowerPingTweakList != null) LowerPingTweakList.ItemsSource = _lowerPingTweaks;
        UpdateLowerPingButton();

        if (LowerPingStatusText != null)
            LowerPingStatusText.Text = _lowerPingOn
                ? "Lower Ping is on."
                : "Off. One switch to stop Windows adding latency — fully reversible.";
    }

    private void UpdateLowerPingButton()
    {
        if (LowerPingButton != null)
            LowerPingButton.Content = _lowerPingOn ? "TURN OFF LOWER PING" : "ACTIVATE LOWER PING";
    }

    private async void LowerPing_Click(object sender, RoutedEventArgs e)
    {
        if (_lowerPingBusy) return;

        _lowerPingBusy = true;
        LowerPingButton.IsEnabled = false;
        LowerPingStatusText.Text = _lowerPingOn ? "Reverting…" : "Applying…";

        try
        {
            if (_lowerPingOn) await DeactivateLowerPingAsync();
            else await ActivateLowerPingAsync();
        }
        finally
        {
            _lowerPingBusy = false;
            LowerPingButton.IsEnabled = true;
            UpdateLowerPingButton();

            // The cards share the same settings on disk, so keep them honest.
            foreach (PcTweak tweak in _lowerPingTweaks) tweak.Refresh();
        }
    }

    private async Task ActivateLowerPingAsync()
    {
        (List<string> applied, int adminSkipped) = await Task.Run(() =>
        {
            var state = TweakState.Load();
            var done = new List<string>();
            int skipped = 0;

            foreach (PcTweak tweak in _lowerPing.Tweaks)
            {
                tweak.Refresh();
                if (tweak.IsApplied != false) continue;   // already on, or unreadable

                if (!tweak.CanAct)
                {
                    if (tweak.RequiresAdmin) skipped++;
                    continue;
                }

                if (tweak.Toggle(state) == null && tweak.IsApplied == true)
                    done.Add(tweak.Id);
            }

            return (done, skipped);
        });

        _lowerPingApplied = applied;
        _lowerPingOn = true;
        SaveAppSettings();

        string status = $"Lower Ping on — {applied.Count} tweak{(applied.Count == 1 ? "" : "s")} applied.";
        if (adminSkipped > 0 && !TweakEnvironment.IsElevated)
            status += $" {adminSkipped} more need admin — restart as administrator to include {(adminSkipped == 1 ? "it" : "them")}.";

        LowerPingStatusText.Text = status;
    }

    private async Task DeactivateLowerPingAsync()
    {
        List<string> toRevert = _lowerPingApplied.ToList();

        int reverted = await Task.Run(() =>
        {
            var state = TweakState.Load();
            int done = 0;

            foreach (string id in toRevert)
            {
                PcTweak? tweak = _lowerPing.Tweaks.FirstOrDefault(t => t.Id == id);
                if (tweak == null) continue;

                tweak.Refresh();
                if (tweak.IsApplied == true && tweak.Toggle(state) == null) done++;
            }

            return done;
        });

        _lowerPingApplied.Clear();
        _lowerPingOn = false;
        SaveAppSettings();

        LowerPingStatusText.Text =
            $"Lower Ping off. Reverted {reverted} tweak{(reverted == 1 ? "" : "s")} and put your settings back.";
    }
}
