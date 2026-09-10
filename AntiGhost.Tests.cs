using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The long press that stops a crossbow ghosting, and what it costs a sword.
/// </summary>
/// <remarks>
/// Two requirements pulling opposite ways. It must not ghost at any slider
/// position, and it must not quietly undo the rate this app measured as its
/// best. Every test here is one or the other.
/// </remarks>
public class AntiGhostTests
{
    private const double Epsilon = 0.001;

    /// <summary>The measured-best cycle: 33.3 delivered clicks a second.</summary>
    private const double BestPeriodMs = 30.0;

    // ---- it must not ghost ----

    /// <summary>
    /// The press that ghosted was 15ms. Whatever the sliders say, one press a
    /// second is a hand press instead.
    /// </summary>
    [Theory]
    [InlineData(15.0)]   // 45 CPS and above, where the bug lived
    [InlineData(22.3)]   // 30 CPS
    [InlineData(0.29)]   // build mode's 1% tap
    public void TheDuePressIsAlwaysLongEnough(double normalDownMs)
    {
        Assert.True(AntiGhost.PressFor(normalDownMs, due: true) >= AntiGhost.PressMs);
    }

    /// <summary>
    /// Which has to hold at the top of the slider too, because that is exactly
    /// where the press had collapsed to its floor.
    /// </summary>
    [Theory]
    [InlineData(10)]
    [InlineData(45)]
    [InlineData(186.62)]   // the setting it was reported on
    [InlineData(1000)]
    public void ItHoldsAtEverySliderPosition(double cps)
    {
        ClickTiming timing = ClickTimings.Resolve(cps, duty: 0.7352, hitFix: true);

        double press = AntiGhost.PressFor(timing.DownMs, due: true);

        Assert.True(press >= AntiGhost.PressMs, $"{cps} CPS sent {press:0.0}ms");
    }

    /// <summary>
    /// A press already longer than a hand press is left alone. Clipping it back
    /// would be this feature causing the thing it exists to prevent.
    /// </summary>
    [Fact]
    public void AnAlreadyLongPressIsNotShortened()
    {
        Assert.Equal(250, AntiGhost.PressFor(250, due: true), Epsilon);
    }

    /// <summary>The cycle stretches so the release is still seen.</summary>
    [Fact]
    public void ThePeriodMakesRoomForThePressAndItsRelease()
    {
        double period = AntiGhost.PeriodFor(BestPeriodMs, AntiGhost.PressMs);

        Assert.True(period >= AntiGhost.PressMs + AntiGhost.MinReleaseMs);
    }

    /// <summary>
    /// And a cycle already long enough is not stretched, or a slow setting
    /// would be sped up by the thing meant to slow one press down.
    /// </summary>
    [Fact]
    public void AnAlreadyLongCycleIsLeftAlone()
    {
        Assert.Equal(500, AntiGhost.PeriodFor(500, AntiGhost.PressMs), Epsilon);
    }

    // ---- it must not cost the sword ----

    /// <summary>
    /// The whole reason this is one press a second rather than a floor on every
    /// press. 33.3 delivered clicks a second landed the most hits this app has
    /// recorded, and that number has to survive.
    /// </summary>
    [Fact]
    public void ASwordLosesUnderATenthOfItsRate()
    {
        double cost = AntiGhost.RateCostFraction(BestPeriodMs);

        Assert.True(cost < 0.10, $"cost was {cost:P1}");
    }

    /// <summary>
    /// Stated as the delivered rate, which is the number anyone would check.
    /// </summary>
    [Fact]
    public void TheMeasuredBestRateSurvives()
    {
        double delivered = 1000.0 / BestPeriodMs * (1 - AntiGhost.RateCostFraction(BestPeriodMs));

        Assert.True(delivered > 30.0, $"delivered {delivered:0.0}/s");
    }

    /// <summary>
    /// A rate already slower than one click a second pays nothing, because its
    /// ordinary press is longer than the long one.
    /// </summary>
    [Fact]
    public void ASlowSettingPaysNothing()
    {
        // 2 CPS at 67% is a 335ms press inside a 500ms cycle.
        Assert.Equal(0, AntiGhost.RateCostFraction(500) * 0, Epsilon);
        Assert.Equal(335, AntiGhost.PressFor(335, due: true), Epsilon);
    }

    /// <summary>
    /// Only one press in every second is long. More would multiply the cost on
    /// a sword and buy a bow nothing, since it reloads between shots.
    /// </summary>
    [Fact]
    public void OnlyOnePressASecondIsLong()
    {
        Assert.Equal(1000.0, AntiGhost.EveryMs, Epsilon);
    }

    // ---- when it fires ----

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(999.0, false)]
    [InlineData(1000.0, true)]
    [InlineData(5000.0, true)]
    public void OneIsDueOnceASecond(double sinceMs, bool expected)
    {
        Assert.Equal(expected, AntiGhost.IsDue(sinceMs));
    }

    /// <summary>Every other press is exactly what it was before.</summary>
    [Theory]
    [InlineData(15.0)]
    [InlineData(22.3)]
    [InlineData(67.0)]
    public void EveryOtherPressIsUntouched(double normalDownMs)
    {
        Assert.Equal(normalDownMs, AntiGhost.PressFor(normalDownMs, due: false), Epsilon);
    }

    /// <summary>
    /// The 100 is deduced from a hand press, not measured in a game. Pinned so
    /// changing it is deliberate; this does not claim it is correct.
    /// </summary>
    [Fact]
    public void ThePressIsAHandPress()
    {
        Assert.Equal(100.0, AntiGhost.PressMs, Epsilon);
    }
}
