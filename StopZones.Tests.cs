using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The pointer positions that stop a running clicker.
/// </summary>
/// <remarks>
/// Reported as "the corner doesn't work". The geometry turned out to be right:
/// all four corners of a 3840x1080 desktop were measured reachable at zero
/// offset. What was missing was any way to check that without a real mouse.
///
/// The fixture is the desktop it was reported on — two 1920x1080 monitors side
/// by side, each with a 48px taskbar — so these are that machine's real numbers
/// rather than round ones chosen to be easy.
/// </remarks>
public class StopZonesTests
{
    private static readonly ScreenRect Desktop = new(0, 0, 3839, 1079);

    private static readonly ScreenRect Left = new(0, 0, 1919, 1079);
    private static readonly ScreenRect LeftWork = new(0, 0, 1919, 1031);

    private static readonly ScreenRect Right = new(1920, 0, 3839, 1079);
    private static readonly ScreenRect RightWork = new(1920, 0, 3839, 1031);

    // ---- corners ----

    /// <summary>
    /// All four, including the two that span monitors. This is the test that
    /// did not exist when the failsafe was reported broken.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3839, 0)]
    [InlineData(0, 1079)]
    [InlineData(3839, 1079)]
    public void EveryCornerOfTheDesktopStopsIt(int x, int y)
    {
        Assert.True(StopZones.InCorner(Desktop, x, y));
    }

    /// <summary>A throw that lands slightly short still counts.</summary>
    [Theory]
    [InlineData(6, 6)]
    [InlineData(3833, 6)]
    [InlineData(6, 1073)]
    public void LandingJustShortOfACornerStillCounts(int x, int y)
    {
        Assert.True(StopZones.InCorner(Desktop, x, y));
    }

    /// <summary>
    /// An edge is not a corner. On a desktop two monitors wide the pointer
    /// crosses edges constantly, and a failsafe that fired there would go off
    /// mid-fight.
    /// </summary>
    [Theory]
    [InlineData(1920, 0)]      // top edge, between the monitors
    [InlineData(0, 540)]       // far left, halfway down
    [InlineData(3839, 540)]    // far right, halfway down
    [InlineData(1920, 1079)]   // bottom edge, between the monitors
    public void AnEdgeIsNotACorner(int x, int y)
    {
        Assert.False(StopZones.InCorner(Desktop, x, y));
    }

    /// <summary>
    /// The seam between two monitors is not a corner either, even though it is
    /// a corner of each individual screen. Stopping there would fire every time
    /// the pointer crossed between them.
    /// </summary>
    [Theory]
    [InlineData(1919, 0)]
    [InlineData(1920, 1079)]
    public void TheSeamBetweenMonitorsIsNotACorner(int x, int y)
    {
        Assert.False(StopZones.InCorner(Desktop, x, y));
    }

    [Fact]
    public void TheMiddleOfTheScreenIsNotACorner()
    {
        Assert.False(StopZones.InCorner(Desktop, 1920, 540));
    }

    /// <summary>
    /// Widened from 2. Kept as a test so shrinking it again is a decision
    /// somebody makes rather than something that drifts.
    /// </summary>
    [Fact]
    public void TheCornerMarginIsForgivingEnoughToHit()
    {
        Assert.True(StopZones.CornerMarginPx >= 6);
    }

    // ---- taskbar and top ----

    /// <summary>The strip below the usable area is the taskbar.</summary>
    [Theory]
    [InlineData(960, 1032)]
    [InlineData(960, 1079)]
    [InlineData(0, 1050)]
    public void TheTaskbarStopsIt(int x, int y)
    {
        Assert.True(StopZones.OnTaskbarOrTop(Left, LeftWork, x, y));
    }

    [Fact]
    public void TheTopEdgeStopsIt()
    {
        Assert.True(StopZones.OnTaskbarOrTop(Left, LeftWork, 960, 0));
    }

    [Fact]
    public void JustUnderTheTopEdgeDoesNot()
    {
        Assert.False(StopZones.OnTaskbarOrTop(Left, LeftWork, 960, 40));
    }

    [Fact]
    public void TheMiddleOfTheScreenIsNeither()
    {
        Assert.False(StopZones.OnTaskbarOrTop(Left, LeftWork, 960, 540));
    }

    /// <summary>
    /// Just above the taskbar is where the pointer sits to click a taskbar
    /// button, and firing there would make the desktop unusable while running.
    /// </summary>
    [Fact]
    public void JustAboveTheTaskbarDoesNot()
    {
        Assert.False(StopZones.OnTaskbarOrTop(Left, LeftWork, 960, 1031));
    }

