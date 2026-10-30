using System;

namespace JinxyClicker;

/// <summary>
/// Humanize: randomises the gap between clicks so the stream isn't perfectly
/// even. This is the opposite goal to Ultra Accuracy — it trades a little
/// hit-registration consistency for a less robotic pattern — so it is an
/// explicit opt-in and off by default.
/// </summary>
public static class ClickJitter
{
    /// <summary>The gap never falls below this, so jitter can't invert or stall.</summary>
    public const double MinGapMs = 1.0;

    /// <summary>
    /// The jittered gap. <paramref name="amount01"/> is the humanize amount as a
    /// 0–1 fraction; <paramref name="rand01"/> is a fresh random value in [0,1).
    /// The gap is scaled by a symmetric factor in [1-amount, 1+amount].
    /// </summary>
    public static double Gap(double baseGapMs, double amount01, double rand01)
    {
        if (amount01 <= 0) return baseGapMs;

        double j = Math.Clamp(amount01, 0.0, 1.0);
        double factor = 1.0 + (rand01 * 2.0 - 1.0) * j;
        return Math.Max(MinGapMs, baseGapMs * factor);
    }
}
