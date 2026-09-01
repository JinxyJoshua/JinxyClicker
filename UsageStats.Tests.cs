using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The launch and time-open counters shown on the dev panel.
/// </summary>
/// <remarks>
/// These only ever accumulate — nothing recomputes them from a source of truth
/// — so a single bad value is permanent. Most of what is pinned here is about
/// refusing bad values rather than about arithmetic.
/// </remarks>
public class UsageStatsTests
{
    [Fact]
    public void CountsEachLaunch()
    {
        var usage = new UsageStats();

        usage.RecordLaunch(new System.DateTime(2026, 8, 31));
        usage.RecordLaunch(new System.DateTime(2026, 9, 1));

        Assert.Equal(2, usage.Launches);
    }

    /// <summary>The first run is stamped once and then left alone.</summary>
    [Fact]
    public void RemembersOnlyTheFirstRunDate()
    {
        var usage = new UsageStats();

        usage.RecordLaunch(new System.DateTime(2026, 8, 31));
        usage.RecordLaunch(new System.DateTime(2026, 9, 1));

        Assert.Equal("2026-08-31", usage.FirstRun);
    }

    [Fact]
    public void AddsUpSessions()
    {
        var usage = new UsageStats();

        usage.AddSession(60);
        usage.AddSession(90);

        Assert.Equal(150, usage.OpenSeconds);
    }

    /// <summary>
    /// The clock can move backwards — a manual change, daylight saving, a
    /// machine resuming from sleep. A negative session would silently eat real
    /// hours out of a total that is never recomputed.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100000)]
    [InlineData(double.NaN)]
    public void RefusesASessionThatCannotHaveHappened(double seconds)
    {
        var usage = new UsageStats { OpenSeconds = 500 };

        usage.AddSession(seconds);

        Assert.Equal(500, usage.OpenSeconds);
    }

    /// <summary>
    /// A clock jump forward is just as damaging in the other direction, and
    /// would put an unreachable number on the panel for good.
    /// </summary>
    [Fact]
    public void CapsAnImpossiblyLongSession()
    {
        var usage = new UsageStats();

        usage.AddSession(400 * 24 * 60 * 60);

        Assert.Equal(UsageStats.MaxSessionSeconds, usage.OpenSeconds);
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(90, "1m")]
    [InlineData(3600, "1h 0m")]
    [InlineData(5400, "1h 30m")]
    [InlineData(360000, "100h 0m")]
    public void ReadsAsSomethingAPersonWouldSay(double seconds, string expected)
    {
        Assert.Equal(expected, UsageStats.Format(seconds));
    }

    // ---- the shared counter ----

    [Fact]
    public void ReadsTheCountersTotals()
    {
        UsageTotals? t = UsageReporter.Read("""{"opens":1200,"seconds":86400}""");

        Assert.NotNull(t);
        Assert.Equal(1200, t.Opens);
        Assert.Equal(86400, t.Seconds);
    }

    /// <summary>
    /// The reply comes from a server anyone can POST to, so a nonsense total
    /// has to read as "no answer" rather than be shown as fact.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"opens":-5,"seconds":10}""")]
    [InlineData("""{"opens":5,"seconds":-10}""")]
    [InlineData("""{"error":"no KV binding"}""")]
    public void RefusesATotalThatCannotBeReal(string json)
    {
        UsageTotals? t = UsageReporter.Read(json);

        Assert.True(t == null || (t.Opens == 0 && t.Seconds == 0));
    }

    /// <summary>
    /// Bounded before it is sent, not after. A total that is only added to
    /// cannot recover from one absurd session, and unlike the local file
    /// nobody can go and edit it.
    /// </summary>
    [Fact]
    public void CapsWhatASingleSessionCanAddToTheSharedTotal()
    {
        string body = UsageReporter.SessionBody(400 * 24 * 60 * 60);

        Assert.Contains(((long)UsageStats.MaxSessionSeconds).ToString(), body);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void SendsZeroForASessionThatCannotHaveHappened(double seconds)
    {
        Assert.Equal("{\"seconds\":0}", UsageReporter.SessionBody(seconds));
    }

    /// <summary>An unconfigured build must not call anything at all.</summary>
    [Fact]
    public void DoesNothingWithNoEndpointConfigured()
    {
        if (UsageReporter.Configured) return;

        UsageReporter.ReportOpen();
        UsageReporter.ReportSession(60);

        Assert.False(UsageReporter.Configured);
    }
}
