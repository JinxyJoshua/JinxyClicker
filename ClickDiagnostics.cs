using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace JinxyClicker;

/// <summary>What the clicker actually did, as opposed to what it was asked to do.</summary>
/// <param name="Samples">Gaps measured. Zero means nothing has been clicked yet.</param>
/// <param name="DeliveredCps">Clicks per second actually sent over the window.</param>
/// <param name="MedianMs">The typical gap between clicks.</param>
/// <param name="WorstMs">The longest single gap. This is the one that loses hits.</param>
/// <param name="JitterMs">
/// Spread of the gaps, as the mean distance from the median. Consistency is what
/// hit registration depends on, and an average alone hides it completely.
/// </param>
/// <param name="Stalls">Gaps far longer than the median — moments the loop was not running.</param>
public sealed record TimingStats(
    int Samples, double DeliveredCps, double MedianMs, double WorstMs, double JitterMs, int Stalls);

/// <summary>
/// Measures the clicker's real timing so a report can be a measurement rather
/// than a feeling.
/// </summary>
/// <remarks>
/// "Hit registration is inconsistent" is a symptom with many possible causes,
/// and none of them are visible from inside a running app. The average rate is
/// no help: a loop delivering a click every 30 ms and one alternating 5 ms and
/// 55 ms have the same average and behave completely differently in a game.
/// What matters is the spread and the worst case, so those are what this keeps.
///
/// Recording is deliberately close to free — one timestamp appended to a fixed
/// ring buffer, no allocation, no locking beyond a cheap swap. Anything heavier
/// would change the timing it is supposed to be observing.
/// </remarks>
public static class ClickDiagnostics
{
    /// <summary>
    /// How many gaps are kept. At 40 CPS this is roughly the last twelve
    /// seconds, which is long enough to catch a stall and short enough to still
    /// describe what is happening right now.
    /// </summary>
    public const int Window = 512;

    /// <summary>A gap this many times the median is a stall, not jitter.</summary>
    public const double StallFactor = 3.0;

    private static readonly long[] Stamps = new long[Window];
    private static int _count;
    private static int _next;
    private static readonly object Gate = new();

    /// <summary>Records that a click was delivered. Called from the click loop.</summary>
    public static void RecordClick()
    {
        long now = Stopwatch.GetTimestamp();

        lock (Gate)
        {
            Stamps[_next] = now;
            _next = (_next + 1) % Window;
            if (_count < Window) _count++;
        }
    }

    /// <summary>Forgets everything, so a new run is not judged by the last one.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _count = 0;
            _next = 0;
        }
    }

    /// <summary>The gaps between recorded clicks, oldest first, in milliseconds.</summary>
    public static List<double> Gaps()
    {
        long[] ordered;

        lock (Gate)
        {
            if (_count < 2) return new List<double>();

            ordered = new long[_count];

            // The buffer wraps, so the oldest entry is not always at zero.
            int start = _count < Window ? 0 : _next;

            for (int i = 0; i < _count; i++) ordered[i] = Stamps[(start + i) % Window];
        }

        double perTick = 1000.0 / Stopwatch.Frequency;

        var gaps = new List<double>(ordered.Length - 1);

        for (int i = 1; i < ordered.Length; i++)
        {
            double gap = (ordered[i] - ordered[i - 1]) * perTick;

            // A non-positive gap means the clock went backwards, which it can
            // on some machines. Not a measurement, so not counted as one.
            if (gap > 0) gaps.Add(gap);
        }

        return gaps;
    }

    public static TimingStats Current() => Summarise(Gaps());

    /// <summary>
    /// Turns raw gaps into the numbers worth reading.
    /// </summary>
    /// <remarks>
    /// Median rather than mean throughout: one 600 ms stall drags a mean far
    /// enough to make a healthy loop look broken, and a broken one look merely
    /// untidy. The stall count carries that information instead, where it can
    /// be read as what it is.
    /// </remarks>
    public static TimingStats Summarise(IReadOnlyList<double> gaps)
    {
        if (gaps.Count == 0) return new TimingStats(0, 0, 0, 0, 0, 0);

        List<double> sorted = gaps.OrderBy(g => g).ToList();

        double median = sorted[sorted.Count / 2];
        double worst = sorted[^1];
        double total = gaps.Sum();

        double jitter = gaps.Sum(g => Math.Abs(g - median)) / gaps.Count;

        int stalls = gaps.Count(g => g > median * StallFactor);

        // Over the window actually measured, not over a nominal second.
        double delivered = total > 0 ? gaps.Count * 1000.0 / total : 0;

        return new TimingStats(gaps.Count, delivered, median, worst, jitter, stalls);
    }
}
