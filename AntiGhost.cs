using System;

namespace JinxyClicker;

/// <summary>
/// The occasional long press that keeps a drawn weapon from ghosting.
/// </summary>
/// <remarks>
/// A crossbow was firing nothing. The cause was press length: HitFix floors a
/// press at 15ms and raises the period to fit, so above about 45 CPS every
/// press is a flat 15ms however high the slider goes. That is under whatever
/// the bow needs — clicking by hand fires it every time, and a hand press runs
/// near 60-120ms.
///
/// The obvious fix is to floor every press at a hand press. It cannot be done.
/// The best rate this app has measured is 33.3 delivered clicks a second, which
/// is a 30ms cycle, and 15 of that is already the release. A 100ms press and 33
/// clicks a second are arithmetically exclusive — flooring every press would
/// cap a sword at 8.7/s.
///
/// So only one press a second is long. A sword loses about one click in twelve
/// and nothing else. A bow needs one long press to fire and cannot fire faster
/// than that anyway, so it loses nothing at all.
///
/// Always on, with nothing to configure. A toggle was built first and thrown
/// away: somebody whose shots are vanishing wants them to stop vanishing, not
/// a setting to go and find.
/// </remarks>
public static class AntiGhost
{
    /// <summary>
    /// How long the occasional press is held.
    /// </summary>
    /// <remarks>
    /// DEDUCED, not measured. The only fact available is that a hand click
    /// fires the bow, and a hand click runs somewhere near 60-120ms; 100 sits
    /// inside that with room either side.
    ///
    /// Raising it costs proportionally more of the click rate, which is the
    /// reason not to be generous "just in case": at 100ms the cost is under a
    /// tenth of the rate, and <see cref="AntiGhostTests"/> holds that line.
    /// </remarks>
    public const double PressMs = 100.0;

    /// <summary>
    /// How often a long press is sent.
    /// </summary>
    /// <remarks>
    /// Once a second. More often multiplies the cost on a sword for no gain on
    /// a bow, which has its own reload between shots and cannot use them.
    /// </remarks>
    public const double EveryMs = 1000.0;

    /// <summary>
    /// The gap after a long press. A press with no observed release is a held
    /// button, not a shot.
    /// </summary>
    public const double MinReleaseMs = 15.0;

    /// <summary>Whether this click is the one that gets held.</summary>
    public static bool IsDue(double sinceLastLongMs) => sinceLastLongMs >= EveryMs;

    /// <summary>
    /// How long to hold this press.
    /// </summary>
    /// <remarks>
    /// Never shortens one. At a low enough rate the ordinary press is already
    /// longer than this, and clipping it back to 100ms would be this feature
    /// causing the very thing it exists to prevent.
    /// </remarks>
    public static double PressFor(double normalDownMs, bool due) =>
        due ? Math.Max(normalDownMs, PressMs) : normalDownMs;

    /// <summary>The cycle length that leaves room for the press and its release.</summary>
    public static double PeriodFor(double normalPeriodMs, double pressMs) =>
        Math.Max(normalPeriodMs, pressMs + MinReleaseMs);

    /// <summary>
    /// What this costs, as a share of the click rate, at a given cycle length.
    /// </summary>
    /// <remarks>
    /// Here so the cost is a number the tests can hold rather than a claim in a
    /// comment. One long cycle replaces one ordinary one in every second.
    /// </remarks>
    public static double RateCostFraction(double normalPeriodMs)
    {
        if (normalPeriodMs <= 0) return 0;

        double longPeriod = PeriodFor(normalPeriodMs, PressFor(normalPeriodMs, due: true));
        double added = longPeriod - normalPeriodMs;

        return added / (EveryMs + added);
    }
}
