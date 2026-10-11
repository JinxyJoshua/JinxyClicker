using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace JinxyClicker;

/// <summary>
/// Swaps Roblox's fonts for a custom one by repointing every font family to it.
/// </summary>
/// <remarks>
/// Roblox resolves a Font to a file through the JSON descriptors in
/// <c>content\fonts\families</c>: each face has an <c>assetId</c> that is either a
/// local file (<c>rbxasset://fonts/...</c>) or a cloud asset (<c>rbxassetid://...</c>).
/// Replacing the local .otf files only changes the faces that use them — chat and
/// some weights point at cloud assets and stay unchanged. So instead we drop the
/// custom font in beside the stock ones and rewrite every face's assetId to it,
/// which covers every weight of every family, chat included.
///
/// Each rewritten JSON keeps a one-time <c>.jinxybak</c> copy of the original so it
/// can be put back. A Roblox update lays down a fresh folder, so the swap is
/// re-applied on launch, exactly like the crosshair. Only cosmetic content files
/// are touched, never the executable.
/// </remarks>
public static class RobloxFonts
{
    private const string BackupSuffix = ".jinxybak";
    private const string CustomFontFile = "JinxyCustom.ttf";
    private const string CustomAssetUri = "rbxasset://fonts/" + CustomFontFile;

    /// <summary>
    /// Drops the custom font into the folder and repoints every family at it.
    /// Returns how many family descriptors were rewritten.
    /// </summary>
    public static int Apply(string fontsDir, string customFontPath)
    {
        if (!Directory.Exists(fontsDir)) throw new DirectoryNotFoundException(fontsDir);
        if (!File.Exists(customFontPath)) throw new FileNotFoundException("Custom font not found", customFontPath);

        string familiesDir = Path.Combine(fontsDir, "families");
        if (!Directory.Exists(familiesDir)) throw new DirectoryNotFoundException(familiesDir);

        File.Copy(customFontPath, Path.Combine(fontsDir, CustomFontFile), overwrite: true);

        int rewritten = 0;
        foreach (string json in Directory.EnumerateFiles(familiesDir, "*.json"))
        {
            string backup = json + BackupSuffix;
            bool madeBackup = false;
            if (!File.Exists(backup)) { File.Copy(json, backup); madeBackup = true; }

            try { File.WriteAllText(json, PointFamilyAtFont(File.ReadAllText(json), CustomAssetUri)); }
            catch
            {
                // Undo the backup we just made so a half-apply doesn't leave a stray
                // one that would make IsApplied lie.
                if (madeBackup) { try { File.Delete(backup); } catch { } }
                throw;
            }
            rewritten++;
        }
        return rewritten;
    }

    /// <summary>Restores every rewritten family and removes the custom font.</summary>
    public static int Reset(string fontsDir)
    {
        if (!Directory.Exists(fontsDir)) return 0;

        int restored = 0;
        string familiesDir = Path.Combine(fontsDir, "families");
        if (Directory.Exists(familiesDir))
        {
            foreach (string backup in Directory.EnumerateFiles(familiesDir, "*" + BackupSuffix).ToList())
            {
                string target = backup[..^BackupSuffix.Length];
                try { File.Copy(backup, target, overwrite: true); File.Delete(backup); restored++; }
                catch { /* leave it for a retry */ }
            }
        }
        try { File.Delete(Path.Combine(fontsDir, CustomFontFile)); } catch { }
        return restored;
    }

    /// <summary>True if a custom font is currently applied in this fonts folder.</summary>
    public static bool IsApplied(string fontsDir)
    {
        string familiesDir = Path.Combine(fontsDir, "families");
        return Directory.Exists(familiesDir)
               && Directory.EnumerateFiles(familiesDir, "*" + BackupSuffix).Any();
    }

    /// <summary>
    /// Pure JSON edit: points every face's assetId at <paramref name="assetUri"/>,
    /// leaving names, weights and styles alone. Exposed for testing.
    /// </summary>
    public static string PointFamilyAtFont(string json, string assetUri) =>
        Regex.Replace(json, "(\"assetId\"\\s*:\\s*\")[^\"]*(\")", "${1}" + assetUri + "${2}");

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
