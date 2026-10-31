using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace JinxyClicker;

/// <summary>The foreground app's name, cleaned for use as a clip subfolder.</summary>
internal static class ForegroundApp
{
    public static string Name()
    {
        try
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "Roblox";

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0) return "Roblox";

            using Process p = Process.GetProcessById((int)pid);
            string n = p.ProcessName;

            // The app itself is in front when you click Start, and this is a
            // Roblox tool, so both map to Roblox rather than a "Jinxy" folder.
            if (n.Contains("roblox", StringComparison.OrdinalIgnoreCase)) return "Roblox";
            if (n.Contains("jinxy", StringComparison.OrdinalIgnoreCase)) return "Roblox";

            return Clean(n);
        }
        catch
        {
            return "Roblox";
        }
    }

    private static string Clean(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string s = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (s.Length == 0) return "Clips";
        return char.ToUpperInvariant(s[0]) + s[1..];
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
}
