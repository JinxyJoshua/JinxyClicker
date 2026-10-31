using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>The leagues the score overlay can follow, with their ESPN path segment.</summary>
public enum ScoreLeague { CollegeFootball, NFL, NBA, NHL, MLB }

/// <summary>
/// One game's live state, UI-free so it can be parsed and tested without WPF.
/// Colors are the team's hex (no leading '#'); the overlay turns them into brushes.
/// <paramref name="State"/> is ESPN's "pre" / "in" / "post".
/// </summary>
public sealed record GameInfo(
    string Id,
    string ShortName,
    string AwayAbbr, string AwayColorHex, int AwayScore, string AwayId, string AwayLogo, int AwayRank,
    string HomeAbbr, string HomeColorHex, int HomeScore, string HomeId, string HomeLogo, int HomeRank,
    string StatusText, string State, string PossessionId, string DownDistance)
{
    public bool InProgress => State == "in";
    public bool Final => State == "post";
    /// <summary>The better (lower) of the two teams' AP ranks; 99 when neither is ranked.</summary>
    public int BestRank => Math.Min(AwayRank <= 0 ? 99 : AwayRank, HomeRank <= 0 ? 99 : HomeRank);
    public bool AwayHasBall => PossessionId.Length > 0 && PossessionId == AwayId;
    public bool HomeHasBall => PossessionId.Length > 0 && PossessionId == HomeId;
}

/// <summary>A team in the favorites picker: abbreviation, full name, logo URL.</summary>
public sealed record TeamInfo(string Abbr, string Name, string Logo);

