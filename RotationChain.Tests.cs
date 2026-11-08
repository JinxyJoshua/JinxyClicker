using System.Collections.Generic;
using System.Linq;
using JinxyClicker;
using Xunit;

public class RotationChainTests
{
    private static RotationChain Chain(params RotationSlot[] slots) =>
        new(slots, SwitchTrigger.Toggle, 0);

    [Fact]
    public void ToMacro_BuildsKeysAndPerSlotHolds()
    {
        KeyMacro? m = Chain(new RotationSlot("1", 1300), new RotationSlot("4", 100)).ToMacro();

        Assert.NotNull(m);
        Assert.Equal(new[] { (int)'1', (int)'4' }, m!.Keys);
        Assert.Equal(new[] { 1300, 100 }, m.HoldsMs);
        Assert.Equal(1300, m.IntervalMs);   // the first slot's delay is the base interval
    }

    [Fact]
    public void ToMacro_CarriesStopAfterRotations()
    {
        KeyMacro? m = new RotationChain(
            new[] { new RotationSlot("1", 100), new RotationSlot("2", 100) },
            SwitchTrigger.Toggle, 3).ToMacro();

        Assert.Equal(3, m!.MaxRotations);
    }

    [Fact]
    public void ToMacro_ZeroStopAfterRunsForever()
    {
        KeyMacro? m = Chain(new RotationSlot("1", 100), new RotationSlot("2", 100)).ToMacro();
        Assert.Equal(0, m!.MaxRotations);
    }

    [Fact]
    public void BlankSlotsAreIgnored()
    {
        RotationChain chain = Chain(
            new RotationSlot("1", 100), new RotationSlot("", 100), new RotationSlot("2", 100));

        Assert.Null(chain.Problem());
        Assert.Equal(2, chain.ToMacro()!.Keys.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoKeyedSlotsIsAProblem(int count)
    {
        RotationSlot[] slots = Enumerable.Range(0, count)
            .Select(_ => new RotationSlot("1", 100)).ToArray();

        Assert.NotNull(Chain(slots).Problem());
        Assert.Null(Chain(slots).ToMacro());
    }

    [Fact]
    public void AMultiCharacterSlotIsAProblem()
    {
        Assert.NotNull(Chain(new RotationSlot("12", 100), new RotationSlot("3", 100)).Problem());
    }

    [Fact]
    public void ADelayOutOfRangeIsAProblem()
    {
        Assert.NotNull(Chain(new RotationSlot("1", 100), new RotationSlot("2", 999_999)).Problem());
        Assert.NotNull(Chain(new RotationSlot("1", 0), new RotationSlot("2", 100)).Problem());
    }

    [Fact]
    public void AValidChainHasNoProblemAndMakesAMacro()
    {
        RotationChain chain = Chain(
            new RotationSlot("Q", 500), new RotationSlot("E", 500), new RotationSlot("R", 500));

        Assert.Null(chain.Problem());
        KeyMacro? m = chain.ToMacro();
        Assert.NotNull(m);
        Assert.Equal(3, m!.Keys.Length);
    }
}
