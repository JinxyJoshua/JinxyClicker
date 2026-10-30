using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace JinxyClicker;

// The network-latency tweaks behind the Lower Ping (Plus) feature. Each is a
// standard, documented Windows setting, written through the same reversible
// PcTweak contract as the Optimizations page: the prior value is recorded before
// anything changes, so Revert restores what the machine actually had.
//
// None of these touch the game or the network path itself — they only stop
// Windows from adding its own latency (throttling, batching, power-saving,
// background uploads). Honest about the ceiling: on an idle, uncongested link
// there is nothing queued for them to clear.

/// <summary>
/// NetworkThrottlingIndex. Windows paces multimedia and network work to ~10
/// packets/ms by default, which is exactly the kind of pacing a twitchy game
/// does not want. 0xffffffff disables it.
/// </summary>
public sealed class NetworkThrottlingTweak : PcTweak
{
    private const string Key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string ValueName = "NetworkThrottlingIndex";
    private const int Off = unchecked((int)0xFFFFFFFF);
    private const int Default = 10;

    public override string Id => "net-throttling";
    public override string Name => "Disable network throttling";
    public override string Description => "Windows paces network traffic to about 10 packets per millisecond for background multimedia. Turning it off lets a game send as fast as it wants.";
    public override string Impact => "Real effect";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(Key);
        if (key == null) return null;
        return key.GetValue(ValueName) is int value && value == Off;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(Key, writable: true)
            ?? throw new InvalidOperationException("Could not open the multimedia system profile key.");

