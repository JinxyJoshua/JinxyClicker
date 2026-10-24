using System;

namespace JinxyClicker;

/// <summary>
/// The right-button block Blockhit assist raises in the gap between attacks.
/// </summary>
/// <param name="Blocks">Whether the gap is long enough to hold a readable block.</param>
/// <param name="BlockMs">How long the right button is held down.</param>
/// <param name="ReleaseMs">
/// The clear gap left after the block's release, before the next attack. When
/// <see cref="Blocks"/> is false this is the whole gap, so the loop waits the
/// same total time either way.
/// </param>
public readonly record struct BlockHitPlan(bool Blocks, double BlockMs, double ReleaseMs);

/// <summary>
/// Works out the sword block that Blockhit assist raises between left-click
/// attacks.
/// </summary>
/// <remarks>
/// In BedWars a "block-hit" is holding the sword's right-click block while
/// tapping attacks through it: the block cuts the knockback you take without
/// stopping you swinging. The assist reproduces that by filling the otherwise
/// idle gap between attacks with a right-button hold — so it never slows the
/// attack cadence, it just stops that gap being wasted.
///
/// A block the game cannot read is worse than none — it costs the gap and buys
/// nothing — so the block only goes out when the gap is long enough for a
/// press the game will see plus a clear release after it. At the auto-tune
/// ceiling the gap is too short, which is honest: you cannot both swing at the
/// absolute maximum and block between every swing.
/// </remarks>
public static class BlockHit
{
    /// <summary>
    /// Shortest block the game can read — the same frame reasoning as HitFix's
    /// press floor: a press shorter than this can fall between two input reads
    /// and never be seen.
    /// </summary>
    public const double MinBlockMs = 15.0;

    /// <summary>
    /// A short clear gap kept after the block is released, so the next attack is
    /// not sent on top of the block.
    /// </summary>
    public const double ReleaseMarginMs = 4.0;

    /// <summary>
    /// Plans the block for one attack cycle from the timing already being sent.
    /// </summary>
    public static BlockHitPlan Plan(double periodMs, double downMs)
    {
        double gap = periodMs - downMs;
        double blockMs = gap - ReleaseMarginMs;

        if (gap <= 0 || blockMs < MinBlockMs)
            return new BlockHitPlan(false, 0, Math.Max(gap, 0));

        return new BlockHitPlan(true, blockMs, ReleaseMarginMs);
    }
}
