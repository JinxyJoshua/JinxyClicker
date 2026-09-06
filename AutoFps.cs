using System;
using System.Collections.Generic;
using System.Linq;

namespace JinxyClicker;

/// <summary>What one candidate frame rate actually did on this machine.</summary>
/// <param name="RequestedFps">The rate the capture was asked for.</param>
/// <param name="AchievedFps">The rate it managed.</param>
/// <param name="CoresUsed">
/// Processor time divided by wall clock — 1.0 is one core saturated. Measured
/// rather than assumed, because it is what the game loses.
/// </param>
public sealed record FpsReading(int RequestedFps, double AchievedFps, double CoresUsed)
{
    /// <summary>Achieved as a fraction of requested. 1.0 is keeping up.</summary>
    public double Kept => RequestedFps > 0 ? AchievedFps / RequestedFps : 0;
}

/// <summary>What the probe concluded.</summary>
public sealed record FpsChoice(int Fps, IReadOnlyList<FpsReading> Readings, string Headline, string Detail);

/// <summary>
/// Picks a recording frame rate by measuring the machine rather than defaulting.
/// </summary>
/// <remarks>
/// The same setting behaves completely differently on different PCs, which is
/// what "it records fine on one of my computers and not the other" is. Three
/// things decide it and none are knowable in advance: whether a hardware encoder
/// is present, how many pixels the chosen monitor has, and what else the machine
/// is doing. So they get measured.
///
/// Two separate questions, and a rate has to pass both. Whether the capture can
/// keep up at all — a machine asked for 144 and delivering 90 writes a file that
/// claims 144, and constant-frame-rate pacing then pads it, which is smooth but
/// no better than the 90 it really had. And whether it costs more than a
/// background recorder should, because the whole point of recording is to still
/// be playing the game while it happens.
/// </remarks>
public static class AutoFps
{
    /// <summary>The rates the recorder offers, fastest first.</summary>
    /// <remarks>
    /// Matches the buttons on the recorder page. A rate this picks that has no
    /// button could not be shown as chosen.
    /// </remarks>
    public static readonly IReadOnlyList<int> Offered = new[] { 360, 240, 180, 165, 144, 120, 60, 30 };

    /// <summary>The rate used when nothing else can be established.</summary>
    public const int Fallback = 30;

    /// <summary>How far short of the requested rate still counts as keeping up.</summary>
    /// <remarks>
    /// Capture is sampled off a live desktop and lands a frame either side of
    /// the target as a matter of course; a real capture asked for 30 and
    /// measured 28.12 without anything being wrong.
    /// </remarks>
    public const double KeepTolerance = 0.10;

    /// <summary>
    /// How much CPU a background recorder may use, in cores, on a small machine.
    /// </summary>
    /// <remarks>
    /// Measured here: 1920x1080 at 30 through Desktop Duplication and a
    /// hardware encoder costs about 0.31 of a core, and the whole virtual
    /// desktop through gdigrab cost 0.78. The budget sits above the first and
    /// below the second on purpose — the pipeline that was dropping people's
    /// frames should not pass.
    /// </remarks>
    public const double SmallMachineBudgetCores = 0.75;

    /// <summary>Share of the whole machine a recorder may take, on a large one.</summary>
    public const double BudgetShareOfMachine = 0.10;

    /// <summary>
    /// The CPU budget for this machine, in cores.
    /// </summary>
    /// <remarks>
    /// Scaled, because a fixed figure means opposite things at the two ends: on
    /// four cores three quarters of one is a fifth of the machine, and on
    /// sixteen it is a rounding error. A budget that does not scale would hold a
    /// workstation to a laptop's rate for no reason.
    /// </remarks>
    public static double BudgetCores(int processorCount) =>
        Math.Max(SmallMachineBudgetCores, Math.Max(1, processorCount) * BudgetShareOfMachine);

