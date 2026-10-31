using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>
/// Renders a clip caption to a transparent, full-frame PNG: the text sits in the
/// lower third over a rounded dark bar for readability, everything else clear. The
/// montage builder overlays this image onto the clip at 0,0, so it is sized to the
/// output frame. Like <see cref="TitleCard"/> it is plain WPF drawing and must run
/// on the UI thread.
/// </summary>
public static class CaptionCard
{
    /// <summary>
    /// Draws the caption and writes it to <paramref name="outputPath"/>. Returns the
    /// path, or null when there is no text or drawing fails.
    /// </summary>
    public static string? Render(string text, int width, int height, string outputPath)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;   // no caption to burn in

        try
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // A fully transparent canvas so only the caption shows over the clip.
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

                double maxWidth = width * 0.84;
                var typeface = new Typeface(new FontFamily("Segoe UI"),
                    FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    typeface, height * 0.052, Brushes.White, 1.0)
                {
                    TextAlignment = TextAlignment.Center,
                    MaxTextWidth = maxWidth,
                    MaxLineCount = 2,
                    Trimming = TextTrimming.CharacterEllipsis
                };

                // Sit the text in the lower third, inside a padded dark bar.
                double pad = ft.Height * 0.35;
                double boxW = Math.Min(ft.WidthIncludingTrailingWhitespace + pad * 2, maxWidth + pad * 2);
                double boxH = ft.Height + pad * 2;
                double boxX = (width - boxW) / 2.0;
                double boxY = height - boxH - height * 0.08;

                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xB4, 0x0A, 0x0A, 0x0E)), null,
                    new Rect(boxX, boxY, boxW, boxH), 14, 14);
                dc.DrawText(ft, new Point((width - maxWidth) / 2.0, boxY + pad));
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
