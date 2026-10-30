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
}
