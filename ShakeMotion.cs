using System;

namespace JinxyClicker;

/// <summary>
/// The smoothness easing for Shaky Tracking.
/// </summary>
/// <remarks>
/// The shake picks a fresh random point inside its box every tick. Without
/// smoothing it jumps straight there, which reads as a sharp jitter. Smoothing
/// moves only a fraction of the remaining distance each tick instead, so the
/// cursor eases toward a moving target and glides like a hand drifting rather
/// than flicking. It is pure motion quality — there is no target to lock onto,
/// no player detection; the random point is still random.
/// </remarks>
public static class ShakeMotion
{
    /// <summary>
    /// Fraction of the remaining distance moved per tick at full smoothness. Low
    /// enough to glide, high enough that a 1px box still visibly moves.
    /// </summary>
    public const double MinFactor = 0.08;

    /// <summary>
    /// How much of the way to the target to move this tick, from a 0–1 smoothness.
    /// 0 → 1.0 (jump the whole way), 1 → <see cref="MinFactor"/> (a small glide step).
    /// </summary>
    public static double Factor(double smoothness) =>
        1.0 - Math.Clamp(smoothness, 0.0, 1.0) * (1.0 - MinFactor);

    /// <summary>Eases <paramref name="current"/> toward <paramref name="target"/> by one tick.</summary>
    public static double Ease(double current, double target, double smoothness) =>
        current + (target - current) * Factor(smoothness);
}
