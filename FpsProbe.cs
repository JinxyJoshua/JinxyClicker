using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JinxyClicker;

/// <summary>
/// Measures what frame rates this machine can actually capture at.
/// </summary>
/// <remarks>
/// Deliberately does not pace the output. <see cref="CaptureBackend.PacingArgs"/>
/// makes ffmpeg hold the requested rate by duplicating frames it did not get,
/// which is right for a recording and fatal for a measurement — every candidate
/// would report exactly the rate it was asked for and the probe would always
/// choose the fastest. What is wanted here is the raw rate the capture managed.
///
/// Nothing is written to disk. The frames go to the null muxer, so the encoder
/// does its real work and the measurement costs a few seconds and no space.
/// </remarks>
public sealed class FpsProbe
{
    /// <summary>Discarded before measuring: encoder start-up is not the steady rate.</summary>
    public const double WarmUpSeconds = 1.2;

    /// <summary>Measured window, after the warm-up.</summary>
    public const double MeasureSeconds = 2.5;

    private static readonly Regex FrameCount = new(@"frame=\s*(\d+)", RegexOptions.Compiled);

    /// <summary>
    /// Tries each candidate from fastest down, stopping at the first that is
    /// good enough.
    /// </summary>
    /// <remarks>
    /// Descending and short-circuiting because the answer is "the fastest that
    /// works": once one passes, every slower one would also pass and measuring
    /// them would only make the user wait. A machine that manages the top rate
    /// is done in one pass.
    /// </remarks>
    public async Task<FpsChoice> RunAsync(
        string ffmpeg, DisplayInfo? display, int refreshHz, IProgress<int>? progress, CancellationToken token)
    {
        var readings = new List<FpsReading>();
        IReadOnlyList<int> candidates = AutoFps.CandidatesFor(refreshHz);

        foreach (int fps in candidates)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(fps);

            FpsReading? reading = await MeasureAsync(ffmpeg, display, fps, token).ConfigureAwait(false);
            if (reading == null) continue;

            readings.Add(reading);

            if (AutoFps.IsUsable(reading, Environment.ProcessorCount)) break;
        }

        return AutoFps.Choose(readings, Environment.ProcessorCount, candidates);
    }

    /// <summary>One candidate, or null when the capture would not start at all.</summary>
    private static async Task<FpsReading?> MeasureAsync(
        string ffmpeg, DisplayInfo? display, int fps, CancellationToken token)
    {
        string arguments =
            $"-y {CaptureBackend.InputArgs(display, fps)} " +
            $"{CaptureBackend.EncoderArgs(ffmpeg, display, fps)} -f null -";

        var info = new ProcessStartInfo(ffmpeg, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using Process? process = Process.Start(info);
        if (process == null) return null;

        // Written to from the reader thread, read from this one.
        long frames = 0;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;

            Match match = FrameCount.Match(e.Data);
            if (match.Success && long.TryParse(match.Groups[1].Value, out long value))
                Interlocked.Exchange(ref frames, value);
        };

        process.OutputDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        // The probe measures what a recording would cost, so it has to run at
        // the priority a recording runs at.
        CaptureBackend.YieldToTheGame(process);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(WarmUpSeconds), token).ConfigureAwait(false);

            if (process.HasExited) return null;

            long startFrames = Interlocked.Read(ref frames);
            TimeSpan startCpu = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();

            await Task.Delay(TimeSpan.FromSeconds(MeasureSeconds), token).ConfigureAwait(false);

            clock.Stop();

            if (process.HasExited) return null;

            long grabbed = Interlocked.Read(ref frames) - startFrames;
            double seconds = clock.Elapsed.TotalSeconds;
            double cpuSeconds = (process.TotalProcessorTime - startCpu).TotalSeconds;

            if (seconds <= 0 || grabbed <= 0) return null;

            return new FpsReading(fps, grabbed / seconds, cpuSeconds / seconds);
        }
        finally
        {
            Quit(process);
        }
    }

    /// <summary>
    /// Asks ffmpeg to stop, and kills it only if it will not.
    /// </summary>
    /// <remarks>
    /// Nothing here is being written, so a kill would lose nothing — but a
    /// hardware encoder left mid-session can refuse to initialise for the next
    /// probe, and the next probe is a second away.
    /// </remarks>
    private static void Quit(Process process)
    {
        try
        {
            if (process.HasExited) return;

            process.StandardInput.WriteLine("q");
            process.StandardInput.Flush();

            if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true);
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        }
    }
}
