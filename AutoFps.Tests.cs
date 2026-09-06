using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace JinxyClicker.Tests;

/// <summary>
/// Choosing a recording frame rate by measurement.
/// </summary>
/// <remarks>
/// The reason this exists is that the same setting behaves differently on
/// different machines, so what these pin is that the choice can never be
/// flattering: never faster than the screen, never faster than the capture
/// actually managed, and never past what a background recorder should cost.
/// </remarks>
public class AutoFpsTests
{
    // ---- what is worth trying ----

    [Theory]
    [InlineData(60, new[] { 60, 30 })]
    [InlineData(144, new[] { 144, 120, 60, 30 })]
    [InlineData(120, new[] { 120, 60, 30 })]
    [InlineData(75, new[] { 60, 30 })]
    [InlineData(240, new[] { 240, 180, 165, 144, 120, 60, 30 })]
    public void NeverTriesToCaptureFasterThanTheScreenDraws(int refreshHz, int[] expected)
    {
        Assert.Equal(expected, AutoFps.CandidatesFor(refreshHz));
    }

    /// <summary>
    /// Windows reports the refresh rate as a whole number and the common rates
    /// are not whole numbers: an NTSC-derived 60 Hz panel reports 59, 144
    /// reports 143, 240 reports 239. Both monitors on the machine this was
    /// written on report 59, and without the rounding both were held to 30 —
    /// a 60 Hz screen being told it could not record at 60.
    /// </summary>
    [Theory]
    [InlineData(59, 60)]
    [InlineData(119, 120)]
    [InlineData(143, 144)]
    [InlineData(164, 165)]
    [InlineData(179, 180)]
    [InlineData(239, 240)]
    [InlineData(359, 360)]
    public void AScreenReportingJustUnderItsRateStillGetsThatRate(int reported, int nominal)
    {
        Assert.Contains(nominal, AutoFps.CandidatesFor(reported));
    }

    /// <summary>
    /// The slack is for reporting, not for overshooting. A genuine 60 Hz screen
    /// must not be offered 120.
    /// </summary>
    [Fact]
    public void TheRoundingDoesNotReachTheNextRateUp()
    {
        Assert.DoesNotContain(120, AutoFps.CandidatesFor(60));
        Assert.DoesNotContain(144, AutoFps.CandidatesFor(120));
    }

    /// <summary>
    /// Desktop Duplication has as many distinct frames as the screen draws. The
    /// rest would be duplicates the encoder still pays for.
    /// </summary>
    [Fact]
    public void ASixtyHertzScreenIsNeverAskedForOneHundredAndFortyFour()
    {
        Assert.DoesNotContain(144, AutoFps.CandidatesFor(60));
    }

    [Fact]
    public void AnUnknownRefreshRateIsTreatedAsSixty()
    {
        Assert.Equal(AutoFps.CandidatesFor(60), AutoFps.CandidatesFor(0));
    }

    /// <summary>A screen slower than the slowest option still gets one.</summary>
    [Fact]
    public void ThereIsAlwaysSomethingToFallBackTo()
    {
        Assert.NotEmpty(AutoFps.CandidatesFor(24));
    }

