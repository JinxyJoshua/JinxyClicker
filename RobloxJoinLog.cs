using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace JinxyClicker;

/// <summary>One game the player joined, as the client log records it.</summary>
public readonly record struct JoinedGame(long PlaceId, long UniverseId);

/// <summary>
/// Reads the games a player has recently joined out of Roblox's own client logs,
/// so they can be offered as pick-to-play tiles.
/// </summary>
/// <remarks>
/// Roblox writes a line on every join that carries both ids:
///   <c>Report game_join_loadtime: placeid:6872265039, join_time:…, universeid:2619619496, …</c>
/// The place id is what launches a game; the universe id is what the public
/// thumbnail and name APIs take. Only files already on disk are read — nothing
/// touches the game process or the network here.
/// </remarks>
public static class RobloxJoinLog
{
    private static readonly Regex JoinLine = new(
        @"placeid:(\d+),.*?universeid:(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The distinct games the log shows were joined, most recent first, capped at
    /// <paramref name="max"/>.
    /// </summary>
    public static IReadOnlyList<JoinedGame> ParseRecent(string? logText, int max = 12)
    {
        var result = new List<JoinedGame>();
        if (string.IsNullOrEmpty(logText)) return result;

        // The log is oldest-first, so collect in order then walk from the end,
        // keeping the first time each place is seen — that is its most recent join.
        var all = new List<JoinedGame>();
        foreach (Match m in JoinLine.Matches(logText))
            if (long.TryParse(m.Groups[1].Value, out long placeId)
                && long.TryParse(m.Groups[2].Value, out long universeId))
                all.Add(new JoinedGame(placeId, universeId));

        var seen = new HashSet<long>();
        for (int i = all.Count - 1; i >= 0 && result.Count < max; i--)
            if (seen.Add(all[i].PlaceId))
                result.Add(all[i]);

        return result;
    }
}
