using System;
using System.Collections.Generic;
using JinxyClicker;
using Xunit;

public class RewardWheelTests
{
    [Fact]
    public void Odds_sum_to_100()
    {
        double sum = 0;
        foreach (double v in RewardWheel.Odds.Values) sum += v;
        Assert.Equal(100.0, sum, 3);
    }

    [Theory]
    [InlineData(0.00, Rarity.Common)]     // start of Common band
    [InlineData(0.49, Rarity.Common)]     // inside Common (0..0.50)
    [InlineData(0.50, Rarity.Rare)]       // start of Rare (0.50..0.78)
    [InlineData(0.77, Rarity.Rare)]
    [InlineData(0.78, Rarity.Epic)]       // Epic (0.78..0.92)
    [InlineData(0.91, Rarity.Epic)]
    [InlineData(0.92, Rarity.Legendary)]  // Legendary (0.92..0.985)
    [InlineData(0.984, Rarity.Legendary)]
    [InlineData(0.99, Rarity.Mythic)]     // Mythic (~0.985..1.0)
    [InlineData(0.999, Rarity.Mythic)]
    public void RollRarity_maps_the_roll_to_the_right_band(double roll, Rarity expected)
    {
        Assert.Equal(expected, RewardWheel.RollRarity(0, roll));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    [InlineData(0.99)]
    public void Pity_forces_legendary_or_better_on_the_28th_dry_spin(double roll)
    {
        // 27 spins without a Legendary means this one (the 28th) is guaranteed.
        Rarity r = RewardWheel.RollRarity(RewardWheel.PityThreshold - 1, roll);
        Assert.True(r is Rarity.Legendary or Rarity.Mythic, $"expected Legendary+ but got {r}");
    }

    [Fact]
    public void Pity_does_not_trigger_one_spin_early()
    {
        // At 26 dry spins a Common roll is still allowed.
        Assert.Equal(Rarity.Common, RewardWheel.RollRarity(PityMinusTwo(), 0.0));
    }

    private static int PityMinusTwo() => RewardWheel.PityThreshold - 2;

    [Fact]
    public void Legendary_and_mythic_reset_pity()
    {
        Assert.True(RewardWheel.ResetsPity(Rarity.Legendary));
        Assert.True(RewardWheel.ResetsPity(Rarity.Mythic));
        Assert.False(RewardWheel.ResetsPity(Rarity.Epic));
        Assert.False(RewardWheel.ResetsPity(Rarity.Common));
    }

    private static readonly List<Reward> Catalog = new()
    {
        new Reward("c1", "Common A", Rarity.Common, RewardKind.Stardust, 15),
        new Reward("r1", "Rare A", Rarity.Rare, RewardKind.Cursor),
        new Reward("e1", "Epic A", Rarity.Epic, RewardKind.Wallpaper),
        new Reward("l1", "Legend A", Rarity.Legendary, RewardKind.Cursor),
        new Reward("m1", "Myth A", Rarity.Mythic, RewardKind.Wallpaper),
    };

    [Fact]
    public void Roll_returns_a_reward_of_the_catalog()
    {
        Reward r = RewardWheel.Roll(Catalog, 0, new Random(1));
        Assert.Contains(r, Catalog);
    }

    [Fact]
    public void Roll_under_pity_yields_a_legendary_or_mythic_item()
    {
        for (int seed = 0; seed < 25; seed++)
        {
            Reward r = RewardWheel.Roll(Catalog, RewardWheel.PityThreshold - 1, new Random(seed));
            Assert.True(r.Rarity is Rarity.Legendary or Rarity.Mythic);
        }
    }

    [Fact]
    public void Roll_falls_back_when_the_target_rarity_has_no_items()
    {
        // Only a Common item exists; a Mythic target must still return it, not throw.
        var sparse = new List<Reward> { new("c", "Only", Rarity.Common, RewardKind.Stardust, 10) };
        Reward r = RewardWheel.Roll(sparse, 0, new Random(5));
        Assert.Equal("c", r.Id);
    }

    [Fact]
    public void Empty_catalog_throws()
    {
        Assert.Throws<ArgumentException>(() => RewardWheel.Roll(new List<Reward>(), 0, new Random()));
    }

    [Fact]
    public void Distribution_is_close_to_the_published_odds()
    {
        var rng = new Random(12345);
        var counts = new Dictionary<Rarity, int>();
        const int n = 200_000;
        for (int i = 0; i < n; i++)
        {
            Rarity r = RewardWheel.RollRarity(0, rng.NextDouble());
            counts[r] = counts.GetValueOrDefault(r) + 1;
        }
        foreach ((Rarity rarity, double pct) in RewardWheel.Odds)
        {
            double actual = 100.0 * counts.GetValueOrDefault(rarity) / n;
            Assert.True(Math.Abs(actual - pct) < 1.0, $"{rarity}: expected ~{pct}%, got {actual:0.00}%");
        }
    }

    // ---- SpinTarget: where the wheel animates to on a spin ----

    [Theory]
    [InlineData(0)]       // first spin
    [InlineData(137)]
    [InlineData(1980)]    // after a prior spin — the angle has accumulated
    [InlineData(3960)]    // after several — this is where the old code barely moved
    public void SpinTarget_always_turns_at_least_the_requested_full_turns(double current)
    {
        double to = RewardWheel.SpinTarget(current, landing: 90, turns: 5);
        double moved = to - current;
        Assert.True(moved >= 5 * 360, $"only moved {moved}°, wanted >= {5 * 360}°");
        Assert.True(moved < 6 * 360, $"moved {moved}°, wanted < {6 * 360}°");
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(1980, 250)]
    [InlineData(3960, 12.5)]
    [InlineData(777, 0)]
    public void SpinTarget_rests_on_the_chosen_orientation(double current, double landing)
    {
        double to = RewardWheel.SpinTarget(current, landing, turns: 5);
        double resting = (to % 360 + 360) % 360;
        double want = (landing % 360 + 360) % 360;
        Assert.Equal(want, resting, precision: 6);
    }

    [Fact]
    public void SpinTarget_normalizes_a_landing_outside_0_to_360()
    {
        double to = RewardWheel.SpinTarget(0, landing: -30, turns: 5);   // -30 == 330
        double resting = (to % 360 + 360) % 360;
        Assert.Equal(330, resting, precision: 6);
    }
}
