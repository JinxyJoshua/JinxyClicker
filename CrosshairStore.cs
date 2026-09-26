using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace JinxyClicker;

/// <summary>
/// Keeps the crosshair images people import, beside the other settings.
/// </summary>
/// <remarks>
/// An imported crosshair is a picture the user chose rather than one built from
/// a shape and a colour, so it has to live somewhere the app owns and an
/// uninstall clears. It is copied into a folder next to the settings — never
/// referenced where it was picked from — and re-encoded to PNG on the way in, so
/// a JP'g or a stray colour profile cannot trip up the cursor writing later. The
/// stored name is a GUID, so two imports of "crosshair.png" cannot collide.
/// </remarks>
public static class CrosshairStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "JinxyClicker", "crosshairs");

    /// <summary>The open-dialog filter for a crosshair image.</summary>
    public const string FileFilter =
        "Images (PNG, JPG, BMP, GIF)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";

    /// <summary>
    /// Copies a picked image into the crosshair folder as PNG.
    /// </summary>
    /// <returns>The stored bare file name, or null if the image could not be read.</returns>
    public static string? Store(string sourcePath)
    {
        try
        {
            BitmapSource? image = CrosshairImage.TryLoadImage(sourcePath);
            if (image == null) return null;

            Directory.CreateDirectory(Folder);

            string name = Guid.NewGuid().ToString("N") + ".png";

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (FileStream file = File.Create(Path.Combine(Folder, name)))
                encoder.Save(file);

            return name;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The full path of a stored crosshair image.</summary>
    public static string Resolve(string file) => Path.Combine(Folder, file);

    /// <summary>Removes a stored image, if it is still there.</summary>
    public static void Delete(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return;

        try
        {
            string path = Path.Combine(Folder, file);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A leftover image is harmless; failing to delete it is not worth a crash.
        }
    }
}
