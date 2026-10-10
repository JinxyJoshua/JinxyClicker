using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>
/// Renders a montage's intro title card to a PNG: a dark frame with the player's
/// text centred and an accent underline. It is plain WPF drawing, so it must run
/// on the UI thread — which is where a montage render is kicked off from.
/// </summary>
public static class TitleCard
{
    /// <summary>
    /// Draws the card and writes it to <paramref name="outputPath"/>. Returns the
    /// path, or null when there is no text (no intro) or drawing fails.
    /// </summary>
    public static string? Render(string text, int width, int height, Color accent, string outputPath)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;   // no text, no intro — checked before any I/O

        try
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // A near-black frame, matching the app's dark background.
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x16)), null,
                    new Rect(0, 0, width, height));

                double maxWidth = width * 0.86;
                var typeface = new Typeface(new FontFamily("Segoe UI"),
                    FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, height * 0.14, Brushes.White, 1.0)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = maxWidth,
                    MaxLineCount = 2,
                    Trimming = TextTrimming.CharacterEllipsis
                };

                double tx = (width - maxWidth) / 2.0;
                double ty = (height - ft.Height) / 2.0;
                dc.DrawText(ft, new Point(tx, ty));

                // An accent bar under the text.
                double barWidth = Math.Min(width * 0.5, 420);
                double barHeight = Math.Max(4, height * 0.008);
                double barY = ty + ft.Height + height * 0.035;
                dc.DrawRectangle(new SolidColorBrush(accent), null,
                    new Rect((width - barWidth) / 2.0, barY, barWidth, barHeight));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream fs = File.Create(outputPath);
            encoder.Save(fs);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }
}
