using System.Collections.Generic;
using System.Linq;

namespace JinxyClicker;

/// <summary>
/// The Lower Ping (Plus) bundle: the network tweaks worth turning on together to
/// stop Windows adding its own latency.
/// </summary>
/// <remarks>
/// Same reversible <see cref="PcTweak"/> contract as <see cref="GamingMode"/> —
/// each records its prior value before changing anything, and the one switch
/// reverts only the tweaks it actually turned on, leaving anything the user had
/// set by hand alone. Ordered strongest-effect first.
///
/// Honest about the ceiling: these stop Windows throttling, batching and
/// background-uploading on your own machine. None of them can shorten the
/// physical path to the server, so on an idle, uncongested connection there is
/// nothing for them to clear.
/// </remarks>
public sealed class LowerPing
{
    private readonly IReadOnlyList<PcTweak> _tweaks;

    public LowerPing(IReadOnlyList<PcTweak>? tweaks = null) => _tweaks = tweaks ?? Curated();

    public IReadOnlyList<PcTweak> Tweaks => _tweaks;

    /// <summary>The tweaks the switch applies, strongest-effect first.</summary>
    public static IReadOnlyList<PcTweak> Curated() => new PcTweak[]
    {
        new NetworkThrottlingTweak(),      // real; needs admin
        new NagleTweak(),                  // real for small-packet games; needs admin
        new WindowsUpdateSharingTweak(),   // real while Update is active; needs admin
        new BackgroundStoreAppsTweak(),    // small, steady; no admin
        new WifiPowerTweak()               // Wi-Fi only; no admin
    };

    /// <summary>The ids in the bundle, for guarding the composition in tests.</summary>
    public IEnumerable<string> Ids => _tweaks.Select(t => t.Id);

    /// <summary>The ids that only take effect when the app is running as admin.</summary>
    public IEnumerable<string> AdminIds => _tweaks.Where(t => t.RequiresAdmin).Select(t => t.Id);
}
