using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JinxyClicker;

public enum MontageShape { Landscape, Vertical }

/// <summary>A clip in a montage, with an optional trim window (seconds).</summary>
public readonly record struct MontageClip(string Path, double? StartSec = null, double? EndSec = null);

/// <summary>
/// Builds (and, in RenderAsync, runs) the ffmpeg command that turns a list of
/// clips into one montage: every clip scaled to a common frame, concatenated,
/// with music looped over the top.
/// </summary>
public static partial class MontageMaker
{
    public static string BuildArgs(IReadOnlyList<MontageClip> clips, string? musicPath,
        MontageShape shape, string outputPath)
    {
        bool hasMusic = !string.IsNullOrWhiteSpace(musicPath);
        int w = shape == MontageShape.Vertical ? 1080 : 1920;
        int h = shape == MontageShape.Vertical ? 1920 : 1080;

        var sb = new StringBuilder("-y ");
        foreach (MontageClip clip in clips) sb.Append($"-i \"{clip.Path}\" ");
        if (hasMusic) sb.Append($"-stream_loop -1 -i \"{musicPath}\" ");

        // Per-clip: optional trim, then normalize to a common frame; then concat.
        string fit = shape == MontageShape.Vertical
            ? $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}"
            : $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2";

        var filter = new StringBuilder();
        for (int i = 0; i < clips.Count; i++)
            filter.Append($"[{i}:v]{TrimFilter(clips[i])}{fit},setsar=1,fps=30[v{i}];");
        for (int i = 0; i < clips.Count; i++)
            filter.Append($"[v{i}]");
        filter.Append($"concat=n={clips.Count}:v=1:a=0[outv]");

        sb.Append($"-filter_complex \"{filter}\" -map \"[outv]\" ");

        if (hasMusic)
            sb.Append($"-map \"{clips.Count}:a\" -shortest -c:a aac ");

        sb.Append($"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"");
        return sb.ToString();
    }

    private static string TrimFilter(MontageClip clip)
    {
        if (!clip.StartSec.HasValue && !clip.EndSec.HasValue) return "";
        var sb = new StringBuilder($"trim=start={Fmt(clip.StartSec ?? 0)}");
        if (clip.EndSec.HasValue) sb.Append($":end={Fmt(clip.EndSec.Value)}");
        sb.Append(",setpts=PTS-STARTPTS,");
        return sb.ToString();
    }

    private static string Fmt(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Reads the "Duration: HH:MM:SS.ms" line ffmpeg prints; 0 if absent.</summary>
    public static double ParseDuration(string ffmpegOutput)
    {
        Match m = Regex.Match(ffmpegOutput ?? "", @"Duration:\s*(\d+):(\d\d):(\d\d(?:\.\d+)?)");
        if (!m.Success) return 0;
        return int.Parse(m.Groups[1].Value) * 3600
             + int.Parse(m.Groups[2].Value) * 60
             + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>How long a clip is, in seconds, via ffmpeg. 0 if it can't be read.</summary>
    public static async Task<double> ProbeDurationAsync(string path)
    {
        string? ffmpeg = ScreenRecorder.FindFfmpeg();
        if (ffmpeg == null) return 0;
        try
        {
            var info = new ProcessStartInfo(ffmpeg, $"-i \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using Process? p = Process.Start(info);
            if (p == null) return 0;
            string err = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await p.WaitForExitAsync().ConfigureAwait(false);
            return ParseDuration(err);
        }
        catch { return 0; }
    }

    /// <summary>
    /// Renders the montage to a new file in <paramref name="outputFolder"/>. Returns
    /// the output path, or null on any failure — no ffmpeg, no clips, an ffmpeg
    /// error, or an empty output.
    /// </summary>
    public static async Task<string?> RenderAsync(IReadOnlyList<MontageClip> clips, string? musicPath,
        MontageShape shape, string outputFolder)
    {
        if (clips is null || clips.Count == 0) return null;

        string? ffmpeg = ScreenRecorder.FindFfmpeg();
        if (ffmpeg == null) return null;

        try
        {
            Directory.CreateDirectory(outputFolder);
            string output = Path.Combine(outputFolder, $"montage-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");
            string args = BuildArgs(clips, musicPath, shape, output);

            var info = new ProcessStartInfo(ffmpeg, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? p = Process.Start(info);
            if (p == null) return null;
            p.ErrorDataReceived += (_, _) => { };
            p.OutputDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            await p.WaitForExitAsync().ConfigureAwait(false);

            return File.Exists(output) && new FileInfo(output).Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
