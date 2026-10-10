using System.Collections.Generic;
using System.Text;

namespace JinxyClicker;

public enum MontageShape { Landscape, Vertical }

/// <summary>
/// Builds (and, in RenderAsync, runs) the ffmpeg command that turns a list of
/// clips into one montage: every clip scaled to a common frame, concatenated,
/// with music looped over the top.
/// </summary>
public static partial class MontageMaker
{
    public static string BuildArgs(IReadOnlyList<string> clipPaths, string? musicPath,
        MontageShape shape, string outputPath)
    {
        bool hasMusic = !string.IsNullOrWhiteSpace(musicPath);
        int w = shape == MontageShape.Vertical ? 1080 : 1920;
        int h = shape == MontageShape.Vertical ? 1920 : 1080;

        var sb = new StringBuilder("-y ");
        foreach (string clip in clipPaths) sb.Append($"-i \"{clip}\" ");
        if (hasMusic) sb.Append($"-stream_loop -1 -i \"{musicPath}\" ");

        // Per-clip normalize to a common frame, then concat the video streams.
        string fit = shape == MontageShape.Vertical
            ? $"scale={w}:{h}:force_original_aspect_ratio=increase,crop={w}:{h}"
            : $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2";

        var filter = new StringBuilder();
        for (int i = 0; i < clipPaths.Count; i++)
            filter.Append($"[{i}:v]{fit},setsar=1,fps=30[v{i}];");
        for (int i = 0; i < clipPaths.Count; i++)
            filter.Append($"[v{i}]");
        filter.Append($"concat=n={clipPaths.Count}:v=1:a=0[outv]");

        sb.Append($"-filter_complex \"{filter}\" -map \"[outv]\" ");

        if (hasMusic)
            sb.Append($"-map \"{clipPaths.Count}:a\" -shortest -c:a aac ");

        sb.Append($"-c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart \"{outputPath}\"");
        return sb.ToString();
    }
}
