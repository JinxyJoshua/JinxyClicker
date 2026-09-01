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

        if (span == UsageSpan.AllTime)
            return new List<UsageRow> { Fold("All time", ordered, before: null) };

        Func<UsageDay, string> key = span == UsageSpan.Monthly
            ? d => d.Date[..7]
            : d => d.Date;

        var rows = new List<UsageRow>();

        // The last download reading taken before the span being folded.
        //
        // Carrying it is what makes a daily figure possible at all. A day holds
        // one reading, so a span of one day can never contain the two a
        // difference needs — measured only from readings inside the span, every
        // daily row was a dash forever. The growth belonging to a day is from
        // the previous reading to that day's, which means looking back past the
        // edge of the span.
        long? carried = null;

        foreach (IGrouping<string, UsageDay> group in ordered
                     .GroupBy(key)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            List<UsageDay> inSpan = group.ToList();

            rows.Add(Fold(Label(group.Key), inSpan, carried));

            List<long> readings = Readings(inSpan);
            if (readings.Count > 0) carried = readings[^1];
        }

        // Newest first, like the history page. Folded oldest first because the
        // carried reading only makes sense going forwards.
        rows.Reverse();

        return rows;
    }

    private static List<long> Readings(IEnumerable<UsageDay> days) =>
        days.Where(d => d.Downloads != null).Select(d => d.Downloads!.Value).ToList();

    /// <summary>
    /// Sums a span, and takes downloads as the growth across it.
    /// </summary>
    /// <remarks>
    /// Growth is measured from the last reading before the span. Where there is
    /// none — the very first span on record — it falls back to the span's own
    /// first reading, which is why "all time" reports the growth across every
    /// reading rather than nothing.
    ///
    /// A span with no reading before it and only one inside it cannot describe
    /// a change at all, and says so with null rather than zero.
    /// </remarks>
    private static UsageRow Fold(string label, List<UsageDay> days, long? before)
    {
        long opens = days.Sum(d => d.Opens);
        double seconds = days.Sum(d => d.Seconds);

        List<long> readings = Readings(days);

        long? baseline = before ?? (readings.Count >= 2 ? readings[0] : null);

        long? downloads = baseline != null && readings.Count > 0
            ? readings[^1] - baseline
            : null;

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

    /// <summary>How many days are kept. Two years, matching the shared counter.</summary>
    public const int MaxDays = 730;

    /// <summary>
    /// Keeps the most recent days and drops the rest.
    /// </summary>
    /// <remarks>
    /// Without this the file grows by a row every day it is used, for as long
    /// as the app is installed, and is parsed in full at every launch. Nothing
    /// reads past two years — the shared counter trims to the same figure, so
    /// the two do not disagree about how far back "all time" goes.
    /// </remarks>
    public static void Trim(List<UsageDay> days, int keep = MaxDays)
    {
        if (days.Count <= keep) return;

        List<UsageDay> kept = days
            .OrderBy(d => d.Date, StringComparer.Ordinal)
            .TakeLast(keep)
            .ToList();

        days.Clear();
        days.AddRange(kept);
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
