using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>
/// Reads a kit's picture, trims it to the character, and puts it on the backdrop.
/// </summary>
/// <remarks>
/// The pictures are cut-outs on transparency, at anything from 240x316 to
/// 2000x2000, with surrounding empty space of wildly different margins. Drawn as
/// they come, one kit fills its tile and the next floats in the middle of it,
/// and a black silhouette disappears against a dark panel entirely.
///
/// Reading them is not the formality it looks like. See <see cref="Decode"/> —
/// WPF's ordinary way of loading a picture throws the transparency away on these
/// files, and a kit then draws as a solid rectangle of whatever colour happened
/// to be stored underneath.
///
/// Done when the picture is shown rather than when it is saved, so the file
/// stays exactly as it arrived — the backdrop can be changed later without
/// touching a hundred images, and anyone dropping in their own picture gets the
/// same treatment for free.
/// </remarks>
public static class KitArtImage
{
    public const int Canvas = 256;

    /// <summary>
    /// Reads a picture off disk with its transparency intact.
    /// </summary>
    /// <remarks>
    /// Both halves of this are load-bearing, and the app shipped without either.
    ///
    /// PreservePixelFormat, because a plain BitmapImage colour-converts what it
    /// decodes to the "closest" format and picks Bgr32 for these — a format with
    /// no alpha channel at all. Measured on the real files, the default path
    /// returns a picture that is 0% transparent where the file is 64%. Every
    /// transparent pixel becomes whatever colour sits under it, which is white
    /// for one kit, black for another and orange for a third, and the kit is
    /// drawn as a solid rectangle of that colour.
    ///
    /// The conversion to Bgra32, because PreservePixelFormat leaves the source
    /// reporting Default, and cropping and drawing from that loses the alpha
    /// again further down. Pinning the format here means everything after it
    /// works on pixels that are known to carry alpha.
    ///
    /// The stream is closed before returning. Left open, WPF holds the file for
    /// as long as the picture is shown, and replacing a kit's picture fails
    /// because the file being overwritten is in use by the list showing it.
    /// </remarks>
    public static BitmapSource? Decode(string path)
    {
        try
        {
            var decoded = new BitmapImage();

            using (var stream = System.IO.File.OpenRead(path))
            {
                decoded.BeginInit();
                decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                decoded.StreamSource = stream;
                decoded.EndInit();
            }

            var withAlpha = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            withAlpha.Freeze();

            return withAlpha;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How much of the square the character may occupy.</summary>
    /// <remarks>
    /// Short of the full square even though the surround is invisible: it is
    /// what keeps every kit the same size as every other, instead of each one
    /// scaled by however tightly its own artwork happens to be cropped.
    /// </remarks>
    private const double Fill = 0.88;

    /// <summary>
    /// The visible bounds of a picture, ignoring its transparent surround.
    /// </summary>
    /// <remarks>
    /// Framing on the file's own size instead would scale by however much empty
    /// space each one happens to carry, which is the difference between a kit
    /// filling its tile and floating in the middle of it.
    /// </remarks>
    public static Int32Rect VisibleBounds(BitmapSource source)
    {
        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = opaque.PixelWidth;
        int height = opaque.PixelHeight;
        int stride = width * 4;

        var pixels = new byte[stride * height];
        opaque.CopyPixels(pixels, stride, 0);

        int left = width, top = height, right = -1, bottom = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Above a low floor rather than any alpha at all: these carry a
                // faint halo of near-transparent pixels that would otherwise set
                // the bounds and undo the trim.
                if (pixels[y * stride + x * 4 + 3] <= 12) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return right < left || bottom < top
            ? new Int32Rect(0, 0, width, height)
            : new Int32Rect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>Lit in the middle, dark at the rim.</summary>
    /// <remarks>
    /// A flat dark backdrop swallows the dark kits — one measured 8% visible,
    /// near enough a black square. Light gathered behind the character is what
    /// lets a black silhouette and a white yeti both read, and the rim colour
    /// matches the panel so the square's edges do not show.
    /// </remarks>
    private static readonly Color GlowColour = Color.FromRgb(0x4A, 0x3A, 0x76);
    private static readonly Color EdgeColour = Color.FromRgb(0x17, 0x11, 0x29);

    /// <summary>Draws a picture trimmed and centred on the backdrop.</summary>
    public static ImageSource OnBackdrop(BitmapSource source)
    {
        Int32Rect bounds = VisibleBounds(source);

        var art = new CroppedBitmap(source, bounds);

        double scale = Math.Min(Canvas * Fill / art.PixelWidth, Canvas * Fill / art.PixelHeight);
        double drawnWidth = art.PixelWidth * scale;
        double drawnHeight = art.PixelHeight * scale;

        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            var glow = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.75,
                RadiusY = 0.75
            };

            glow.GradientStops.Add(new GradientStop(GlowColour, 0));
            glow.GradientStops.Add(new GradientStop(EdgeColour, 1));

            dc.DrawRectangle(glow, null, new Rect(0, 0, Canvas, Canvas));

            dc.DrawImage(art, new Rect(
                (Canvas - drawnWidth) / 2, (Canvas - drawnHeight) / 2,
                drawnWidth, drawnHeight));
        }

        var rendered = new RenderTargetBitmap(Canvas, Canvas, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();

        return rendered;
    }
}
