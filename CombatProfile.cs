using System.Collections.Generic;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// A curated bundle of the clicker's existing settings for one combat
/// situation.
/// </summary>
/// <remarks>
/// Deliberately not per-weapon hit rates. The server registers at most about
/// one melee hit per ~30ms whatever the weapon, so a profile claiming a faster
/// rate for a "sharper" sword would be fiction. What a profile honestly is: a
/// one-click way to set the rate, duty, HitFix, auto-tune and blockhit that
/// suit attacking, block-hitting or bridging, so the player stops hand-tuning
/// five controls for each.
/// </remarks>
/// <param name="Id">Stable identifier, stored and round-tripped.</param>
/// <param name="Name">Shown on the button.</param>
/// <param name="Blurb">One honest line about what the bundle is for.</param>
/// <param name="Cps">Clicks per second.</param>
/// <param name="Cdc">Click-down cycle, as a percentage (duty × 100).</param>
/// <param name="HitFix">Whether HitFix's floors are enforced.</param>
/// <param name="AutoTune">Whether the rate is locked to the measured-best ceiling.</param>
/// <param name="BlockHit">Whether the sword block is raised between attacks.</param>
/// <param name="Shaky">Whether shaky tracking is on.</param>
public sealed record CombatProfile(
    string Id, string Name, string Blurb,
    double Cps, double Cdc, bool HitFix, bool AutoTune, bool BlockHit, bool Shaky);

/// <summary>The shipped combat profiles.</summary>
public static class CombatProfiles
{
    public static readonly CombatProfile MaxHits = new(
        "maxhits", "Max hits",
        "Locks to the rate measured to land the most hits — every click registers, none floods the server.",
        Cps: 33, Cdc: 50, HitFix: true, AutoTune: true, BlockHit: false, Shaky: false);

    public static readonly CombatProfile BlockHitCombo = new(
        "blockhit", "Blockhit combo",
        "Raises your sword block between hits to cut the knockback you take. Rate tuned to leave room to block.",
        Cps: 12, Cdc: 50, HitFix: true, AutoTune: false, BlockHit: true, Shaky: false);

    public static readonly CombatProfile Bridge = new(
        "bridge", "Bridge / clutch",
        "A lower, steadier rate for placing blocks and clutch plays, where a flat-out rate just gets in the way.",
        Cps: 10, Cdc: 55, HitFix: true, AutoTune: false, BlockHit: false, Shaky: false);

    public static readonly IReadOnlyList<CombatProfile> All = new[] { MaxHits, BlockHitCombo, Bridge };

    /// <summary>The profile with this id, or null for an unknown or missing id.</summary>
    public static CombatProfile? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);
}
