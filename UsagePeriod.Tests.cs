using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Rolling day buckets up into daily, monthly and all-time rows.
/// </summary>
/// <remarks>
/// Opens and seconds add up. Downloads do not, and nearly everything here is
/// about that difference — GitHub publishes a running total, so a span's
/// downloads can only be the growth across it. Summing the readings instead
/// would produce a huge meaningless figure that still looks like a number.
/// </remarks>
public class UsagePeriodTests
{
    private static List<UsageDay> Sample() => new()
    {
        new UsageDay("2026-08-30", 3, 600, 100),
        new UsageDay("2026-08-31", 5, 1200, 130),
        new UsageDay("2026-09-01", 2, 300, 150),
    };

    [Fact]
    public void ADailyRowPerDayNewestFirst()
    {
        List<UsageRow> rows = UsagePeriod.Rows(Sample(), UsageSpan.Daily);

        Assert.Equal(3, rows.Count);
        Assert.Contains("1 Sep", rows[0].Label);
    }

    [Fact]
    public void MonthlyFoldsTheDaysTogether()
    {
        List<UsageRow> rows = UsagePeriod.Rows(Sample(), UsageSpan.Monthly);

        Assert.Equal(2, rows.Count);

        UsageRow august = rows.Single(r => r.Label.StartsWith("August"));

        Assert.Equal(8, august.Opens);
        Assert.Equal(1800, august.Seconds);
    }

    [Fact]
    public void AllTimeIsASingleRow()
    {
        List<UsageRow> rows = UsagePeriod.Rows(Sample(), UsageSpan.AllTime);

        Assert.Single(rows);
        Assert.Equal(10, rows[0].Opens);
        Assert.Equal(2100, rows[0].Seconds);
    }

    // ---- downloads are growth, not a sum ----

    /// <summary>
    /// The mistake this guards. Adding the readings gives 380; the real answer
    /// is 50, because they are three readings of one running total.
    /// </summary>
    [Fact]
    public void AllTimeDownloadsAreTheGrowthNotTheSum()
    {
        UsageRow row = UsagePeriod.Rows(Sample(), UsageSpan.AllTime)[0];

        Assert.Equal(50, row.Downloads);
    }

    [Fact]
    public void MonthlyDownloadsAreTheGrowthWithinTheMonth()
    {
        UsageRow august = UsagePeriod.Rows(Sample(), UsageSpan.Monthly)
            .Single(r => r.Label.StartsWith("August"));

        Assert.Equal(30, august.Downloads);
    }

    /// <summary>
    /// A span holding one reading is still measurable, as long as something was
    /// read before it — September's growth is from August's last reading.
    /// </summary>
    /// <remarks>
    /// This used to assert null, which was wrong and was hiding a real bug: the
    /// growth was measured only from readings inside the span, so a span with
    /// one reading always reported nothing. That is every daily row, since a day
    /// holds exactly one reading — the Daily view could never show a download
    /// figure at all.
    /// </remarks>
    [Fact]
    public void ASpanWithOneReadingMeasuresFromTheOneBeforeIt()
    {
        UsageRow september = UsagePeriod.Rows(Sample(), UsageSpan.Monthly)
            .Single(r => r.Label.StartsWith("September"));

        Assert.Equal(20, september.Downloads);
    }

    /// <summary>
    /// The regression that matters, stated directly: daily downloads have to
    /// work. Each day's figure is the growth since the previous reading.
    /// </summary>
    [Fact]
    public void DailyDownloadsAreTheGrowthSinceThePreviousDay()
    {
        List<UsageRow> rows = UsagePeriod.Rows(Sample(), UsageSpan.Daily);

        Assert.Equal(20, rows.Single(r => r.Label.Contains("1 Sep")).Downloads);
        Assert.Equal(30, rows.Single(r => r.Label.Contains("31 Aug")).Downloads);
    }

    /// <summary>
    /// The exception: the very first reading has nothing to be compared against,
    /// so that day says it does not know rather than showing a zero — which
    /// would read as "nobody downloaded it".
    /// </summary>
    [Fact]
    public void TheFirstDayEverRecordedHasNoDownloadFigure()
    {
        UsageRow first = UsagePeriod.Rows(Sample(), UsageSpan.Daily)
            .Single(r => r.Label.Contains("30 Aug"));

        Assert.Null(first.Downloads);
        Assert.Equal("—", first.DownloadsText);
    }

