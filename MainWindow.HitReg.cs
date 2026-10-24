using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JinxyClicker;

/// <summary>
/// The Hit Registration page (Plus): Auto-tune, Blockhit assist and the
/// one-click combat profiles, gathered under the PLUS group.
/// </summary>
/// <remarks>
/// Its own tab rather than inline on the Clicker page, so it sits with the
/// other paid features. The engine still reads these through <c>_settings</c>
/// like every other clicker control, whichever page is open; this file only
/// wires the UI, the page gate and the profile bundles. Nothing here claims a
/// hit-reg rate the server does not actually give: the ceiling is uniform, so
/// the profiles are honest setting bundles, not per-weapon magic.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Id of the combat profile last applied, for the lit button. Null for none.</summary>
    private string? _activeCombatProfile;

    // Built once from CombatProfiles.All so the buttons cannot drift from the
    // bundles the engine and tests use.
    private bool _combatButtonsBuilt;

    private void LoadHitReg(AppSettings s)
    {
        BuildCombatProfileButtons();

        if (AutoTuneToggle is not null) AutoTuneToggle.IsChecked = s.AutoTune;
        if (BlockHitToggle is not null) BlockHitToggle.IsChecked = s.BlockHit;

        _activeCombatProfile = CombatProfiles.ById(s.CombatProfile)?.Id;
        LightCombatProfile();

        // The licence may not be evaluated yet at first load; ApplyHitRegPro is
        // also called from RefreshLicense, so a lapsed pass still forces these
        // off. Call it here too so a free install opens locked.
        ApplyHitRegPro();
    }

    private void BuildCombatProfileButtons()
    {
        if (_combatButtonsBuilt || CombatProfileRow is null) return;
        _combatButtonsBuilt = true;

        foreach (CombatProfile p in CombatProfiles.All)
        {
            var b = new Button
            {
                Content = p.Name,
                Tag = p.Id,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                FontSize = 11
            };
            // No hover tooltip on these: the profile's intent is already in the
            // card's blurb above. Disabled outright so no inherited style can
            // bring one back.
            ToolTipService.SetIsEnabled(b, false);
            b.Click += CombatProfile_Click;
            CombatProfileRow.Children.Add(b);
        }
    }

    private void NavHitReg_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        ShowPage(NavHitReg, PageHitReg, "Hit Registration", "Land more of the clicks you send");
    }

    /// <summary>
    /// Keeps the Plus gate honest: the tab stays visible so Free users can see
    /// it, but a free install (or a lapsed pass) landing on the page is sent to
    /// the key page, and the paid toggles are forced off so the engine cannot
    /// keep running a Plus timing for free. Called from RefreshLicense and on load.
    /// </summary>
    private void ApplyHitRegPro()
    {
        if (IsPro) return;

        // Setting IsChecked only fires EngineSetting_Changed when it actually
        // changes, so this is a no-op for a free user who never had it on.
        if (AutoTuneToggle is not null) AutoTuneToggle.IsChecked = false;
        if (BlockHitToggle is not null) BlockHitToggle.IsChecked = false;
        _activeCombatProfile = null;
        LightCombatProfile();

        if (PageHitReg is { Visibility: Visibility.Visible })
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");
    }

    private void CombatProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        if (sender is not Button { Tag: string id } || CombatProfiles.ById(id) is not { } p) return;

        ApplyCombatProfile(p);
    }

    private void ApplyCombatProfile(CombatProfile p)
    {
        // Set the controls the profile owns, then let the one update path
        // republish the engine snapshot. The sliders are clamped to their own
        // ranges, so a profile can never push a value the UI would reject.
        CpsSlider.Value = System.Math.Clamp(p.Cps, CpsSlider.Minimum, CpsSlider.Maximum);
        CdcSlider.Value = System.Math.Clamp(p.Cdc, CdcSlider.Minimum, CdcSlider.Maximum);
        HitFix.IsChecked = p.HitFix;
        ShakyTracking.IsChecked = p.Shaky;
        if (AutoTuneToggle is not null) AutoTuneToggle.IsChecked = p.AutoTune;
        if (BlockHitToggle is not null) BlockHitToggle.IsChecked = p.BlockHit;

        _activeCombatProfile = p.Id;
        LightCombatProfile();

        UpdateEngineSettings();
        SaveAppSettings();   // persist the chosen profile id at once
    }

    // Lights the active profile's button and dims the rest.
    private void LightCombatProfile()
    {
        if (CombatProfileRow is null) return;

        foreach (object child in CombatProfileRow.Children)
        {
            if (child is not Button b || b.Tag is not string id) continue;

            bool active = id == _activeCombatProfile;
            b.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            b.Background = active ? (Brush)FindResource("Accent") : Brushes.Transparent;
            b.ClearValue(ForegroundProperty);
        }
    }
}
