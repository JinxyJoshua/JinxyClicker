using System.Collections.Generic;
using System.Text.Json;
using JinxyClicker;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// A setup profile bundles the whole app's look-and-feel under a name, so a
/// player can flip between "Ranked", "Chill", etc. in one click.
/// </summary>
public class SetupProfileTests
{
    [Theory]
    [InlineData("  Ranked  ", "Ranked")]
    [InlineData("a  b", "a b")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void CleanNameTrimsAndRejectsEmpty(string? input, string? expected)
        => Assert.Equal(expected, SetupProfiles.CleanName(input));

    [Fact]
    public void CleanNameCapsLength()
    {
        string big = new('x', 40);
        Assert.Equal(SetupProfiles.MaxNameLength, SetupProfiles.CleanName(big)!.Length);
    }

    [Fact]
    public void SaveAddsANewProfile()
    {
        var list = new List<SetupProfile>();
        SetupProfiles.Save(list, new SetupProfile { Name = "Ranked" });
        Assert.Single(list);
        Assert.Equal("Ranked", list[0].Name);
    }

    [Fact]
    public void SaveReplacesByNameCaseInsensitivelyKeepingPosition()
    {
        var list = new List<SetupProfile>
        {
            new() { Name = "Ranked", AccentColor = "#111111" },
            new() { Name = "Chill" }
        };
        SetupProfiles.Save(list, new SetupProfile { Name = "ranked", AccentColor = "#FF0000" });

        Assert.Equal(2, list.Count);
        Assert.Equal("#FF0000", list[0].AccentColor);  // replaced in place
        Assert.Equal("Chill", list[1].Name);
    }

    [Fact]
    public void RemoveDeletesByNameCaseInsensitively()
    {
        var list = new List<SetupProfile> { new() { Name = "Ranked" }, new() { Name = "Chill" } };
        SetupProfiles.Remove(list, "RANKED");
        Assert.Single(list);
        Assert.Equal("Chill", list[0].Name);
    }

    [Fact]
    public void FindIsCaseInsensitiveAndNullSafe()
    {
        var list = new List<SetupProfile> { new() { Name = "Ranked" } };
        Assert.Equal("Ranked", SetupProfiles.Find(list, "ranked")!.Name);
        Assert.Null(SetupProfiles.Find(list, "nope"));
        Assert.Null(SetupProfiles.Find(list, null));
    }

    [Theory]
    [InlineData("6872265039", "https://www.roblox.com/games/6872265039")]       // bare place id
    [InlineData("  6872265039  ", "https://www.roblox.com/games/6872265039")]   // trimmed
    [InlineData("https://www.roblox.com/games/6872265039/BedWars", "https://www.roblox.com/games/6872265039/BedWars")] // full url kept
    [InlineData("www.roblox.com/games/6872265039", "https://www.roblox.com/games/6872265039")] // add scheme
    [InlineData("roblox://experiences/start?placeId=6872265039", "roblox://experiences/start?placeId=6872265039")]     // deep link kept
    public void ResolvePlayUrlNormalizes(string input, string expected)
        => Assert.Equal(expected, SetupProfiles.ResolvePlayUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolvePlayUrlIsNullWhenBlank(string? input)
        => Assert.Null(SetupProfiles.ResolvePlayUrl(input));

    [Theory]
    [InlineData("6872265039", "roblox://experiences/start?placeId=6872265039")]                                  // bare id -> launch
    [InlineData("https://www.roblox.com/games/6872265039/BedWars", "roblox://experiences/start?placeId=6872265039")] // id pulled from url
    [InlineData("www.roblox.com/games/6872265039", "roblox://experiences/start?placeId=6872265039")]
    [InlineData("roblox://experiences/start?placeId=6872265039", "roblox://experiences/start?placeId=6872265039")] // deep link kept
    public void ResolvePlayLaunchBuildsADeepLink(string input, string expected)
        => Assert.Equal(expected, SetupProfiles.ResolvePlayLaunch(input));

    [Fact]
    public void ResolvePlayLaunchFallsBackToTheUrlWhenNoIdIsFound()
        => Assert.Equal("https://example.com/foo", SetupProfiles.ResolvePlayLaunch("https://example.com/foo"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ResolvePlayLaunchIsNullWhenBlank(string? input)
        => Assert.Null(SetupProfiles.ResolvePlayLaunch(input));

    [Fact]
    public void ProfileRoundTripsThroughJson()
    {
        var p = new SetupProfile
        {
            Name = "Ranked",
            CrosshairName = "Red Dot",
            ClickPresetName = "Measured",
            FastFlagsOn = true,
            ThemeMode = "Dark",
            AccentColor = "#FF4B52"
        };
        var back = JsonSerializer.Deserialize<SetupProfile>(JsonSerializer.Serialize(p))!;

        Assert.Equal("Ranked", back.Name);
        Assert.Equal("Red Dot", back.CrosshairName);
        Assert.Equal("Measured", back.ClickPresetName);
        Assert.True(back.FastFlagsOn);
        Assert.Equal("Dark", back.ThemeMode);
        Assert.Equal("#FF4B52", back.AccentColor);
    }
}
