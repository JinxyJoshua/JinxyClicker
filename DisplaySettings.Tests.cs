using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyClicker;

public class DisplaySettingsTests
{
    private static readonly List<DisplayMode> Modes = new()
    {
        new DisplayMode(1920, 1080, 144),
        new DisplayMode(1920, 1080, 120),
        new DisplayMode(1920, 1080, 60),
        new DisplayMode(1920, 1080, 60),   // duplicate
        new DisplayMode(1440, 1080, 60),
        new DisplayMode(1280, 720, 60),
        new DisplayMode(1280, 720, 144),
    };

    [Fact]
    public void ResolutionsOf_dedupes_and_orders_widest_first()
    {
        List<Resolution> r = DisplaySettings.ResolutionsOf(Modes);

        Assert.Equal(new Resolution(1920, 1080), r[0]);
        Assert.Equal(new Resolution(1440, 1080), r[1]);
        Assert.Equal(new Resolution(1280, 720), r[2]);
        Assert.Equal(3, r.Count);   // the duplicate 1920x1080 collapses
    }

    [Fact]
    public void RefreshesOf_lists_only_that_resolutions_rates_highest_first()
    {
        List<int> r = DisplaySettings.RefreshesOf(Modes, 1920, 1080);

        Assert.Equal(new[] { 144, 120, 60 }, r);   // deduped, descending
    }

    [Fact]
    public void RefreshesOf_is_empty_for_an_unknown_resolution()
    {
        Assert.Empty(DisplaySettings.RefreshesOf(Modes, 800, 600));
    }

    [Fact]
    public void Resolution_renders_as_width_by_height()
    {
        Assert.Equal("1920 × 1080", new Resolution(1920, 1080).ToString());
    }

    [Fact]
    public void PresetsFor_lists_native_first_then_supported_curated_only()
    {
        var modes = new List<DisplayMode>
        {
            new(1920, 1080, 144), new(1600, 900, 60), new(1280, 960, 60), new(800, 600, 60),
        };

        var p = DisplaySettings.PresetsFor(modes);

        Assert.Equal(new Resolution(1920, 1080), p[0].Res);   // native = widest
        Assert.Equal("Native", p[0].Label);
        Assert.Contains(p, x => x.Res == new Resolution(1600, 900));   // supported curated
        Assert.Contains(p, x => x.Res == new Resolution(1280, 960));
        Assert.DoesNotContain(p, x => x.Res == new Resolution(800, 600));   // not a curated preset
        Assert.Single(p.Where(x => x.Res == new Resolution(1920, 1080)));   // native not duplicated
    }

    [Fact]
    public void PresetsFor_is_empty_without_modes()
        => Assert.Empty(DisplaySettings.PresetsFor(new List<DisplayMode>()));

    [Fact]
    public void Hardware_queries_do_not_throw_or_corrupt()
    {
        // Runs the real Win32 / DisplayConfig calls on the test machine. Values are
        // environment-dependent, but a bad P/Invoke layout would crash the host here
        // (especially the HDR DisplayConfig path with its fixed-size struct array).
        _ = DisplaySettings.SupportedModes();
        _ = DisplaySettings.ScalingPercent();
        _ = DisplaySettings.CurrentBits();
    }
}
