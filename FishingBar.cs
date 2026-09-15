using System;

namespace JinxyClicker;

/// <summary>
/// What one look at the fishing bar found, and what to do about it.
/// </summary>
/// <remarks>
/// Read off recordings of the minigame, and corrected by a second one. The bar
/// has a wide green <em>zone</em> — the target — and a thin vertical
/// <em>needle</em> that the player steers by holding or releasing the mouse.
/// Keeping the needle inside the zone fills a second bar underneath and lands
/// the fish. The needle is what moves under your control; the zone is where it
/// has to be.
///
/// The first recording hid this: the needle sat inside a centred zone the whole
/// time, so which one moved was impossible to tell, and the first build steered
/// the wrong one. The second recording showed the needle stuck to the right of
/// a zone on the left, catching nothing — the needle is the thing to drive.
///
/// This type is the decision, kept away from the screen-grabbing so it can be
/// tested against the real frames the numbers came from.
/// </remarks>
public readonly record struct FishingReading(bool BarPresent, int ZoneLeft, int ZoneRight, int NeedleX)
{
    public int ZoneCenter => (ZoneLeft + ZoneRight) / 2;

    public bool NeedleInZone => NeedleX >= ZoneLeft && NeedleX <= ZoneRight;

    /// <summary>Nothing found — the minigame is not on screen.</summary>
    public static readonly FishingReading None = new(false, 0, 0, 0);
}

/// <summary>What the mouse should be doing this instant.</summary>
public enum FishAction
{
    /// <summary>No bar on screen. Leave the mouse alone.</summary>
    Idle,

    /// <summary>Let go, so the needle drifts the way holding does not push it.</summary>
    Release,

    /// <summary>Hold the button, to push the needle toward the zone.</summary>
    Hold
}

public static class FishingBar
{
    /// <summary>
    /// The smallest green run worth calling the zone, in pixels.
    /// </summary>
    /// <remarks>
    /// The zone measured about 195px wide on a 1920-wide capture. A run far
    /// shorter than that is a stray green pixel from the world — the map has
    /// grass on it — or the thin needle itself, not the zone.
    /// </remarks>
    public const int MinZoneWidthPx = 60;

    /// <summary>
    /// Finds the green zone on one scanned row: the widest run of the zone's
    /// green.
    /// </summary>
    /// <param name="green">
    /// One bool per pixel across the row, true where the pixel is zone-green.
    /// </param>
    /// <returns>Left and right pixel of the widest qualifying run, or null.</returns>
    /// <remarks>
    /// Widest run rather than first, because the thin needle is green too when it
    /// sits outside the zone, and a bare green speck from the grass must not be
    /// mistaken for the target.
    /// </remarks>
    public static (int Left, int Right)? FindZone(ReadOnlySpan<bool> green)
    {
        int bestL = -1, bestR = -1, bestW = 0;
        int runL = -1;

        for (int x = 0; x <= green.Length; x++)
        {
            bool on = x < green.Length && green[x];

            if (on && runL < 0) runL = x;
            else if (!on && runL >= 0)
            {
                int w = x - runL;
                if (w > bestW) { bestW = w; bestL = runL; bestR = x - 1; }
                runL = -1;
            }
        }

        return bestW >= MinZoneWidthPx ? (bestL, bestR) : null;
    }

    /// <summary>
    /// Whether the button should be held, given where the needle is relative to
    /// the zone it has to be steered into.
    /// </summary>
    /// <param name="reading">The zone and needle this instant.</param>
    /// <param name="deadzonePx">
    /// How near the zone centre counts as close enough, so a needle sitting in
    /// the zone is left to drift rather than the button buzzing on and off every
    /// frame.
    /// </param>
    /// <param name="holdPushesRight">
    /// Which way holding drives the needle. A game that runs the other way flips
    /// this once.
    /// </param>
    public static FishAction Decide(FishingReading reading, int deadzonePx, bool holdPushesRight)
    {
        if (!reading.BarPresent) return FishAction.Idle;

        // Aim the needle at the middle of the zone; the zone's own width is the
        // room for error either side.
        int error = reading.NeedleX - reading.ZoneCenter;

        if (Math.Abs(error) <= deadzonePx) return FishAction.Release;

        // Needle left of the zone centre (error < 0) has to be pushed right.
        bool needToPushRight = error < 0;

        return needToPushRight == holdPushesRight ? FishAction.Hold : FishAction.Release;
    }
}
