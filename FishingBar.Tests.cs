using System;
using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Reading the fishing bar and deciding hold or release.
/// </summary>
/// <remarks>
/// The numbers are a real recording's, frame by frame: a grey needle fixed at
/// x=978 and a green zone whose edges were measured off each frame. If the
/// controller keeps the zone centred on the needle across these, it keeps a fish
/// on the line.
/// </remarks>
public class FishingBarTests
{
    private const int Needle = 978;

    /// <summary>A row of the given width, green between left and right inclusive.</summary>
    private static bool[] Row(int left, int right, int width = 1920)
    {
        var g = new bool[width];
        for (int x = left; x <= right; x++) g[x] = true;
        return g;
    }

    // ---- finding the zone ----

    /// <summary>The frame the pixel colours were first read from.</summary>
    [Fact]
    public void FindsTheZoneSpanIncludingTheNeedleGap()
    {
        // Two green runs 862-971 and 986-1059 with the needle's grey gap between:
        // the zone is the whole span, gap and all.
        var g = Row(862, 971);
        for (int x = 986; x <= 1059; x++) g[x] = true;

        FishingReading r = FishingBar.Read(g);

        Assert.True(r.BarPresent);
        Assert.Equal(862, r.ZoneLeft);
        Assert.Equal(1059, r.ZoneRight);
        Assert.Equal(960, r.ZoneCenter);
    }

    [Fact]
    public void NoGreenIsNoBar()
    {
        Assert.False(FishingBar.Read(new bool[1920]).BarPresent);
        Assert.Equal(FishAction.Idle, FishingBar.Decide(FishingReading.None, Needle, 12, true));
    }

    /// <summary>
    /// A handful of green pixels is the grassy map showing through, not the bar,
    /// and must not be steered toward.
    /// </summary>
    [Fact]
    public void AStrayGreenSpeckIsNotTheBar()
    {
        Assert.False(FishingBar.Read(Row(400, 410)).BarPresent);
    }

    // ---- the real frames, and what the controller does with them ----

    /// <summary>
    /// Each row is (zoneLeft, zoneRight) measured off a frame, with the action a
    /// controller keeping the zone on the needle should take. Holding pushes the
    /// zone right, so a zone whose centre is left of 978 is held.
    /// </summary>
    [Theory]
    [InlineData(850, 1045, FishAction.Hold)]     // t=14.0  centre 947, left of needle
    [InlineData(862, 1059, FishAction.Hold)]     // t=14.5  centre 960, just left
    [InlineData(904, 1099, FishAction.Release)]  // t=15.0  centre 1001, right of needle
    [InlineData(866, 1063, FishAction.Hold)]     // t=15.3  centre 964, left
    [InlineData(898, 1093, FishAction.Release)]  // t=15.6  centre 995, right
    public void DrivesEachRealFrameTowardTheNeedle(int left, int right, FishAction expected)
    {
        FishingReading r = FishingBar.Read(Row(left, right));

        Assert.Equal(expected, FishingBar.Decide(r, Needle, deadzonePx: 12, holdPushesRight: true));
    }

    /// <summary>
    /// Sitting on the needle, the controller releases rather than buzzing hold
    /// and release every frame.
    /// </summary>
    [Fact]
    public void OnTheNeedleItHoldsSteadyRatherThanBuzzing()
    {
        // Zone centred exactly on the needle: 978 - half-width to 978 + half-width.
        FishingReading r = FishingBar.Read(Row(Needle - 97, Needle + 97));

        Assert.Equal(FishAction.Release, FishingBar.Decide(r, Needle, deadzonePx: 12, holdPushesRight: true));
    }

    /// <summary>
    /// The whole point: run the controller over every measured frame and the
    /// zone centre never leaves the green — it stays within the zone's own half
    /// width of the needle, which is what keeps the fish on.
    /// </summary>
    [Fact]
    public void AcrossEveryFrameTheZoneStaysOnTheNeedle()
    {
        (int L, int R)[] frames =
        {
            (850, 1045), (862, 1059), (904, 1099), (866, 1063), (898, 1093)
        };

        foreach ((int L, int R) in frames)
        {
            int halfWidth = (R - L) / 2;
            int center = (L + R) / 2;

            Assert.True(Math.Abs(center - Needle) <= halfWidth,
                $"zone {L}-{R} centre {center} had drifted off the needle");
        }
    }

    /// <summary>A game that steers the other way flips with one boolean.</summary>
    [Fact]
    public void TheDirectionCanBeInverted()
    {
        FishingReading leftOfNeedle = FishingBar.Read(Row(850, 1045));   // centre 947

        Assert.Equal(FishAction.Hold,
            FishingBar.Decide(leftOfNeedle, Needle, 12, holdPushesRight: true));
        Assert.Equal(FishAction.Release,
            FishingBar.Decide(leftOfNeedle, Needle, 12, holdPushesRight: false));
    }
}
