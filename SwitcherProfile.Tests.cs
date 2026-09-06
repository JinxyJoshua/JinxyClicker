using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Named auto switchers, several of which can run at once.
/// </summary>
/// <remarks>
/// There was one switcher configured in place. The risk in making it a list is
/// not the timing — that is the same macro engine as before — but identity:
/// two switchers that resolve to one engine name are one switcher started
/// twice, and the second silently replaces the first.
/// </remarks>
public class SwitcherProfileTests
{
    private static SwitcherProfile Profile(
        string name = "Sword + bow", string a = "3", string b = "1",
        int first = 150, int second = 900, bool enabled = true) =>
        new(name, a, b, first, second, KeyMacro.DefaultEquipMs, HotkeyBinding.Unbound, enabled);

    // ---- identity ----

    /// <summary>
    /// The engine keys macros by name, so two switchers must never resolve to
    /// one. Before this was a list there was a single reserved name.
    /// </summary>
    [Fact]
    public void TwoSwitchersNeverShareAnEngineName()
    {
        Assert.NotEqual(Profile("Sword + bow").EngineName, Profile("Pick + gumdrop").EngineName);
    }

    /// <summary>
    /// The leading space is what keeps these out of the macro list, since a
    /// typed macro name is trimmed and so can never begin with one.
    /// </summary>
    [Fact]
    public void AnEngineNameCannotCollideWithATypedMacroName()
    {
        Assert.StartsWith(" ", Profile().EngineName);
    }

    // ---- what stops one running ----

    [Fact]
    public void ACompleteSwitcherRuns()
    {
        Assert.Null(Profile().Problem());
        Assert.True(Profile().IsUsable);
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("3", "")]
    [InlineData("ab", "1")]
    [InlineData("3", "12")]
    public void BothSlotsHaveToBeOneKey(string a, string b)
    {
        Assert.Contains("slots", Profile(a: a, b: b).Problem());
    }

    [Fact]
    public void ASwitcherNeedsAName()
    {
        Assert.Contains("name", Profile(name: "  ").Problem());
    }

    [Theory]
    [InlineData(0, 900)]
    [InlineData(150, 0)]
    [InlineData(150, 99999)]
    public void HoldTimesHaveToBeReachable(int first, int second)
    {
        Assert.Contains("Hold times", Profile(first: first, second: second).Problem());
    }

    /// <summary>The card shows this, so it must name the field that is wrong.</summary>
    [Fact]
    public void TheProblemSaysWhichFieldIsWrong()
    {
        Assert.NotEqual(Profile(a: "").Problem(), Profile(first: 0).Problem());
    }

    // ---- becoming a macro ----

    [Fact]
    public void RunsAsAMacroOverBothSlots()
    {
        KeyMacro macro = Profile(a: "3", b: "1").ToMacro(clickPeriodMs: 30)!;

        Assert.NotNull(macro);
        Assert.Equal(2, macro.Keys.Length);
        Assert.Equal(Profile().EngineName, macro.Name);
    }

    [Fact]
    public void AnUnusableSwitcherProducesNoMacro()
    {
        Assert.Null(Profile(a: "").ToMacro(clickPeriodMs: 30));
    }

    /// <summary>
    /// The first hold is raised to whatever still lands a shot after the equip
    /// animation. A hold shorter than that draws the weapon and swaps away
    /// before it fires — the single switcher already did this, and it has to
    /// survive becoming a list.
    /// </summary>
    [Fact]
    public void TheFirstHoldIsRaisedToWhereAShotStillLands()
    {
        var quick = Profile(first: KeyMacro.MinIntervalMs);

        int effective = quick.EffectiveFirstHoldMs(clickPeriodMs: 30);

        Assert.True(effective >= quick.HoldFirstMs);
        Assert.Equal(effective, quick.ToMacro(30)!.HoldsMs![0]);
    }

    [Fact]
    public void AHoldAlreadyLongEnoughIsLeftAlone()
    {
        var slow = Profile(first: 500);

        Assert.Equal(500, slow.EffectiveFirstHoldMs(clickPeriodMs: 30));
    }

    [Fact]
    public void TheSecondHoldIsTheOneThatWasAskedFor()
    {
        Assert.Equal(900, Profile(second: 900).ToMacro(30)!.HoldsMs![1]);
    }

    // ---- the store ----

    [Fact]
    public void SavingOverANameEditsRatherThanDuplicates()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow", a: "3") };

        SwitcherStore.Upsert(list, Profile("Sword + bow", a: "5"));

        Assert.Single(list);
        Assert.Equal("5", list[0].SlotA);
    }

    [Fact]
    public void SavingUnderANewNameAddsIt()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow") };

        SwitcherStore.Upsert(list, Profile("Pick + gumdrop"));

        Assert.Equal(2, list.Count);
    }

    /// <summary>Names are the identity, so case must not create a twin.</summary>
    [Fact]
    public void NamesMatchWithoutRegardToCase()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow") };

        SwitcherStore.Upsert(list, Profile("SWORD + BOW"));

        Assert.Single(list);
    }

    [Fact]
    public void ANewSwitcherGetsANameNobodyIsUsing()
    {
        var list = new List<SwitcherProfile> { Profile("Switcher"), Profile("Switcher 2") };

        string name = SwitcherStore.UnusedName(list);

        Assert.DoesNotContain(list, s => string.Equals(s.Name, name, System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheFirstSwitcherGetsThePlainName()
    {
        Assert.Equal("Switcher", SwitcherStore.UnusedName(new List<SwitcherProfile>()));
    }

    // ---- being switched off ----

    /// <summary>
    /// Same rule the macros follow: switched off releases the hotkey claim so
    /// something else may take it.
    /// </summary>
    [Fact]
    public void ADisabledSwitcherDoesNotHoldItsKey()
    {
        var off = Profile(enabled: false) with { Hotkey = HotkeyBinding.FromKey(System.Windows.Input.Key.G) };

        var claims = new[] { new HotkeyClaim(off.Name, off.Hotkey.VirtualKey, off.Enabled) };

        Assert.False(HotkeyClaims.IsTaken(claims, off.Hotkey.VirtualKey));
    }

    [Fact]
    public void AnEnabledSwitcherHoldsItsKey()
    {
        var on = Profile() with { Hotkey = HotkeyBinding.FromKey(System.Windows.Input.Key.G) };

        var claims = new[] { new HotkeyClaim(on.Name, on.Hotkey.VirtualKey, on.Enabled) };

        Assert.True(HotkeyClaims.IsTaken(claims, on.Hotkey.VirtualKey));
    }
}
