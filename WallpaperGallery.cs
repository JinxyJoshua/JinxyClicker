using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>One of the backgrounds that comes with the app.</summary>
/// <param name="Name">What it is called in the picker.</param>
/// <param name="Style">Which of the four shapes it is drawn with.</param>
public sealed record WallpaperDesign(
    string Name, WallpaperStyle Style, string Deep, string Mid, string Glow);

/// <summary>The shapes a built-in background can take.</summary>
public enum WallpaperStyle
{
    /// <summary>Two soft lights on a dark ground.</summary>
    Aurora,

    /// <summary>A diagonal wash, darkest at one corner.</summary>
    Wash,

    /// <summary>A faint grid over a wash.</summary>
    Grid,

    /// <summary>Scattered points of light.</summary>
    Stars
}

/// <summary>
/// Backgrounds that ship with the app, drawn rather than stored.
/// </summary>
/// <remarks>
/// <b>Why these are generated and not image files.</b> The installer is around
/// eighty megabytes and a good deal of work went into getting it down from a
/// hundred and thirteen. A dozen backgrounds at 1080p would put several of
/// those megabytes straight back for pictures most people will look at once.
/// Drawn at the size the screen actually is, they cost nothing to ship, stay
/// sharp on any monitor, and raise no question about whose artwork they are.
///
/// Every one is dark on purpose. This sits behind a dark interface and is
/// dimmed by default on top of that, so a bright background does not read as
/// cheerful — it reads as text that cannot be seen.
///
/// The picture is written into the settings folder through the same path a
/// chosen file takes, so everything downstream — the preview, the dimming
/// slider, restoring it at launch — works without knowing where it came from.
/// </remarks>
public static class WallpaperGallery
{
    /// <summary>
    /// Rendered at 1080p regardless of the monitor.
    /// </summary>
    /// <remarks>
    /// Stretched to fill, so this only decides how much detail there is to
    /// stretch. Larger costs seconds of drawing and megabytes on disk for a
    /// picture that is dimmed by half and sits behind opaque panels.
    /// </remarks>
    public const int Width = 1920;
    public const int Height = 1080;

    public static IReadOnlyList<WallpaperDesign> All { get; } = new[]
    {
        new WallpaperDesign("Aurora",     WallpaperStyle.Aurora, "#0B0714", "#1A1033", "#7C5CFF"),
        new WallpaperDesign("Deep Sea",   WallpaperStyle.Aurora, "#03080F", "#0A2036", "#2E9CCF"),
        new WallpaperDesign("Ember",      WallpaperStyle.Aurora, "#0F0603", "#2A0F08", "#FF6B3D"),
        new WallpaperDesign("Moss",       WallpaperStyle.Aurora, "#050C08", "#0D2418", "#3FBF7F"),
        new WallpaperDesign("Dusk",       WallpaperStyle.Wash,   "#0A0713", "#241436", "#B15CFF"),
        new WallpaperDesign("Slate",      WallpaperStyle.Wash,   "#0B0D10", "#1B2129", "#6E7C8C"),
        new WallpaperDesign("Blueprint",  WallpaperStyle.Grid,   "#04080F", "#0B1A2B", "#3E7FBF"),
        new WallpaperDesign("Carbon",     WallpaperStyle.Grid,   "#08080A", "#15161A", "#5A5D66"),
        new WallpaperDesign("Midnight",   WallpaperStyle.Stars,  "#02040A", "#080E1C", "#9FB6FF"),
        new WallpaperDesign("Nebula",     WallpaperStyle.Stars,  "#0A040F", "#1C0B2B", "#D07CFF"),
    };

