using System;

namespace JinxyClicker;

/// <summary>How what is being delivered compares to what was asked for.</summary>
public enum OutputState
{
    /// <summary>Not clicking, so there is nothing to measure yet.</summary>
    Idle,

    /// <summary>Delivering materially less than the setting asks for.</summary>
    Shortfall,

    /// <summary>Delivering the setting, but the setting is past the point of use.</summary>
    OverDriven,

    /// <summary>Delivering the setting, inside a rate the server keeps up with.</summary>
    Matching
}

/// <summary>
/// The difference between the rate someone set and the rate that leaves the app.
/// </summary>
/// <remarks>
/// The sliders are a request. HitFix's floors, the duty cycle and the operating
/// system's scheduler all sit between that request and what the game receives,
/// and the gap is routinely large — a 193 CPS setting delivering 33 /s is the
/// ordinary case rather than a fault.
///
/// Left unsaid, the bigger number reads as the better setting, which is exactly
/// backwards: a run measured at 41 CPS landed 34 hits where a 193 CPS run landed
/// 33. This is the arithmetic behind saying so.
/// </remarks>
public static class ClickOutput
{
    /// <summary>
    /// The rate past which extra clicks stop arriving as extra hits.
    /// </summary>
    /// <remarks>
    /// A boundary for the wording, not a limit. Nothing is prevented — the
    /// slider still goes where it goes, and the app only says what happens.
    /// </remarks>
    public const double DiminishingReturnsCps = 50.0;

    /// <summary>
    /// How far short delivery must fall before it is worth mentioning.
    /// </summary>
    /// <remarks>
    /// Measurement is a difference of click counts over a wall-clock second, so
    /// it jitters by a click either way at any rate. A tolerance stops the panel
    /// flickering between verdicts while nothing has actually changed.
    /// </remarks>
    public const double MismatchTolerance = 0.15;

    public static OutputState Classify(bool running, double setCps, double deliveredCps)
    {
        if (!running) return OutputState.Idle;

        // A nonsense reading is not evidence of a shortfall. NaN slips through
        // any comparison it is put in, so it is refused up front.
        if (double.IsNaN(deliveredCps) || double.IsNaN(setCps)) return OutputState.Idle;

        if (setCps > 0 && deliveredCps < setCps * (1.0 - MismatchTolerance))
            return OutputState.Shortfall;

        return setCps > DiminishingReturnsCps ? OutputState.OverDriven : OutputState.Matching;
    }

    /// <summary>The sentence shown under the delivered rate.</summary>
    public static string Verdict(OutputState state, double setCps, double deliveredCps) => state switch
    {
        OutputState.Idle =>
            "Start clicking to measure what the game actually receives.",

        OutputState.Shortfall =>
            $"Your {setCps:0.0} setting is really sending {deliveredCps:0.0}. "
            + "Lowering the slider until these match costs you nothing and makes the rate honest.",

        OutputState.OverDriven =>
            $"Landing every click, but above ~{DiminishingReturnsCps:0} /s the extra ones stop becoming extra hits. "
            + "The Measured preset is the fastest rate that still counted.",

        _ => "Matching the setting, inside the range the server keeps up with."
    };

    /// <summary>
    /// Whether the verdict is worth colouring. A line that is always lit stops
    /// being read, so only the two states asking for a change are accented.
    /// </summary>
    public static bool IsWarning(OutputState state) =>
        state is OutputState.Shortfall or OutputState.OverDriven;
}