/// <summary>
/// Reads live scores from ESPN's public scoreboard API. The HTTP fetch and the JSON
/// parsing are separate so the parsing — the part that actually breaks when a payload
/// shifts — can be tested against captured JSON without a network call.
/// </summary>
public static class SportsScores
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // ESPN's site API serves JSON to browsers; a UA keeps it from 403-ing.
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JinxyClicker");
        return c;
    }

    public static string PathFor(ScoreLeague league) => league switch
    {
        ScoreLeague.CollegeFootball => "football/college-football",
        ScoreLeague.NFL => "football/nfl",
        ScoreLeague.NBA => "basketball/nba",
        ScoreLeague.NHL => "hockey/nhl",
        ScoreLeague.MLB => "baseball/mlb",
        _ => "football/nfl"
    };

    public static string DisplayName(ScoreLeague league) => league switch
    {
        ScoreLeague.CollegeFootball => "College Football",
        ScoreLeague.NFL => "NFL",
        ScoreLeague.NBA => "NBA",
        ScoreLeague.NHL => "NHL",
        ScoreLeague.MLB => "MLB",
        _ => league.ToString()
    };

    public static string UrlFor(ScoreLeague league) =>
        $"https://site.api.espn.com/apis/site/v2/sports/{PathFor(league)}/scoreboard";

    /// <summary>Today's games for a league, or an empty list on any failure.</summary>
    public static async Task<List<GameInfo>> GetGamesAsync(ScoreLeague league)
    {
        try
        {
            string json = await Http.GetStringAsync(UrlFor(league)).ConfigureAwait(false);
            return ParseScoreboard(json);
        }
        catch
        {
            return new List<GameInfo>();
        }
    }

    /// <summary>Today's college-football games involving a ranked (AP Top 25) team,
    /// best rank first.</summary>
    public static async Task<List<GameInfo>> GetTop25GamesAsync()
    {
        List<GameInfo> all = await GetGamesAsync(ScoreLeague.CollegeFootball).ConfigureAwait(false);
        List<GameInfo> ranked = all.FindAll(g => g.BestRank <= 25);
        ranked.Sort((a, b) => a.BestRank.CompareTo(b.BestRank));
        return ranked;
    }

    /// <summary>Every team in a league (for the favorites picker), or empty on failure.</summary>
    public static async Task<List<TeamInfo>> GetTeamsAsync(ScoreLeague league)
    {
        try
        {
            string url = $"https://site.api.espn.com/apis/site/v2/sports/{PathFor(league)}/teams?limit=1000";
            return ParseTeams(await Http.GetStringAsync(url).ConfigureAwait(false));
        }
        catch
        {
            return new List<TeamInfo>();
        }
    }

    /// <summary>Parses an ESPN teams payload. Never throws; a bad entry is skipped.</summary>
    public static List<TeamInfo> ParseTeams(string json)
    {
        var teams = new List<TeamInfo>();
        if (string.IsNullOrWhiteSpace(json)) return teams;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sports", out JsonElement sports)
                || sports.ValueKind != JsonValueKind.Array || sports.GetArrayLength() == 0) return teams;
            if (!sports[0].TryGetProperty("leagues", out JsonElement leagues)
                || leagues.ValueKind != JsonValueKind.Array || leagues.GetArrayLength() == 0) return teams;
            if (!leagues[0].TryGetProperty("teams", out JsonElement arr)
                || arr.ValueKind != JsonValueKind.Array) return teams;

            foreach (JsonElement t in arr.EnumerateArray())
            {
                if (!t.TryGetProperty("team", out JsonElement team)) continue;
                string abbr = Str(team, "abbreviation");
                if (abbr.Length == 0) continue;
                string name = Str(team, "displayName");
                string logo = "";
                if (team.TryGetProperty("logos", out JsonElement logos)
                    && logos.ValueKind == JsonValueKind.Array && logos.GetArrayLength() > 0)
                    logo = Str(logos[0], "href");
                teams.Add(new TeamInfo(abbr, name.Length == 0 ? abbr : name, logo));
            }
        }
        catch { }
        return teams;
    }

    /// <summary>Re-reads a single game by id, or null if it is no longer listed.</summary>
    public static async Task<GameInfo?> GetGameAsync(ScoreLeague league, string id)
    {
        foreach (GameInfo g in await GetGamesAsync(league).ConfigureAwait(false))
            if (g.Id == id) return g;
        return null;
    }

    /// <summary>Parses an ESPN scoreboard payload into games. Never throws — a bad or
    /// partial event is skipped rather than failing the whole list.</summary>
    public static List<GameInfo> ParseScoreboard(string json)
    {
        var games = new List<GameInfo>();
        if (string.IsNullOrWhiteSpace(json)) return games;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("events", out JsonElement events)
                || events.ValueKind != JsonValueKind.Array)
                return games;

            foreach (JsonElement ev in events.EnumerateArray())
            {
                GameInfo? g = ParseEvent(ev);
                if (g != null) games.Add(g);
            }
        }
        catch { /* malformed payload — return whatever parsed */ }

        return games;
    }

    private static GameInfo? ParseEvent(JsonElement ev)
    {
        try
        {
            string id = Str(ev, "id");
            string shortName = Str(ev, "shortName");

            if (!ev.TryGetProperty("competitions", out JsonElement comps)
                || comps.ValueKind != JsonValueKind.Array || comps.GetArrayLength() == 0)
                return null;
            JsonElement comp = comps[0];

            string statusText = "", state = "";
            if (ev.TryGetProperty("status", out JsonElement st)
                && st.TryGetProperty("type", out JsonElement stType))
            {
                statusText = Str(stType, "shortDetail");
                state = Str(stType, "state");
            }

            // Football possession / down live in the competition's "situation".
            string possession = "", down = "";
            if (comp.TryGetProperty("situation", out JsonElement sit))
            {
                possession = Str(sit, "possession");
                down = Str(sit, "downDistanceText");
            }

            if (!comp.TryGetProperty("competitors", out JsonElement cs)
                || cs.ValueKind != JsonValueKind.Array)
                return null;

            string awA = "", awC = "", awId = "", awLogo = "", hmA = "", hmC = "", hmId = "", hmLogo = "";
            int awS = 0, hmS = 0, awRank = 99, hmRank = 99;
            bool haveHome = false, haveAway = false;

            foreach (JsonElement c in cs.EnumerateArray())
            {
                bool home = Str(c, "homeAway") == "home";
                string abbr = "", color = "", teamId = "", logo = "";
                if (c.TryGetProperty("team", out JsonElement team))
                {
                    abbr = Str(team, "abbreviation");
                    color = Str(team, "color");
                    teamId = Str(team, "id");
                    logo = Str(team, "logo");
                }
                int score = int.TryParse(Str(c, "score"), out int sv) ? sv : 0;
                int rank = 99;
                if (c.TryGetProperty("curatedRank", out JsonElement cr)
                    && cr.TryGetProperty("current", out JsonElement cur)
                    && cur.TryGetInt32(out int rv) && rv > 0 && rv <= 25)
                    rank = rv;

                if (home) { hmA = abbr; hmC = color; hmS = score; hmId = teamId; hmLogo = logo; hmRank = rank; haveHome = true; }
                else { awA = abbr; awC = color; awS = score; awId = teamId; awLogo = logo; awRank = rank; haveAway = true; }
            }

            if (!haveHome || !haveAway) return null;
            return new GameInfo(id, shortName,
                awA, awC, awS, awId, awLogo, awRank,
                hmA, hmC, hmS, hmId, hmLogo, hmRank,
                statusText, state, possession, down);
        }
        catch { return null; }
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
