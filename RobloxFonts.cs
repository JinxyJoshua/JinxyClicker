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
/// We do two things so the swap reaches everything. First, drop the custom font in
/// beside the stock ones and rewrite every face's assetId to it — that covers every
/// weight of every family that resolves a font by name, cloud-asset faces included.
/// Second, overwrite the physical UI font files too: the new in-experience chat
/// references its font file directly rather than through the family descriptor, so
/// repointing the JSON alone leaves it on the stock font. Emoji and non-Latin
/// fallback files are left alone (see <see cref="IsReplaceableFontFile"/>).
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

        // Also overwrite the physical font files. Repointing the family descriptors
        // covers everything that resolves a font by family name, but the new
        // in-experience chat references its font FILE directly, so only replacing the
        // file itself reaches it. Fallbacks and emoji are left as they are.
        byte[] fontBytes = File.ReadAllBytes(customFontPath);
        foreach (string file in Directory.EnumerateFiles(fontsDir))
        {
            if (!IsReplaceableFontFile(Path.GetFileName(file))) continue;

            string backup = file + BackupSuffix;
            bool madeBackup = false;
            try
            {
                if (!File.Exists(backup)) { File.Copy(file, backup); madeBackup = true; }
                File.WriteAllBytes(file, fontBytes);
            }
            catch
            {
                // Most likely a running Roblox holding the file open. Best effort:
                // drop the stray backup and carry on — the family-JSON swap still
                // applied, and the MOD page (or next launch with Roblox closed)
                // overwrites the file then. Never fail the whole apply over it.
                if (madeBackup) { try { File.Delete(backup); } catch { } }
            }
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

        // Put the physical font files back (the ones Apply overwrote).
        foreach (string backup in Directory.EnumerateFiles(fontsDir, "*" + BackupSuffix).ToList())
        {
            string target = backup[..^BackupSuffix.Length];
            try { File.Copy(backup, target, overwrite: true); File.Delete(backup); }
            catch { /* leave it for a retry */ }
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
    /// True if this file in the fonts folder is one we overwrite with the custom
    /// font. Repointing the family descriptors covers everything that resolves a
    /// font by family name, but the new in-experience chat references its font FILE
    /// directly, so only overwriting the file reaches it. Emoji and the non-Latin
    /// fallbacks (Noto*) are left alone — a Latin font in their place would render
    /// those scripts as missing-glyph boxes. Exposed for testing.
    /// </summary>
    public static bool IsReplaceableFontFile(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (!ext.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".ttf", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals(CustomFontFile, StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.StartsWith("Noto", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals("RobloxEmoji.ttf", StringComparison.OrdinalIgnoreCase)) return false;
        if (fileName.Equals("TwemojiMozilla.ttf", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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
