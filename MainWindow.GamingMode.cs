using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// The one-switch Gaming Mode on the Optimizations page.
/// </summary>
/// <remarks>
/// It applies the curated <see cref="GamingMode"/> bundle, unlocks Roblox's FPS,
/// raises Roblox's priority and frees RAM — then remembers exactly what of that it
/// changed, so turning it off reverts its own doing and leaves anything the user
/// had set by hand alone.
///
/// The tweak work spawns powercfg and touches the registry, so it runs off the UI
/// thread. That is safe here because Gaming Mode holds its own PcTweak instances,
/// separate from the ones bound to the Tweaks page — nothing on screen is bound to
/// the objects being changed on the background thread. The priority step is the one
/// exception, and it stays on the UI thread because it reads a checkbox.
/// </remarks>
public partial class MainWindow
{
    private readonly GamingMode _gaming = new();
    private bool _gamingBusy;

    private bool _gamingOn;
    private List<string> _gamingAppliedTweaks = new();
    private bool _gamingSetFps;
    private int? _gamingPrevFpsCap;
    private bool _gamingSetPriority;

    private void LoadGamingMode(AppSettings s)
    {
        _gamingOn = s.GamingModeOn;
        _gamingAppliedTweaks = s.GamingModeAppliedTweaks ?? new List<string>();
        _gamingSetFps = s.GamingModeSetFps;
        _gamingPrevFpsCap = s.GamingModePrevFpsCap;
        _gamingSetPriority = s.GamingModeSetPriority;

        UpdateGamingModeButton();

        if (GamingModeStatusText != null)
            GamingModeStatusText.Text = _gamingOn
                ? "Gaming Mode is on."
                : "Off. One switch for a real, reversible FPS boost.";
    }

    private void UpdateGamingModeButton()
    {
        if (GamingModeButton != null)
            GamingModeButton.Content = _gamingOn ? "TURN OFF GAMING MODE" : "ACTIVATE GAMING MODE";
    }

    private async void GamingMode_Click(object sender, RoutedEventArgs e)
    {
        if (_gamingBusy) return;

        _gamingBusy = true;
        GamingModeButton.IsEnabled = false;
        GamingModeStatusText.Text = _gamingOn ? "Reverting…" : "Applying…";

        try
        {
            if (_gamingOn) await DeactivateGamingModeAsync();
            else await ActivateGamingModeAsync();
        }
        finally
        {
            _gamingBusy = false;
            GamingModeButton.IsEnabled = true;
            UpdateGamingModeButton();

            // The individual tweak cards share these settings, so keep them honest.
            RefreshTweaks();
        }
    }

    private async Task ActivateGamingModeAsync()
    {
        bool hadPriority = RobloxPriority.IsChecked == true;

        ActivateResult result = await Task.Run(() =>
        {
            var state = TweakState.Load();
            var applied = new List<string>();
            int adminSkipped = 0;

            foreach (PcTweak tweak in _gaming.Tweaks)
            {
                tweak.Refresh();
                if (tweak.IsApplied != false) continue; // already on, or unreadable

                if (!tweak.CanAct)
                {
                    if (tweak.RequiresAdmin) adminSkipped++;
                    continue;
                }

                if (tweak.Toggle(state) == null && tweak.IsApplied == true)
                    applied.Add(tweak.Id);
            }

            int? prevFps = null;
            bool fpsOk = false;
            try
            {
                prevFps = FastFlagStore.CurrentFpsCap();
                FastFlagStore.ApplyFpsCap(FastFlagStore.UnlimitedFps);
                fpsOk = true;
            }
            catch
            {
                // Roblox not installed, or its settings not writable — the rest of
                // Gaming Mode still stands.
            }

            (int trimmed, _) = MemoryTools.TrimWorkingSets();

            return new ActivateResult(applied, prevFps, fpsOk, adminSkipped, trimmed);
        });

        _gamingAppliedTweaks = result.Applied;
        _gamingSetFps = result.FpsUnlocked;
        _gamingPrevFpsCap = result.PreviousFps;

        if (!hadPriority)
        {
            RobloxPriority.IsChecked = true;
            _gamingSetPriority = true;
        }
        ApplyRobloxPriority();

        _gamingOn = true;
        SaveAppSettings();

        GamingModeStatusText.Text = BuildActivatedStatus(result);
    }

    private async Task DeactivateGamingModeAsync()
    {
        List<string> toRevert = _gamingAppliedTweaks.ToList();
        bool setFps = _gamingSetFps;
        int? prevFps = _gamingPrevFpsCap;

        int reverted = await Task.Run(() =>
        {
            var state = TweakState.Load();
            int done = 0;

            foreach (string id in toRevert)
            {
                PcTweak? tweak = _gaming.Tweaks.FirstOrDefault(t => t.Id == id);
                if (tweak == null) continue;

                tweak.Refresh();
                if (tweak.IsApplied == true && tweak.Toggle(state) == null) done++;
            }

            if (setFps)
            {
                try { FastFlagStore.ApplyFpsCap(prevFps); }
                catch { /* best effort — the cap is cosmetic to restore */ }
            }

            return done;
        });

        if (_gamingSetPriority)
        {
            RobloxPriority.IsChecked = false;
            RestoreRobloxPriority();
        }

        _gamingAppliedTweaks.Clear();
        _gamingSetFps = false;
        _gamingPrevFpsCap = null;
        _gamingSetPriority = false;
        _gamingOn = false;
        SaveAppSettings();

        GamingModeStatusText.Text =
            $"Gaming Mode off. Reverted {reverted} tweak{(reverted == 1 ? "" : "s")} and put your FPS and priority back.";
    }

    private static string BuildActivatedStatus(ActivateResult r)
    {
        var parts = new List<string>
        {
            $"{r.Applied.Count} tweak{(r.Applied.Count == 1 ? "" : "s")} applied"
        };

        if (r.FpsUnlocked) parts.Add("FPS unlocked in Roblox");
        parts.Add("Roblox priority raised");
        if (r.Trimmed > 0) parts.Add($"RAM freed on {r.Trimmed} process{(r.Trimmed == 1 ? "" : "es")}");

        string status = "Gaming Mode on — " + string.Join(", ", parts) + ".";

        if (r.AdminSkipped > 0 && !TweakEnvironment.IsElevated)
            status += $" {r.AdminSkipped} more need admin — restart as administrator to include {(r.AdminSkipped == 1 ? "it" : "them")}.";

        return status;
    }

    private readonly record struct ActivateResult(
        List<string> Applied, int? PreviousFps, bool FpsUnlocked, int AdminSkipped, int Trimmed);
}
