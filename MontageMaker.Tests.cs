using System.Collections.Generic;
using JinxyClicker;
using Xunit;

public class MontageMakerTests
{
    private static readonly List<string> TwoClips = new() { @"C:\c\a.mp4", @"C:\c\b.mp4" };

    [Fact]
    public void Builds_concat_for_each_clip()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("-i \"C:\\c\\a.mp4\"", args);
        Assert.Contains("-i \"C:\\c\\b.mp4\"", args);
        Assert.Contains("concat=n=2:v=1:a=0[outv]", args);
        Assert.Contains("-map \"[outv]\"", args);
        Assert.Contains("\"C:\\out.mp4\"", args);
    }

    [Fact]
    public void Landscape_targets_1920x1080_and_letterboxes()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("1920:1080", args);
        Assert.Contains("force_original_aspect_ratio=decrease", args);
        Assert.Contains("pad=1920:1080", args);
    }

    [Fact]
    public void Vertical_targets_1080x1920_and_crops()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Vertical, @"C:\out.mp4");
        Assert.Contains("1080:1920", args);
        Assert.Contains("force_original_aspect_ratio=increase", args);
        Assert.Contains("crop=1080:1920", args);
    }

    [Fact]
    public void Music_loops_and_is_mapped()
    {
        string args = MontageMaker.BuildArgs(TwoClips, @"C:\m\song.mp3", MontageShape.Landscape, @"C:\out.mp4");
        Assert.Contains("-stream_loop -1 -i \"C:\\m\\song.mp3\"", args);
        Assert.Contains("-map \"2:a\"", args);   // music is input index 2 (after 2 clips)
        Assert.Contains("-shortest", args);
        Assert.Contains("-c:a aac", args);
    }

    [Fact]
    public void No_music_has_no_audio_map()
    {
        string args = MontageMaker.BuildArgs(TwoClips, null, MontageShape.Landscape, @"C:\out.mp4");
        Assert.DoesNotContain(":a\"", args);
        Assert.DoesNotContain("-shortest", args);
    }
}
