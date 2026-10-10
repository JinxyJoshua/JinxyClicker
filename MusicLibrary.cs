using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// The montage music: the tracks shipped in &lt;app&gt;/music, plus the user's own
/// file when they pick one.
/// </summary>
public static class MusicLibrary
{
    public static IReadOnlyList<string> Bundled(string appFolder)
    {
        try
        {
            string dir = Path.Combine(appFolder, "music");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.mp3").OrderBy(f => f).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Resolves the chosen music to a full path: an existing full path wins; then a
    /// bundled file name; then the first bundled track; else null.
    /// </summary>
    public static string? Resolve(string? chosen, string appFolder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                if (File.Exists(chosen)) return chosen;
                string inMusic = Path.Combine(appFolder, "music", chosen);
                if (File.Exists(inMusic)) return inMusic;
            }
            return Bundled(appFolder).FirstOrDefault();
        }
        catch { return null; }
    }
}
