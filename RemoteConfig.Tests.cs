using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Reading the published settings file.
/// </summary>
/// <remarks>
/// This file is fetched over the network and changes how the app behaves on
/// every machine running it, so almost everything here is about refusing to be
/// harmed by it. The rule the tests enforce: a config can only choose a value
/// the app would already have accepted, and anything it gets wrong falls back
/// to what shipped.
/// </remarks>
public class RemoteConfigTests
{
    [Fact]
    public void ReadsTheValuesItRecognises()
    {
        RemoteConfig config = RemoteConfig.Parse(
            """{"hitFixMinDownMs":12,"hitFixMinUpMs":9,"macroEquipMs":80}""");

        Assert.Equal(12, config.HitFixMinDownMs);
        Assert.Equal(9, config.HitFixMinUpMs);
        Assert.Equal(80, config.MacroEquipMs);
    }

    /// <summary>
    /// A config naming one setting changes that one and leaves the rest alone.
    /// </summary>
    [Fact]
    public void EverythingUnmentionedKeepsItsShippedValue()
    {
        RemoteConfig config = RemoteConfig.Parse("""{"hitFixMinDownMs":11}""");

        Assert.Equal(11, config.HitFixMinDownMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinUpMs, config.HitFixMinUpMs);
        Assert.Equal(KeyMacro.DefaultEquipMs, config.MacroEquipMs);
        Assert.True(config.RecorderEnabled);
    }

    // ---- it cannot ask for something the app would refuse ----

    /// <summary>
    /// The bound that matters. A zero or negative press time would mean clicks
    /// the game cannot register, delivered to everybody at once.
    /// </summary>
    [Theory]
    [InlineData("""{"hitFixMinDownMs":0}""", 1)]
    [InlineData("""{"hitFixMinDownMs":-50}""", 1)]
    [InlineData("""{"hitFixMinDownMs":100000}""", 100)]
    public void ClampsAValueOutOfRange(string json, double expected)
    {
        Assert.Equal(expected, RemoteConfig.Parse(json).HitFixMinDownMs);
    }

    [Theory]
    [InlineData("""{"macroEquipMs":-5}""", 0)]
    [InlineData("""{"macroEquipMs":999999}""", 1000)]
    public void ClampsTheEquipDelayToo(string json, int expected)
    {
        Assert.Equal(expected, RemoteConfig.Parse(json).MacroEquipMs);
    }

    /// <summary>A value of the wrong shape is ignored, not coerced.</summary>
    [Theory]
    [InlineData("""{"hitFixMinDownMs":"twelve"}""")]
    [InlineData("""{"hitFixMinDownMs":null}""")]
    [InlineData("""{"hitFixMinDownMs":{"a":1}}""")]
    [InlineData("""{"hitFixMinDownMs":[12]}""")]
    public void IgnoresAValueThatIsNotANumber(string json)
    {
        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs,
            RemoteConfig.Parse(json).HitFixMinDownMs);
    }

    // ---- switches ----

    [Fact]
    public void CanTurnAFeatureOff()
    {
        RemoteConfig config = RemoteConfig.Parse(
            """{"recorderEnabled":false,"kitArtFetchEnabled":false}""");

        Assert.False(config.RecorderEnabled);
        Assert.False(config.KitArtFetchEnabled);
    }

    /// <summary>
    /// A switch of the wrong type stays on. Features fail to enabled, because a
    /// typo in this file must not silently remove something people rely on.
    /// </summary>
    [Theory]
    [InlineData("""{"recorderEnabled":"false"}""")]
    [InlineData("""{"recorderEnabled":0}""")]
    [InlineData("""{"recorderEnabled":null}""")]
    public void ASwitchOfTheWrongTypeIsIgnored(string json)
    {
        Assert.True(RemoteConfig.Parse(json).RecorderEnabled);
    }

    // ---- the notice ----

    [Fact]
    public void ReadsANotice()
    {
        Assert.Equal("Known issue with recording, fix coming.",
            RemoteConfig.Parse("""{"notice":"Known issue with recording, fix coming."}""").Notice);
    }

    /// <summary>A mistake in the file must not become a wall of text on screen.</summary>
    [Fact]
    public void CapsTheNoticeLength()
    {
        string huge = new string('x', 5000);

        Assert.Equal(RemoteConfig.MaxNoticeLength,
            RemoteConfig.Parse($$"""{"notice":"{{huge}}"}""").Notice.Length);
    }

    // ---- anything at all could come back ----

    /// <summary>
    /// Every one of these has to leave the app running on its shipped defaults.
    /// The file is fetched over a network and might be a 404 page, a rate-limit
    /// message, or simply absent.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("404: Not Found")]
    [InlineData("""{"unknownKey":true}""")]
    public void FallsBackToTheShippedValues(string json)
    {
        RemoteConfig config = RemoteConfig.Parse(json);

        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, config.HitFixMinDownMs);
        Assert.Equal(ClickTimings.DefaultHitFixMinUpMs, config.HitFixMinUpMs);
        Assert.Equal(KeyMacro.DefaultEquipMs, config.MacroEquipMs);
        Assert.True(config.RecorderEnabled);
        Assert.True(config.KitArtFetchEnabled);
        Assert.Equal("", config.Notice);
    }

    // ---- where it may come from ----

    /// <summary>
    /// Pinned for the same reason the updater pins its own host: whatever this
    /// returns changes how the app behaves everywhere.
    /// </summary>
    [Fact]
    public void OnlyEverReadsFromRawGithub()
    {
        Assert.True(RemoteConfig.IsTrusted(RemoteConfig.Url));
        Assert.StartsWith("https://raw.githubusercontent.com/", RemoteConfig.Url);
        Assert.Contains($"/{Updater.Owner}/{Updater.Repo}/", RemoteConfig.Url);
    }

    [Theory]
    [InlineData("http://raw.githubusercontent.com/x")]
    [InlineData("https://raw.githubusercontent.com.evil.example/x")]
    [InlineData("https://evil.example/config.json")]
    [InlineData("file:///C:/config.json")]
    [InlineData(null)]
    public void RefusesAnywhereElse(string? url)
    {
        Assert.False(RemoteConfig.IsTrusted(url));
    }

    /// <summary>The file shipped in the repo has to be one the app can read.</summary>
    [Fact]
    public void TheConfigInTheRepositoryParses()
    {
        string path = System.IO.Path.Combine(
            System.AppContext.BaseDirectory, "..", "..", "..", "..", "config.json");

        if (!System.IO.File.Exists(path)) return;

        RemoteConfig config = RemoteConfig.Parse(System.IO.File.ReadAllText(path));

        Assert.Equal(ClickTimings.DefaultHitFixMinDownMs, config.HitFixMinDownMs);
        Assert.True(config.RecorderEnabled);
    }
}
