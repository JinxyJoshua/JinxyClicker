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
/// The look of a montage beyond the clips themselves: shape, an optional intro
/// title card, and whether clips crossfade into each other.
/// </summary>
/// <param name="Shape">Landscape (letterboxed) or Vertical (cropped to fill).</param>
/// <param name="Crossfade">Fade each segment into the next instead of a hard cut.</param>
/// <param name="TitleImagePath">A pre-rendered intro image shown first, or null for none.</param>
/// <param name="TitleSeconds">How long the title card holds.</param>
/// <param name="FadeSeconds">The length of each crossfade.</param>
public readonly record struct MontageOptions(
    MontageShape Shape,
    bool Crossfade = false,
    string? TitleImagePath = null,
    double TitleSeconds = 2.0,
    double FadeSeconds = 0.5);

/// <summary>
/// Builds (and, in RenderAsync, runs) the ffmpeg command that turns a list of
/// clips into one montage: every clip scaled to a common frame, concatenated,
/// with music looped over the top.
/// </summary>
public static partial class MontageMaker
{
    public static string BuildArgs(IReadOnlyList<MontageClip> clips, string? musicPath,
        MontageOptions options, string outputPath, IReadOnlyList<double>? clipDurations = null)
    {
        bool hasMusic = !string.IsNullOrWhiteSpace(musicPath);
        bool hasTitle = !string.IsNullOrWhiteSpace(options.TitleImagePath);
        int baseIdx = hasTitle ? 1 : 0;   // clips start after the optional title input

        int w = options.Shape == MontageShape.Vertical ? 1080 : 1920;
        int h = options.Shape == MontageShape.Vertical ? 1920 : 1080;

        var sb = new StringBuilder("-y ");
        if (hasTitle) sb.Append($"-loop 1 -t {Fmt(options.TitleSeconds)} -i \"{options.TitleImagePath}\" ");
        foreach (MontageClip clip in clips) sb.Append($"-i \"{clip.Path}\" ");
        if (hasMusic) sb.Append($"-stream_loop -1 -i \"{musicPath}\" ");

        string fit = options.Shape == MontageShape.Vertical
            ? $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}"
            : $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2";

        // Normalize every segment (title first, then each clip) to a common frame.
        // Each gets a stable label and a known duration so crossfade offsets add up.
        var labels = new List<string>();
        var durations = new List<double>();
        var filter = new StringBuilder();

        if (hasTitle)
        {
            filter.Append($"[0:v]{fit},setsar=1,fps=30[vt];");
            labels.Add("[vt]");
            durations.Add(options.TitleSeconds);
        }
        for (int i = 0; i < clips.Count; i++)
        {
            filter.Append($"[{baseIdx + i}:v]{TrimFilter(clips[i])}{fit},setsar=1,fps=30[v{i}];");
            labels.Add($"[v{i}]");
            durations.Add(EffectiveDuration(clips[i], clipDurations, i));
        }

        // Crossfade needs each clip's real length; without it, fall back to hard
        // cuts. Every segment must also be longer than the fade, or xfade fails.
        bool haveDurations = clipDurations is not null
            && clipDurations.Count == clips.Count
            && clipDurations.All(d => d > 0);
        bool doCrossfade = options.Crossfade && labels.Count >= 2 && haveDurations
            && durations.All(d => d > options.FadeSeconds);

        if (doCrossfade)
        {
            double fade = options.FadeSeconds;
            string acc = labels[0];
            double accLen = durations[0];
            for (int k = 1; k < labels.Count; k++)
            {
                double offset = accLen - fade;
                string outLabel = k == labels.Count - 1 ? "[outv]" : $"[x{k}]";
                filter.Append($"{acc}{labels[k]}xfade=transition=fade:duration={Fmt(fade)}:offset={Fmt(offset)}{outLabel};");
                acc = outLabel;
                accLen += durations[k] - fade;   // overlap eats one fade per join
            }
        }
        else
        {
            foreach (string label in labels) filter.Append(label);
            filter.Append($"concat=n={labels.Count}:v=1:a=0[outv]");
        }

        sb.Append($"-filter_complex \"{filter.ToString().TrimEnd(';')}\" -map \"[outv]\" ");

        if (hasMusic)
            sb.Append($"-map \"{baseIdx + clips.Count}:a\" -shortest -c:a aac ");

        sb.Append($"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"");
        return sb.ToString();
    }

    /// <summary>How long a clip plays after its trim, falling back to its full length.</summary>
    private static double EffectiveDuration(MontageClip clip, IReadOnlyList<double>? durations, int i)
    {
        double full = durations is not null && i < durations.Count ? durations[i] : 0;
        double start = clip.StartSec ?? 0;
        double end = clip.EndSec ?? full;
        return Math.Max(0, end - start);
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
        MontageOptions options, string outputFolder)
    {
        if (clips is null || clips.Count == 0) return null;

        string? ffmpeg = ScreenRecorder.FindFfmpeg();
        if (ffmpeg == null) return null;

        try
        {
            // Crossfade offsets need each clip's real length; probe them up front.
            // A title card counts as a segment, so probe even for a single clip.
            List<double>? durations = null;
            if (options.Crossfade && clips.Count >= 1)
            {
                durations = new List<double>(clips.Count);
                foreach (MontageClip clip in clips)
                    durations.Add(await ProbeDurationAsync(clip.Path).ConfigureAwait(false));
            }

            Directory.CreateDirectory(outputFolder);
            string output = Path.Combine(outputFolder, $"montage-{DateTime.Now:yyyy-MM-dd-HHmmss}.mp4");
            string args = BuildArgs(clips, musicPath, options, output, durations);

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

            // ffmpeg returns non-zero on failure; a file can still be left behind
            // truncated, so the exit code is what says the render actually worked.
            return p.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
