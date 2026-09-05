using System;
using System.Globalization;

namespace JinxyClicker;

/// <summary>
/// The words on the in-game overlay.
/// </summary>
/// <remarks>
/// Separated from the window so what it says can be tested without a screen.
///
/// <b>What is deliberately not here: a hit rate.</b> The app synthesises input
/// and reads nothing back from the game — it has no way to know whether a click
/// became a hit, and inventing a number would be worse than showing none. What
/// it can report is what it actually sent and how evenly, which is the half of
/// hit registration the app is responsible for. If the rate is right and the
/// timing is steady and hits still are not landing, that is ping or the server,
/// and the overlay saying so is more useful than a fabricated percentage.
/// </remarks>
public static class OverlayReadout
{
    /// <summary>Above this many stalls, the run is visibly rough.</summary>
    public const int RoughStalls = 3;

    /// <summary>Jitter past this stops being tidy, in milliseconds.</summary>
    public const double LooseJitterMs = 3.0;

    /// <summary>The big number: what is actually reaching the game.</summary>
    public static string Headline(TimingStats stats) =>
        stats.Samples == 0
            ? "—"
            : stats.DeliveredCps.ToString("0.0", CultureInfo.CurrentCulture) + " CPS";

    /// <summary>
    /// The line underneath: how even the clicking is.
    /// </summary>
    /// <remarks>
    /// Consistency rather than speed, because speed is already the headline and
    /// evenness is the part people cannot feel. A stall is a gap far longer than
    /// the typical one — the moments where clicks silently go missing.
    /// </remarks>
    public static string Detail(TimingStats stats)
    {
        if (stats.Samples == 0) return "waiting for the first clicks";

        string jitter = "±" + stats.JitterMs.ToString("0.0", CultureInfo.CurrentCulture) + " ms";

        if (stats.Stalls == 0)
            return stats.JitterMs <= LooseJitterMs ? $"steady  {jitter}" : $"uneven  {jitter}";

        string stalls = stats.Stalls == 1 ? "1 stall" : $"{stats.Stalls} stalls";

        return $"{jitter}  ·  {stalls}";
    }

    /// <summary>
    /// Whether the line should be coloured as a warning.
    /// </summary>
    /// <remarks>
    /// Only when something is actually wrong. An overlay that is always lit
    /// stops being read, and this one sits over a game.
    /// </remarks>
    public static bool IsWarning(TimingStats stats) =>
        stats.Samples > 0 && (stats.Stalls >= RoughStalls || stats.JitterMs > LooseJitterMs * 2);

    /// <summary>
    /// A note when the rate is past what the game converts into hits.
    /// </summary>
    /// <remarks>
    /// Empty most of the time. It exists because a slider set to 180 delivers
    /// the same as one set to 34, and someone watching a number climb while
    /// nothing improves has no way to know that from inside the game.
    /// </remarks>
    public static string RateNote(double deliveredCps) =>
        deliveredCps > ClickOutput.DiminishingReturnsCps
            ? $"above ~{ClickOutput.MeasuredBestCps:0}/s, extra clicks stop becoming hits"
            : "";
}