        state.Remember(Id, key.GetValue(ValueName) is int existing ? existing.ToString() : null);
        key.SetValue(ValueName, Off, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(Key, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
            key.SetValue(ValueName, previous, RegistryValueKind.DWord);
        else
            // The value ships present with a value of 10, so restore that rather
            // than deleting a value Windows expects to find.
            key.SetValue(ValueName, Default, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Nagle's algorithm, off. Nagle holds small outbound packets back to coalesce
/// them, trading a little latency for a little bandwidth — the wrong trade for a
/// game. Disabled per network interface via TcpAckFrequency + TCPNoDelay.
/// </summary>
public sealed class NagleTweak : PcTweak
{
    private const string InterfacesKey =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string AckFrequency = "TcpAckFrequency";
    private const string NoDelay = "TCPNoDelay";

    public override string Id => "nagle";
    public override string Name => "Send small packets at once";
    public override string Description => "Turns off Nagle's algorithm, which holds small packets back to bundle them. Games want each input sent the instant it happens.";
    public override string Impact => "Real for small-packet games";
    public override bool RequiresAdmin => true;

    /// <summary>Interface subkeys that currently hold an IP address — the live ones.</summary>
    private static IEnumerable<string> ActiveInterfaces()
    {
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(InterfacesKey);
        if (root == null) yield break;

        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? sub = root.OpenSubKey(name);
            if (sub == null) continue;

            string dhcp = sub.GetValue("DhcpIPAddress") as string ?? "";
            string[] stat = sub.GetValue("IPAddress") as string[] ?? Array.Empty<string>();

            bool hasDhcp = dhcp.Length > 0 && dhcp != "0.0.0.0";
            bool hasStatic = stat.Any(a => !string.IsNullOrEmpty(a) && a != "0.0.0.0");

            if (hasDhcp || hasStatic) yield return name;
        }
    }

    protected override bool? ReadApplied()
    {
        List<string> active = ActiveInterfaces().ToList();
        if (active.Count == 0) return null;

        // Applied only when every live interface has both values set — a partial
        // state reads as "not applied" so toggling it on completes the set.
        foreach (string name in active)
        {
            using RegistryKey? sub = Registry.LocalMachine.OpenSubKey($@"{InterfacesKey}\{name}");
            bool ack = sub?.GetValue(AckFrequency) is int a && a == 1;
            bool nd = sub?.GetValue(NoDelay) is int n && n == 1;
            if (!ack || !nd) return false;
        }
        return true;
    }

    protected override void DoApply(TweakState state)
    {
        List<string> touched = new();

        foreach (string name in ActiveInterfaces())
        {
            using RegistryKey? sub = Registry.LocalMachine.OpenSubKey($@"{InterfacesKey}\{name}", writable: true);
            if (sub == null) continue;

            state.Remember($"{Id}:{name}:ack", sub.GetValue(AckFrequency) is int a ? a.ToString() : null);
            state.Remember($"{Id}:{name}:nd", sub.GetValue(NoDelay) is int n ? n.ToString() : null);

            sub.SetValue(AckFrequency, 1, RegistryValueKind.DWord);
            sub.SetValue(NoDelay, 1, RegistryValueKind.DWord);
            touched.Add(name);
        }

        if (touched.Count == 0)
            throw new InvalidOperationException("No active network interface to apply this to.");

        // The list of interfaces we changed, so Revert touches exactly those even
        // if the live set changes later.
        state.Remember(Id, string.Join(",", touched));
    }

    protected override void DoRevert(TweakState state)
    {
        if (!state.TryTake(Id, out string? list) || list == null) return;

        foreach (string name in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            using RegistryKey? sub = Registry.LocalMachine.OpenSubKey($@"{InterfacesKey}\{name}", writable: true);
            if (sub == null) continue;

            Restore(sub, state, $"{Id}:{name}:ack", AckFrequency);
            Restore(sub, state, $"{Id}:{name}:nd", NoDelay);
        }
    }

    // These values do not exist on a stock interface, so the usual revert is to
    // delete them; a recorded prior value is restored instead.
    private static void Restore(RegistryKey sub, TweakState state, string stateKey, string valueName)
    {
        if (state.TryTake(stateKey, out string? stored) && stored != null && int.TryParse(stored, out int previous))
            sub.SetValue(valueName, previous, RegistryValueKind.DWord);
        else
            sub.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Stops the Wi-Fi adapter dozing between packets, by setting its power-saving
/// mode to Maximum Performance on the active power plan. No effect on a wired
/// connection; costs a little battery on a laptop.
/// </summary>
public sealed class WifiPowerTweak : PcTweak
{
    // Power Saving Mode setting under the Wireless Adapter Settings sub-group.
    private const string SubWireless = "19cbb8fa-5279-450e-9fac-8a3d5fedd0c1";
    private const string PowerSavingMode = "12bbebe6-58d6-4636-95bb-3217ef867c1a";
    private const string MaxPerformance = "0";

    public override string Id => "wifi-power";
    public override string Name => "Wi-Fi at full power";
    public override string Description => "Stops the Wi-Fi adapter saving power between packets, which can add bursts of latency. Nothing to do on a wired PC; a little more battery on a laptop.";
    public override string Impact => "Wi-Fi only";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        // Read the active scheme's current AC index for this setting.
        string output = PowerCfg.Run($"/q SCHEME_CURRENT {SubWireless} {PowerSavingMode}");
        Match ac = Regex.Match(output, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
        if (!ac.Success) return null;   // setting not present (no wireless support)
        return long.TryParse(ac.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out long value)
               && value == 0;
    }

    protected override void DoApply(TweakState state)
    {
        string output = PowerCfg.Run($"/q SCHEME_CURRENT {SubWireless} {PowerSavingMode}");
        Match ac = Regex.Match(output, @"Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)");
        state.Remember(Id, ac.Success ? ac.Groups[1].Value : null);

        PowerCfg.Run($"/setacvalueindex SCHEME_CURRENT {SubWireless} {PowerSavingMode} {MaxPerformance}");
        PowerCfg.Run($"/setdcvalueindex SCHEME_CURRENT {SubWireless} {PowerSavingMode} {MaxPerformance}");
        PowerCfg.Run("/setactive SCHEME_CURRENT");

        if (ReadApplied() != true)
            throw new InvalidOperationException("Windows would not change the Wi-Fi power setting. It may need administrator.");
    }

    protected override void DoRevert(TweakState state)
    {
        // 1 = Medium Power Saving, the Windows default on AC for most plans; a
        // recorded prior value wins.
        string previous = "1";
        if (state.TryTake(Id, out string? stored) && stored != null
            && long.TryParse(stored, System.Globalization.NumberStyles.HexNumber, null, out long value))
        {
            previous = value.ToString();
        }

        PowerCfg.Run($"/setacvalueindex SCHEME_CURRENT {SubWireless} {PowerSavingMode} {previous}");
        PowerCfg.Run($"/setdcvalueindex SCHEME_CURRENT {SubWireless} {PowerSavingMode} {previous}");
        PowerCfg.Run("/setactive SCHEME_CURRENT");
    }
}

/// <summary>
/// Delivery Optimization peer sharing, off. Windows Update can upload updates you
/// already have to other PCs over your connection; this stops it stealing your
/// upstream mid-game.
/// </summary>
public sealed class WindowsUpdateSharingTweak : PcTweak
{
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization";
    private const string ValueName = "DODownloadMode";
    private const int HttpOnly = 0;   // no peer-to-peer upload or download

    public override string Id => "wudo-off";
    public override string Name => "Stop sharing Windows updates";
    public override string Description => "Windows Update can upload updates to other PCs over your connection. This stops it using your upstream bandwidth while you play.";
    public override string Impact => "Real when Update is active";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        if (key == null) return false;
        return key.GetValue(ValueName) is int value && value == HttpOnly;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the Delivery Optimization policy key.");

        state.Remember(Id, key.GetValue(ValueName) is int existing ? existing.ToString() : null);
        key.SetValue(ValueName, HttpOnly, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
            key.SetValue(ValueName, previous, RegistryValueKind.DWord);
        else
            // No policy was set before, so remove ours and let Windows' own
            // default (peer sharing on the local network) stand.
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

/// <summary>
/// Background activity for Store apps, off. Store apps sync and refresh on their
/// own schedule; this stops them doing it mid-game. They still work normally when
/// opened.
/// </summary>
public sealed class BackgroundStoreAppsTweak : PcTweak
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string ValueName = "GlobalUserDisabled";

    public override string Id => "bg-store-apps";
    public override string Name => "Stop background Store apps";
    public override string Description => "Store apps stop syncing and refreshing in the background. They still work the moment you open them.";
    public override string Impact => "Small, steady";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Key);
        if (key == null) return false;
        return key.GetValue(ValueName) is int value && value == 1;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(Key, writable: true)
            ?? throw new InvalidOperationException("Could not open the background applications key.");

        state.Remember(Id, key.GetValue(ValueName) is int existing ? existing.ToString() : null);
        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Key, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
            key.SetValue(ValueName, previous, RegistryValueKind.DWord);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
