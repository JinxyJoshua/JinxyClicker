using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>
/// How often this copy of the app has been opened, and for how long.
/// </summary>
/// <remarks>
/// This machine only. Nothing is sent anywhere, and there is nothing here that
/// could identify anyone — it is two numbers in a file beside the settings, the
/// same as the click history already kept.
///
/// It is deliberately not a total across everybody who runs the app. That would
/// mean every copy reporting to a server, which needs somewhere to report to
/// and, more to the point, needs the people running it to be told it is
/// happening. Counting your own use needs neither.
/// </remarks>
public sealed class UsageStats
{
    private static readonly string StoreFile = SettingsPath.For("usage.json");

    /// <summary>Times the app has been opened on this machine.</summary>
    public int Launches { get; set; }

    /// <summary>Total seconds the app has been open, across all of them.</summary>
    public double OpenSeconds { get; set; }

    /// <summary>When it was first opened here, so the totals have a span.</summary>
    public string FirstRun { get; set; } = "";

    /// <summary>
    /// The same numbers broken down by day.
    /// </summary>
    /// <remarks>
    /// Kept alongside the running totals rather than derived from them, because
    /// the totals predate this list — a copy that has been in use for weeks has
    /// hours behind it and no days recorded, and rebuilding a history it never
    /// kept would mean inventing one.
    /// </remarks>
    public List<UsageDay> Days { get; set; } = new();

    public static UsageStats Load()
    {
        try
        {
            if (!File.Exists(StoreFile)) return new UsageStats();

            return JsonSerializer.Deserialize<UsageStats>(File.ReadAllText(StoreFile))
                   ?? new UsageStats();
        }
        catch
        {
            return new UsageStats();
        }
    }

    public void Save()
    {
        try
        {
            // Bounded before writing, not after loading, so the file on disk can
            // never be the thing that grows without limit.
            UsagePeriod.Trim(Days);

            File.WriteAllText(StoreFile, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Counters are a nicety. Losing one is not worth surfacing.
        }
    }

    /// <summary>Counts this launch. Called once, at startup.</summary>
    public void RecordLaunch(DateTime nowLocal)
    {
        Launches++;

        string today = nowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (FirstRun.Length == 0) FirstRun = today;

        UsagePeriod.Add(Days, today, opens: 1, seconds: 0);
    }

    /// <summary>
    /// Adds the time this session stayed open.
    /// </summary>
    /// <remarks>
    /// Negative and absurd values are refused rather than trusted. The clock can
    /// move — a manual change, a daylight saving jump, a machine resuming from
    /// sleep — and a single bad reading would otherwise sit in the total for
    /// good, since nothing here is ever recomputed.
    /// </remarks>
    public void AddSession(double seconds) => AddSession(seconds, DateTime.Now);

    public void AddSession(double seconds, DateTime whenLocal)
    {
        if (double.IsNaN(seconds) || seconds <= 0) return;

        double capped = Math.Min(seconds, MaxSessionSeconds);

        OpenSeconds += capped;

        UsagePeriod.Add(Days,
            whenLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            opens: 0, seconds: capped);
    }

    /// <summary>A day. Longer than any believable single sitting.</summary>
    public const double MaxSessionSeconds = 24 * 60 * 60;

    public string LaunchesText => Launches.ToString("N0", CultureInfo.CurrentCulture);

    public string OpenText => Format(OpenSeconds);

    /// <summary>Reads as hours once there are hours, and minutes before that.</summary>
    public static string Format(double seconds)
    {
        if (seconds < 60) return $"{Math.Max(0, (int)seconds)}s";

        var span = TimeSpan.FromSeconds(seconds);

        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";

        return $"{(int)span.TotalHours}h {span.Minutes}m";
    }
}
