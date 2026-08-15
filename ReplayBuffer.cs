using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// A rolling buffer of the last N seconds, so a hotkey can save something that
/// has already happened.
/// </summary>
/// <remarks>
/// ffmpeg writes fixed-length segments and wraps around a fixed count, so disk
/// use is bounded. Saving picks the newest segments and concatenates them.
///
/// Segments are MPEG-TS, not MP4, and that is the load-bearing decision. An MP4
/// only becomes readable once its index is written at the end, so the segment
/// currently being written would be unusable — losing exactly the last second
/// before the hotkey, which is the second that matters. TS has no such index
/// and a partially written file still decodes.
/// </remarks>
public sealed class ReplayBuffer : IDisposable
{
    /// <summary>One second per segment: the finest granularity worth the file churn.</summary>
    private const int SegmentSeconds = 1;

    private Process? _process;
    private string? _bufferDirectory;

    public bool IsRunning => _process is { HasExited: false };

    public int CapacitySeconds { get; private set; }

    /// <param name="display">
    /// The monitor to buffer, or null for the whole virtual desktop. Matches the
    /// recorder's choice — buffering a different screen than the one being
    /// recorded would be indefensible.
    /// </param>
    public void Start(int capacitySeconds, int framesPerSecond, DisplayInfo? display = null)
    {
        if (IsRunning) return;

        string? ffmpeg = ScreenRecorder.FindFfmpeg()
            ?? throw new FileNotFoundException(
                "ffmpeg.exe was not found. It should sit in an 'ffmpeg' folder next to the app.");

        CapacitySeconds = Math.Max(10, capacitySeconds);

        _bufferDirectory = Path.Combine(Path.GetTempPath(), "JinxyClicker", "replay");
        Directory.CreateDirectory(_bufferDirectory);

        foreach (string stale in Directory.GetFiles(_bufferDirectory, "buf*.ts"))
        {
            try { File.Delete(stale); } catch { /* in use by a previous run */ }
        }

        string pattern = Path.Combine(_bufferDirectory, "buf%04d.ts");

        // -g forces a keyframe every segment, so each one decodes on its own and
        // the pieces can be joined without re-encoding.
        string arguments =
            $"-y -f gdigrab -framerate {framesPerSecond} {ScreenRecorder.CropFor(display)}-i desktop " +
            $"-c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p -g {framesPerSecond} " +
            $"-f segment -segment_time {SegmentSeconds} -segment_format mpegts " +
            $"-segment_wrap {CapacitySeconds / SegmentSeconds + 1} -reset_timestamps 1 \"{pattern}\"";

        var info = new ProcessStartInfo(ffmpeg, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        _process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg would not start.");

        // Undrained pipes fill and stall the process partway through.
        _process.ErrorDataReceived += (_, _) => { };
        _process.OutputDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();
    }

    /// <summary>
    /// Writes the most recent <paramref name="seconds"/> to an MP4.
    /// </summary>
    /// <returns>The saved path, or null if the buffer held nothing usable.</returns>
    public async Task<string?> SaveLastAsync(int seconds, string outputDirectory)
    {
        if (_bufferDirectory == null) return null;

        string? ffmpeg = ScreenRecorder.FindFfmpeg();
        if (ffmpeg == null) return null;

        // Newest first by write time — with wrapping filenames the timestamp is
        // the only thing that says which segment is which.
        FileInfo[] newest = new DirectoryInfo(_bufferDirectory)
            .GetFiles("buf*.ts")
            .Where(f => f.Length > 0)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(Math.Max(1, seconds / SegmentSeconds) + 1)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToArray();

        if (newest.Length == 0) return null;

        Directory.CreateDirectory(outputDirectory);

        string output = Path.Combine(outputDirectory, $"replay-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");
        string joined = string.Join("|", newest.Select(f => f.FullName));

        // Stream copy: no re-encode, so saving is near-instant and costs nothing
        // beyond the read. -t trims the leading overshoot from the extra segment.
        string arguments =
            $"-y -i \"concat:{joined}\" -c copy -t {seconds} -movflags +faststart \"{output}\"";

        var info = new ProcessStartInfo(ffmpeg, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using Process? process = Process.Start(info);
        if (process == null) return null;

        process.ErrorDataReceived += (_, _) => { };
        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return File.Exists(output) && new FileInfo(output).Length > 0 ? output : null;
    }

    public void Stop()
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

                if (!process.WaitForExit(5000)) process.Kill(entireProcessTree: true);
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

    public void Dispose() => Stop();
}
