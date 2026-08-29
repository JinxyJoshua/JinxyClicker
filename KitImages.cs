using System;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// Pictures for kits, kept in a folder beside the settings.
/// </summary>
/// <remarks>
/// The app ships none. The game has well over a hundred kits and their art
/// belongs to the game, so the picture for a kit is one somebody supplies —
/// screenshotted once and kept, the same way the wallpaper is.
///
/// Named after the kit rather than listed in a file, so dropping images into
/// the folder by hand works exactly as well as choosing them in the app. A kit
/// with no picture shows its name, which is what the list did before.
/// </remarks>
public static class KitImages
{
    /// <summary>Formats WPF decodes without an extra codec.</summary>
    private static readonly string[] Allowed = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    public static string FileFilter =>
        "Images|" + string.Join(";", Allowed.Select(e => "*" + e)) + "|All files|*.*";

    /// <summary>
    /// The file name a kit's picture is stored under.
    /// </summary>
    /// <remarks>
    /// Kit names are typed, so they can carry anything — slashes and colons
    /// included, which would send a write somewhere else entirely. Everything
    /// that is not a letter, digit, space or dash becomes an underscore.
    /// </remarks>
    public static string FileNameFor(string kit, string extension)
    {
        string safe = new string(kit
            .Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_')
            .ToArray())
            .Trim();

        return safe.Length == 0 ? "kit" + extension : safe + extension;
    }

    public static bool IsSupported(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && Allowed.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Where kit pictures live.</summary>
    private static string Folder()
    {
        // Resolved through SettingsPath so the pictures sit beside everything
        // else the app keeps, and survive reinstalling.
        string marker = SettingsPath.For("kits.marker");
        string folder = Path.Combine(Path.GetDirectoryName(marker)!, "kits");

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>The picture for a kit, or null if it has none.</summary>
    public static string? Find(string kit)
    {
        try
        {
            string folder = Folder();

            return Allowed
                .Select(ext => Path.Combine(folder, FileNameFor(kit, ext)))
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Copies a chosen picture in as this kit's.
    /// </summary>
    /// <returns>The stored path, or null if it could not be used.</returns>
    public static string? Set(string kit, string sourcePath)
    {
        if (!IsSupported(sourcePath) || !File.Exists(sourcePath)) return null;

        try
        {
            // Any earlier picture goes first, whatever format it was in, or a
            // kit ends up with two and the one found depends on list order.
            Remove(kit);

            string target = Path.Combine(Folder(),
                FileNameFor(kit, Path.GetExtension(sourcePath).ToLowerInvariant()));

            File.Copy(sourcePath, target, overwrite: true);

            return target;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Deletes a kit's picture, whatever format it was stored in.</summary>
    public static void Remove(string kit)
    {
        try
        {
            string folder = Folder();

            foreach (string ext in Allowed)
            {
                string path = Path.Combine(folder, FileNameFor(kit, ext));

                if (File.Exists(path)) File.Delete(path);
            }
        }
        catch
        {
            // A locked picture is not worth failing over — it is overwritten
            // next time one is chosen.
        }
    }
}
