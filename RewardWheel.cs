using System;
using System.Collections.Generic;

namespace JinxyClicker;

public enum Rarity { Common, Rare, Epic, Legendary, Mythic }

/// <summary>What a wheel slice gives: soft currency, a cosmetic, or Premium time.</summary>
public enum RewardKind { Stardust, Cursor, Wallpaper, Crosshair, Premium }

/// <summary>
/// One reward in the catalog. <paramref name="Value"/> is the amount for Stardust,
/// the hours for Premium, and unused (0) for cosmetics (which are owned once).
/// </summary>
public sealed record Reward(string Id, string Name, Rarity Rarity, RewardKind Kind, int Value = 0)
{
    /// <summary>Cosmetics count toward the collection; Stardust/Premium don't.</summary>
    public bool Collectible => Kind is RewardKind.Cursor or RewardKind.Wallpaper or RewardKind.Crosshair;
}

/// <summary>
/// The daily-rewards randomness: the published rarity odds, a weighted roll, and the
/// pity rule that guarantees a Legendary (or better) by the 28th spin. Kept free of
/// UI so the fairness math can be tested on its own.
/// </summary>
public static class RewardWheel
{
    /// <summary>Chance of each rarity per spin, as percentages that sum to 100.</summary>
    public static readonly IReadOnlyDictionary<Rarity, double> Odds = new Dictionary<Rarity, double>
    {
        [Rarity.Common] = 50.0,
        [Rarity.Rare] = 28.0,
        [Rarity.Epic] = 14.0,
        [Rarity.Legendary] = 6.5,
        [Rarity.Mythic] = 1.5,
    };

    /// <summary>A Legendary (or Mythic) is guaranteed no later than this spin in a dry streak.</summary>
    public const int PityThreshold = 28;

    // Low-to-high so the cumulative walk matches the odds table's order.
    private static readonly Rarity[] Order =
        { Rarity.Common, Rarity.Rare, Rarity.Epic, Rarity.Legendary, Rarity.Mythic };

    /// <summary>
    /// Picks a rarity from a roll in [0,1). When <paramref name="spinsSinceLegendary"/>
    /// has reached <see cref="PityThreshold"/>-1 (so this is the 28th dry spin), the
    /// result is forced to Legendary or Mythic, split by their own odds.
    /// </summary>
    public static Rarity RollRarity(int spinsSinceLegendary, double roll)
    {
        roll = Math.Clamp(roll, 0.0, 0.9999999);

        if (spinsSinceLegendary >= PityThreshold - 1)
        {
            double leg = Odds[Rarity.Legendary], myth = Odds[Rarity.Mythic];
            return roll < myth / (leg + myth) ? Rarity.Mythic : Rarity.Legendary;
        }

        double cumulative = 0;
        foreach (Rarity r in Order)
        {
            cumulative += Odds[r] / 100.0;
            if (roll < cumulative) return r;
        }
        return Rarity.Common;   // rounding guard
    }

    /// <summary>True once a rarity counts as Legendary or Mythic (resets the pity count).</summary>
    public static bool ResetsPity(Rarity r) => r is Rarity.Legendary or Rarity.Mythic;

    /// <summary>
    /// Rolls a full reward: a rarity (with pity) then a random item of that rarity from
    /// the catalog. Falls back to the nearest lower rarity that has items, so a sparse
    /// catalog never returns null.
    /// </summary>
    public static Reward Roll(IReadOnlyList<Reward> catalog, int spinsSinceLegendary, Random rng)
    {
        if (catalog is null || catalog.Count == 0)
            throw new ArgumentException("catalog is empty", nameof(catalog));

        Rarity target = RollRarity(spinsSinceLegendary, rng.NextDouble());
        return PickOfRarity(catalog, target, rng);
    }

    private static Reward PickOfRarity(IReadOnlyList<Reward> catalog, Rarity target, Random rng)
    {
        // Walk down from the target until a rarity with items is found, then up.
        for (int r = (int)target; r >= 0; r--)
        {
            Reward? pick = RandomOf(catalog, (Rarity)r, rng);
            if (pick != null) return pick;
        }
        for (int r = (int)target + 1; r <= (int)Rarity.Mythic; r++)
        {
            Reward? pick = RandomOf(catalog, (Rarity)r, rng);
            if (pick != null) return pick;
        }
        return catalog[rng.Next(catalog.Count)];
    }

    private static Reward? RandomOf(IReadOnlyList<Reward> catalog, Rarity r, Random rng)
    {
        var bucket = new List<Reward>();
        foreach (Reward reward in catalog)
            if (reward.Rarity == r) bucket.Add(reward);
        return bucket.Count == 0 ? null : bucket[rng.Next(bucket.Count)];
    }

    /// <summary>
    /// The angle to animate the wheel to: at least <paramref name="turns"/> whole
    /// turns beyond where it is now, finishing at <paramref name="landing"/> — the
    /// 0–360° orientation that puts the chosen slice under the top pointer.
    /// </summary>
    /// <remarks>
    /// Measured from the real current angle, which keeps growing spin after spin.
    /// Anchoring the target to <c>angle % 360</c> instead (an earlier bug) cancelled
    /// the full turns out against the accumulated angle, so every spin after the
    /// first barely moved.
    /// </remarks>
    public static double SpinTarget(double currentAngle, double landing, int turns)
    {
        double currentMod = (currentAngle % 360 + 360) % 360;
        double norm = (landing % 360 + 360) % 360;
        double forward = (norm - currentMod + 360) % 360;   // 0–360° to reach the landing
        return currentAngle + turns * 360 + forward;
    }
}
