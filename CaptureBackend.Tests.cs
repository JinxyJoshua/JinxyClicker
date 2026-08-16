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
}
