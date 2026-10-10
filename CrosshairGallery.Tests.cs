using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// A saved custom crosshair turns into a drawable style. Pro adds fine control
/// (thickness, gap, dot size, opacity, glow); without it, the classic look holds.
/// </summary>
public class CustomCrosshairTests
{
    [Fact]
    public void ToStyleUsesClassicDefaultsWhenNothingIsTuned()
    {
        var c = new CustomCrosshair { Shape = "Cross", Color = "#FF0000" };
        CrosshairStyle s = c.ToStyle();

        Assert.Equal(CrosshairShape.Cross, s.Shape);
        Assert.Equal("#FF0000", s.ColorHex);
        Assert.Equal(3, s.Thickness);
        Assert.Equal(4, s.Gap);
        Assert.Equal(3, s.DotSize);
        Assert.Equal(100, s.OpacityPercent);
        Assert.Equal(0, s.GlowPercent);
    }

    [Fact]
    public void ToStyleCarriesProTuningWhenSet()
    {
        var c = new CustomCrosshair
        {
            Shape = "CrossDot",
            Color = "#00FFEE",
            DotColor = "#FFFFFF",
            Thickness = 6,
            Gap = 10,
            DotSize = 5,
            Opacity = 70,
            Glow = 40
        };
        CrosshairStyle s = c.ToStyle();

        Assert.Equal(6, s.Thickness);
        Assert.Equal(10, s.Gap);
        Assert.Equal(5, s.DotSize);
        Assert.Equal(70, s.OpacityPercent);
        Assert.Equal(40, s.GlowPercent);
        Assert.Equal("#FFFFFF", s.EffectiveDotColor);
    }
}
