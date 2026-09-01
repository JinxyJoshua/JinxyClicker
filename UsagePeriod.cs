using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace JinxyClicker;

/// <summary>One day's usage, as stored and as reported.</summary>
/// <param name="Date">yyyy-MM-dd, so it sorts as text and reads the same everywhere.</param>
/// <param name="Downloads">
/// Total downloads as they stood that day, or null for a day nobody recorded a
/// figure. Not a daily count — see <see cref="UsagePeriod.Rows"/>.
/// </param>
public sealed record UsageDay(string Date, long Opens, double Seconds, long? Downloads = null);

/// <summary>A row on the dev page: a span of time and what happened in it.</summary>
public sealed record UsageRow(string Label, long Opens, double Seconds, long? Downloads)
{
    public string OpensText => Opens.ToString("N0", CultureInfo.CurrentCulture);

    public string HoursText => UsageStats.Format(Seconds);

    /// <summary>A dash rather than a zero when nothing was recorded.</summary>
    /// <remarks>
    /// Downloads only exist for spans covered by a recorded figure, and a zero
    /// there would read as "nobody downloaded it" rather than "not known".
    /// </remarks>
    public string DownloadsText =>
        Downloads == null ? "—" : Downloads.Value.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>How far back a set of rows covers.</summary>
public enum UsageSpan
{
    Daily,
    Monthly,
    AllTime
}

/// <summary>
/// Turns stored days into the rows the dev page shows.
/// </summary>
/// <remarks>
/// Opens and seconds simply add up across a span. Downloads do not, and that is
/// the one thing worth understanding here.
///
/// GitHub publishes a running total per release, never a per-day figure, so a
/// day's downloads can only be worked out as the difference between two
/// recorded totals. That means the number is a <em>change over the span</em>
/// derived from snapshots, it only exists for days something recorded a figure,
/// and it cannot be recovered for any day before the recording started. Adding
/// the snapshots together instead would produce an enormous meaningless number,
/// which is the mistake this exists to prevent.
/// </remarks>
public static class UsagePeriod
{
    public static List<UsageRow> Rows(IEnumerable<UsageDay> days, UsageSpan span)
    {
        List<UsageDay> ordered = days
            .Where(d => d.Date.Length >= 7)
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .ToList();

        if (ordered.Count == 0) return new List<UsageRow>();

        return span switch
        {
            UsageSpan.AllTime => new List<UsageRow> { Fold("All time", ordered) },
            UsageSpan.Monthly => Group(ordered, d => d.Date[..7]),
            _ => Group(ordered, d => d.Date)
        };
    }

    private static List<UsageRow> Group(List<UsageDay> ordered, Func<UsageDay, string> key) =>
        ordered
            .GroupBy(key)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => Fold(Label(g.Key), g.ToList()))
            .ToList();

    /// <summary>
    /// Sums a span, and takes downloads as the growth across it.
    /// </summary>
    /// <remarks>
    /// The growth is measured from the last figure <em>before</em> the span
    /// where one exists — which for a single day is the previous day's total,
    /// and is why a span's first recorded day shows a dash rather than the
    /// whole running total as if it had all happened at once.
    /// </remarks>
    private static UsageRow Fold(string label, List<UsageDay> days)
    {
        long opens = days.Sum(d => d.Opens);
        double seconds = days.Sum(d => d.Seconds);

        List<long> figures = days
            .Where(d => d.Downloads != null)
            .Select(d => d.Downloads!.Value)
            .ToList();

        long? downloads = figures.Count >= 2 ? figures[^1] - figures[0] : null;

        return new UsageRow(label, opens, seconds, downloads);
    }

    /// <summary>"2026-08-31" reads as a date, "2026-08" as a month.</summary>
    public static string Label(string key)
    {
        if (DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime day))
        {
            return day.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture);
        }

        if (DateTime.TryParseExact(key, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime month))
        {
            return month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        }

        return key;
    }

    /// <summary>Adds a day's use to a list, or starts that day if it is new.</summary>
    public static void Add(List<UsageDay> days, string date, long opens, double seconds)
    {
        int at = days.FindIndex(d => d.Date == date);

        if (at < 0)
        {
            days.Add(new UsageDay(date, opens, seconds));
            return;
        }

        days[at] = days[at] with
        {
            Opens = days[at].Opens + opens,
            Seconds = days[at].Seconds + seconds
        };
    }

    /// <summary>Records the download total as it stands today.</summary>
    /// <remarks>
    /// Replaces rather than adds. It is a reading of a running total, not an
    /// amount, so two readings on the same day are the same fact twice.
    /// </remarks>
    public static void RecordDownloads(List<UsageDay> days, string date, long total)
    {
        int at = days.FindIndex(d => d.Date == date);

        if (at < 0)
        {
            days.Add(new UsageDay(date, 0, 0, total));
            return;
        }

        days[at] = days[at] with { Downloads = total };
    }
}
