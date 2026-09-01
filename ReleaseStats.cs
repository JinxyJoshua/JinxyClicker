using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>How many people have downloaded each release.</summary>
public sealed record ReleaseDownloads(string Tag, long Downloads);

/// <summary>
/// Download counts, read from the releases the app already talks to.
/// </summary>
/// <remarks>
/// Worth being clear about what this is and is not. GitHub counts every
/// download of every release asset and publishes that number, so this is a real
/// figure that costs nothing to collect — no server, and nothing gathered from
/// anybody's machine. It is a fact about the repository, not about its users.
///
/// It is also not a user count. One person downloading three versions is three
/// downloads, and a mirror or a scraper counts too.
/// </remarks>
public static class ReleaseStats
{
    public static string AllReleasesUrl =>
        $"https://api.github.com/repos/{Updater.Owner}/{Updater.Repo}/releases?per_page=100";

    /// <summary>
    /// Reads every release and its asset downloads out of the API's reply.
    /// </summary>
    /// <remarks>
    /// Drafts are skipped: they are not downloadable by anyone but the author,
    /// so counting them would inflate the total with nothing.
    /// </remarks>
    public static List<ReleaseDownloads> Read(string json)
    {
        var releases = new List<ReleaseDownloads>();

        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            if (root.ValueKind != JsonValueKind.Array) return releases;

            foreach (JsonElement release in root.EnumerateArray())
            {
                if (release.TryGetProperty("draft", out JsonElement draft)
                    && draft.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                string tag = release.TryGetProperty("tag_name", out JsonElement t)
                    ? t.GetString() ?? "" : "";

                long total = 0;

                if (release.TryGetProperty("assets", out JsonElement assets)
                    && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("download_count", out JsonElement c)
                            && c.TryGetInt64(out long count))
                        {
                            total += count;
                        }
                    }
                }

                if (tag.Length > 0) releases.Add(new ReleaseDownloads(tag, total));
            }
        }
        catch
        {
            // A malformed reply is no numbers, not a crash. Nobody asked for
            // this at a moment when a failure would matter.
        }

        return releases;
    }

    public static long Total(IEnumerable<ReleaseDownloads> releases)
    {
        long total = 0;

        foreach (ReleaseDownloads release in releases) total += release.Downloads;

        return total;
    }

    /// <summary>Fetches the counts, or null if they could not be read.</summary>
    public static async Task<List<ReleaseDownloads>?> FetchAsync(CancellationToken token)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            // GitHub refuses a request with no user agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyClicker");

            string json = await http.GetStringAsync(AllReleasesUrl, token).ConfigureAwait(false);

            return Read(json);
        }
        catch
        {
            return null;
        }
    }
}
