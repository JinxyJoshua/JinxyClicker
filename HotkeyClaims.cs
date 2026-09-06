using System.Collections.Generic;
using System.Linq;

namespace JinxyClicker;

/// <summary>One thing holding a hotkey, and whether it is switched on.</summary>
/// <param name="Name">What to call it if it turns out to be in the way.</param>
/// <param name="VirtualKey">The key it holds. Zero means it holds none.</param>
/// <param name="Live">
/// False for anything switched off — a disabled macro, or the auto switcher
/// with its own Disable applied.
/// </param>
public readonly record struct HotkeyClaim(string Name, int VirtualKey, bool Live);

/// <summary>
/// Who may take a hotkey off whom.
/// </summary>
/// <remarks>
/// A disabled macro cannot be started by its key, by its switch, or by anything
/// else — the app says so itself. It was still holding its key against every
/// other action, so the only way to reuse a key was to delete the macro that
/// was already doing nothing with it.
///
/// So being switched off releases the claim. The key is not taken away: the
/// macro keeps it, and gets it back if nothing else has claimed it by the time
/// it is switched on again. What changes is that a dormant claim no longer
/// outranks a live one.
///
/// Kept as plain functions over a list of claims rather than reaching into the
/// window's fields, so the rule can be tested without a UI — the previous
/// collision logic could only be exercised by clicking.
/// </remarks>
public static class HotkeyClaims
{
    /// <summary>
    /// Whether <paramref name="virtualKey"/> is already spoken for.
    /// </summary>
    /// <remarks>
    /// Only live claims block. Anything switched off is passed over, which is
    /// the whole point.
    /// </remarks>
    public static bool IsTaken(IEnumerable<HotkeyClaim> claims, int virtualKey) =>
        Blocker(claims, virtualKey) != null;

    /// <summary>The live claim standing in the way, or null if the key is free.</summary>
    public static HotkeyClaim? Blocker(IEnumerable<HotkeyClaim> claims, int virtualKey)
    {
        if (virtualKey == 0) return null;

        foreach (HotkeyClaim claim in claims)
        {
            if (claim.Live && claim.VirtualKey == virtualKey) return claim;
        }

        return null;
    }

    /// <summary>
    /// Whether something being switched on has to give up its key first.
    /// </summary>
    /// <remarks>
    /// The other half of releasing a dormant claim. While it slept, its key
    /// could have been taken; waking up on a key something else answers to
    /// would leave one press firing two things, which the app has always
    /// treated as looking like a fault rather than like a choice.
    ///
    /// The one waking gives way, because the claim it would be overriding was
    /// made while this one was doing nothing.
    /// </remarks>
    public static HotkeyClaim? WouldCollideOnWaking(
        IEnumerable<HotkeyClaim> others, int virtualKey) =>
        Blocker(others, virtualKey);

    /// <summary>Every claim except the one being asked about, by name.</summary>
    public static IEnumerable<HotkeyClaim> Except(IEnumerable<HotkeyClaim> claims, string name) =>
        claims.Where(c => !string.Equals(c.Name, name, System.StringComparison.Ordinal));
}
