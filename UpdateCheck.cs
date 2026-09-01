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
        // A build with no update source never asks. That is how a dev build
        // avoids downloading the public installer and replacing itself with the
        // public app, dev tab and all.
        if (!UpdateSource.Current.CanUpdate) return;

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

            await InstallAsync(release, owner).ConfigureAwait(true);
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

        Authorise(http);

        string json = await http.GetStringAsync(Updater.LatestReleaseUrl).ConfigureAwait(false);

        return Updater.ReadRelease(json);
    }

    /// <summary>
    /// Adds the token when the source needs one, and nothing when it does not.
    /// </summary>
    /// <remarks>
    /// Public releases are deliberately fetched without credentials. Sending a
    /// token where none is needed would put it on the wire for no reason, and
    /// the public path is the one that runs on thousands of machines.
    /// </remarks>
    private static void Authorise(HttpClient http)
    {
        if (!UpdateSource.Current.IsPrivate) return;

        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", UpdateSource.Current.Token);
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
    private static async Task InstallAsync(ReleaseInfo release, Window owner)
    {
        if (!Updater.IsTrustedDownload(release.InstallerUrl)) return;

        string target = Path.Combine(Path.GetTempPath(),
            $"JinxyClicker-{release.Tag}-setup.exe");

        // The installer is around a hundred megabytes, so this is tens of
        // seconds on an ordinary connection. Without something on screen the
        // app simply sits there after the prompt is accepted, looking like the
        // click did nothing — which is exactly how it read the first time.
        var progress = new UpdateProgressWindow(release.Tag) { Owner = owner };
        progress.Show();

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"{Updater.Repo}-updater");

            Authorise(http);

            // A private asset is served from the API, which returns the release
            // metadata as JSON unless this says otherwise. Without it the
            // "installer" downloaded is a few hundred bytes of JSON.
            if (UpdateSource.Current.IsPrivate)
                http.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");

            using HttpResponseMessage response = await http
                .GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(true);

            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;

            await using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(true))
            await using (var file = File.Create(target))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;

                while ((read = await source.ReadAsync(buffer).ConfigureAwait(true)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(true);
                    written += read;

                    progress.Report(written, total);
                }
            }

            progress.HandingOver();
        }
        catch
        {
            progress.Close();
            throw;
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
