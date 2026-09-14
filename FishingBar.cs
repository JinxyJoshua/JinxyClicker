using System;

namespace JinxyClicker;

/// <summary>
/// What one look at the fishing bar found, and what to do about it.
/// </summary>
/// <remarks>
/// Read off a recording of the minigame frame by frame rather than guessed. The
/// bar has a grey needle that stays put — the target — and a green zone that
/// slides left and right, which the player steers by holding or releasing the
/// mouse. Keeping the zone centred on the needle fills a second bar underneath,
/// and filling it lands the fish.
///
/// So the whole of the automation is: find the green zone, compare its centre to
/// the needle, and hold when the zone has drifted to one side. This type is that
/// comparison, kept away from the screen-grabbing and the clicking so it can be
/// tested against the real frames the numbers came from.
/// </remarks>
public readonly record struct FishingReading(bool BarPresent, int ZoneLeft, int ZoneRight)
{
    public int ZoneCenter => (ZoneLeft + ZoneRight) / 2;

    /// <summary>Nothing found — the minigame is not on screen.</summary>
    public static readonly FishingReading None = new(false, 0, 0);
}

/// <summary>What the mouse should be doing this instant.</summary>
public enum FishAction
{
    /// <summary>No bar on screen. Leave the mouse alone.</summary>
    Idle,

    /// <summary>Zone has drifted to the release side of the needle. Let go.</summary>
    Release,

    /// <summary>Zone has drifted to the hold side of the needle. Hold the button.</summary>
    Hold
}

public static class FishingBar
{
    /// <summary>
    /// The smallest green zone worth believing, in pixels.
    /// </summary>
    /// <remarks>
    /// The zone measured about 195px wide on a 1920-wide capture. A run far
    /// shorter than that is a stray green pixel from the world — the map has
    /// grass on it — not the bar, so it is refused rather than steered toward.
    /// </remarks>
    public const int MinZoneWidthPx = 60;

    /// <summary>
    /// Finds the green zone on one scanned row.
    /// </summary>
    /// <param name="green">
    /// One bool per pixel across the row: true where the pixel is the zone's
    /// green. The needle draws a short grey gap in the middle of the green, so
    /// the zone is the span from the first green pixel to the last, gap and all.
    /// </param>
    /// <remarks>
    /// Span rather than segment, deliberately. The needle splits the green into
    /// two runs and either run alone has a centre that lurches as the needle
    /// crosses it; the outer edges of the whole zone move smoothly, which is
    /// what a controller needs.
    /// </remarks>
    public static FishingReading Read(ReadOnlySpan<bool> green)
    {
        int left = -1, right = -1;

        for (int x = 0; x < green.Length; x++)
        {
            if (!green[x]) continue;
            if (left < 0) left = x;
            right = x;
        }

        if (left < 0 || right - left + 1 < MinZoneWidthPx)
            return FishingReading.None;

        return new FishingReading(true, left, right);
    }

    /// <summary>
    /// Whether the button should be held, given where the zone sits relative to
    /// the needle.
    /// </summary>
    /// <param name="reading">The zone this instant.</param>
    /// <param name="needleX">The fixed target the zone is steered onto.</param>
    /// <param name="deadzonePx">
    /// How far off centre is tolerated before acting. Without it the controller
    /// flips hold and release every frame while the zone sits on the needle,
    /// which reads as a buzz rather than a hold.
    /// </param>
    /// <param name="holdPushesRight">
    /// Which way holding moves the zone. Read off the recording it pushes the
    /// zone toward the needle from the left, so a zone left of the needle is
    /// held; a game that runs the other way flips this once.
    /// </param>
    public static FishAction Decide(
        FishingReading reading, int needleX, int deadzonePx, bool holdPushesRight)
    {
        if (!reading.BarPresent) return FishAction.Idle;

        int error = reading.ZoneCenter - needleX;

        if (Math.Abs(error) <= deadzonePx) return FishAction.Release;

        // Zone is left of the needle (error < 0). Holding is right when it moves
        // the zone toward the needle from that side.
        bool needToPushRight = error < 0;

        return needToPushRight == holdPushesRight ? FishAction.Hold : FishAction.Release;
    }
}
