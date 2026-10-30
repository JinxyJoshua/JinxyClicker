using Xunit;

namespace JinxyClicker;

/// <summary>
/// The smoothness easing behind Shaky Tracking. At zero smoothness the shake
/// jumps the whole way to each new random point (the old sharp jitter); higher
/// smoothness moves only a fraction of the way, so the cursor glides.
/// </summary>
public class ShakeMotionTests
{
    private const double Epsilon = 0.0001;

    [Fact]
    public void ZeroSmoothness_JumpsTheWholeWay()
    {
        // factor 1.0 — the cursor lands exactly on the target, as it always did.
        Assert.Equal(1.0, ShakeMotion.Factor(0), Epsilon);
        Assert.Equal(10.0, ShakeMotion.Ease(current: 0, target: 10, smoothness: 0), Epsilon);
    }

    [Fact]
    public void FullSmoothness_MovesOnlyASmallStep()
    {
        Assert.Equal(ShakeMotion.MinFactor, ShakeMotion.Factor(1), Epsilon);
        // 8% of the way from 0 toward 10.
        Assert.Equal(10 * ShakeMotion.MinFactor, ShakeMotion.Ease(0, 10, 1), Epsilon);
    }

    [Fact]
    public void MoreSmoothness_AlwaysMeansASmallerStep()
    {
        Assert.True(ShakeMotion.Factor(0.25) > ShakeMotion.Factor(0.5));
        Assert.True(ShakeMotion.Factor(0.5) > ShakeMotion.Factor(0.75));
    }

    [Fact]
    public void TheStepNeverOvershootsTheTarget()
    {
        // Eased position stays between where it was and where it is heading, for
        // any smoothness — so the cursor can never fly past the edge of the box.
        for (double sm = 0; sm <= 1.0; sm += 0.1)
        {
            double next = ShakeMotion.Ease(current: 2, target: 8, smoothness: sm);
            Assert.InRange(next, 2, 8);

            double back = ShakeMotion.Ease(current: 8, target: 2, smoothness: sm);
            Assert.InRange(back, 2, 8);
        }
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void SmoothnessIsClampedToItsRange(double outOfRange)
    {
        double factor = ShakeMotion.Factor(outOfRange);
        Assert.True(factor >= ShakeMotion.MinFactor - Epsilon && factor <= 1.0 + Epsilon,
            $"factor {factor} should stay within [{ShakeMotion.MinFactor}, 1.0]");
    }
}
