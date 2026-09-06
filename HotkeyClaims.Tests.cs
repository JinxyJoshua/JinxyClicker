using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Who may take a hotkey off whom.
/// </summary>
/// <remarks>
/// A disabled macro cannot run by any route, and the app says as much on the
/// card — but it went on holding its key against everything else, so the only
/// way to reuse that key was to delete a macro that was already doing nothing.
/// </remarks>
public class HotkeyClaimsTests
{
    private const int F = 0x46;
    private const int G = 0x47;

    private static HotkeyClaim Live(string name, int key) => new(name, key, Live: true);
    private static HotkeyClaim Off(string name, int key) => new(name, key, Live: false);

    [Fact]
    public void ALiveClaimBlocksTheKey()
    {
        Assert.True(HotkeyClaims.IsTaken(new[] { Live("Grim reaper", F) }, F));
    }

    /// <summary>The whole point of the change.</summary>
    [Fact]
    public void ADisabledClaimDoesNotBlockTheKey()
    {
        Assert.False(HotkeyClaims.IsTaken(new[] { Off("Grim reaper", F) }, F));
    }

    [Fact]
    public void ALiveClaimStillBlocksWhenADisabledOneSharesTheKey()
    {
        var claims = new[] { Off("Grim reaper", F), Live("AS", F) };

        Assert.True(HotkeyClaims.IsTaken(claims, F));
        Assert.Equal("AS", HotkeyClaims.Blocker(claims, F)!.Value.Name);
    }

    /// <summary>The message has to name the thing actually in the way.</summary>
    [Fact]
    public void TheBlockerNamesItself()
    {
        Assert.Equal("Auto switcher",
            HotkeyClaims.Blocker(new[] { Live("Auto switcher", F) }, F)!.Value.Name);
    }

    [Fact]
    public void AKeyNobodyHoldsIsFree()
    {
        Assert.Null(HotkeyClaims.Blocker(new[] { Live("Grim reaper", G) }, F));
    }

    /// <summary>
    /// Unbound is zero, and several actions read "Not set" at once. Zero must
    /// never match zero, or the first unbound action would block every other.
    /// </summary>
    [Fact]
    public void NothingCollidesWithBeingUnbound()
    {
        Assert.Null(HotkeyClaims.Blocker(new[] { Live("Grim reaper", 0), Live("AS", 0) }, 0));
        Assert.False(HotkeyClaims.IsTaken(new[] { Live("Grim reaper", 0) }, 0));
    }

    // ---- switching something back on ----

    /// <summary>
    /// While it slept its key could have been claimed. Waking onto a key
    /// something else answers to would have one press fire two things.
    /// </summary>
    [Fact]
    public void WakingOntoATakenKeyIsACollision()
    {
        var others = new[] { Live("AS", F) };

        Assert.Equal("AS", HotkeyClaims.WouldCollideOnWaking(others, F)!.Value.Name);
    }

    [Fact]
    public void WakingOntoAFreeKeyIsFine()
    {
        Assert.Null(HotkeyClaims.WouldCollideOnWaking(new[] { Live("AS", G) }, F));
    }

    /// <summary>
    /// Nothing took the key while it slept, so it gets it back. Switching a
    /// macro off and straight back on must leave it exactly as it was.
    /// </summary>
    [Fact]
    public void SwitchingOffAndOnAgainKeepsTheKey()
    {
        var others = new[] { Off("Grim reaper", F) };

        Assert.Null(HotkeyClaims.WouldCollideOnWaking(others, F));
    }

    [Fact]
    public void SomethingElseAlsoAsleepDoesNotBlockWaking()
    {
        Assert.Null(HotkeyClaims.WouldCollideOnWaking(new[] { Off("AS", F) }, F));
    }

    // ---- excluding yourself ----

    /// <summary>
    /// A thing never collides with itself, or nothing could ever keep the key
    /// it already has.
    /// </summary>
    [Fact]
    public void SomethingIsNeverItsOwnBlocker()
    {
        var claims = new[] { Live("Grim reaper", F), Live("AS", G) };

        Assert.Null(HotkeyClaims.WouldCollideOnWaking(
            HotkeyClaims.Except(claims, "Grim reaper"), F));
    }

    [Fact]
    public void ExcludingOneLeavesTheRest()
    {
        var claims = new[] { Live("a", F), Live("b", G) };

        Assert.Equal(new[] { "b" }, HotkeyClaims.Except(claims, "a").Select(c => c.Name));
    }
}
