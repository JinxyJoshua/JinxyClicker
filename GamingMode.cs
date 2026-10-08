using System.Collections.Generic;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// The one-switch performance bundle: the subset of <see cref="PcTweak"/> worth
/// turning on together for a real, felt gain, with nothing marginal or risky in it.
/// </summary>
/// <remarks>
/// Deliberately a curated list, not "every tweak there is". The tweaks whose own
/// honest Impact text admits they rarely do anything — QoS ("depends entirely on
/// your router"), SysMain ("little or none on an SSD") — or that trade a reboot
/// for a coin-flip — GPU scheduling ("mixed — reboot required") — are left off, so
/// switching Gaming Mode on is never a worse deal than the user was promised. The
/// Tracking Helper is left off too: it changes how the mouse feels, which is a
/// personal choice rather than a performance win, and it keeps its own control.
///
/// Everything in here is reversible, because every <see cref="PcTweak"/> records
/// the prior value before it changes anything. Gaming Mode reverts only the tweaks
/// it actually turned on, so one the user had already applied by hand is left alone
/// when the switch goes off.
/// </remarks>
public sealed class GamingMode
{
    private readonly IReadOnlyList<PcTweak> _tweaks;

    public GamingMode(IReadOnlyList<PcTweak>? tweaks = null) => _tweaks = tweaks ?? Curated();

    public IReadOnlyList<PcTweak> Tweaks => _tweaks;

    /// <summary>The tweaks the switch applies, strongest-effect first.</summary>
    public static IReadOnlyList<PcTweak> Curated() => new PcTweak[]
    {
        new HighPerformancePlanTweak(),       // real; no admin
        new FullscreenOptimizationsTweak(),   // real, the one most likely felt; no admin
        new GameDvrTweak(),                   // real on some machines; no admin
        new VisualEffectsTweak(),             // small, helps weak GPUs; no admin
        new TransparencyTweak(),              // small but real; no admin
        new CoreParkingTweak(),               // real; needs admin
        new PowerThrottlingTweak()            // safe; needs admin
    };

    /// <summary>The ids in the bundle, for guarding the composition in tests.</summary>
    public IEnumerable<string> Ids => _tweaks.Select(t => t.Id);

    /// <summary>The ids that only take effect when the app is running as admin.</summary>
    public IEnumerable<string> AdminIds => _tweaks.Where(t => t.RequiresAdmin).Select(t => t.Id);
}
