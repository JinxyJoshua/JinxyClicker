using System;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Deciding whether a newer build exists, and what to download.
/// </summary>
/// <remarks>
/// Every failure here is silent by design — the check runs at startup and says
/// nothing when it finds nothing, so a tag that will not parse reads as "no
/// update available" forever rather than as a bug. That is exactly why the
/// parsing is tested rather than trusted.
/// </remarks>
public class UpdaterTests
{
    [Theory]
    [InlineData("v1.3.0", "1.3.0")]
    [InlineData("1.3.0", "1.3.0")]
    [InlineData("V2.0.1", "2.0.1")]
    [InlineData("  v1.2.0  ", "1.2.0")]
    public void ReadsATagHoweverItWasWritten(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), Updater.ParseVersion(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("v")]
    public void RefusesATagItCannotUnderstand(string? tag)
    {
        Assert.Null(Updater.ParseVersion(tag));
    }

    /// <summary>
    /// Strictly newer. Equal is the ordinary case — the version already running
    /// — and offering it would prompt on every single launch.
    /// </summary>
    [Fact]
    public void DoesNotOfferTheVersionAlreadyInstalled()
    {
        Assert.False(Updater.IsNewer(new Version(1, 2, 0), new Version(1, 2, 0)));
    }

    [Fact]
    public void DoesNotOfferAnOlderRelease()
    {
        Assert.False(Updater.IsNewer(new Version(1, 2, 0), new Version(1, 1, 0)));
    }

    [Fact]
    public void OffersANewerRelease()
    {
        Assert.True(Updater.IsNewer(new Version(1, 2, 0), new Version(1, 3, 0)));
    }

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("1.0.0", null)]
    public void OffersNothingWhenEitherVersionIsUnknown(string? current, string? candidate)
    {
        Assert.False(Updater.IsNewer(
            current == null ? null : Version.Parse(current),
            candidate == null ? null : Version.Parse(candidate)));
    }

    // ---- where the download may come from ----

    /// <summary>
    /// The URL arrives in a JSON response and this app runs whatever it points
    /// at, so the host is pinned rather than trusted.
    /// </summary>
    [Theory]
    [InlineData("https://github.com/JinxyJoshua/JinxyClicker/releases/download/v1.3.0/Setup.exe")]
    [InlineData("https://objects.githubusercontent.com/whatever/Setup.exe")]
    public void AcceptsGitHubsOwnDownloadHosts(string url)
    {
        Assert.True(Updater.IsTrustedDownload(url));
    }

    [Theory]
    [InlineData("http://github.com/x/Setup.exe")]              // not https
    [InlineData("https://github.com.evil.example/Setup.exe")]  // lookalike host
    [InlineData("https://evil.example/Setup.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void RefusesAnywhereElse(string? url)
    {
        Assert.False(Updater.IsTrustedDownload(url));
    }

    // ---- picking the installer ----

    /// <summary>
    /// By name, never by position. GitHub adds source archives to every release
    /// on its own, so the first asset is routinely not the installer.
    /// </summary>
    [Fact]
    public void PicksTheInstallerRatherThanWhateverIsFirst()
    {
        var assets = new[]
        {
            ("Source code (zip)", "https://github.com/x/source.zip"),
            ("JinxyAutoClicker-Beta-Setup-1.3.0.exe", "https://github.com/x/Setup.exe")
        };

        Assert.Equal("https://github.com/x/Setup.exe", Updater.PickInstaller(assets));
    }

    /// <summary>An executable that is not a setup is not the update.</summary>
    [Fact]
    public void IgnoresExecutablesThatAreNotTheInstaller()
    {
        var assets = new[] { ("ffmpeg.exe", "https://github.com/x/ffmpeg.exe") };

        Assert.Null(Updater.PickInstaller(assets));
    }

    /// <summary>An installer hosted somewhere untrusted is not offered at all.</summary>
    [Fact]
    public void WillNotPickAnInstallerFromAnUntrustedHost()
    {
        var assets = new[] { ("Setup-1.3.0.exe", "https://evil.example/Setup.exe") };

        Assert.Null(Updater.PickInstaller(assets));
    }

    [Fact]
    public void PicksNothingFromAnEmptyRelease()
    {
        Assert.Null(Updater.PickInstaller(Array.Empty<(string, string)>()));
    }

    // ---- reading GitHub's response ----

    private const string GoodRelease = """
    {
      "tag_name": "v1.3.0",
      "draft": false,
      "prerelease": false,
      "body": "Fixed the switcher.\nAdded right click.",
      "assets": [
        { "name": "Source code (zip)", "browser_download_url": "https://github.com/x/s.zip" },
        { "name": "JinxyAutoClicker-Beta-Setup-1.3.0.exe",
          "browser_download_url": "https://github.com/x/Setup.exe" }
      ]
    }
    """;

    [Fact]
    public void ReadsAPublishedRelease()
    {
        ReleaseInfo? release = Updater.ReadRelease(GoodRelease);

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 3, 0), release!.Version);
        Assert.Equal("v1.3.0", release.Tag);
        Assert.Equal("https://github.com/x/Setup.exe", release.InstallerUrl);
    }

    /// <summary>
    /// A prerelease is opt-in and a draft is not published. Neither is something
    /// to push at everyone who opens the app.
    /// </summary>
    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void SkipsDraftsAndPrereleases(string flag)
    {
        string json = GoodRelease.Replace($"\"{flag}\": false", $"\"{flag}\": true");

        Assert.NotEqual(GoodRelease, json);  // the rewrite has to have landed
        Assert.Null(Updater.ReadRelease(json));
    }

    /// <summary>
    /// A release with no installer attached — uploaded late, or forgotten —
    /// must not produce a prompt with nothing behind it.
    /// </summary>
    [Fact]
    public void OffersNothingWhenNoInstallerWasAttached()
    {
        string json = GoodRelease.Replace(
            "\"name\": \"JinxyAutoClicker-Beta-Setup-1.3.0.exe\"", "\"name\": \"notes.txt\"");

        Assert.Null(Updater.ReadRelease(json));
    }

    /// <summary>
    /// This runs at startup against whatever the network returns. Nothing it
    /// receives may throw.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"tag_name\": \"latest\"}")]
    [InlineData("{\"message\": \"API rate limit exceeded\"}")]
    public void SurvivesAnythingTheNetworkReturns(string json)
    {
        Assert.Null(Updater.ReadRelease(json));
    }

    // ---- notes ----

    [Fact]
    public void TrimsNotesToWhatFitsInAPrompt()
    {
        string notes = string.Join("\n", new string('x', 80), new string('y', 80),
            new string('z', 400));

        string summary = Updater.Summarise(notes, maxLines: 6, maxChars: 100);

        Assert.True(summary.Length <= 101, $"summary was {summary.Length} chars");
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void DropsBlankLinesFromNotes()
    {
        Assert.Equal("one" + Environment.NewLine + "two",
            Updater.Summarise("one\n\n\ntwo"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HasNothingToSayAboutEmptyNotes(string notes)
    {
        Assert.Equal("", Updater.Summarise(notes));
    }

    /// <summary>The check only ever looks at this app's own repository.</summary>
    [Fact]
    public void OnlyEverAsksAboutThisRepository()
    {
        Assert.Equal("https://api.github.com/repos/JinxyJoshua/JinxyClicker/releases/latest",
            Updater.LatestReleaseUrl);
    }
}
