using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Blockhit assist raises the sword (a right-button hold) in the idle gap
/// between left-click attacks, so the knockback you take is cut without
/// changing how fast you swing. These pin the one decision that carries real
/// risk: when there is room to block, and that a block plus its release always
/// add up to exactly the gap they replace — so turning blockhit on never
/// changes the attack cadence.
/// </summary>
public class BlockHitTests
{
    private const double Epsilon = 0.001;

    [Fact]
    public void FillsTheGapWithABlockWhenThereIsRoom()
    {
        // 12/s at half duty: ~83ms cycle, ~42ms held, ~42ms gap — plenty.
        ClickTiming t = ClickTimings.Resolve(cps: 12, duty: 0.5, hitFix: true);

        BlockHitPlan plan = BlockHit.Plan(t.PeriodMs, t.DownMs);

        Assert.True(plan.Blocks);
        Assert.True(plan.BlockMs >= BlockHit.MinBlockMs);
        Assert.Equal(BlockHit.ReleaseMarginMs, plan.ReleaseMs, Epsilon);
    }

    [Fact]
    public void BlockAndReleaseAlwaysAddUpToTheGapTheyReplace()
    {
        ClickTiming t = ClickTimings.Resolve(cps: 12, duty: 0.5, hitFix: true);
        double gap = t.PeriodMs - t.DownMs;

        BlockHitPlan plan = BlockHit.Plan(t.PeriodMs, t.DownMs);

        Assert.Equal(gap, plan.BlockMs + plan.ReleaseMs, Epsilon);
    }

    [Fact]
    public void DoesNotBlockWhenTheGapIsTooShortToReadABlock()
    {
        // The auto-tune ceiling leaves only a ~15ms gap — no room for a block
        // that the game could read plus a clear release after it.
        ClickTiming t = ClickTimings.Optimal();

        BlockHitPlan plan = BlockHit.Plan(t.PeriodMs, t.DownMs);

        Assert.False(plan.Blocks);
        Assert.Equal(0.0, plan.BlockMs, Epsilon);
        // Even when it can't block, the gap it hands back is the real one, so
        // the loop still waits the right amount of time.
        Assert.Equal(t.PeriodMs - t.DownMs, plan.ReleaseMs, Epsilon);
    }

    [Fact]
    public void AnIdleTimingNeverBlocks()
    {
        BlockHitPlan plan = BlockHit.Plan(periodMs: 0, downMs: 0);

        Assert.False(plan.Blocks);
    }
}
