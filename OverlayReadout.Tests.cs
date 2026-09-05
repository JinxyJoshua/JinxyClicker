using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// What the in-game overlay says.
/// </summary>
/// <remarks>
/// It sits over a game while someone is playing, so the rules it has to keep
/// are: never claim to know something the app cannot measure, and never light
/// up unless something is actually wrong.
/// </remarks>
public class OverlayReadoutTests
{
    private static TimingStats Stats(
        int samples = 100, double cps = 33.3, double median = 30,
        double worst = 34, double jitter = 0.6, int stalls = 0) =>
        new(samples, cps, median, worst, jitter, stalls);

    [Fact]
    public void ShowsTheRateActuallyBeingSent()
    {
        Assert.Equal("33.3 CPS", OverlayReadout.Headline(Stats(cps: 33.3)));
    }

    /// <summary>
    /// Before any clicks there is nothing to report, and a zero would read as
    /// "clicking at zero" rather than "not started".
    /// </summary>
    [Fact]
    public void SaysNothingRatherThanZeroBeforeItHasMeasuredAnything()
    {
        TimingStats empty = Stats(samples: 0, cps: 0);

        Assert.Equal("—", OverlayReadout.Headline(empty));
        Assert.Contains("waiting", OverlayReadout.Detail(empty));
        Assert.False(OverlayReadout.IsWarning(empty));
    }

    // ---- the second line is consistency, not speed ----

    [Fact]
    public void CallsATidyRunSteady()
    {
        Assert.Contains("steady", OverlayReadout.Detail(Stats(jitter: 0.6, stalls: 0)));
    }

    [Fact]
    public void CallsAWobblyRunUneven()
    {
        Assert.Contains("uneven", OverlayReadout.Detail(Stats(jitter: 8, stalls: 0)));
    }

    /// <summary>Stalls are where clicks go missing, so they are named.</summary>
    [Theory]
    [InlineData(1, "1 stall")]
    [InlineData(4, "4 stalls")]
    public void CountsStalls(int stalls, string expected)
    {
        Assert.Contains(expected, OverlayReadout.Detail(Stats(stalls: stalls)));
    }

    // ---- it only lights up when something is wrong ----

    [Fact]
    public void AGoodRunIsNotAWarning()
    {
        Assert.False(OverlayReadout.IsWarning(Stats(jitter: 0.6, stalls: 0)));
    }

    [Theory]
    [InlineData(0.6, 5)]      // stalling
    [InlineData(20.0, 0)]     // very uneven
    public void ARoughRunIs(double jitter, int stalls)
    {
        Assert.True(OverlayReadout.IsWarning(Stats(jitter: jitter, stalls: stalls)));
    }

    /// <summary>
    /// One stall in a long run is normal and must not light the overlay, or it
    /// would be lit permanently and stop being read.
    /// </summary>
    [Fact]
    public void ASingleStallIsNotWorthShouting()
    {
        Assert.False(OverlayReadout.IsWarning(Stats(jitter: 0.6, stalls: 1)));
    }

    // ---- the rate note ----

    [Fact]
    public void SaysNothingAboutTheRateWhenItIsSensible()
    {
        Assert.Equal("", OverlayReadout.RateNote(33.3));
        Assert.Equal("", OverlayReadout.RateNote(12));
    }

    /// <summary>
    /// A slider at 180 delivers what a slider at 34 does, and from inside the
    /// game there is no way to know that. Hence the note.
    /// </summary>
    [Fact]
    public void WarnsWhenTheRateIsPastWhatBecomesHits()
    {
        Assert.NotEqual("", OverlayReadout.RateNote(120));
    }

    /// <summary>
    /// The overlay must never imply it knows what happened in the game. The app
    /// reads nothing from Roblox, so any hit figure would be invented.
    /// </summary>
    [Fact]
    public void NeverClaimsToKnowAboutHitsLanded()
    {
        foreach (TimingStats stats in new[]
                 {
                     Stats(), Stats(samples: 0), Stats(stalls: 9), Stats(jitter: 30)
                 })
        {
            string text = OverlayReadout.Headline(stats) + " " + OverlayReadout.Detail(stats);

            Assert.DoesNotContain("hit", text, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("%", text);
        }
    }
}
