using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>A recently joined game, ready to show as a pick-to-play tile.</summary>
public sealed record RecentGame(long PlaceId, long UniverseId, string Name, string? IconUrl);

/// <summary>
/// The games a player recently joined, with their icon and name — read from the
/// Roblox client logs and filled in from Roblox's public (no-auth) thumbnail and
/// game APIs.
/// </summary>
public static class RecentGames
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<IReadOnlyList<RecentGame>> LoadAsync(int max = 8)
    {
        // Read extra joins because dedup-by-universe below can collapse several of
        // them, and we still want up to `max` distinct games to show.
        List<JoinedGame> joins = await Task.Run(() => ReadRecentJoins(max * 3)).ConfigureAwait(false);
        if (joins.Count == 0) return Array.Empty<RecentGame>();

        // One game can be joined under more than one place id but shares a universe
        // id — and the name and icon come from the universe, so those would show as
        // identical tiles. Keep each universe once (the most recent, since joins are
        // already most-recent-first), then cap at `max`.
        var seenUniverse = new HashSet<long>();
        joins = joins.Where(j => seenUniverse.Add(j.UniverseId)).Take(max).ToList();

        string ids = string.Join(",", joins.Select(j => j.UniverseId).Distinct());
        Dictionary<long, string> names = await FetchNamesAsync(ids).ConfigureAwait(false);
        Dictionary<long, string> icons = await FetchIconsAsync(ids).ConfigureAwait(false);

        var result = new List<RecentGame>();
        foreach (JoinedGame j in joins)
        {
            names.TryGetValue(j.UniverseId, out string? name);
            icons.TryGetValue(j.UniverseId, out string? icon);
            result.Add(new RecentGame(j.PlaceId, j.UniverseId,
                string.IsNullOrWhiteSpace(name) ? $"Place {j.PlaceId}" : name!, icon));
        }
        return result;
    }

    // Reads the most recent few log files, oldest-first so the newest joins land at
    // the end where ParseRecent treats them as most recent.
    private static List<JoinedGame> ReadRecentJoins(int max)
    {
        try
        {
            string dir = RobloxServerLog.DefaultLogDirectory;
            if (!Directory.Exists(dir)) return new List<JoinedGame>();

            IEnumerable<FileInfo> files = new DirectoryInfo(dir).GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(6)
                .OrderBy(f => f.LastWriteTimeUtc);

            var sb = new StringBuilder();
            foreach (FileInfo f in files)
                try { sb.AppendLine(ReadShared(f.FullName)); } catch { /* skip a locked log */ }

            return RobloxJoinLog.ParseRecent(sb.ToString(), max).ToList();
        }
        catch
        {
            return new List<JoinedGame>();
        }
    }

    // Roblox keeps the current session's log open for writing, so it must be read
    // with a sharing mode that allows that — otherwise the game you're in now is
    // the one game the picker misses.
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    private static async Task<Dictionary<long, string>> FetchNamesAsync(string universeIds)
    {
        var map = new Dictionary<long, string>();
        try
        {
            string json = await Http.GetStringAsync(
                $"https://games.roblox.com/v1/games?universeIds={universeIds}").ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement e in doc.RootElement.GetProperty("data").EnumerateArray())
                if (e.TryGetProperty("id", out JsonElement id) && e.TryGetProperty("name", out JsonElement name))
                    map[id.GetInt64()] = name.GetString() ?? "";
        }
        catch { /* names are optional; tiles fall back to the place id */ }
        return map;
    }

    private static async Task<Dictionary<long, string>> FetchIconsAsync(string universeIds)
    {
        var map = new Dictionary<long, string>();
        try
        {
            string json = await Http.GetStringAsync(
                $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeIds}&size=150x150&format=Png&returnPolicy=PlaceHolder")
                .ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (JsonElement e in doc.RootElement.GetProperty("data").EnumerateArray())
                if (e.TryGetProperty("targetId", out JsonElement id)
                    && e.TryGetProperty("imageUrl", out JsonElement img)
                    && img.ValueKind == JsonValueKind.String)
                    map[id.GetInt64()] = img.GetString()!;
        }
        catch { /* icons are optional; tiles show a placeholder */ }
        return map;
    }
}