    /// <summary>
    /// Asked about the wrong screen it says no, rather than reading the second
    /// monitor's coordinates against the first monitor's taskbar.
    /// </summary>
    [Fact]
    public void APointOnAnotherMonitorIsNotInThisOnesZones()
    {
        Assert.False(StopZones.OnTaskbarOrTop(Left, LeftWork, 2880, 1079));
    }

    /// <summary>The second monitor has its own taskbar and its own top.</summary>
    [Theory]
    [InlineData(2880, 1079)]
    [InlineData(2880, 0)]
    public void TheSecondMonitorHasItsOwnZones(int x, int y)
    {
        Assert.True(StopZones.OnTaskbarOrTop(Right, RightWork, x, y));
    }

    /// <summary>
    /// A taskbar moved to the left edge is still found, because the test is
    /// "inside the screen but outside the usable part" rather than a guess
    /// about where Windows keeps it.
    /// </summary>
    [Fact]
    public void ATaskbarOnAnotherEdgeIsStillFound()
    {
        var sideways = new ScreenRect(0, 0, 1919, 1079);
        var work = new ScreenRect(60, 0, 1919, 1079);

        Assert.True(StopZones.OnTaskbarOrTop(sideways, work, 20, 540));
        Assert.False(StopZones.OnTaskbarOrTop(sideways, work, 960, 540));
    }

    /// <summary>
    /// An auto-hidden taskbar leaves no strip. The top edge still has to work,
    /// or the option would do nothing at all on that setup.
    /// </summary>
    [Fact]
    public void AnAutoHiddenTaskbarStillLeavesTheTopEdge()
    {
        Assert.True(StopZones.OnTaskbarOrTop(Left, Left, 960, 0));
        Assert.False(StopZones.OnTaskbarOrTop(Left, Left, 960, 1079));
    }

    // ---- the two together ----

    /// <summary>
    /// The corners are the always-on hatch and must not need the option, so a
    /// corner has to stop the clicker whether or not the taskbar zone is on.
    /// </summary>
    [Fact]
    public void ACornerStandsOnItsOwn()
    {
        Assert.True(StopZones.InCorner(Desktop, 0, 0));
        Assert.True(StopZones.InCorner(Desktop, 3839, 1079));
    }

    /// <summary>
    /// The bottom corners fall inside the taskbar strip too. Both saying yes is
    /// correct — the caller stops either way, and there is nothing to resolve.
    /// </summary>
    [Fact]
    public void TheBottomCornersAreInBothZones()
    {
        Assert.True(StopZones.InCorner(Desktop, 0, 1079));
        Assert.True(StopZones.OnTaskbarOrTop(Left, LeftWork, 0, 1079));
    }

    // ---- when a sample should actually stop it ----

    /// <summary>
    /// The hole this closes: a clicker started while the pointer was already on
    /// the taskbar or in a corner never stopped. Entering was the only trigger,
    /// and the zone had been entered before there was anything to stop.
    /// </summary>
    [Fact]
    public void StartingInsideAZoneStopsIt()
    {
        Assert.True(StopZones.ShouldStop(inZone: true, wasInZone: true, running: true, wasRunning: false));
    }

    [Fact]
    public void EnteringAZoneWhileRunningStopsIt()
    {
        Assert.True(StopZones.ShouldStop(inZone: true, wasInZone: false, running: true, wasRunning: true));
    }

    /// <summary>
    /// Parking there does not post a stop every poll for as long as the mouse
    /// sits in it.
    /// </summary>
    [Fact]
    public void SittingInAZoneDoesNotRepeat()
    {
        Assert.False(StopZones.ShouldStop(inZone: true, wasInZone: true, running: true, wasRunning: true));
    }

    [Fact]
    public void NothingHappensWhileItIsNotRunning()
    {
        Assert.False(StopZones.ShouldStop(inZone: true, wasInZone: false, running: false, wasRunning: false));
    }

    [Fact]
    public void BeingOutsideEveryZoneStopsNothing()
    {
        Assert.False(StopZones.ShouldStop(inZone: false, wasInZone: true, running: true, wasRunning: true));
    }

    // ---- the rectangle itself ----

    [Fact]
    public void ARectangleIncludesItsOwnEdges()
    {
        Assert.True(Left.Contains(0, 0));
        Assert.True(Left.Contains(1919, 1079));
        Assert.False(Left.Contains(1920, 0));
        Assert.False(Left.Contains(-1, 0));
    }
}
