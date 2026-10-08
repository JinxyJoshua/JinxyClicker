using System.Linq;
using Xunit;

namespace JinxyClicker;

public class GamingModeTests
{
    private readonly GamingMode _mode = new();

    [Theory]
    [InlineData("high-performance-plan")]
    [InlineData("roblox-fullscreen-opt")]
    [InlineData("game-dvr")]
    [InlineData("visual-effects")]
    [InlineData("transparency")]
    [InlineData("core-parking")]
    [InlineData("power-throttling")]
    public void The_bundle_includes_the_real_impact_tweaks(string id)
    {
        Assert.Contains(id, _mode.Ids);
    }

    [Theory]
    [InlineData("qos-policy")]       // "depends entirely on your router"
    [InlineData("sysmain")]          // "little or none on an SSD"
    [InlineData("gpu-scheduling")]   // "mixed — reboot required"
    [InlineData("tracking-helper")]  // changes mouse feel; a preference, not a boost
    public void The_bundle_excludes_the_marginal_or_risky_tweaks(string id)
    {
        Assert.DoesNotContain(id, _mode.Ids);
    }

    [Fact]
    public void Only_core_parking_and_power_throttling_need_admin()
    {
        Assert.Equal(
            new[] { "core-parking", "power-throttling" }.OrderBy(x => x),
            _mode.AdminIds.OrderBy(x => x));
    }

    [Fact]
    public void Every_bundled_tweak_has_a_distinct_id()
    {
        Assert.Equal(_mode.Ids.Count(), _mode.Ids.Distinct().Count());
    }
}
