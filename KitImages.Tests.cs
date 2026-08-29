using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Turning a typed kit name into a file name.
/// </summary>
/// <remarks>
/// Kit names are typed, so they can contain anything. This is what stands
/// between a name and a file write, and a name carrying a path separator would
/// otherwise send that write somewhere nobody chose.
/// </remarks>
public class KitImagesTests
{
    [Theory]
    [InlineData("Melody", "Melody.png")]
    [InlineData("Axolotl Amy", "Axolotl Amy.png")]
    [InlineData("Dino-Tamer", "Dino-Tamer.png")]
    public void KeepsAnOrdinaryNameAsItIs(string kit, string expected)
    {
        Assert.Equal(expected, KitImages.FileNameFor(kit, ".png"));
    }

    /// <summary>
    /// The one that matters. A separator or a drive letter must not survive
    /// into a path the app then writes to.
    /// </summary>
    [Theory]
    [InlineData(@"..\..\Windows\System32\evil")]
    [InlineData("kits/../../etc/passwd")]
    [InlineData(@"C:\Windows\notepad")]
    [InlineData("a:b*c?d")]
    public void StripsAnythingThatCouldPointElsewhere(string kit)
    {
        string name = KitImages.FileNameFor(kit, ".png");

        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain("..", name);
    }

    /// <summary>
    /// A name made entirely of stripped characters must still produce a usable
    /// file name rather than a bare extension.
    /// </summary>
    [Theory]
    [InlineData("///")]
    [InlineData("...")]
    [InlineData("   ")]
    public void StillProducesAFileNameWhenNothingSurvives(string kit)
    {
        string name = KitImages.FileNameFor(kit, ".png");

        Assert.NotEqual(".png", name);
        Assert.EndsWith(".png", name);
    }

    [Theory]
    [InlineData("art.png")]
    [InlineData("art.JPG")]
    [InlineData("art.webp")]
    public void AcceptsThePicturesWpfCanDecode(string path)
    {
        Assert.True(KitImages.IsSupported(path));
    }

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("notes.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesWhatItCannotShow(string? path)
    {
        Assert.False(KitImages.IsSupported(path));
    }

    [Fact]
    public void HasNoPictureForAKitNobodyGaveOne()
    {
        Assert.Null(KitImages.Find("A Kit That Was Never Given Art 12345"));
    }

    [Fact]
    public void RefusesToStoreAFileThatIsNotThere()
    {
        Assert.Null(KitImages.Set("Melody", @"C:\nowhere\missing.png"));
    }

    [Fact]
    public void RefusesToStoreSomethingItCannotShow()
    {
        Assert.Null(KitImages.Set("Melody", @"C:\nowhere\clip.mp4"));
    }

    [Fact]
    public void OffersEveryAcceptedFormatInThePicker()
    {
        string filter = KitImages.FileFilter;

        Assert.Contains("*.png", filter);
        Assert.Contains("*.jpg", filter);
        Assert.Contains("*.webp", filter);
    }
}
