using Xunit;

namespace JinxyClicker;

/// <summary>
/// Humanize: randomises the gap between clicks so the stream isn't perfectly
/// even. The amount is a 0–1 fraction; the gap is scaled by a symmetric random
/// factor in [1-amount, 1+amount], floored so it can never invert or stall.
/// </summary>
public class ClickJitterTests
{
    private const double Eps = 0.0001;

    [Fact]
    public void ZeroJitter_LeavesTheGapExactlyAsItWas()
    {
        Assert.Equal(30.0, ClickJitter.Gap(30.0, 0, 0.0), Eps);
        Assert.Equal(30.0, ClickJitter.Gap(30.0, 0, 0.99), Eps);
    }

    [Fact]
    public void MidRandom_IsTheUnchangedGap()
    {
        // rand 0.5 → factor 1.0 whatever the amount.
        Assert.Equal(30.0, ClickJitter.Gap(30.0, 0.5, 0.5), Eps);
    }

    [Fact]
    public void LowAndHighRandom_SpreadSymmetricallyAroundTheGap()
    {
        Assert.Equal(15.0, ClickJitter.Gap(30.0, 0.5, 0.0), Eps);   // 1 - 0.5
        Assert.Equal(45.0, ClickJitter.Gap(30.0, 0.5, 1.0), Eps);   // 1 + 0.5
    }

    [Fact]
    public void TheGapNeverFallsBelowTheFloor()
    {
        // A tiny base with heavy down-jitter still returns at least the floor.
        Assert.Equal(ClickJitter.MinGapMs, ClickJitter.Gap(0.5, 1.0, 0.0), Eps);
    }

    [Fact]
    public void AmountIsClampedToOne()
    {
        // Over-1 amounts behave as 1, so the factor stays in [0,2].
        Assert.Equal(60.0, ClickJitter.Gap(30.0, 5.0, 1.0), Eps);
    }
}
