using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Reading a kit's picture without losing its transparency.
/// </summary>
/// <remarks>
/// This covers a bug that shipped and was visible on every kit. Loading a
/// picture the ordinary WPF way — a BitmapImage with OnLoad caching — decoded
/// the real files to Bgr32, a format with no alpha channel at all. Measured on
/// the artwork, a file that is 64% transparent came back 0% transparent.
///
/// Nothing threw. The transparent pixels simply took whatever colour was stored
/// under them, so each kit drew as a solid rectangle: white for one, black for
/// another, orange for a third. The trim meant to find the character then found
/// the whole image, because every pixel now counted as opaque.
/// </remarks>
public class KitArtImageTests
{
    /// <summary>
    /// The fixture is a WebP, deliberately, and that is the whole point.
    /// </summary>
    /// <remarks>
    /// The wiki serves its artwork as WebP, and the fetcher wrote those bytes
    /// out under a hardcoded ".png" name — so every kit picture on disk was a
    /// WebP wearing a PNG extension, and it is the WebP decode path that drops
    /// the alpha.
    ///
    /// An honest PNG fixture decodes correctly with or without the fix and so
    /// tests nothing, which is exactly what a first attempt at this test did:
    /// it passed with the fix removed.
    ///
    /// 40x40 with a 10px clear margin — 400 opaque pixels, 1200 transparent —
    /// and a loud red stored underneath the transparent ones, so a decode that
    /// discards alpha fails loudly rather than producing a dark square that
    /// might pass unnoticed against a dark panel.
    /// </remarks>
    private static string Fixture() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "transparent.webp");

    private static int ClearPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        int clear = 0;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] <= 12) clear++;

        return clear;
    }

    /// <summary>Alpha has to survive the read. The bug returned none.</summary>
    [Fact]
    public void KeepsTheTransparencyWhenReadingAPicture()
    {
        BitmapSource? read = KitArtImage.Decode(Fixture());

        Assert.NotNull(read);
        Assert.Equal(1200, ClearPixels(read));
    }

    /// <summary>
    /// The consequence of the above, and the one that was visible on screen:
    /// with the alpha gone the trim keeps the whole file instead of the
    /// character, so the kit is drawn as a full coloured rectangle.
    /// </summary>
    [Fact]
    public void TrimsToTheVisiblePartRatherThanTheWholeFile()
    {
        BitmapSource? read = KitArtImage.Decode(Fixture());
        Assert.NotNull(read);

        Int32Rect bounds = KitArtImage.VisibleBounds(read);

        Assert.Equal(10, bounds.X);
        Assert.Equal(10, bounds.Y);
        Assert.Equal(20, bounds.Width);
        Assert.Equal(20, bounds.Height);
    }

    /// <summary>A picture that is not there is not a crash.</summary>
    [Fact]
    public void ReturnsNothingForAFileThatIsNotAPicture()
    {
        Assert.Null(KitArtImage.Decode(
            Path.Combine(Path.GetTempPath(), "jinxy-no-such-file-9f21.png")));
    }

    /// <summary>
    /// A fully opaque picture must still work — the trim falls back to the whole
    /// image rather than to nothing.
    /// </summary>
    [Fact]
    public void KeepsTheWholeImageWhenNothingIsTransparent()
    {
        var pixels = new byte[16 * 16 * 4];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = 255;

        var opaque = BitmapSource.Create(
            16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 16 * 4);

        Int32Rect bounds = KitArtImage.VisibleBounds(opaque);

        Assert.Equal(0, bounds.X);
        Assert.Equal(0, bounds.Y);
        Assert.Equal(16, bounds.Width);
        Assert.Equal(16, bounds.Height);
    }
}
