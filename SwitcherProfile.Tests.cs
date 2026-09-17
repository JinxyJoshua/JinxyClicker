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

    // ---- carrying the old single switcher across ----

    /// <summary>
    /// Everyone upgrading has a switcher on the old card. Losing it to the
    /// feature that replaced it would be the app taking something away in
    /// exchange for what it added.
    /// </summary>
    [Fact]
    public void TheOldSingleSwitcherBecomesTheFirstEntry()
    {
        var settings = new AppSettings
        {
            SwitcherSlotA = "3",
            SwitcherSlotB = "1",
            SwitcherIntervalMs = 21,
            SwitcherIntervalBMs = 1300,
            SwitcherEquipMs = 5
        };

        SwitcherProfile carried = SwitcherStore.FromSingle(settings, HotkeyBinding.Unbound);

        Assert.Equal("3", carried.SlotA);
        Assert.Equal("1", carried.SlotB);
        Assert.Equal(21, carried.HoldFirstMs);
        Assert.Equal(1300, carried.HoldSecondMs);
        Assert.Equal(5, carried.EquipMs);
        Assert.False(string.IsNullOrWhiteSpace(carried.Name));
    }

    /// <summary>The key that started it still starts it.</summary>
    [Fact]
    public void TheOldSwitcherKeepsItsHotkey()
    {
        HotkeyBinding key = HotkeyBinding.FromKey(System.Windows.Input.Key.G);

        Assert.Equal(key, SwitcherStore.FromSingle(new AppSettings(), key).Hotkey);
    }

    /// <summary>And a switcher that was disabled comes across disabled.</summary>
    [Fact]
    public void TheOldSwitcherKeepsBeingSwitchedOff()
    {
        var settings = new AppSettings { SwitcherDisabled = true };

        Assert.False(SwitcherStore.FromSingle(settings, HotkeyBinding.Unbound).Enabled);
    }

    // ---- the defaults a fresh install starts from ----

    /// <summary>
    /// These are the numbers the app's author plays on, not round figures
    /// picked to look reasonable, and a fresh install should start from a
    /// setup somebody has actually used.
    /// </summary>
    [Fact]
    public void AFreshInstallStartsFromTheTunedSwitcher()
    {
        var fresh = new AppSettings();

        Assert.Equal("4", fresh.SwitcherSlotA);
        Assert.Equal("1", fresh.SwitcherSlotB);
        Assert.Equal(21, fresh.SwitcherIntervalMs);
        Assert.Equal(5, fresh.SwitcherEquipMs);
        Assert.Equal(1300, fresh.SwitcherIntervalBMs);
    }

    /// <summary>
    /// And that default has to be a switcher that will actually run, or a fresh
    /// install would open on a card explaining what is wrong with itself.
    /// </summary>
    [Fact]
    public void TheDefaultSwitcherIsUsable()
    {
        SwitcherProfile fresh = SwitcherStore.FromSingle(new AppSettings(), HotkeyBinding.Unbound);

        Assert.Null(fresh.Problem());
    }

    /// <summary>
    /// The short first hold is deliberate: the engine raises it to whatever
    /// still lands a click, which is the equip delay plus two click periods.
    /// It is not a value anyone has to get right by hand.
    /// </summary>
    [Fact]
    public void TheDefaultFirstHoldIsRaisedToWhereAClickLands()
    {
        SwitcherProfile fresh = SwitcherStore.FromSingle(new AppSettings(), HotkeyBinding.Unbound);

        // 5 ms of equip plus two periods at the rate HitFix actually delivers.
        Assert.Equal(65, fresh.EffectiveFirstHoldMs(clickPeriodMs: 30));
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

    // ---- what one key starts ----

    /// <summary>
    /// The reported bug: the combined hotkey stopped every switcher and started
    /// none, so pressing it ran the clicker with the rotation standing still.
    /// </summary>
    [Fact]
    public void TheCombinedKeyStartsSomething()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow") };

        Assert.NotEmpty(SwitcherStore.Startable(list));
    }

    /// <summary>
    /// Every switcher, not the first one. The key stops all of them, so it has
    /// to start all of them or it means two different things depending on which
    /// way it is pressed — and running several at once is why this is a list.
    /// </summary>
    [Fact]
    public void ItStartsEveryEnabledSwitcher()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow"), Profile("Pick + gumdrop") };

        Assert.Equal(2, SwitcherStore.Startable(list).Count());
    }

    /// <summary>Switching one off is how it is kept off that key.</summary>
    [Fact]
    public void ASwitchedOffOneIsLeftAlone()
    {
        var list = new List<SwitcherProfile>
        {
            Profile("Sword + bow"),
            Profile("Pick + gumdrop", enabled: false)
        };

        Assert.Equal("Sword + bow", Assert.Single(SwitcherStore.Startable(list)).Name);
    }

    /// <summary>
    /// A half-finished switcher is skipped rather than refusing the key. Its
    /// card already says what is wrong with it, and one broken profile must not
    /// stop the others running.
    /// </summary>
    [Fact]
    public void AnUnusableOneDoesNotBreakTheKey()
    {
        var list = new List<SwitcherProfile> { Profile("Broken", a: ""), Profile("Sword + bow") };

        Assert.Equal("Sword + bow", Assert.Single(SwitcherStore.Startable(list)).Name);
    }

    [Fact]
    public void NothingEnabledStartsNothing()
    {
        var list = new List<SwitcherProfile> { Profile("Sword + bow", enabled: false) };

        Assert.Empty(SwitcherStore.Startable(list));
    }

    [Fact]
    public void AnEnabledSwitcherHoldsItsKey()
    {
        var on = Profile() with { Hotkey = HotkeyBinding.FromKey(System.Windows.Input.Key.G) };

        var claims = new[] { new HotkeyClaim(on.Name, on.Hotkey.VirtualKey, on.Enabled) };

        Assert.True(HotkeyClaims.IsTaken(claims, on.Hotkey.VirtualKey));
    }
}
