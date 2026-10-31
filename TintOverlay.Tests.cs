using System.Linq;
using Xunit;

namespace JinxyClicker;

public class TintStylesTests
{
    [Fact]
    public void EveryPresetHasAUniqueId()
    {
        var ids = TintStyles.All.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void ByIdRoundTripsAndRejectsUnknowns()
    {
        foreach (TintPreset p in TintStyles.All)
            Assert.Same(p, TintStyles.ById(p.Id));

        Assert.Null(TintStyles.ById("nope"));
        Assert.Null(TintStyles.ById(null));
    }

    [Fact]
    public void AlphaScalesWithIntensityAndIsCapped()
    {
        Assert.Equal((byte)0, TintStyles.AlphaFor(0));
        Assert.Equal(TintStyles.MaxAlpha, TintStyles.AlphaFor(100));

        byte mid = TintStyles.AlphaFor(50);
        Assert.True(mid > 0 && mid < TintStyles.MaxAlpha);

        // Never fully opaque, even past the top of the slider.
        Assert.Equal(TintStyles.MaxAlpha, TintStyles.AlphaFor(500));
    }

    [Fact]
    public void VignetteAlphaScalesWithStrengthAndIsCapped()
    {
        Assert.Equal((byte)0, TintStyles.VignetteAlphaFor(0));
        Assert.Equal(TintStyles.MaxVignetteAlpha, TintStyles.VignetteAlphaFor(100));

        byte mid = TintStyles.VignetteAlphaFor(50);
        Assert.True(mid > 0 && mid < TintStyles.MaxVignetteAlpha);

        Assert.Equal(TintStyles.MaxVignetteAlpha, TintStyles.VignetteAlphaFor(1000));
    }
}

public class ColorLooksTests
{
    [Fact]
    public void EveryLookHasAUniqueIdAndReferencesARealTint()
    {
        var ids = ColorLooks.All.Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());

        Assert.All(ColorLooks.All, l =>
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Id));
            Assert.False(string.IsNullOrWhiteSpace(l.Name));
            Assert.False(string.IsNullOrWhiteSpace(l.Blurb));
            Assert.NotNull(TintStyles.ById(l.TintId));
            Assert.InRange(l.Intensity, 0, 100);
            Assert.InRange(l.Vignette, 0, 100);
        });
    }

    [Fact]
    public void ByIdRoundTripsAndRejectsUnknowns()
    {
        foreach (ColorLook l in ColorLooks.All)
            Assert.Same(l, ColorLooks.ById(l.Id));

        Assert.Null(ColorLooks.ById("nope"));
        Assert.Null(ColorLooks.ById(null));
    }
}
