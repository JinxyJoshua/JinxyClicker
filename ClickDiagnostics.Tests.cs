using System.Collections.Generic;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Summarising what the clicker actually delivered.
/// </summary>
/// <remarks>
/// These numbers exist to answer "why is hit registration inconsistent", so the
/// thing they must not do is make an inconsistent loop look fine. Most of what
/// is pinned here is that a stall stays visible instead of being averaged away.
/// </remarks>
public class ClickDiagnosticsTests
{
    private static List<double> Steady(int count, double gapMs)
    {
        var gaps = new List<double>();
        for (int i = 0; i < count; i++) gaps.Add(gapMs);
        return gaps;
    }

    [Fact]
    public void ASteadyLoopReportsItsRate()
    {
        TimingStats stats = ClickDiagnostics.Summarise(Steady(100, 25));

        Assert.Equal(40, stats.DeliveredCps, 1);
        Assert.Equal(25, stats.MedianMs, 1);
        Assert.Equal(0, stats.JitterMs, 1);
        Assert.Equal(0, stats.Stalls);
    }

    /// <summary>
    /// The case the panel exists for. Both of these average 30 ms and one is
    /// unusable — a mean alone would call them identical.
    /// </summary>
    [Fact]
    public void TellsASteadyLoopApartFromAnErraticOne()
    {
        var steady = Steady(100, 30);

        var erratic = new List<double>();
        for (int i = 0; i < 50; i++) { erratic.Add(5); erratic.Add(55); }

        TimingStats a = ClickDiagnostics.Summarise(steady);
        TimingStats b = ClickDiagnostics.Summarise(erratic);

        Assert.Equal(a.DeliveredCps, b.DeliveredCps, 1);   // same rate
        Assert.True(b.JitterMs > a.JitterMs + 20);         // very different feel
    }

    /// <summary>
    /// A single long gap is what loses a hit. It has to survive into the
    /// summary rather than being smoothed into the average.
    /// </summary>
    [Fact]
    public void AStallIsCountedAndKeptAsTheWorstGap()
    {
        var gaps = Steady(100, 25);
        gaps.Add(600);

        TimingStats stats = ClickDiagnostics.Summarise(gaps);

        Assert.Equal(1, stats.Stalls);
        Assert.Equal(600, stats.WorstMs, 1);

        // And the typical gap is unmoved, which is the point of using a median.
        Assert.Equal(25, stats.MedianMs, 1);
    }

    [Fact]
    public void SeveralStallsAreAllCounted()
    {
        var gaps = Steady(100, 20);
        gaps.AddRange(new[] { 300.0, 450.0, 700.0 });

        Assert.Equal(3, ClickDiagnostics.Summarise(gaps).Stalls);
    }

    /// <summary>Ordinary variation is not a stall, or the count means nothing.</summary>
    [Fact]
    public void SmallVariationIsNotAStall()
    {
        var gaps = new List<double>();
        for (int i = 0; i < 50; i++) gaps.Add(i % 2 == 0 ? 24 : 27);

        Assert.Equal(0, ClickDiagnostics.Summarise(gaps).Stalls);
    }

    [Fact]
    public void NothingRecordedReportsNothingRatherThanZeroes()
    {
        TimingStats stats = ClickDiagnostics.Summarise(new List<double>());

        Assert.Equal(0, stats.Samples);
        Assert.Equal(0, stats.DeliveredCps);
    }

    /// <summary>The rate is measured over the window actually observed.</summary>
    [Fact]
    public void RateFollowsTheGapsRatherThanAFixedSecond()
    {
        Assert.Equal(100, ClickDiagnostics.Summarise(Steady(50, 10)).DeliveredCps, 1);
        Assert.Equal(10, ClickDiagnostics.Summarise(Steady(50, 100)).DeliveredCps, 1);
    }

    // ---- recording ----

    [Fact]
    public void RecordsGapsBetweenClicks()
    {
        ClickDiagnostics.Reset();

        for (int i = 0; i < 5; i++)
        {
            ClickDiagnostics.RecordClick();
            System.Threading.Thread.Sleep(5);
        }

        Assert.Equal(4, ClickDiagnostics.Gaps().Count);
    }

    /// <summary>
    /// A run must not be judged by the one before it. Without the reset, the
    /// idle stretch between sessions shows as a stall that never happened.
    /// </summary>
    [Fact]
    public void ResetForgetsTheLastRun()
    {
        ClickDiagnostics.RecordClick();
        ClickDiagnostics.RecordClick();

        ClickDiagnostics.Reset();

        Assert.Empty(ClickDiagnostics.Gaps());
        Assert.Equal(0, ClickDiagnostics.Current().Samples);
    }

    /// <summary>One click is not a gap.</summary>
    [Fact]
    public void ASingleClickProducesNoGap()
    {
        ClickDiagnostics.Reset();
        ClickDiagnostics.RecordClick();

        Assert.Empty(ClickDiagnostics.Gaps());
    }
}
