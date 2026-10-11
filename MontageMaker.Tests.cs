using System.Collections.Generic;
using JinxyClicker;
using Xunit;

public class MontageMakerTests
{
    private static readonly List<MontageClip> TwoClips = new()
    {
        new MontageClip(@"C:\c\a.mp4"),
        new MontageClip(@"C:\c\b.mp4")
    };

    private static MontageOptions Land(bool crossfade = false, string? title = null) =>
        new(MontageShape.Landscape, crossfade, title);

    [Fact]
    public void Builds_concat_for_each_clip()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(), @"C:\out.mp4");
        Assert.Contains("-i \"C:\\c\\a.mp4\"", args);
        Assert.Contains("-i \"C:\\c\\b.mp4\"", args);
        Assert.Contains("concat=n=2:v=1:a=0[outv]", args);
        Assert.Contains("-map \"[outv]\"", args);
        Assert.Contains("\"C:\\out.mp4\"", args);
    }

    [Fact]
    public void Landscape_targets_1920x1080_and_letterboxes()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(), @"C:\out.mp4");
        Assert.Contains("1920:1080", args);
        Assert.Contains("force_original_aspect_ratio=decrease", args);
        Assert.Contains("pad=1920:1080", args);
    }

    [Fact]
    public void Vertical_targets_1080x1920_and_crops()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, new MontageOptions(MontageShape.Vertical), @"C:\out.mp4");
        Assert.Contains("1080:1920", args);
        Assert.Contains("force_original_aspect_ratio=increase", args);
        Assert.Contains("crop=1080:1920", args);
    }

    [Fact]
    public void Music_loops_and_is_mapped()
    {
        string args = MontageMaker.BuildArgs(TwoClips, @"C:\m\song.mp3", Land(), @"C:\out.mp4");
        Assert.Contains("-stream_loop -1 -i \"C:\\m\\song.mp3\"", args);
        Assert.Contains("-map \"2:a\"", args);   // music is input index 2 (after 2 clips)
        Assert.Contains("-shortest", args);
        Assert.Contains("-c:a aac", args);
    }

    [Fact]
    public void No_music_has_no_audio_map()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(), @"C:\out.mp4");
        Assert.DoesNotContain(":a\"", args);
        Assert.DoesNotContain("-shortest", args);
    }

    [Fact]
    public void Trim_adds_a_trim_filter_only_to_the_trimmed_clip()
    {
        var clips = new List<MontageClip>
        {
            new(@"C:\c\a.mp4", 4, 12),   // keep 4s..12s
            new(@"C:\c\b.mp4")           // whole clip
        };
        string args = MontageMaker.BuildArgs(clips, null, Land(), @"C:\out.mp4");
        Assert.Contains("[0:v]trim=start=4:end=12,setpts=PTS-STARTPTS,scale=", args);
        Assert.Contains("[1:v]scale=", args); // untrimmed clip starts straight at scale
        Assert.DoesNotContain("[1:v]trim", args);
    }

    // ---- title card ----

    [Fact]
    public void Title_card_is_the_first_input_and_first_segment()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(title: @"C:\t\title.png"), @"C:\out.mp4");
        Assert.Contains("-loop 1 -t 2 -i \"C:\\t\\title.png\"", args); // held 2s by default
        Assert.Contains("[0:v]scale=", args);                           // title is input 0, normalized
        Assert.Contains("[vt];", args);                                 // labelled [vt]
        Assert.Contains("[1:v]scale=", args);                           // first clip shifts to input 1
        Assert.Contains("[vt][v0][v1]concat=n=3:v=1:a=0[outv]", args);  // title leads the concat
    }

    [Fact]
    public void Title_shifts_the_music_map_index()
    {
        string args = MontageMaker.BuildArgs(TwoClips, @"C:\m\song.mp3", Land(title: @"C:\t\title.png"), @"C:\out.mp4");
        Assert.Contains("-map \"3:a\"", args); // title(0) + 2 clips(1,2) -> music is input 3
    }

    // ---- crossfade ----

    [Fact]
    public void Crossfade_chains_xfade_with_cumulative_offsets()
    {
        var durs = new List<double> { 4, 6, 5 };
        string args = MontageMaker.BuildArgs(ThreeClips, null, Land(crossfade: true), @"C:\out.mp4", durs);
        // first join at 4 - 0.5 = 3.5; second at (4+6-0.5) - 0.5 = 9
        Assert.Contains("[v0][v1]xfade=transition=fade:duration=0.5:offset=3.5[x1];", args);
        Assert.Contains("[x1][v2]xfade=transition=fade:duration=0.5:offset=9[outv]", args);
        Assert.DoesNotContain("concat=", args);
    }

    [Fact]
    public void Crossfade_uses_the_trimmed_length_not_the_full_clip()
    {
        var clips = new List<MontageClip>
        {
            new(@"C:\c\a.mp4", 2, 6),   // kept length 4s
            new(@"C:\c\b.mp4")          // whole 6s
        };
        var durs = new List<double> { 10, 6 };   // a's FULL length is 10, but only 4s kept
        string args = MontageMaker.BuildArgs(clips, null, Land(crossfade: true), @"C:\out.mp4", durs);
        Assert.Contains("offset=3.5[outv]", args); // 4 (kept) - 0.5, not 10 - 0.5
    }

    [Fact]
    public void Crossfade_offsets_from_the_title_duration_when_present()
    {
        var durs = new List<double> { 4, 6 };
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(crossfade: true, title: @"C:\t\title.png"), @"C:\out.mp4", durs);
        // title 2s first: title->clip0 at 2 - 0.5 = 1.5
        Assert.Contains("[vt][v0]xfade=transition=fade:duration=0.5:offset=1.5[x1];", args);
        Assert.Contains("[x1][v1]xfade=transition=fade:duration=0.5:offset=", args);
    }

    [Fact]
    public void Crossfade_falls_back_to_concat_when_a_segment_is_shorter_than_the_fade()
    {
        var durs = new List<double> { 0.3, 6 };   // clip a (0.3s) is shorter than the 0.5s fade
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(crossfade: true), @"C:\out.mp4", durs);
        Assert.DoesNotContain("xfade", args);      // xfade would fail on a too-short clip
        Assert.Contains("concat=n=2:v=1:a=0[outv]", args);
    }

    [Fact]
    public void Crossfade_falls_back_to_concat_without_durations()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, Land(crossfade: true), @"C:\out.mp4", null);
        Assert.DoesNotContain("xfade", args);
        Assert.Contains("concat=n=2:v=1:a=0[outv]", args);
    }

    [Fact]
    public void Crossfade_with_a_single_segment_has_no_xfade()
    {
        var one = new List<MontageClip> { new(@"C:\c\a.mp4") };
        var durs = new List<double> { 4 };
        string args = MontageMaker.BuildArgs(one, null, Land(crossfade: true), @"C:\out.mp4", durs);
        Assert.DoesNotContain("xfade", args);
        Assert.Contains("concat=n=1:v=1:a=0[outv]", args);
    }

    private static readonly List<MontageClip> ThreeClips = new()
    {
        new MontageClip(@"C:\c\a.mp4"),
        new MontageClip(@"C:\c\b.mp4"),
        new MontageClip(@"C:\c\c.mp4")
    };

    [Theory]
    [InlineData("  Duration: 00:00:18.53, start: 0.000000, bitrate: 100 kb/s", 18.53)]
    [InlineData("Duration: 00:01:05.00,", 65.0)]
    [InlineData("Duration: 01:00:00.00,", 3600.0)]
    [InlineData("no duration here", 0.0)]
    public void ParseDuration_reads_ffmpeg_duration(string line, double expected)
    {
        Assert.Equal(expected, MontageMaker.ParseDuration(line), 2);
    }
}
