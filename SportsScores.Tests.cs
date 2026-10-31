using System.Linq;
using JinxyClicker;
using Xunit;

public class SportsScoresTests
{
    // Trimmed to exactly the fields SportsScores reads, in ESPN's shape: the first
    // game is live with #17 away @ #3 home and the home team holding the ball.
    private const string Sample = @"{
      ""events"": [
        {
          ""id"": ""401"",
          ""shortName"": ""FLA VS UGA"",
          ""status"": { ""type"": { ""state"": ""in"", ""shortDetail"": ""2nd - 7:12"" } },
          ""competitions"": [
            {
              ""situation"": { ""possession"": ""61"", ""downDistanceText"": ""3rd & 7"" },
              ""competitors"": [
                { ""homeAway"": ""home"", ""score"": ""14"", ""curatedRank"": { ""current"": 3 },
                  ""team"": { ""id"": ""61"", ""abbreviation"": ""UGA"", ""color"": ""ba0c2f"", ""logo"": ""https://a.espncdn.com/i/teamlogos/ncaa/500/61.png"" } },
                { ""homeAway"": ""away"", ""score"": ""10"", ""curatedRank"": { ""current"": 17 },
                  ""team"": { ""id"": ""57"", ""abbreviation"": ""FLA"", ""color"": ""0021a5"", ""logo"": ""https://a.espncdn.com/i/teamlogos/ncaa/500/57.png"" } }
              ]
            }
          ]
        },
        {
          ""id"": ""402"",
          ""shortName"": ""ND VS NAVY"",
          ""status"": { ""type"": { ""state"": ""post"", ""shortDetail"": ""Final"" } },
          ""competitions"": [
            { ""competitors"": [
              { ""homeAway"": ""home"", ""score"": ""28"", ""curatedRank"": { ""current"": 99 },
                ""team"": { ""id"": ""2426"", ""abbreviation"": ""NAVY"", ""color"": ""00225b"" } },
              { ""homeAway"": ""away"", ""score"": ""31"", ""curatedRank"": { ""current"": 5 },
                ""team"": { ""id"": ""87"", ""abbreviation"": ""ND"", ""color"": ""062340"" } }
            ] }
          ]
        }
      ]
    }";

    [Fact]
    public void Parses_every_event()
    {
        Assert.Equal(2, SportsScores.ParseScoreboard(Sample).Count);
    }

    [Fact]
    public void Reads_teams_scores_and_colors_with_home_away_split()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "401");
        Assert.Equal("FLA VS UGA", g.ShortName);
        Assert.Equal("FLA", g.AwayAbbr);
        Assert.Equal("0021a5", g.AwayColorHex);
        Assert.Equal(10, g.AwayScore);
        Assert.Equal("UGA", g.HomeAbbr);
        Assert.Equal("ba0c2f", g.HomeColorHex);
        Assert.Equal(14, g.HomeScore);
    }

    [Fact]
    public void Reads_logos_and_team_ids()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "401");
        Assert.Equal("57", g.AwayId);
        Assert.Equal("61", g.HomeId);
        Assert.EndsWith("/57.png", g.AwayLogo);
        Assert.EndsWith("/61.png", g.HomeLogo);
    }

    [Fact]
    public void Reads_ranks_and_best_rank()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "401");
        Assert.Equal(17, g.AwayRank);
        Assert.Equal(3, g.HomeRank);
        Assert.Equal(3, g.BestRank);
    }

    [Fact]
    public void Unranked_team_reads_as_99()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "402");
        Assert.Equal(99, g.HomeRank);   // NAVY unranked
        Assert.Equal(5, g.AwayRank);    // ND #5
        Assert.Equal(5, g.BestRank);
    }

    [Fact]
    public void Reads_possession_and_down()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "401");
        Assert.Equal("61", g.PossessionId);
        Assert.Equal("3rd & 7", g.DownDistance);
        Assert.True(g.HomeHasBall);
        Assert.False(g.AwayHasBall);
    }

    [Fact]
    public void No_possession_means_neither_team_has_the_ball()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "402");
        Assert.False(g.HomeHasBall);
        Assert.False(g.AwayHasBall);
    }

    [Fact]
    public void Reads_status_and_in_progress_state()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "401");
        Assert.Equal("2nd - 7:12", g.StatusText);
        Assert.True(g.InProgress);
        Assert.False(g.Final);
    }

    [Fact]
    public void Marks_a_finished_game_as_final()
    {
        GameInfo g = SportsScores.ParseScoreboard(Sample).First(x => x.Id == "402");
        Assert.True(g.Final);
        Assert.False(g.InProgress);
        Assert.Equal("Final", g.StatusText);
    }

    [Fact]
    public void Ranked_filter_keeps_only_top_25_games_best_first()
    {
        var ranked = SportsScores.ParseScoreboard(Sample)
            .Where(g => g.BestRank <= 25)
            .OrderBy(g => g.BestRank)
            .ToList();
        Assert.Equal(2, ranked.Count);
        Assert.Equal("401", ranked[0].Id);   // best rank 3 leads
        Assert.Equal("402", ranked[1].Id);   // then rank 5
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"events\": \"oops\"}")]
    public void Bad_payloads_yield_an_empty_list_without_throwing(string json)
    {
        Assert.Empty(SportsScores.ParseScoreboard(json));
    }

    [Fact]
    public void Skips_an_event_missing_a_competitor()
    {
        const string oneSided = @"{""events"":[{""id"":""9"",""shortName"":""X VS Y"",
            ""competitions"":[{""competitors"":[
              {""homeAway"":""home"",""score"":""3"",""team"":{""abbreviation"":""X"",""color"":""fff""}}
            ]}]}]}";
        Assert.Empty(SportsScores.ParseScoreboard(oneSided));
    }

    [Fact]
    public void Non_numeric_score_defaults_to_zero()
    {
        const string noScore = @"{""events"":[{""id"":""1"",""shortName"":""A VS B"",
            ""competitions"":[{""competitors"":[
              {""homeAway"":""home"",""team"":{""abbreviation"":""A"",""color"":""111""}},
              {""homeAway"":""away"",""team"":{""abbreviation"":""B"",""color"":""222""}}
            ]}]}]}";
        GameInfo g = SportsScores.ParseScoreboard(noScore).Single();
        Assert.Equal(0, g.HomeScore);
        Assert.Equal(0, g.AwayScore);
        Assert.Equal(99, g.BestRank);   // no curatedRank -> unranked
    }
}
