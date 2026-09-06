using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// The capture source selection. Encoder choice is not covered here — it probes
/// the machine's actual GPU by running ffmpeg, which is a different kind of test
/// than this project has anywhere to run.
/// </summary>
public class CaptureBackendTests
{
    private static DisplayInfo Display(int number) =>
        new("\\\\.\\DISPLAY" + number, number, 0, 0, 1920, 1080, number == 1);

    [Fact]
    public void AllDisplays_StaysOnGdigrab()
    {
        string args = CaptureBackend.InputArgs(display: null, framesPerSecond: 30);

        // Desktop Duplication addresses one monitor and cannot span several, so
        // the whole-desktop case has to keep the old source.
        Assert.Contains("gdigrab", args);
        Assert.Contains("-i desktop", args);
        Assert.DoesNotContain("ddagrab", args);
    }

    [Fact]
    public void AChosenDisplay_UsesDesktopDuplication()
    {
        string args = CaptureBackend.InputArgs(Display(1), framesPerSecond: 30);

        Assert.Contains("ddagrab", args);
        Assert.DoesNotContain("gdigrab", args);
    }

    /// <summary>
    /// The whole point of the ddagrab path is picking one screen. Getting the
    /// index wrong records the other monitor.
    /// </summary>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void TheDisplayNumberBecomesAZeroBasedOutputIndex(int number, int expected)
    {
        Assert.Equal(expected, Display(number).OutputIndex);
        Assert.Contains($"output_idx={expected}", CaptureBackend.InputArgs(Display(number), 30));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void TheFramerateReachesBothSources(int fps)
    {
        Assert.Contains($"framerate={fps}", CaptureBackend.InputArgs(Display(1), fps));
        Assert.Contains($"-framerate {fps}", CaptureBackend.InputArgs(null, fps));
    }

    /// <summary>
    /// ddagrab hands over a BGRA surface that AMF refuses outright — the encode
    /// fails with an unhelpful error code rather than degrading. The download
    /// and format conversion are what make the path work at all.
    /// </summary>
    [Fact]
    public void TheDesktopDuplicationPath_ConvertsAwayFromTheHardwareSurface()
    {
        string args = CaptureBackend.InputArgs(Display(1), 30);

        Assert.Contains("hwdownload", args);
        Assert.Contains("format=bgra", args);
    }

    /// <summary>
    /// The filter graph carries commas and has to survive as one argument.
    /// </summary>
    [Fact]
    public void TheFilterGraphIsQuoted()
    {
        string args = CaptureBackend.InputArgs(Display(1), 30);

        Assert.Contains("-filter_complex \"", args);
        Assert.EndsWith("\"", args);
    }

    /// <summary>
    /// x264 rejects odd dimensions outright, which a monitor can report.
    /// </summary>
    [Fact]
    public void OddMonitorDimensionsAreRoundedDown()
    {
        var odd = new DisplayInfo("\\\\.\\DISPLAY1", 1, 0, 0, 1921, 1081, true);

        Assert.Equal(1920, odd.EvenWidth);
        Assert.Equal(1080, odd.EvenHeight);
    }

    // ---- pacing ----

    /// <summary>
    /// Screen capture produces frames when the desktop changes, not on a clock.
    /// A real capture asked for 30 and recorded 28.12, with 16 of its 208 frames
    /// irregularly spaced; the same capture paced to constant frame rate
    /// recorded a flat 30 with 0 of 239 irregular. Players stutter on the first
    /// kind, which is what people described as the clip being laggy.
    /// </summary>
    [Fact]
    public void CapturesArePacedToAConstantFrameRate()
    {
        Assert.Equal("-fps_mode cfr", CaptureBackend.PacingArgs);
    }

    /// <summary>
    /// Both recorders have to carry it. They build their own argument strings,
    /// so nothing but a check like this stops one of them drifting off it.
    /// </summary>
    [Theory]
    [InlineData("ScreenRecorder.cs")]
    [InlineData("ReplayBuffer.cs")]
    public void BothRecordersPaceTheirOutput(string source)
    {
        string path = System.IO.Path.Combine(RepositoryRoot(), source);
        string text = System.IO.File.ReadAllText(path);

        Assert.Contains("CaptureBackend.PacingArgs", text);
        Assert.Contains("CaptureBackend.YieldToTheGame", text);
    }

    /// <summary>
    /// Walks up from the test binary to the folder holding the app's sources.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);

        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "JinxyClicker.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    // ---- bitrate ----

    /// <summary>
    /// A flat figure means opposite things at different sizes. It was 8 Mbit for
    /// everything; a real 1080p60 clip measured 7.8 Mbit of fast motion and was
    /// visibly soft, and the same 8 Mbit on a 1440p machine is spread over
    /// nearly twice the pixels — which is why clips looked worse on some PCs
    /// than others with nothing about the setting different.
    /// </summary>
    [Fact]
    public void MorePixelsGetMoreBits()
    {
        Assert.True(CaptureBackend.BitrateMbit(2560, 1440, 60) > CaptureBackend.BitrateMbit(1920, 1080, 60));
    }

    [Fact]
    public void MoreFramesGetMoreBits()
    {
        Assert.True(CaptureBackend.BitrateMbit(1920, 1080, 60) > CaptureBackend.BitrateMbit(1920, 1080, 30));
    }

    /// <summary>
    /// The case that prompted this. 1080p60 was getting about 8; it should now
    /// get meaningfully more, without running away.
    /// </summary>
    [Fact]
    public void TenEightyAtSixtyGetsMoreThanItUsedTo()
    {
        Assert.InRange(CaptureBackend.BitrateMbit(1920, 1080, 60), 10, 16);
    }

    [Theory]
    [InlineData(1920, 1080, 30)]
    [InlineData(3840, 2160, 60)]
    [InlineData(640, 480, 30)]
    [InlineData(7680, 4320, 240)]
    public void EveryBitrateStaysInsideTheBounds(int width, int height, int fps)
    {
        int mbit = CaptureBackend.BitrateMbit(width, height, fps);

        Assert.InRange(mbit, CaptureBackend.MinimumMbit, CaptureBackend.MaximumMbit);
    }

    /// <summary>Nonsense in must not produce a bitrate of zero, which encodes nothing.</summary>
    [Theory]
    [InlineData(0, 1080, 60)]
    [InlineData(1920, 0, 60)]
    [InlineData(1920, 1080, 0)]
    [InlineData(-1, -1, -1)]
    public void ADegenerateCaptureStillGetsAUsableBitrate(int width, int height, int fps)
    {
        Assert.Equal(CaptureBackend.MinimumMbit, CaptureBackend.BitrateMbit(width, height, fps));
    }

    /// <summary>
    /// The placeholder has to be substituted. Left in, ffmpeg would be handed a
    /// literal "-b:v {BITRATE}M" and refuse to start — so this is the difference
    /// between a recorder and no recorder.
    /// </summary>
    [Fact]
    public void NoPlaceholderSurvivesIntoTheArguments()
    {
        string? ffmpeg = ScreenRecorder.FindFfmpeg();

        // Probing an encoder needs a real ffmpeg. Where there is none there is
        // also no recorder, so there is nothing here to get wrong.
        if (ffmpeg == null) return;

        string args = CaptureBackend.EncoderArgs(ffmpeg, Display(1), 60);

        Assert.DoesNotContain("{BITRATE}", args);
        Assert.Contains("-b:v", args);
    }
}