    public static WallpaperDesign? Find(string? name) =>
        name == null ? null
            : System.Linq.Enumerable.FirstOrDefault(All,
                d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// <summary>Draws one of the designs.</summary>
    public static BitmapSource Render(WallpaperDesign design, int width = Width, int height = Height)
    {
        var visual = new DrawingVisual();

        using (DrawingContext dc = visual.RenderOpen())
        {
            var full = new Rect(0, 0, width, height);

            dc.DrawRectangle(Ground(design, width, height), null, full);

            switch (design.Style)
            {
                case WallpaperStyle.Aurora:
                    Glow(dc, design, width, height, 0.22, 0.18, 0.62, 0.42);
                    Glow(dc, design, width, height, 0.82, 0.78, 0.48, 0.26);
                    break;

                case WallpaperStyle.Grid:
                    DrawGrid(dc, design, width, height);
                    break;

                case WallpaperStyle.Stars:
                    Glow(dc, design, width, height, 0.70, 0.28, 0.70, 0.30);
                    DrawStars(dc, design, width, height);
                    break;
            }

            // A darker floor, so panels sitting low on the window keep their
            // contrast wherever the design happens to be brightest.
            dc.DrawRectangle(
                new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0x66, 0, 0, 0),
                    new Point(0, 0.55), new Point(0, 1)),
                null, full);
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();

        return rendered;
    }

    private static Brush Ground(WallpaperDesign design, int width, int height)
    {
        // Diagonal for the washes so the light has a direction; vertical for
        // the rest, which carry their interest in what is drawn on top.
        bool diagonal = design.Style is WallpaperStyle.Wash;

        var brush = new LinearGradientBrush
        {
            StartPoint = diagonal ? new Point(0, 0) : new Point(0.5, 0),
            EndPoint = diagonal ? new Point(1, 1) : new Point(0.5, 1)
        };

        brush.GradientStops.Add(new GradientStop(C(design.Mid), 0));
        brush.GradientStops.Add(new GradientStop(C(design.Deep), 1));

        return brush;
    }

    private static void Glow(DrawingContext dc, WallpaperDesign design,
        int width, int height, double cx, double cy, double radius, double strength)
    {
        Color glow = C(design.Glow);

        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };

        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)(255 * strength), glow.R, glow.G, glow.B), 0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0, glow.R, glow.G, glow.B), 1));

        double size = width * radius;

        dc.DrawRectangle(brush, null,
            new Rect(width * cx - size / 2, height * cy - size / 2, size, size));
    }

    private static void DrawGrid(DrawingContext dc, WallpaperDesign design, int width, int height)
    {
        Color line = C(design.Glow);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x24, line.R, line.G, line.B)), 1);
        pen.Freeze();

        const int step = 64;

        // Snapped to whole pixels: a line on a half pixel is drawn across two
        // and the grid reads as blurred rather than faint.
        for (int x = 0; x <= width; x += step)
            dc.DrawLine(pen, new Point(x + 0.5, 0), new Point(x + 0.5, height));

        for (int y = 0; y <= height; y += step)
            dc.DrawLine(pen, new Point(0, y + 0.5), new Point(width, y + 0.5));

        Glow(dc, design, width, height, 0.5, 0.35, 0.9, 0.16);
    }

    private static void DrawStars(DrawingContext dc, WallpaperDesign design, int width, int height)
    {
        // A fixed seed, so the same design is the same picture every time. A
        // background that reshuffled itself on every launch would look like a
        // bug rather than a feature.
        var random = new Random(design.Name.GetHashCode() & 0x7FFFFFF);

        Color star = C(design.Glow);

        for (int i = 0; i < 420; i++)
        {
            double x = random.NextDouble() * width;
            double y = random.NextDouble() * height;
            double r = 0.6 + random.NextDouble() * 1.6;
            byte a = (byte)(40 + random.Next(150));

            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, star.R, star.G, star.B)),
                null, new Point(x, y), r, r);
        }
    }

    /// <summary>
    /// Draws a design and installs it as the wallpaper.
    /// </summary>
    /// <returns>The stored file name to save in settings, or null if it failed.</returns>
    /// <remarks>
    /// Written to a temporary file and handed to <see cref="Wallpaper.Store"/>
    /// rather than saved into the settings folder directly. Store already
    /// clears the previous picture and settles on the stored name, and two
    /// routines writing the same file by different rules is how they come to
    /// disagree.
    /// </remarks>
    public static string? Install(WallpaperDesign design)
    {
        string temp = Path.Combine(Path.GetTempPath(),
            $"jinxy-wallpaper-{Guid.NewGuid():N}.png");

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Render(design)));

            using (FileStream file = File.Create(temp)) encoder.Save(file);

            return Wallpaper.Store(temp);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }
}
