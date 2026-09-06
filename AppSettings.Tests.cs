using System.Text.Json;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The defaults people get without choosing anything.
/// </summary>
/// <remarks>
/// Two of these shipped wrong in the same way: something was on because nobody
/// had said otherwise, and it then appeared over everyone's game. A default is
/// a decision made on behalf of every user who never opens the setting, so the
/// ones that put something on screen are pinned here.
/// </remarks>
public class AppSettingsDefaultsTests
{
    /// <summary>
    /// Anything drawn on top of a game has to be opted into. This shipped on,
    /// so it appeared the first time anyone clicked, without being asked for.
    /// </summary>
    [Fact]
    public void TheLiveReadoutIsOffUntilItIsAskedFor()
    {
        Assert.False(new AppSettings().OverlayOn);
    }

    /// <summary>
    /// Settings are stored as the whole object by property name, and every file
    /// written before the readout was turned off carries "ShowOverlay": true —
    /// not because anyone chose it, but because that was the default. Reading
    /// that old name would leave the overlay on for exactly the people it is
    /// being turned off for.
    /// </summary>
    [Fact]
    public void AnOldSettingsFileDoesNotTurnTheReadoutBackOn()
    {
        const string legacy = """{"ShowOverlay":true,"StreamerMode":false}""";

        AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(legacy);

        Assert.NotNull(loaded);
        Assert.False(loaded!.OverlayOn);
    }

    /// <summary>And a file that does say so is honoured.</summary>
    [Fact]
    public void AChoiceToTurnItOnIsKept()
    {
        AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>("""{"OverlayOn":true}""");

        Assert.True(loaded!.OverlayOn);
    }

    /// <summary>
    /// A round trip has to preserve it, or turning it on would last only until
    /// the next launch.
    /// </summary>
    [Fact]
    public void TheChoiceSurvivesBeingWrittenAndReadBack()
    {
        var settings = new AppSettings { OverlayOn = true };

        AppSettings? back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.True(back!.OverlayOn);
    }

    /// <summary>
    /// Null means nobody has chosen a monitor, which resolves to the primary
    /// one. It used to mean "every display", which captured the whole virtual
    /// desktop by default.
    /// </summary>
    [Fact]
    public void NoMonitorIsChosenByDefault()
    {
        Assert.Null(new AppSettings().RecordDisplay);
    }

    /// <summary>
    /// And a deliberate choice of every display has a value of its own, so it
    /// can be told apart from never having chosen.
    /// </summary>
    [Fact]
    public void ChoosingEveryDisplayIsStoredAsSomethingOtherThanNull()
    {
        Assert.False(string.IsNullOrEmpty(AppSettings.AllDisplays));
    }

    // ---- defaults are for new installs only ----

    /// <summary>
    /// The switcher defaults are the app author's own tuned setup, which is the
    /// right starting point for somebody who has never opened the app and the
    /// wrong thing to impose on somebody who has. A stored file wins.
    /// </summary>
    [Fact]
    public void AnExistingSettingsFileKeepsItsOwnSwitcher()
    {
        const string theirs = """
            {"SwitcherSlotA":"7","SwitcherSlotB":"9","SwitcherIntervalMs":300,
             "SwitcherEquipMs":80,"SwitcherIntervalBMs":250}
            """;

        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(theirs)!;

        Assert.Equal("7", loaded.SwitcherSlotA);
        Assert.Equal("9", loaded.SwitcherSlotB);
        Assert.Equal(300, loaded.SwitcherIntervalMs);
        Assert.Equal(80, loaded.SwitcherEquipMs);
        Assert.Equal(250, loaded.SwitcherIntervalBMs);
    }

    /// <summary>
    /// And the switcher carried into the list comes from what they had, not
    /// from the defaults — otherwise upgrading would silently retune the
    /// switcher somebody had already set up.
    /// </summary>
    [Fact]
    public void UpgradingCarriesTheirSwitcherRatherThanTheDefault()
    {
        const string theirs = """
            {"SwitcherSlotA":"7","SwitcherSlotB":"9","SwitcherIntervalMs":300,
             "SwitcherEquipMs":80,"SwitcherIntervalBMs":250}
            """;

        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(theirs)!;

        SwitcherProfile carried = SwitcherStore.FromSingle(loaded, HotkeyBinding.Unbound);

        Assert.Equal("7", carried.SlotA);
        Assert.Equal(300, carried.HoldFirstMs);
        Assert.Equal(80, carried.EquipMs);
        Assert.Equal(250, carried.HoldSecondMs);
    }

    /// <summary>
    /// Only somebody with no settings file at all gets the tuned defaults.
    /// </summary>
    [Fact]
    public void SomebodyWhoHasNeverOpenedItGetsTheTunedSwitcher()
    {
        var fresh = new AppSettings();

        Assert.Equal("4", fresh.SwitcherSlotA);
        Assert.Equal(21, fresh.SwitcherIntervalMs);
        Assert.Equal(5, fresh.SwitcherEquipMs);
        Assert.Equal(1300, fresh.SwitcherIntervalBMs);
    }

    /// <summary>Hiding overlays from capture is also a choice, not a default.</summary>
    [Fact]
    public void StreamerModeIsOffUntilItIsAskedFor()
    {
        Assert.False(new AppSettings().StreamerMode);
    }
}
