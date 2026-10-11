using JinxyClicker;
using Xunit;

namespace JinxyClicker.Tests;

public class SocdTests
{
    [Theory]
    [InlineData(false, false, "LastInputWins")]   // nothing held
    [InlineData(true, false, "LastInputWins")]    // only neg
    [InlineData(false, true, "LastInputWins")]    // only pos
    public void OneOrNoKeyPassesStraightThrough(bool neg, bool pos, string mode)
    {
        var m = mode == "Neutral" ? SocdMode.Neutral : SocdMode.LastInputWins;
        Assert.Equal((neg, pos), Socd.Resolve(neg, pos, lastWasPos: false, m));
        Assert.Equal((neg, pos), Socd.Resolve(neg, pos, lastWasPos: true, m));
    }

    [Fact]
    public void BothHeldLastInputWinsGivesTheLastPressed()
    {
        Assert.Equal((false, true), Socd.Resolve(true, true, lastWasPos: true, SocdMode.LastInputWins));
        Assert.Equal((true, false), Socd.Resolve(true, true, lastWasPos: false, SocdMode.LastInputWins));
    }

    [Fact]
    public void BothHeldNeutralCancelsToNothing()
    {
        Assert.Equal((false, false), Socd.Resolve(true, true, lastWasPos: true, SocdMode.Neutral));
        Assert.Equal((false, false), Socd.Resolve(true, true, lastWasPos: false, SocdMode.Neutral));
    }
}