    /// <summary>
    /// The offered list and the buttons on the recorder page have to be the same
    /// set. A rate chosen here that has no button could not be shown as chosen,
    /// and a button missing from here would never be chosen however capable the
    /// machine — which is how a list written from memory drifts.
    /// </summary>
    [Fact]
    public void TheOfferedRatesAreExactlyTheButtonsOnThePage()
    {
        string xaml = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepositoryRoot(), "MainWindow.xaml"));

        int start = xaml.IndexOf("x:Name=\"RecordFpsPanel\"", System.StringComparison.Ordinal);
        Assert.True(start > 0, "the framerate panel moved or was renamed");

        string panel = xaml[start..xaml.IndexOf("</UniformGrid>", start, System.StringComparison.Ordinal)];

        var onThePage = System.Text.RegularExpressions.Regex.Matches(panel, @"Tag=""(\d+)""")
            .Select(m => int.Parse(m.Groups[1].Value))
            .OrderByDescending(f => f)
            .ToList();

        Assert.Equal(onThePage, AutoFps.Offered);
    }

    private static string RepositoryRoot()
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);

        while (directory != null
               && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "JinxyClicker.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void EveryCandidateIsARateTheRecorderCanActuallyBeSetTo()
    {
        foreach (int refresh in new[] { 24, 30, 60, 75, 120, 144, 240 })
            Assert.All(AutoFps.CandidatesFor(refresh), f => Assert.Contains(f, AutoFps.Offered));
    }

    // ---- the budget ----

    /// <summary>
    /// A fixed core figure means opposite things at the two ends of the range,
    /// so the budget scales with the machine.
    /// </summary>
    [Fact]
    public void ABiggerMachineIsAllowedToSpendMore()
    {
        Assert.True(AutoFps.BudgetCores(16) > AutoFps.BudgetCores(4));
    }

    /// <summary>
    /// The whole-desktop pipeline that was costing people frames measured 0.78
    /// of a core. It must not pass the budget on a small machine — the point of
    /// the budget is to reject exactly that.
    /// </summary>
    [Fact]
    public void ThePipelineThatWasCostingFramesIsOverBudget()
    {
        Assert.False(AutoFps.WithinBudget(new FpsReading(30, 28.12, 0.78), processorCount: 4));
    }

    /// <summary>And the fixed pipeline, at 0.31 of a core, must pass it.</summary>
    [Fact]
    public void TheFixedPipelineIsWithinBudget()
    {
        Assert.True(AutoFps.WithinBudget(new FpsReading(30, 30.0, 0.31), processorCount: 4));
    }

    [Fact]
    public void ABudgetIsAlwaysAtLeastEnoughForSomething()
    {
        foreach (int cores in new[] { 0, 1, 2, 4, 8, 16, 32 })
            Assert.True(AutoFps.BudgetCores(cores) >= AutoFps.SmallMachineBudgetCores);
    }

    // ---- keeping up ----

    [Fact]
    public void ACaptureHoldingItsRateKeepsUp()
    {
        Assert.True(AutoFps.KeepsUp(new FpsReading(60, 59.4, 0.4)));
    }

    /// <summary>
    /// Capture is sampled off a live desktop, so a frame either side of the
    /// target is ordinary rather than a fault. A real capture asked for 30 and
    /// measured 28.12 with nothing wrong with it.
    /// </summary>
    [Fact]
    public void ASingleFrameOfSlackIsNotAFailure()
    {
        Assert.True(AutoFps.KeepsUp(new FpsReading(30, 28.12, 0.3)));
    }

    [Fact]
    public void FallingWellShortDoesNotKeepUp()
    {
        Assert.False(AutoFps.KeepsUp(new FpsReading(144, 90, 1.4)));
    }

    // ---- the choice ----

    [Fact]
    public void TakesTheFastestRateThatHeldUpAndFitTheBudget()
    {
        var readings = new[]
        {
            new FpsReading(144, 90, 1.40),
            new FpsReading(120, 118, 1.20),
            new FpsReading(60, 59.6, 0.55),
            new FpsReading(30, 30.0, 0.30)
        };

        Assert.Equal(60, AutoFps.Choose(readings, processorCount: 4).Fps);
    }

    /// <summary>
    /// A rate the capture could not hold writes a file claiming that rate and
    /// pads the difference. Smooth, but no more real than the rate it managed.
    /// </summary>
    [Fact]
    public void ARateTheMachineCannotHoldIsNeverChosen()
    {
        var readings = new[] { new FpsReading(144, 90, 0.20), new FpsReading(60, 59.9, 0.18) };

        Assert.Equal(60, AutoFps.Choose(readings, processorCount: 8).Fps);
    }

    [Fact]
    public void ARateThatCostsTooMuchIsNeverChosen()
    {
        var readings = new[] { new FpsReading(120, 120, 3.0), new FpsReading(30, 30, 0.3) };

        Assert.Equal(30, AutoFps.Choose(readings, processorCount: 4).Fps);
    }

    [Fact]
    public void AMachineThatManagedNothingStillGetsAUsableRate()
    {
        var readings = new[] { new FpsReading(60, 20, 2.0), new FpsReading(30, 12, 1.9) };

        FpsChoice choice = AutoFps.Choose(readings, processorCount: 4);

        Assert.Equal(AutoFps.Fallback, choice.Fps);
        Assert.Contains(choice.Fps, AutoFps.Offered);
    }

    /// <summary>The one result that must never happen: advice above the evidence.</summary>
    [Fact]
    public void TheChoiceIsNeverFasterThanSomethingThatWasMeasuredToWork()
    {
        foreach (int knee in AutoFps.Offered)
        {
            var readings = AutoFps.Offered
                .Select(f => f <= knee
                    ? new FpsReading(f, f, 0.2)
                    : new FpsReading(f, f * 0.5, 0.2))
                .ToList();

            FpsChoice choice = AutoFps.Choose(readings, processorCount: 8);

            Assert.True(choice.Fps <= knee, $"knee {knee} produced {choice.Fps}");
        }
    }

    [Fact]
    public void EveryChoiceExplainsItself()
    {
        var cases = new List<FpsReading[]>
        {
            new[] { new FpsReading(60, 59.9, 0.2), new FpsReading(30, 30, 0.1) },
            new[] { new FpsReading(60, 30, 0.2), new FpsReading(30, 30, 0.1) },
            new[] { new FpsReading(60, 60, 9.0), new FpsReading(30, 30, 0.1) },
            new[] { new FpsReading(30, 5, 9.0) }
        };

        foreach (FpsReading[] readings in cases)
        {
            FpsChoice choice = AutoFps.Choose(readings, processorCount: 4);

            Assert.False(string.IsNullOrWhiteSpace(choice.Headline));
            Assert.False(string.IsNullOrWhiteSpace(choice.Detail));
        }
    }

    /// <summary>
    /// The probe stops at the first rate that works, so "no faster reading" can
    /// mean either that this was the top of the list or that a faster candidate
    /// never ran. Only the first deserves to be called the fastest the screen
    /// can show.
    /// </summary>
    [Fact]
    public void ARateIsOnlyCalledTheScreenMaximumWhenItActuallyIs()
    {
        var readings = new[] { new FpsReading(60, 59.8, 0.4) };

        string atTheTop = AutoFps.Choose(readings, 8, new[] { 60, 30 }).Detail;
        string notAtTheTop = AutoFps.Choose(readings, 8, new[] { 144, 120, 60, 30 }).Detail;

        Assert.Contains("fastest rate this screen can show", atTheTop);
        Assert.DoesNotContain("fastest rate this screen can show", notAtTheTop);
    }

    [Fact]
    public void TheReadingsAreKeptSoTheChoiceCanBeCheckedAgainstThem()
    {
        var readings = new[] { new FpsReading(30, 30, 0.3) };

        Assert.Equal(readings, AutoFps.Choose(readings, processorCount: 4).Readings);
    }

    [Fact]
    public void AReadingThatWasNeverRunIsNotADivisionByZero()
    {
        Assert.Equal(0, new FpsReading(0, 0, 0).Kept);
        Assert.False(AutoFps.KeepsUp(new FpsReading(0, 0, 0)));
    }
}
