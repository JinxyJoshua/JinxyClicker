using System.Threading;

namespace JinxyClicker;

/// <summary>
/// The elevated FPS worker, run as "JinxyClicker.exe --fps-helper" by a scheduled
/// task with highest privileges. It measures Roblox's FPS over ETW (which needs the
/// elevation the task provides) and writes it to the shared file for the normal app
/// to read. It stops on its own once the app stops asking, so it never lingers.
/// </summary>
public static class FpsHelper
{
    public static void Run()
    {
        using var fps = new RobloxFps();
        fps.Start();
        if (!fps.Available) return;   // not actually elevated — nothing we can do

        while (FpsShared.RecentlyRequested())
        {
            FpsShared.WriteFps(fps.Current);
            Thread.Sleep(500);
        }

        fps.Stop();
        FpsShared.ClearFps();
    }
}
