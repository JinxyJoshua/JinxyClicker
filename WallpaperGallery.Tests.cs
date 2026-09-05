using System.Linq;
using System.Windows.Media;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The backgrounds that ship with the app.
/// </summary>
/// <remarks>
/// They are drawn rather than stored, so the things worth pinning are the ones
/// a picture file would have made obvious at a glance: that every design is
/// dark enough to sit behind the interface, that none of them collide, and that
/// the same design is the same picture every time.
/// </remarks>
public class WallpaperGalleryTests
{
    [Fact]
    public void ShipsSomethingToChooseFrom()
    {
        Assert.True(WallpaperGallery.All.Count >= 8);
    }

    [Fact]
    public void EveryDesignHasItsOwnName()
    {
        var names = WallpaperGallery.All.Select(d => d.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryStyleIsUsedAtLeastOnce()
    {
        foreach (WallpaperStyle style in System.Enum.GetValues<WallpaperStyle>())
            Assert.Contains(WallpaperGallery.All, d => d.Style == style);
    }

    // ---- dark enough to sit behind the interface ----

    /// <summary>
    /// This goes behind a dark interface and is dimmed on top of that. A light
    /// background does not read as cheerful, it reads as text nobody can see.
    /// </summary>
    [Fact]
    public void EveryGroundColourIsDark()
    {
        foreach (WallpaperDesign design in WallpaperGallery.All)
        {
            Assert.True(Brightness(design.Deep) < 0.20,
                $"{design.Name}'s deep colour is too light to sit behind panels");

            Assert.True(Brightness(design.Mid) < 0.32,
                $"{design.Name}'s mid colour is too light to sit behind panels");
        }
    }

    /// <summary>The glow is the one part allowed to be bright — it is the light.</summary>
    [Fact]
    public void EveryGlowIsBrighterThanItsGround()
    {
        foreach (WallpaperDesign design in WallpaperGallery.All)
            Assert.True(Brightness(design.Glow) > Brightness(design.Mid), design.Name);
    }

    [Fact]
    public void EveryColourParses()
    {
        foreach (WallpaperDesign design in WallpaperGallery.All)
        {
            foreach (string hex in new[] { design.Deep, design.Mid, design.Glow })
            {
                object? parsed = ColorConverter.ConvertFromString(hex);
                Assert.NotNull(parsed);
            }
        }
    }

    private static double Brightness(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);

        // Rough perceptual weighting. Precision does not matter here; the
        // question is only "is this dark", not "how dark exactly".
        return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
    }

    // ---- finding one by name ----

    [Fact]
    public void FindsADesignByName()
    {
        WallpaperDesign expected = WallpaperGallery.All[0];

        Assert.Equal(expected, WallpaperGallery.Find(expected.Name));
    }

    /// <summary>A stored name comes back from a settings file and may be typed.</summary>
    [Fact]
    public void FindingIgnoresCase()
    {
        Assert.NotNull(WallpaperGallery.Find(WallpaperGallery.All[0].Name.ToUpperInvariant()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Nonesuch")]
    [InlineData(null)]
    public void FindsNothingForANameItDoesNotHave(string? name)
    {
        Assert.Null(WallpaperGallery.Find(name));
    }
}