    /// <summary>
    /// Slack on the refresh ceiling, because Windows reports it as a whole
    /// number and the common rates are not whole numbers.
    /// </summary>
    /// <remarks>
    /// The NTSC-derived rates are the screen's nominal rate times 1000/1001, and
    /// Windows truncates: a 60 Hz panel reports 59, 120 reports 119, 144 reports
    /// 143, 240 reports 239. Every one of them lands exactly one below.
    ///
    /// Without this both monitors on the machine this was written on reported 59
    /// and were held to 30, because 60 did not fit under 59 — a 60 Hz screen
    /// being told it could not record at 60.
    /// </remarks>
    public const int RefreshRounding = 1;

    /// <summary>
    /// The rates worth trying on a monitor, fastest first.
    /// </summary>
    /// <remarks>
    /// Never meaningfully above the refresh rate. Desktop Duplication has as
    /// many distinct frames as the screen draws, so asking a 60 Hz panel for 144
    /// buys 60 real frames and 84 duplicates — every one of which the encoder
    /// still pays for.
    ///
    /// The slowest rate is always offered even on a screen slower than it, so
    /// there is something to fall back to rather than an empty list.
    /// </remarks>
    public static IReadOnlyList<int> CandidatesFor(int refreshHz)
    {
        int ceiling = (refreshHz > 0 ? refreshHz : 60) + RefreshRounding;

        List<int> usable = Offered.Where(f => f <= ceiling).ToList();

        return usable.Count > 0 ? usable : new List<int> { Offered.Min() };
    }

    /// <summary>Whether the capture held the rate it was asked for.</summary>
    public static bool KeepsUp(FpsReading reading) =>
        reading.RequestedFps > 0 && reading.Kept >= 1.0 - KeepTolerance;

    /// <summary>Whether it left enough of the machine for the game.</summary>
    public static bool WithinBudget(FpsReading reading, int processorCount) =>
        reading.CoresUsed <= BudgetCores(processorCount);

    public static bool IsUsable(FpsReading reading, int processorCount) =>
        KeepsUp(reading) && WithinBudget(reading, processorCount);

    /// <summary>
    /// The fastest rate that both held up and stayed inside the budget.
    /// </summary>
    /// <param name="candidates">
    /// The rates that were worth trying, so the explanation can tell "this is
    /// the top of the list" apart from "everything above it failed". The probe
    /// stops at the first rate that works, so without this a rate chosen after a
    /// candidate failed to run at all would be described as the fastest the
    /// screen can show.
    /// </param>
    public static FpsChoice Choose(
        IReadOnlyList<FpsReading> readings, int processorCount, IReadOnlyList<int>? candidates = null)
    {
        FpsReading? best = readings
            .Where(r => IsUsable(r, processorCount))
            .OrderByDescending(r => r.RequestedFps)
            .FirstOrDefault();

        if (best == null)
        {
            return new FpsChoice(
                Fallback,
                readings,
                $"Recording at {Fallback}.",
                "No rate tested both kept up and left the machine to the game, so this is the slowest one "
                + "on offer. If clips still cost frames, the encoder is probably running on the CPU — "
                + "check the recorder line for which one this machine picked.");
        }

        FpsReading? faster = readings
            .Where(r => r.RequestedFps > best.RequestedFps)
            .OrderBy(r => r.RequestedFps)
            .FirstOrDefault();

        int top = candidates is { Count: > 0 } ? candidates.Max() : best.RequestedFps;

        string detail = faster == null
            ? best.RequestedFps >= top
                ? $"The fastest rate this screen can show, held at {best.AchievedFps:0.#} a second for "
                  + $"{best.CoresUsed:0.00} of a core."
                : $"Held at {best.AchievedFps:0.#} a second for {best.CoresUsed:0.00} of a core. Faster "
                  + "rates were worth trying here but would not run to be measured."
            : !KeepsUp(faster)
                ? $"{faster.RequestedFps} was tried and only reached {faster.AchievedFps:0.#}, which would "
                  + $"have written a file claiming {faster.RequestedFps} and padded the difference. "
                  + $"{best.RequestedFps} is real."
                : $"{faster.RequestedFps} kept up but wanted {faster.CoresUsed:0.00} of a core, over the "
                  + $"{BudgetCores(processorCount):0.00} a background recorder should take. "
                  + $"{best.RequestedFps} costs {best.CoresUsed:0.00}.";

        return new FpsChoice(best.RequestedFps, readings, $"Recording at {best.RequestedFps}.", detail);
    }
}
