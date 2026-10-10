using JinxyClicker;
using Xunit;

namespace JinxyClicker.Tests;

public class RobloxJoinLogTests
{
    [Fact]
    public void ParsesPlaceAndUniverseFromAJoinLoadtimeLine()
    {
        string log = "noise\n"
            + "2026-10-10T19:25:58Z [FLog::GameJoinLoadTime] Report game_join_loadtime: "
            + "placeid:6872265039, join_time:0.56, universeid:2619619496, referral_page:DeepLink\n"
            + "more noise";

        var games = RobloxJoinLog.ParseRecent(log);

        Assert.Single(games);
        Assert.Equal(6872265039L, games[0].PlaceId);
        Assert.Equal(2619619496L, games[0].UniverseId);
    }

    [Fact]
    public void IsMostRecentFirstAndDistinctByPlace()
    {
        string log =
            "placeid:1, join_time:0, universeid:11\n" +
            "placeid:2, join_time:0, universeid:22\n" +
            "placeid:1, join_time:0, universeid:11\n"; // re-joined place 1 last

        var games = RobloxJoinLog.ParseRecent(log);

        Assert.Equal(2, games.Count);
        Assert.Equal(1L, games[0].PlaceId);  // most recent join wins the top spot
        Assert.Equal(2L, games[1].PlaceId);
    }

    [Fact]
    public void RespectsTheMax()
    {
        string log =
            "placeid:1, join_time:0, universeid:11\n" +
            "placeid:2, join_time:0, universeid:22\n" +
            "placeid:3, join_time:0, universeid:33\n";

        Assert.Equal(2, RobloxJoinLog.ParseRecent(log, max: 2).Count);
    }

    [Theory]
    [InlineData("nothing to see here")]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyWhenNoJoins(string? log)
        => Assert.Empty(RobloxJoinLog.ParseRecent(log));
}
