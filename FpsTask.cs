using System;
using System.Diagnostics;

namespace JinxyClicker;

/// <summary>
/// Manages the scheduled task that runs the elevated FPS helper. Creating the task
/// needs admin (one UAC prompt, ever); after that the normal app can run and stop
/// it with no prompt, because the task runs as the current user with highest
/// privileges. This is the standard "elevate one helper without a UAC prompt each
/// time" pattern — the same shape as how Game Bar / vendor overlays avoid it.
/// </summary>
public static class FpsTask
{
    private const string TaskName = "JinxyFpsHelper";

    public static bool Exists()
    {
        try { return RunSchtasks($"/query /tn \"{TaskName}\"") == 0; }
        catch { return false; }
    }

    /// <summary>
    /// Creates the task if it isn't there yet. The create is elevated (one UAC);
    /// returns true if the task exists afterwards (already there, or just made).
    /// </summary>
    public static bool EnsureCreated()
    {
        if (Exists()) return true;
        try
        {
            string exe = Environment.ProcessPath ?? "";
            if (exe.Length == 0) return false;

            // Run the helper as the current user, with highest privileges, on demand.
            string args =
                $"/create /f /rl HIGHEST /sc ONCE /st 00:00 /tn \"{TaskName}\" " +
                $"/tr \"\\\"{exe}\\\" --fps-helper\"";

            var psi = new ProcessStartInfo("schtasks", args)
            {
                Verb = "runas",               // the one UAC prompt
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process? p = Process.Start(psi);
            p?.WaitForExit(15000);
            return Exists();
        }
        catch
        {
            return false;   // the user declined the prompt
        }
    }

    public static void Start() { try { RunSchtasks($"/run /tn \"{TaskName}\""); } catch { } }
    public static void Stop() { try { RunSchtasks($"/end /tn \"{TaskName}\""); } catch { } }

    private static int RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(5000);
        return p.ExitCode;
    }
}
