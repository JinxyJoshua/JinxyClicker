using System;
using System.IO;

namespace JinxyClicker;

/// <summary>
/// The one-way channel from the elevated FPS helper to the normal app: the helper
/// writes the current FPS to a file in the user's local app-data (readable by both,
/// same user), and the app reads it. Only this helper-to-app direction is used — the
/// reverse proved unreliable across the elevation boundary — so the helper's lifetime
/// is driven by Roblox being open, not by the app.
/// </summary>
public static class FpsShared
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JinxyClicker");

    private static string FpsFile => Path.Combine(Dir, "fps.txt");

    private static void EnsureDir() { try { Directory.CreateDirectory(Dir); } catch { } }

    /// <summary>Helper → disk: the latest FPS.</summary>
    public static void WriteFps(int fps)
    {
        try { EnsureDir(); File.WriteAllText(FpsFile, fps.ToString()); } catch { }
    }

    /// <summary>App: the helper's latest FPS, or null if stale/missing.</summary>
    public static int? ReadFps()
    {
        try
        {
            if (!File.Exists(FpsFile)) return null;
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(FpsFile)).TotalSeconds > 4) return null;
            return int.TryParse(File.ReadAllText(FpsFile).Trim(), out int f) ? f : null;
        }
        catch { return null; }
    }

    public static void ClearFps() { try { File.Delete(FpsFile); } catch { } }

    public static void WriteDebug(string line)
    {
        try { EnsureDir(); File.WriteAllText(Path.Combine(Dir, "fps-debug.txt"), DateTime.Now + " " + line); } catch { }
    }
}
