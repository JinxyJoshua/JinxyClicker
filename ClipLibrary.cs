using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JinxyClicker;

/// <summary>One saved clip on disk.</summary>
public readonly record struct ClipInfo(string Path, string Name, DateTime SavedUtc, long Bytes);

/// <summary>Lists and deletes the clip MP4s in the clip folder.</summary>
public static class ClipLibrary
{
    public static IReadOnlyList<ClipInfo> List(string folder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Array.Empty<ClipInfo>();

            return new DirectoryInfo(folder)
                .GetFiles("*.mp4")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new ClipInfo(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
                .ToList();
        }
        catch
        {
            return Array.Empty<ClipInfo>();
        }
    }

    public static bool Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); return !File.Exists(path); }
        catch { return false; }
    }
}
