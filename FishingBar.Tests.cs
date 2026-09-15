using System;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Reading the fishing bar and deciding hold or release.
/// </summary>
/// <remarks>
/// Two recordings' worth of real numbers. The first had the needle sitting
/// inside a centred zone; the second had the needle stuck to the right of a zone
/// on the left, catching nothing. The controller has to drive the needle into
/// the zone in both — it is the needle that moves, not the zone.
/// </remarks>
public class FishingBarTests
{
    private static bool[] Row(int left, int right, int width = 1920)
    {
        var g = new bool[width];
        for (int x = left; x <= right; x++) g[x] = true;
        return g;
    }

    private static FishingReading Read(int zoneL, int zoneR, int needleX)
    {
        var z = FishingBar.FindZone(Row(zoneL, zoneR));
        return z is null
            ? FishingReading.None
            : new FishingReading(true, z.Value.Left, z.Value.Right, needleX);
    }

    // ---- finding the zone ----

    [Fact]
    public void FindsTheWidestGreenRunAsTheZone()
    {
        // A thin needle-green run on the right, and the wide zone on the left:
        // the zone is the wide one.
        var g = Row(626, 823);
        for (int x = 1106; x <= 1115; x++) g[x] = true;   // the needle, thin

        var z = FishingBar.FindZone(g);

        Assert.Equal((626, 823), z);
    }

    [Fact]
    public void NoGreenIsNoZone()
    {
        Assert.Null(FishingBar.FindZone(new bool[1920]));
    }

    [Fact]
    public void AStrayGreenSpeckIsNotTheZone()
    {
        Assert.Null(FishingBar.FindZone(Row(400, 410)));
    }

    [Fact]
    public void NoBarMeansIdle()
    {
        Assert.Equal(FishAction.Idle, FishingBar.Decide(FishingReading.None, 14, true));
    }

    // ---- the failing recording: needle right of a left-hand zone ----

    /// <summary>
    /// The whole reason for the rebuild. Zone on the left (626-823, centre 724),
    /// needle stuck on the right (~1110). Holding pushes right, so the fix is to
    /// release and let the needle fall left into the zone — the opposite of what
    /// the stuck bot did.
    /// </summary>
    [Theory]
    [InlineData(1046)]
    [InlineData(1114)]
    [InlineData(1116)]
    public void ANeedleRightOfTheZoneIsReleasedToFallIn(int needleX)
    {
        FishingReading r = Read(626, 823, needleX);

        Assert.Equal(FishAction.Release, FishingBar.Decide(r, deadzonePx: 14, holdPushesRight: true));
    }

    /// <summary>A needle left of the zone is held, to push it right into the zone.</summary>
    [Fact]
    public void ANeedleLeftOfTheZoneIsHeld()
    {
        FishingReading r = Read(626, 823, 300);

        Assert.Equal(FishAction.Hold, FishingBar.Decide(r, deadzonePx: 14, holdPushesRight: true));
    }

    // ---- the first recording: needle inside a centred zone ----

    /// <summary>
    /// Needle at 978 inside the zone 862-1059 (centre 960). Close to centre, so
    /// it releases rather than buzzing — the needle is where it should be.
    /// </summary>
    [Fact]
    public void ANeedleNearTheZoneCentreHoldsSteady()
    {
        FishingReading r = Read(862, 1059, 978);

        Assert.Equal(FishAction.Release, FishingBar.Decide(r, deadzonePx: 20, holdPushesRight: true));
    }

    /// <summary>
    /// Whichever side of the zone centre the needle is, the action drives it back
    /// toward the middle — so across a run of frames it stays inside the zone.
    /// </summary>
    [Theory]
    [InlineData(900, FishAction.Hold)]     // left of centre 960 -> push right
    [InlineData(1020, FishAction.Release)] // right of centre -> fall left
    public void DrivesTheNeedleTowardTheZoneCentre(int needleX, FishAction expected)
    {
        FishingReading r = Read(862, 1059, needleX);

        Assert.Equal(expected, FishingBar.Decide(r, deadzonePx: 14, holdPushesRight: true));
    }

    /// <summary>A game that steers the other way flips with one boolean.</summary>
    [Fact]
    public void TheDirectionCanBeInverted()
    {
        FishingReading needleRight = Read(626, 823, 1110);

        Assert.Equal(FishAction.Release, FishingBar.Decide(needleRight, 14, holdPushesRight: true));
        Assert.Equal(FishAction.Hold, FishingBar.Decide(needleRight, 14, holdPushesRight: false));
    }
}