    /// <summary>Newest first, like the history page.</summary>
    [Fact]
    public void RowsComeBackNewestFirst()
    {
        List<UsageRow> rows = UsagePeriod.Rows(Sample(), UsageSpan.Daily);

        Assert.Contains("1 Sep", rows[0].Label);
        Assert.Contains("30 Aug", rows[^1].Label);
    }

    [Fact]
    public void DaysBeforeAnyReadingHaveNoDownloadFigure()
    {
        var days = new List<UsageDay>
        {
            new("2026-08-01", 1, 60),
            new("2026-08-02", 1, 60),
        };

        Assert.Null(UsagePeriod.Rows(days, UsageSpan.AllTime)[0].Downloads);
    }

    // ---- accumulating ----

    [Fact]
    public void AddsToAnExistingDayRatherThanRepeatingIt()
    {
        var days = new List<UsageDay>();

        UsagePeriod.Add(days, "2026-08-31", opens: 1, seconds: 100);
        UsagePeriod.Add(days, "2026-08-31", opens: 1, seconds: 50);

        Assert.Single(days);
        Assert.Equal(2, days[0].Opens);
        Assert.Equal(150, days[0].Seconds);
    }

    /// <summary>
    /// A reading replaces rather than adds — two readings on one day are the
    /// same fact twice, and adding them would inflate the running total.
    /// </summary>
    [Fact]
    public void ASecondReadingOnADayReplacesTheFirst()
    {
        var days = new List<UsageDay>();

        UsagePeriod.RecordDownloads(days, "2026-08-31", 100);
        UsagePeriod.RecordDownloads(days, "2026-08-31", 120);

        Assert.Single(days);
        Assert.Equal(120, days[0].Downloads);
    }

    [Fact]
    public void ARecordedReadingDoesNotDisturbThatDaysUsage()
    {
        var days = new List<UsageDay>();

        UsagePeriod.Add(days, "2026-08-31", opens: 4, seconds: 900);
        UsagePeriod.RecordDownloads(days, "2026-08-31", 100);

        Assert.Equal(4, days[0].Opens);
        Assert.Equal(900, days[0].Seconds);
        Assert.Equal(100, days[0].Downloads);
    }

    // ---- whatever the counter returns ----

    [Fact]
    public void NoDaysMeansNoRows()
    {
        Assert.Empty(UsagePeriod.Rows(new List<UsageDay>(), UsageSpan.Daily));
    }

    /// <summary>The buckets come off a server anyone can post to.</summary>
    [Fact]
    public void IgnoresADayWhoseDateIsNotADate()
    {
        var days = new List<UsageDay> { new("", 5, 100), new("x", 5, 100) };

        Assert.Empty(UsagePeriod.Rows(days, UsageSpan.Daily));
    }

    [Fact]
    public void ShowsAKeyItCannotParseRatherThanBlank()
    {
        Assert.Equal("2026-13-45", UsagePeriod.Label("2026-13-45"));
    }

    // ---- bounded history ----

    /// <summary>
    /// The file gains a row every day the app is used, forever, and is parsed
    /// in full at every launch. Two years is the cap, matching the shared
    /// counter so the two cannot disagree about how far back all-time goes.
    /// </summary>
    [Fact]
    public void KeepsOnlyTheMostRecentDays()
    {
        var days = new List<UsageDay>();

        for (int i = 1; i <= 40; i++)
            days.Add(new UsageDay($"2026-01-{i:00}", 1, 60));

        UsagePeriod.Trim(days, keep: 30);

        Assert.Equal(30, days.Count);
        Assert.Equal("2026-01-11", days[0].Date);
        Assert.Equal("2026-01-40", days[^1].Date);
    }

    [Fact]
    public void LeavesAShortHistoryAlone()
    {
        var days = new List<UsageDay> { new("2026-01-01", 1, 60) };

        UsagePeriod.Trim(days);

        Assert.Single(days);
    }

    /// <summary>Trimming keeps the newest, whatever order they arrived in.</summary>
    [Fact]
    public void DropsTheOldestEvenWhenTheListIsUnordered()
    {
        var days = new List<UsageDay>
        {
            new("2026-03-03", 1, 60),
            new("2026-01-01", 1, 60),
            new("2026-02-02", 1, 60),
        };

        UsagePeriod.Trim(days, keep: 2);

        Assert.Equal(new[] { "2026-02-02", "2026-03-03" }, days.Select(d => d.Date));
    }
}
