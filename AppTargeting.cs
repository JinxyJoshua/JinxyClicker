using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace JinxyClicker;

/// <summary>
/// Which apps the clicker is allowed to click over.
/// </summary>
public enum ClickTargetMode
{
    /// <summary>Clicks everywhere (the default).</summary>
    Off,
    /// <summary>Clicks only when the app under the pointer is on the list.</summary>
    Whitelist,
    /// <summary>Clicks everywhere except when the app under the pointer is on the list.</summary>
    Blacklist
}

/// <summary>
/// The per-click app gate. Before each click the engine resolves the executable
/// of the window under the pointer and asks <see cref="Allows"/> whether to go
/// ahead. The decision is pure and lives apart from the Win32 lookup so it can
/// be tested without a desktop; <see cref="PointerApp"/> does the lookup.
/// </summary>
public static class AppTargeting
{
    /// <summary>
    /// Whether a click is allowed over the app whose executable file name is
    /// <paramref name="exe"/> (null when the app under the pointer can't be
    /// determined). A Whitelist denies the unknown app; a Blacklist allows it.
    /// </summary>
    public static bool Allows(ClickTargetMode mode, IReadOnlySet<string> apps, string? exe)
    {
        if (mode == ClickTargetMode.Off) return true;

        string? key = Normalize(exe);
        bool listed = key != null && apps.Contains(key);
        return mode == ClickTargetMode.Whitelist ? listed : !listed;
    }

    /// <summary>
    /// An executable reduced to the comparable key: file name only, trimmed and
    /// lower-cased. Returns null for a null or blank value so an unknown app is
    /// never mistaken for a listed one.
    /// </summary>
    public static string? Normalize(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;

        string name = exe.Trim();
        int slash = name.LastIndexOfAny(Separators);
        if (slash >= 0) name = name[(slash + 1)..];

        return name.Length == 0 ? null : name.ToLowerInvariant();
    }

    /// <summary>Builds the comparable set from raw user entries, dropping blanks.</summary>
    public static HashSet<string> Set(IEnumerable<string> entries)
    {
        var set = new HashSet<string>();
        foreach (string e in entries)
        {
            string? n = Normalize(e);
            if (n != null) set.Add(n);
        }
        return set;
    }

    private static readonly char[] Separators = { '\\', '/' };
}

/// <summary>
/// Resolves the executable file name of the window under the mouse pointer. Kept
/// out of <see cref="AppTargeting"/> so the decision stays testable; this half
/// is all Win32 and runs on the click thread, so the PID→name lookup is cached.
/// </summary>
public static class PointerApp
{
    // Process ids are reused slowly; caching name by id keeps the per-click
    // cost to two cheap window calls once an app has been seen.
    private static readonly Dictionary<uint, string?> NameByPid = new();
    private static readonly object Gate = new();

    /// <summary>
    /// The executable file name (e.g. "RobloxPlayerBeta.exe") of the window
    /// currently under the pointer, or null if it can't be determined.
    /// </summary>
    public static string? ExeUnderCursor()
    {
        if (!GetCursorPos(out POINT p)) return null;

        IntPtr hwnd = WindowFromPoint(p);
        if (hwnd == IntPtr.Zero) return null;

        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;

        lock (Gate)
        {
            if (NameByPid.TryGetValue(pid, out string? cached)) return cached;
            string? name = AppTargeting.Normalize(ImageName(pid));
            NameByPid[pid] = name;
            return name;
        }
    }

    private static string? ImageName(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
}
