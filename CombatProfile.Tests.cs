using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Combat profiles are curated bundles of the settings this app already has —
/// not per-weapon hit-reg magic, because the registration ceiling is the same
/// for every melee weapon. These pin that each profile is internally honest:
/// the blockhit profile actually leaves room to block, the max-hits profile
/// actually locks to the ceiling.
/// </summary>
public class CombatProfileTests
{
    [Fact]
    public void EveryProfileHasAUniqueId()
    {
        var ids = CombatProfiles.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void ByIdRoundTripsEveryProfileAndRejectsUnknowns()
    {
        foreach (CombatProfile p in CombatProfiles.All)
            Assert.Same(p, CombatProfiles.ById(p.Id));

        Assert.Null(CombatProfiles.ById("nope"));
        Assert.Null(CombatProfiles.ById(null));
    }

    [Fact]
    public void TheMaxHitsProfileLocksToTheCeiling()
    {
        CombatProfile p = CombatProfiles.ById("maxhits")!;

        Assert.True(p.AutoTune);
        Assert.False(p.BlockHit);   // the ceiling leaves no room to block
    }

    [Fact]
    public void TheBlockhitProfileActuallyLeavesRoomToBlock()
    {
        CombatProfile p = CombatProfiles.ById("blockhit")!;

        Assert.True(p.BlockHit);
        Assert.False(p.AutoTune);   // auto-tune's ceiling would starve the block

        // The honest part: run the profile's own CPS/duty through the real
        // timing and confirm a block genuinely fits. A blockhit profile whose
        // rate leaves no gap to block would be a lie.
        ClickTiming t = ClickTimings.Resolve(p.Cps, p.Cdc / 100.0, hitFix: p.HitFix);
        Assert.True(BlockHit.Plan(t.PeriodMs, t.DownMs).Blocks);
    }
}
