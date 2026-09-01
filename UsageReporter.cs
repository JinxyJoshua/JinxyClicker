using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>Totals across every copy of the app, as the counter reports them.</summary>
/// <param name="Days">
/// The same numbers by day, newest last. Empty for a counter that has not
/// collected any yet, which is every counter on its first day.
/// </param>
public sealed record UsageTotals(long Opens, double Seconds, List<UsageDay> Days);

/// <summary>
/// Adds this copy's use to a shared counter, and reads the totals back.
/// </summary>
/// <remarks>
/// Two numbers go up: one when the app opens, and the seconds it stayed open
/// when it closes. Nothing else is sent — no name, no machine id, no settings,
/// nothing that could pick one person out of the total. The server cannot tell
/// two launches from one machine apart from two launches on two machines, which
/// is the whole point of a counter rather than analytics.
///
/// Every call fails silently and none of them block anything. A counter that is
/// down, rate limited, or blocked by a firewall must not delay the app opening
/// or hold up its closing — the number is for curiosity, and the app is for
/// playing.
///
/// With no endpoint configured this does nothing at all, which is the state a
/// fork or a local build is in.
/// </remarks>
public static class UsageReporter
{
    /// <summary>
    /// Where the counter lives. Empty disables all of this.
    /// </summary>
    /// <remarks>
    /// Set this to the deployed worker's address — see Server/usage-worker.js,
    /// which is the other half of this and is meant to be read alongside it.
    /// </remarks>
    public const string Endpoint = "";

    public static bool Configured => Endpoint.Length > 0;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    /// <summary>Reads the totals, or null if they could not be had.</summary>
    public static async Task<UsageTotals?> FetchAsync(CancellationToken token)
    {
        if (!Configured) return null;

        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyClicker");

            return Read(await http.GetStringAsync(Endpoint, token).ConfigureAwait(false));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads the counter's reply.</summary>
    public static UsageTotals? Read(string json)
    {
        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement;

            if (root.ValueKind != JsonValueKind.Object) return null;

            long opens = root.TryGetProperty("opens", out JsonElement o)
                         && o.TryGetInt64(out long v) ? v : 0;

            double seconds = root.TryGetProperty("seconds", out JsonElement s)
                             && s.TryGetDouble(out double d) ? d : 0;

            // Negatives mean a broken or tampered counter, not a real total.
            if (opens < 0 || seconds < 0 || double.IsNaN(seconds)) return null;

            return new UsageTotals(opens, seconds, ReadDays(root));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the per-day buckets, skipping anything malformed.
    /// </summary>
    /// <remarks>
    /// A bad day is dropped rather than failing the whole reply. The totals are
    /// the headline figure and they parse independently — losing the breakdown
    /// because one bucket is odd would be the wrong trade.
    /// </remarks>
    private static List<UsageDay> ReadDays(JsonElement root)
    {
        var days = new List<UsageDay>();

        if (!root.TryGetProperty("days", out JsonElement map)
            || map.ValueKind != JsonValueKind.Object)
        {
            return days;
        }

        foreach (JsonProperty entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;

            long opens = entry.Value.TryGetProperty("opens", out JsonElement o)
                         && o.TryGetInt64(out long v) && v >= 0 ? v : 0;

            double seconds = entry.Value.TryGetProperty("seconds", out JsonElement sec)
                             && sec.TryGetDouble(out double d) && d >= 0 && !double.IsNaN(d) ? d : 0;

            long? downloads = entry.Value.TryGetProperty("downloads", out JsonElement dl)
                              && dl.TryGetInt64(out long n) && n >= 0 ? n : null;

            days.Add(new UsageDay(entry.Name, opens, seconds, downloads));
        }

        return days.OrderBy(d => d.Date, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Records the download total as it stands now.
    /// </summary>
    /// <remarks>
    /// Sent only by a build that can see the panel, because it is the only one
    /// that reads GitHub — and one reading a day is all a day needs. Downloads
    /// per day can only ever be the difference between two readings, so the
    /// figure starts existing from the first one and cannot be recovered for
    /// any day before that.
    /// </remarks>
    public static void ReportDownloads(long total)
    {
        if (total <= 0) return;

        Post($"{{\"downloads\":{total}}}");
    }

    /// <summary>The body sent when the app opens.</summary>
    public static string OpenBody() => """{"opens":1}""";

    /// <summary>
    /// The body sent when it closes.
    /// </summary>
    /// <remarks>
    /// Bounded and rounded here rather than trusted at the far end. A clock
    /// that jumped, or a machine that slept, would otherwise put a session of
    /// implausible length into a total that is never recomputed — and unlike a
    /// local file, nobody can go and fix that one.
    /// </remarks>
    public static string SessionBody(double seconds)
    {
        double clean = double.IsNaN(seconds) || seconds <= 0
            ? 0
            : Math.Min(seconds, UsageStats.MaxSessionSeconds);

        return $"{{\"seconds\":{Math.Round(clean)}}}";
    }

    /// <summary>Counts this launch. Returns without waiting on the network.</summary>
    public static void ReportOpen() => Post(OpenBody());

    /// <summary>Adds this session's length. Safe to call while closing.</summary>
    public static void ReportSession(double seconds)
    {
        if (seconds <= 0) return;

        Post(SessionBody(seconds));
    }

    private static void Post(string body)
    {
        if (!Configured) return;

        // Deliberately not awaited. The app must not wait on a counter, least
        // of all while it is trying to close.
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = Timeout };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("JinxyClicker");

                using var content = new StringContent(body, Encoding.UTF8, "application/json");

                await http.PostAsync(Endpoint, content).ConfigureAwait(false);
            }
            catch
            {
                // No counter, no problem.
            }
        });
    }
}
