using System;
using System.IO;

namespace JinxyClicker;

/// <summary>
/// The tiny file-based channel between the normal (non-admin) app and the elevated
/// FPS helper. Both run as the same user, so the user's local app-data folder is
/// readable and writable by both. The helper writes the current FPS; the app writes
/// a heartbeat so the helper knows it's still wanted and exits on its own when it
/// isn't (e.g. the app closed).
/// </summary>
public static class FpsShared
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JinxyClicker");

    private static string FpsFile => Path.Combine(Dir, "fps.txt");
    private static string RequestFile => Path.Combine(Dir, "fps-request.txt");

    private static void EnsureDir() { try { Directory.CreateDirectory(Dir); } catch { } }

    /// <summary>App → helper: "still want FPS". Call every few seconds while on.</summary>
    public static void Ping()
    {
        try { EnsureDir(); File.WriteAllText(RequestFile, "1"); } catch { }
    }

    /// <summary>Helper: whether the app asked for FPS recently (else exit).</summary>
    public static bool RecentlyRequested() => FreshWithin(RequestFile, 10);

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
            if (!FreshWithin(FpsFile, 4)) return null;
            return int.TryParse(File.ReadAllText(FpsFile).Trim(), out int f) ? f : null;
        }
        catch { return null; }
    }

    public static void ClearFps() { try { File.Delete(FpsFile); } catch { } }

    private static bool FreshWithin(string path, int seconds)
    {
        try { return File.Exists(path) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < seconds; }
        catch { return false; }
    }
}
