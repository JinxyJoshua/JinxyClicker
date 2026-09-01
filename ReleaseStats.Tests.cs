using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Reading download counts out of the releases API.
/// </summary>
/// <remarks>
/// The numbers are shown as fact on a panel, so the parsing has to be right
/// about which releases count and survive whatever the API returns — including
/// the rate-limit object, which comes back with a 200 and is not an array.
/// </remarks>
public class ReleaseStatsTests
{
    private const string Reply = """
    [
      { "tag_name": "v1.4.1", "draft": false,
        "assets": [ { "download_count": 12 }, { "download_count": 3 } ] },
      { "tag_name": "v1.4.0", "draft": false,
        "assets": [ { "download_count": 40 } ] },
      { "tag_name": "v1.5.0-draft", "draft": true,
        "assets": [ { "download_count": 999 } ] },
      { "tag_name": "v1.0.0", "draft": false, "assets": [] }
    ]
    """;

    [Fact]
    public void AddsUpEveryAssetInARelease()
    {
        var releases = ReleaseStats.Read(Reply);

        Assert.Equal(15, releases.Single(r => r.Tag == "v1.4.1").Downloads);
    }

    /// <summary>
    /// A draft cannot be downloaded by anyone but its author, so counting it
    /// would inflate the total with downloads that never happened.
    /// </summary>
    [Fact]
    public void SkipsDrafts()
    {
        Assert.DoesNotContain(ReleaseStats.Read(Reply), r => r.Tag == "v1.5.0-draft");
    }

    [Fact]
    public void ARleaseWithNoAssetsCountsAsZeroRatherThanVanishing()
    {
        Assert.Equal(0, ReleaseStats.Read(Reply).Single(r => r.Tag == "v1.0.0").Downloads);
    }

    [Fact]
    public void TotalsAcrossEveryRelease()
    {
        Assert.Equal(55, ReleaseStats.Total(ReleaseStats.Read(Reply)));
    }

    /// <summary>
    /// This runs against whatever GitHub returns. The rate-limit reply is the
    /// one that matters: it arrives as a 200 with an object, not an array, so
    /// anything assuming a list would throw on the panel opening.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"message":"API rate limit exceeded","documentation_url":"https://docs.github.com"}""")]
    [InlineData("""[{"tag_name":"v1","assets":[{"download_count":"lots"}]}]""")]
    public void SurvivesAnythingTheApiReturns(string json)
    {
        var releases = ReleaseStats.Read(json);

        Assert.Equal(0, ReleaseStats.Total(releases));
    }

    [Fact]
    public void OnlyEverAsksThisRepo()
    {
        Assert.StartsWith(
            $"https://api.github.com/repos/{Updater.Owner}/{Updater.Repo}/releases",
            ReleaseStats.AllReleasesUrl);
    }
}
