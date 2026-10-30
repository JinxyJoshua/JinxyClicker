using System.Linq;
using Xunit;

namespace JinxyClicker;

public class LowerPingTests
{
    private readonly LowerPing _bundle = new();

    [Theory]
    [InlineData("net-throttling")]
    [InlineData("nagle")]
    [InlineData("wifi-power")]
    [InlineData("wudo-off")]
    [InlineData("bg-store-apps")]
    public void The_bundle_includes_every_network_tweak(string id)
    {
        Assert.Contains(id, _bundle.Ids);
    }

    [Fact]
    public void The_bundle_is_only_network_tweaks()
    {
        // None of the CPU/GPU/visual tweaks from the Optimizations page leaked in.
        Assert.DoesNotContain("high-performance-plan", _bundle.Ids);
        Assert.DoesNotContain("gpu-scheduling", _bundle.Ids);
        Assert.DoesNotContain("visual-effects", _bundle.Ids);
    }

    [Fact]
    public void The_machine_wide_tweaks_need_admin_and_the_per_user_ones_do_not()
    {
        Assert.Equal(
            new[] { "nagle", "net-throttling", "wudo-off" }.OrderBy(x => x),
            _bundle.AdminIds.OrderBy(x => x));
    }

    [Fact]
    public void Every_tweak_has_a_distinct_id()
    {
        Assert.Equal(_bundle.Ids.Count(), _bundle.Ids.Distinct().Count());
    }
}
