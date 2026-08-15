using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MyBlinkStyleClicker;

/// <summary>
/// Screen capture, backed by ffmpeg's gdigrab.
/// </summary>
/// <remarks>
/// Kept behind a small surface — Start, StopAsync, IsRecording — so the backend
/// can be swapped for Windows.Graphics.Capture later without the page changing.
/// ffmpeg was chosen first because its behaviour can actually be verified;
/// several hundred lines of unverified D3D interop is a worse thing to ship.
///
/// Ship ffmpeg.exe in an "ffmpeg" folder beside the executable and this works on
/// any PC with nothing installed.
/// </remarks>
public sealed class ScreenRecorder : IDisposable
{
    private Process? _process;

    public bool IsRecording => _process is { HasExited: false };

    public string? OutputPath { get; private set; }

    public DateTime StartedUtc { get; private set; }

    /// <summary>
    /// gdigrab reads the whole virtual desktop and crops to these bounds. They
    /// are input options, so they have to precede -i, and the trailing space is
    /// deliberate — the caller concatenates straight onto "-i desktop".
    /// </summary>
    internal static string CropFor(DisplayInfo? display) =>
        display == null
            ? string.Empty
            : $"-offset_x {display.X} -offset_y {display.Y} " +
              $"-video_size {display.EvenWidth}x{display.EvenHeight} ";

    /// <summary>Bundled copy first, so a machine without ffmpeg still records.</summary>
    public static string? FindFfmpeg()
    {
        string baseDirectory = AppContext.BaseDirectory;

        string[] candidates =
        {
            Path.Combine(baseDirectory, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDirectory, "ffmpeg.exe")
        };

        foreach (string candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        // Falls back to whatever is on PATH, which is a convenience for
        // development rather than something to rely on for distribution.
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable == null) return null;

        foreach (string directory in pathVariable.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    /// <param name="display">
    /// The monitor to capture, or null for the whole virtual desktop.
    /// </param>
    public string Start(string outputDirectory, int framesPerSecond, DisplayInfo? display = null)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        string? ffmpeg = FindFfmpeg()
            ?? throw new FileNotFoundException(
                "ffmpeg.exe was not found. It should sit in an 'ffmpeg' folder next to the app.");

        Directory.CreateDirectory(outputDirectory);

        string path = Path.Combine(outputDirectory,
            $"clip-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");

        // yuv420p because anything else refuses to play in browsers and most
        // chat clients, which is where these clips are going.
        string arguments =
            $"-y -f gdigrab -framerate {framesPerSecond} {CropFor(display)}-i desktop " +
            $"-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p " +
            $"-movflags +faststart \"{path}\"";

        var info = new ProcessStartInfo(ffmpeg, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        _process = Process.Start(info)
            ?? throw new InvalidOperationException("ffmpeg would not start.");

        // ffmpeg writes continuously to stderr. Left undrained the pipe fills
        // and the process blocks partway through the recording.
        _process.ErrorDataReceived += (_, _) => { };
        _process.OutputDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        OutputPath = path;
        StartedUtc = DateTime.UtcNow;

        return path;
    }

    /// <summary>
    /// Asks ffmpeg to stop rather than killing it.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. An MP4's index is written when encoding
    /// finishes; a killed process leaves a file with no moov atom, which no
    /// player will open. Sending "q" lets ffmpeg finalise properly.
    /// </remarks>
    public async Task<string?> StopAsync()
    {
        Process? process = _process;
        string? path = OutputPath;

        if (process == null) return null;

        try
        {
            if (!process.HasExited)
            {
                // ConfigureAwait(false) throughout: Window_Closing has to block
                // on this, and a continuation posted back to the UI thread it is
                // blocking would deadlock the app on exit.
                await process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);

                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Last resort. The file is likely unplayable, which is why
                    // the graceful path is tried first.
                    process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
            _process = null;
        }

        return path != null && File.Exists(path) && new FileInfo(path).Length > 0 ? path : null;
    }

    /// <summary>
    /// Blocking stop for shutdown, where there is nothing to await on.
    /// </summary>
    /// <remarks>
    /// Sends the same graceful quit as StopAsync — killing the process would
    /// leave an MP4 with no index, unplayable in anything.
    /// </remarks>
    public void StopBlocking()
    {
        Process? process = _process;
        _process = null;

        if (process == null) return;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("q");
                process.StandardInput.Flush();

                if (!process.WaitForExit(10_000)) process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
            _process?.Dispose();
        }
        catch
        {
            // Nothing useful to do while tearing down.
        }

        _process = null;
    }
}
