using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The verdict on what is actually leaving the app.
/// </summary>
/// <remarks>
/// This is the one thing the app says that a rate slider cannot, so it has to be
/// right. Saying "matching" while a 193 setting delivers 33 would be worse than
/// saying nothing — it would confirm the wrong number.
/// </remarks>
public class ClickOutputTests
{
    [Fact]
    public void SaysNothingUntilItHasSomethingToMeasure()
    {
        Assert.Equal(OutputState.Idle, ClickOutput.Classify(running: false, 41.2, 0));
    }

    /// <summary>
    /// The case this exists for. A 193 setting delivering 33 is the ordinary
    /// result of HitFix's floors, and it is what makes a slower clicker win.
    /// </summary>
    [Fact]
    public void CallsOutASettingThatIsNotBeingDelivered()
    {
        Assert.Equal(OutputState.Shortfall,
            ClickOutput.Classify(running: true, setCps: 193.62, deliveredCps: 33.3));
    }

    [Fact]
    public void NamesBothNumbersSoTheGapIsReadable()
    {
        string verdict = ClickOutput.Verdict(OutputState.Shortfall, 193.62, 33.3);

        Assert.Contains("193.6", verdict);
        Assert.Contains("33.3", verdict);
    }

    /// <summary>
    /// Measurement is a click count over a wall-clock second, so it wobbles by a
    /// click either way. Without tolerance the panel would flip verdicts every
    /// tick while nothing had changed.
    /// </summary>
    [Theory]
    [InlineData(40.0, 39.0)]
    [InlineData(40.0, 35.0)]
    public void DoesNotCryShortfallOverMeasurementWobble(double set, double delivered)
    {
        Assert.NotEqual(OutputState.Shortfall, ClickOutput.Classify(true, set, delivered));
    }

    /// <summary>
    /// Delivering everything asked for, where the ask itself is past the point
    /// the server keeps up. Distinct from a shortfall: nothing is being lost in
    /// the app, so telling someone to lower the slider for that reason would be
    /// wrong.
    /// </summary>
    [Fact]
    public void SeparatesTooFastFromNotDelivering()
    {
        Assert.Equal(OutputState.OverDriven,
            ClickOutput.Classify(running: true, setCps: 120.0, deliveredCps: 120.0));
    }

    [Fact]
    public void IsContentWhenTheRateIsHonestAndUsable()
    {
        Assert.Equal(OutputState.Matching,
            ClickOutput.Classify(running: true, setCps: 41.2, deliveredCps: 41.2));
    }

    /// <summary>The measured preset must not read as a warning.</summary>
    [Fact]
    public void TheMeasuredPresetReadsAsFine()
    {
        OutputState state = ClickOutput.Classify(true, 41.2, 41.0);

        Assert.False(ClickOutput.IsWarning(state));
    }

    /// <summary>
    /// Only the two states asking for a change are coloured. A line that is
    /// always lit stops being read at all.
    /// </summary>
    [Theory]
    [InlineData(OutputState.Shortfall, true)]
    [InlineData(OutputState.OverDriven, true)]
    [InlineData(OutputState.Matching, false)]
    [InlineData(OutputState.Idle, false)]
    public void ColoursOnlyWhatNeedsAction(OutputState state, bool expected)
    {
        Assert.Equal(expected, ClickOutput.IsWarning(state));
    }

    /// <summary>
    /// A zero setting is reachable — it means armed but idle — and must not be
    /// reported as failing to deliver a rate nobody asked for.
    /// </summary>
    [Fact]
    public void AZeroSettingIsNotAShortfall()
    {
        Assert.NotEqual(OutputState.Shortfall, ClickOutput.Classify(true, 0.0, 0.0));
    }

    /// <summary>
    /// NaN slips silently through every comparison it appears in, so a bad
    /// reading must be refused rather than allowed to pick a verdict at random.
    /// </summary>
    [Theory]
    [InlineData(double.NaN, 40.0)]
    [InlineData(40.0, double.NaN)]
    public void RefusesAnImpossibleReading(double set, double delivered)
    {
        Assert.Equal(OutputState.Idle, ClickOutput.Classify(true, set, delivered));
    }

    [Fact]
    public void EveryStateHasSomethingToSay()
    {
        foreach (OutputState state in new[]
                 {
                     OutputState.Idle, OutputState.Shortfall,
                     OutputState.OverDriven, OutputState.Matching
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(ClickOutput.Verdict(state, 41.2, 41.2)));
        }
    }
}
