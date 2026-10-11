using System;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// Swaps Roblox's default UI font for a custom one by overwriting the default
/// font faces in a client's <c>content\fonts</c> folder, keeping a one-time
/// backup of each original so it can be put back.
/// </summary>
/// <remarks>
/// Roblox renders chat, menus and nametags in BuilderSans (the current default)
/// with SourceSansPro behind some older UI. Overwriting those faces with one
/// custom font changes the look almost everywhere text appears. A Roblox update
/// lays down a fresh version folder with the original fonts, so the swap is
/// re-applied on launch (see the caller) exactly like the crosshair.
///
/// Anything this touches is a cosmetic content file, never the executable.
/// </remarks>
public static class RobloxFonts
{
    // The default faces we overwrite. Any not present in a given client are
    // simply skipped, so this stays correct as Roblox adds or renames faces.
    public static readonly string[] DefaultFaces =
    {
        "BuilderSans-Regular.otf", "BuilderSans-Medium.otf",
        "BuilderSans-Bold.otf", "BuilderSans-ExtraBold.otf",
        "SourceSansPro-Regular.ttf", "SourceSansPro-Bold.ttf",
        "SourceSansPro-Semibold.ttf", "SourceSansPro-Light.ttf", "SourceSansPro-It.ttf",
    };

    // Marks a saved original. "BuilderSans-Regular.otf.jinxybak" sits beside the
    // face it backs up; its presence is also how IsApplied knows a swap is live.
    private const string BackupSuffix = ".jinxybak";

    /// <summary>
    /// Overwrites each present default face with <paramref name="customFontPath"/>,
    /// backing up the untouched original once. Returns how many faces were replaced.
    /// </summary>
    public static int Apply(string fontsDir, string customFontPath)
    {
        if (!Directory.Exists(fontsDir)) throw new DirectoryNotFoundException(fontsDir);
        if (!File.Exists(customFontPath)) throw new FileNotFoundException("Custom font not found", customFontPath);

        int replaced = 0;
        foreach (string face in DefaultFaces)
        {
            string target = Path.Combine(fontsDir, face);
            if (!File.Exists(target)) continue;

            string backup = target + BackupSuffix;
            // Back up the TRUE original only the first time, so re-applying a
            // second font never overwrites the real one with a custom one.
            bool madeBackup = false;
            if (!File.Exists(backup)) { File.Copy(target, backup); madeBackup = true; }

            try
            {
                File.Copy(customFontPath, target, overwrite: true);
            }
            catch
            {
                // The overwrite failed (a running Roblox is holding this face). Undo
                // the backup we just made, so a half-apply never leaves a stray
                // .jinxybak — that would make IsApplied lie and block every future
                // re-apply while the face is actually still stock.
                if (madeBackup) { try { File.Delete(backup); } catch { } }
                throw;
            }
            replaced++;
        }
        return replaced;
    }

    /// <summary>
    /// Restores every backed-up original and removes the backups. Returns how many
    /// faces were restored.
    /// </summary>
    public static int Reset(string fontsDir)
    {
        if (!Directory.Exists(fontsDir)) return 0;

        int restored = 0;
        foreach (string face in DefaultFaces)
        {
            string target = Path.Combine(fontsDir, face);
            string backup = target + BackupSuffix;
            if (!File.Exists(backup)) continue;

            File.Copy(backup, target, overwrite: true);
            File.Delete(backup);
            restored++;
        }
        return restored;
    }

    /// <summary>True if a custom font is currently applied in this fonts folder.</summary>
    public static bool IsApplied(string fontsDir)
    {
        if (!Directory.Exists(fontsDir)) return false;
        return DefaultFaces.Any(face => File.Exists(Path.Combine(fontsDir, face) + BackupSuffix));
    }

    /// <summary>
    /// The live Roblox client's <c>content\fonts</c> folder, or null if Roblox
    /// isn't installed. Mirrors how the FastFlags code finds the live version.
    /// </summary>
    public static string? FindFontsFolder()
    {
        try
        {
            string versions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox", "Versions");
            if (!Directory.Exists(versions)) return null;

            DirectoryInfo? newest = new DirectoryInfo(versions)
                .GetDirectories()
                .Where(d => File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
                .OrderByDescending(d => d.LastWriteTimeUtc)
                .FirstOrDefault();

            if (newest == null) return null;
            string fonts = Path.Combine(newest.FullName, "content", "fonts");
            return Directory.Exists(fonts) ? fonts : null;
        }
        catch
        {
            return null;
        }
    }
}
