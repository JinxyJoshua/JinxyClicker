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

    /// <summary>Hiding overlays from capture is also a choice, not a default.</summary>
    [Fact]
    public void StreamerModeIsOffUntilItIsAskedFor()
    {
        Assert.False(new AppSettings().StreamerMode);
    }
}
