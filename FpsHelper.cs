using System.Diagnostics;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// The elevated FPS worker, run as "JinxyClicker.exe --fps-helper" by a scheduled
/// task with highest privileges. It measures Roblox's FPS over ETW (which needs the
/// elevation the task provides) and writes it to the shared file for the normal app
/// to read. Its lifetime follows Roblox: it keeps measuring while Roblox is open and
/// exits once Roblox has been gone for a bit, so it never lingers. The app stops it
/// outright (via the task) when the overlay's FPS readout is turned off.
/// </summary>
public static class FpsHelper
{
    public static void Run()
    {
        using var fps = new RobloxFps();
        fps.Start();
        FpsShared.WriteDebug($"available={fps.Available} err={fps.LastError}");
        if (!fps.Available) return;   // not actually elevated / session blocked

        int idle = 0;           // consecutive 500ms ticks with no Roblox
        int ticks = 0;
        const int cap = 2 * 60 * 60 * 2;   // ~2h hard safety cap (ticks at 500ms)
        const int idleExit = 120;          // Roblox gone ~60s → done

        while (ticks++ < cap)
        {
            FpsShared.WriteFps(fps.Current);
            idle = RobloxRunning() ? 0 : idle + 1;
            if (idle > idleExit) break;
            Thread.Sleep(500);
        }

        fps.Stop();
        FpsShared.ClearFps();
    }

    private static bool RobloxRunning()
    {
        try { return Process.GetProcessesByName("RobloxPlayerBeta").Length > 0; }
        catch { return false; }
    }
}
