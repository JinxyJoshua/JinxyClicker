using System.IO;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The wallpaper's file handling and dimming arithmetic.
/// </summary>
/// <remarks>
/// Decoding and painting need a window and are not tested here. What is tested
/// is everything deciding <em>which</em> file gets used and how dark it goes —
/// which is where this can quietly do the wrong thing, by following a path out
/// of a hand-edited settings file or by darkening a picture out of existence.
/// </remarks>
public class WallpaperTests
{
    [Theory]
    [InlineData("shot.png")]
    [InlineData("shot.jpg")]
    [InlineData("shot.jpeg")]
    [InlineData("shot.bmp")]
    [InlineData("shot.webp")]
    [InlineData(@"C:\Users\someone\Pictures\Holiday Photo.PNG")]
    public void AcceptsThePicturesWpfCanDecode(string path)
    {
        Assert.True(Wallpaper.IsSupported(path));
    }

    /// <summary>
    /// Refused up front rather than accepted and silently failing to decode,
    /// which would read as the wallpaper feature being broken.
    /// </summary>
    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("nodotpng")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RefusesWhatItCannotShow(string? path)
    {
        Assert.False(Wallpaper.IsSupported(path));
    }

    /// <summary>
    /// One stem for every wallpaper, so a new choice replaces the old rather
    /// than leaving the settings folder filling up with unreachable pictures.
    /// </summary>
    [Fact]
    public void StoresEveryWallpaperUnderTheSameName()
    {
        string first = Wallpaper.StoredNameFor(@"C:\a\sunset.png");
        string second = Wallpaper.StoredNameFor(@"D:\b\mountains.png");

        Assert.Equal(first, second);
    }

    /// <summary>The extension survives, because the decoder is chosen from it.</summary>
    [Theory]
    [InlineData(@"C:\a\sunset.PNG", ".png")]
    [InlineData(@"C:\a\sunset.JpEg", ".jpeg")]
    public void KeepsTheExtensionAndLowercasesIt(string source, string expected)
    {
        Assert.Equal(expected, Path.GetExtension(Wallpaper.StoredNameFor(source)));
    }

    [Theory]
    [InlineData(45, 45)]
    [InlineData(0, 0)]
    [InlineData(-20, Wallpaper.MinDimming)]
    [InlineData(500, Wallpaper.MaxDimming)]
    public void ClampsDimmingIntoRange(int given, int expected)
    {
        Assert.Equal(expected, Wallpaper.ClampDimming(given));
    }

    /// <summary>
    /// The ceiling is what stops a hand-edited 100 painting the picture out
    /// completely and reading as the feature being broken.
    /// </summary>
    [Fact]
    public void NeverDarkensAllTheWay()
    {
        Assert.True(Wallpaper.DimmingOpacity(100) < 1.0);
        Assert.True(Wallpaper.MaxDimming < 100);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(45, 0.45)]
    public void ReadsDimmingAsAnOpacity(int percent, double expected)
    {
        Assert.Equal(expected, Wallpaper.DimmingOpacity(percent), 3);
    }

    /// <summary>
    /// Only a bare name is followed. A settings file carrying a path — edited
    /// by hand, or copied from another machine — must not send the app reading
    /// somewhere of its choosing.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"..\..\somewhere\else.png")]
    [InlineData(@"sub\folder\wallpaper.png")]
    [InlineData("/etc/passwd")]
    public void RefusesAStoredNameCarryingAPath(string stored)
    {
        Assert.Null(Wallpaper.Resolve(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolvesNothingWhenNoneIsSet(string? stored)
    {
        Assert.Null(Wallpaper.Resolve(stored));
    }

    [Fact]
    public void ResolvesNothingWhenTheCopyIsGone()
    {
        Assert.Null(Wallpaper.Resolve("wallpaper-that-was-never-stored.png"));
    }

    [Fact]
    public void OffersEveryAcceptedFormatInThePicker()
    {
        string filter = Wallpaper.FileFilter;

        Assert.Contains("*.png", filter);
        Assert.Contains("*.jpg", filter);
        Assert.Contains("*.jpeg", filter);
        Assert.Contains("*.bmp", filter);
        Assert.Contains("*.webp", filter);
    }

    [Fact]
    public void RefusesToStoreAFileThatIsNotThere()
    {
        Assert.Null(Wallpaper.Store(@"C:\nowhere\missing.png"));
    }

    [Fact]
    public void RefusesToStoreSomethingItCannotShow()
    {
        Assert.Null(Wallpaper.Store(@"C:\nowhere\clip.mp4"));
    }

    /// <summary>The default sits inside the range the slider offers.</summary>
    [Fact]
    public void DefaultDimmingIsUsable()
    {
        Assert.Equal(Wallpaper.DefaultDimming, Wallpaper.ClampDimming(Wallpaper.DefaultDimming));
        Assert.Equal(Wallpaper.DefaultDimming, new AppSettings().WallpaperDimming);
    }

    /// <summary>Nothing is set on a fresh install.</summary>
    [Fact]
    public void ShipsWithNoWallpaper()
    {
        Assert.Equal("", new AppSettings().WallpaperFile);
    }
}
