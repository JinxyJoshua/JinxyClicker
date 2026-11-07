using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace JinxyClicker;

/// <summary>
/// The Music page (Plus): YouTube Music hosted in an embedded web view. Search
/// inside YouTube Music's own page and play without leaving the app, and it
/// keeps playing while you game.
/// </summary>
/// <remarks>
/// YouTube Music, not Spotify, on purpose — it plays with no sign-in and no
/// Premium, so the music itself costs the user nothing; access to the page is
/// what Plus gates. The web view is only stood up the first time the page is
/// opened, so it costs nothing at launch, and against a per-user data folder
/// because the install folder may be read-only. If the Edge WebView2 runtime is
/// missing the init throws and the page shows a way to get it rather than a
/// blank pane.
/// </remarks>
public partial class MainWindow
{
    // Whether the web view has been (or is being) initialised. Reset on failure
    // so reopening the tab retries — e.g. after the runtime is installed.
    private bool _musicStarted;

    private async void NavMusic_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPro) { NavPro_Click(sender, e); return; }   // gated: send to the key page
        ShowPage(NavMusic, PageMusic, "Music", "Search a song and play it while you game");
        await EnsureMusicWebAsync();
    }

    // The tab stays visible so Free users see what Plus unlocks; a free install
    // (or a lapsed pass) landing on the page is sent to the key page. Called from
    // RefreshLicense.
    private void ApplyMusicPro()
    {
        if (!IsPro && PageMusic is { Visibility: Visibility.Visible })
            ShowPage(NavProCorner, PagePro, "Plus", "Unlock the paid features with a key");
    }

    private async Task EnsureMusicWebAsync()
    {
        if (_musicStarted || MusicWeb is null) return;
        _musicStarted = true;

        try
        {
            // %APPDATA%\JinxyClicker\WebView2 — writable whatever folder the app
            // was installed into.
            string dataFolder = Path.Combine(
                Path.GetDirectoryName(SettingsPath.For("app_settings.json"))!, "WebView2");
            Directory.CreateDirectory(dataFolder);

            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await MusicWeb.EnsureCoreWebView2Async(env);

            MusicWeb.CoreWebView2.Navigate(MusicSearch.Home);
        }
        catch
        {
            // Runtime missing or blocked. Show the fallback and allow a retry.
            _musicStarted = false;
            if (MusicWeb is not null) MusicWeb.Visibility = Visibility.Collapsed;
            if (MusicFallback is not null) MusicFallback.Visibility = Visibility.Visible;
        }
    }

    // Disposed on window close so the embedded browser's processes exit and its
    // user-data folder is unlocked for the next launch. Safe if it was never
    // initialised.
    private void CloseMusic()
    {
        try { MusicWeb?.Dispose(); } catch { }
    }

    // Opens the runtime download in the user's normal browser — the embedded
    // view is the very thing that isn't working, so it can't open its own link.
    private void MusicGetRuntime_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://developer.microsoft.com/microsoft-edge/webview2/",
                UseShellExecute = true
            });
        }
        catch { /* No browser to open it; the on-screen text still names the runtime. */ }
    }
}
