using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace JinxyClicker;

/// <summary>
/// Asks GitHub whether there is a newer build, and offers to install it.
/// </summary>
/// <remarks>
/// Runs once at startup, off the UI thread, and stays silent unless there is
/// something to offer — no release, no network, a malformed response and a
/// timeout all end the same way, with nothing shown. Nobody asked for this on
/// launch, so it must never be what someone notices about opening the app.
///
/// The installer is Inno Setup, which can update in place: it closes the
/// running app, replaces it, and starts it again. So accepting the prompt is
/// the whole interaction — no download folder, no hunting for the file.
/// </remarks>
public static class UpdateCheck
{
    /// <summary>Given up on rather than left hanging behind a splash screen.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>The running build's version, as the csproj declares it.</summary>
    public static Version? CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? new Version(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build)
            : null;

    public static async Task RunAsync(Window owner)
    {
        try
        {
            ReleaseInfo? release = await FetchAsync().ConfigureAwait(true);

            if (release == null || !Updater.IsNewer(CurrentVersion, release.Version)) return;

            if (!owner.IsLoaded) return;

            string notes = Updater.Summarise(release.Notes);
            string body =
                $"JinxyClicker {release.Tag} is available. You have {CurrentVersion}."
                + Environment.NewLine + Environment.NewLine
                + (notes.Length > 0 ? notes + Environment.NewLine + Environment.NewLine : "")
                + "Install it now? The app will close, update, and reopen.";

            MessageBoxResult answer = MessageBox.Show(
                owner, body, "Update available",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);

            if (answer != MessageBoxResult.Yes) return;

            await InstallAsync(release).ConfigureAwait(true);
        }
        catch
        {
            // An update check is the least important thing happening at startup.
            // It never takes the app down with it.
        }
    }

    private static async Task<ReleaseInfo?> FetchAsync()
    {
        using var http = new HttpClient { Timeout = Timeout };

        // GitHub rejects requests without one, with a 403 that looks like rate
        // limiting rather than a missing header.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Updater.Repo}-updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        string json = await http.GetStringAsync(Updater.LatestReleaseUrl).ConfigureAwait(false);

        return Updater.ReadRelease(json);
    }

    /// <summary>
    /// Downloads the installer and hands over to it.
    /// </summary>
    /// <remarks>
    /// The URL is re-checked against the trusted hosts immediately before use,
    /// not only where the release was parsed. This method runs an executable off
    /// the internet, and that is worth confirming at the point it happens rather
    /// than trusting that it was confirmed earlier.
    /// </remarks>
    private static async Task InstallAsync(ReleaseInfo release)
    {
        if (!Updater.IsTrustedDownload(release.InstallerUrl)) return;

        string target = Path.Combine(Path.GetTempPath(),
            $"JinxyClicker-{release.Tag}-setup.exe");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Updater.Repo}-updater");

            await using Stream source =
                await http.GetStreamAsync(release.InstallerUrl).ConfigureAwait(false);
            await using var file = File.Create(target);

            await source.CopyToAsync(file).ConfigureAwait(false);
        }

        // Inno Setup's own flags. CLOSEAPPLICATION and RESTARTAPPLICATIONS are
        // what make this an update rather than a second install sitting behind
        // a locked executable.
        var start = new ProcessStartInfo
        {
            FileName = target,
            Arguments = "/SILENT /CLOSEAPPLICATION /RESTARTAPPLICATIONS",
            UseShellExecute = true
        };

        Process.Start(start);

        // Left running deliberately. The installer closes it, and quitting here
        // first would race the handover — a closed app cannot be restarted by
        // RESTARTAPPLICATIONS, which is how someone ends up updated but staring
        // at nothing.
    }
}
