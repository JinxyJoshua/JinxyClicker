using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Where a build is allowed to fetch an update from.
/// </summary>
/// <remarks>
/// This decides what gets downloaded and executed, so the interesting cases are
/// the refusals. The one that motivated it: a dev build pointed at the public
/// releases would download the public installer and replace itself with the
/// public app — the developer panel would disappear, and it would look like a
/// bug rather than like the build overwriting itself with a different product.
/// </remarks>
public class UpdateSourceTests
{
    [Fact]
    public void ThePublicSourceIsTheRealRepository()
    {
        Assert.Equal("JinxyJoshua", UpdateSource.Public.Owner);
        Assert.Equal("JinxyClicker", UpdateSource.Public.Repo);
        Assert.True(UpdateSource.Public.CanUpdate);
        Assert.False(UpdateSource.Public.IsPrivate);
    }

    /// <summary>
    /// The safe default for a dev build. Nothing configured must mean nothing
    /// downloaded, never "fall back to the public release".
    /// </summary>
    [Fact]
    public void ASourceWithNothingConfiguredCannotUpdate()
    {
        Assert.False(UpdateSource.None.CanUpdate);
        Assert.False(new UpdateSource("", "").CanUpdate);
        Assert.False(new UpdateSource("someone", "").CanUpdate);
        Assert.False(new UpdateSource("", "something").CanUpdate);
    }

    [Fact]
    public void AsksTheApiAboutItsOwnRepository()
    {
        Assert.Equal(
            "https://api.github.com/repos/me/private-app/releases/latest",
            new UpdateSource("me", "private-app", "tok").LatestReleaseUrl);
    }

    // ---- which address an asset is fetched from ----

    /// <summary>
    /// A private release's browser_download_url serves a login page to anyone
    /// without a session. Downloading that would install an HTML page renamed
    /// to .exe, and nothing about it would look like a failure.
    /// </summary>
    [Fact]
    public void APrivateSourceDownloadsFromTheApiNotTheBrowserUrl()
    {
        Assert.Equal("url", new UpdateSource("me", "repo", "tok").AssetUrlField);
    }

    [Fact]
    public void APublicSourceDownloadsFromTheBrowserUrl()
    {
        Assert.Equal("browser_download_url", UpdateSource.Public.AssetUrlField);
    }

    // ---- where a download may come from ----

    [Theory]
    [InlineData("https://github.com/o/r/releases/download/v1/setup.exe")]
    [InlineData("https://objects.githubusercontent.com/x")]
    [InlineData("https://release-assets.githubusercontent.com/x")]
    public void AcceptsGithubsOwnDownloadHosts(string url)
    {
        Assert.True(UpdateSource.Public.IsTrustedDownload(url));
    }

    [Theory]
    [InlineData("http://github.com/o/r/setup.exe")]          // not https
    [InlineData("https://github.com.evil.example/setup.exe")] // lookalike
    [InlineData("https://evil.example/setup.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void RefusesAnywhereElse(string? url)
    {
        Assert.False(UpdateSource.Public.IsTrustedDownload(url));
    }

    /// <summary>
    /// The API host is where a private release's assets live, so a private
    /// source has to accept it.
    /// </summary>
    [Fact]
    public void APrivateSourceMayDownloadFromTheApi()
    {
        Assert.True(new UpdateSource("me", "repo", "tok")
            .IsTrustedDownload("https://api.github.com/repos/me/repo/releases/assets/1"));
    }

    /// <summary>
    /// And a public source may not. The public path has no reason to download
    /// from the API, and the narrower list is the narrower thing to get wrong.
    /// </summary>
    [Fact]
    public void APublicSourceMayNot()
    {
        Assert.False(UpdateSource.Public
            .IsTrustedDownload("https://api.github.com/repos/o/r/releases/assets/1"));
    }

    /// <summary>
    /// Whatever a build points at, the shipped default has to be the public
    /// releases — a public build that shipped pointing anywhere else would
    /// update thousands of people from the wrong place.
    /// </summary>
    [Fact]
    public void TheDefaultSourceIsPublic()
    {
        Assert.Equal(UpdateSource.Public.Owner, UpdateSource.Current.Owner);
        Assert.Equal(UpdateSource.Public.Repo, UpdateSource.Current.Repo);
        Assert.False(UpdateSource.Current.IsPrivate);
    }
}
