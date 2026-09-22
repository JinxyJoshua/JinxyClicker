using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>
/// Turns a <see cref="CrosshairStyle"/> into a square PNG, centred, on a
/// transparent background — the picture that becomes a Roblox cursor, a gallery
/// tile and the preview, all from one place.
/// </summary>
/// <remarks>
/// The gallery styles are drawn to look right in a 64-pixel image; every other
/// size scales from that, so a crosshair keeps its proportions whether it is a
/// tiny tile, a big preview or the 32-pixel first-person cursor. Must be called
/// on the UI thread — <see cref="RenderTargetBitmap"/> needs one.
/// </remarks>
public static class CrosshairImage
{
    /// <summary>The size the gallery styles are authored against.</summary>
    public const double BaseImage = 64.0;

    /// <summary>A frozen bitmap of the crosshair, for tiles and the preview.</summary>
    public static BitmapSource RenderBitmap(CrosshairStyle style, int pixels, double sizeFactor)
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            Draw(dc, style, pixels, sizeFactor);

        var bmp = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The crosshair as PNG bytes, ready to write over a cursor file.</summary>
    public static byte[] RenderPng(CrosshairStyle style, int pixels, double sizeFactor)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderBitmap(style, pixels, sizeFactor)));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void Draw(DrawingContext dc, CrosshairStyle style, int pixels, double sizeFactor)
    {
        double centre = pixels / 2.0;
        double s = pixels / BaseImage * sizeFactor;

        Brush colour = Frozen(Parse(style.ColorHex), style.Opacity);
        Brush dotColour = Frozen(Parse(style.EffectiveDotColor), style.Opacity);

        // A contrasting edge keeps the crosshair readable on any background —
        // dark behind a light crosshair, light behind a dark one.
        Brush armEdge = Frozen(Contrast(Parse(style.ColorHex)), style.Opacity);
        Brush dotEdge = Frozen(Contrast(Parse(style.EffectiveDotColor)), style.Opacity);

        double stroke = Math.Max(1.0, style.StrokeWidth * s);
        double edgeExtra = Math.Max(2.0, 2.0 * s);

        foreach (CrosshairArm a in style.Arms())
        {
            var p1 = new Point(centre + a.X1 * s, centre + a.Y1 * s);
            var p2 = new Point(centre + a.X2 * s, centre + a.Y2 * s);

            if (style.Outline) dc.DrawLine(RoundPen(armEdge, stroke + edgeExtra), p1, p2);
            dc.DrawLine(RoundPen(colour, stroke), p1, p2);
        }

        if (style.HasRing)
        {
            var c = new Point(centre, centre);
            double r = style.RingRadius * s;

            if (style.Outline) dc.DrawEllipse(null, RoundPen(armEdge, stroke + edgeExtra), c, r, r);
            dc.DrawEllipse(null, RoundPen(colour, stroke), c, r, r);
        }

        if (style.HasDot)
        {
            var c = new Point(centre, centre);
            double r = Math.Max(1.0, style.DotRadius * s);

            if (style.Outline) dc.DrawEllipse(dotEdge, null, c, r + Math.Max(1.0, s), r + Math.Max(1.0, s));
            dc.DrawEllipse(dotColour, null, c, r, r);
        }
    }

    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        return pen;
    }

    private static Brush Frozen(Color colour, double opacity)
    {
        var brush = new SolidColorBrush(colour) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Color.FromRgb(0x33, 0xFF, 0x66); }
    }

    /// <summary>Black behind a light colour, white behind a dark one.</summary>
    private static Color Contrast(Color c)
    {
        // Rec. 601 luma, enough to tell a light crosshair from a dark one.
        double luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luma < 0.4 ? Color.FromRgb(255, 255, 255) : Color.FromRgb(0, 0, 0);
    }
}
