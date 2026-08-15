using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace MyBlinkStyleClicker;

/// <summary>
/// Remembers what each setting was before a tweak changed it, so Revert restores
/// what the machine actually had rather than a guessed default.
/// </summary>
public sealed class TweakState
{
    private const string STATE_FILE = "tweak_state.json";

    /// <summary>Tweak id to prior value. A null value means "did not exist".</summary>
    public Dictionary<string, string?> Previous { get; set; } = new();

    public static TweakState Load()
    {
        try
        {
            if (!File.Exists(STATE_FILE)) return new TweakState();
            return JsonSerializer.Deserialize<TweakState>(File.ReadAllText(STATE_FILE)) ?? new TweakState();
        }
        catch
        {
            return new TweakState();
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(STATE_FILE,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Remember(string id, string? value) => Previous[id] = value;

    public bool TryTake(string id, out string? value) => Previous.TryGetValue(id, out value);
}

public static class TweakEnvironment
{
    public static bool IsElevated
    {
        get
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Relaunches this executable through the UAC prompt.</summary>
    public static bool RestartElevated()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (exe == null) return false;

            Process.Start(new ProcessStartInfo(exe) { Verb = "runas", UseShellExecute = true });
            return true;
        }
        catch
        {
            // The user declined the prompt, or the shell refused.
            return false;
        }
    }
}

internal static class PowerCfg
{
    public static string Run(string arguments)
    {
        var info = new ProcessStartInfo("powercfg", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process? process = Process.Start(info);
        if (process == null) return string.Empty;

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        return output;
    }

    public static Guid? ActiveScheme()
    {
        Match match = Regex.Match(Run("/getactivescheme"), "([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})");
        return match.Success && Guid.TryParse(match.Groups[1].Value, out Guid guid) ? guid : null;
    }
}

public abstract class PcTweak : INotifyPropertyChanged
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>Honest assessment, shown on the card. Not marketing.</summary>
    public abstract string Impact { get; }

    public abstract bool RequiresAdmin { get; }

    private bool? _isApplied;

    /// <summary>Null when the setting could not be read at all.</summary>
    public bool? IsApplied
    {
        get => _isApplied;
        private set
        {
            _isApplied = value;
            Raise(nameof(IsApplied));
            Raise(nameof(StatusText));
            Raise(nameof(ActionText));
            Raise(nameof(CanAct));
        }
    }

    public string StatusText
    {
        get
        {
            if (IsApplied == null) return "Could not read this setting";

            if (RequiresAdmin && !TweakEnvironment.IsElevated)
            {
                return IsApplied.Value
                    ? "Applied — you have to restart as admin to revert this"
                    : "You have to restart as admin to apply this";
            }

            return IsApplied.Value ? "Applied" : "Not applied";
        }
    }

    public string ActionText => IsApplied == true ? "Revert" : "Apply";

    public bool CanAct => IsApplied != null && (!RequiresAdmin || TweakEnvironment.IsElevated);

    public void Refresh()
    {
        try
        {
            IsApplied = ReadApplied();
        }
        catch
        {
            IsApplied = null;
        }
    }

    /// <summary>Applies or reverts, whichever the current state calls for.</summary>
    /// <returns>An error message, or null on success.</returns>
    public string? Toggle(TweakState state)
    {
        try
        {
            if (IsApplied == true) DoRevert(state);
            else DoApply(state);

            state.Save();
            Refresh();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            Refresh();
            return $"{Name}: Windows refused the change. Restart as administrator and try again.";
        }
        catch (Exception ex)
        {
            Refresh();
            return $"{Name}: {ex.Message}";
        }
    }

    protected abstract bool? ReadApplied();
    protected abstract void DoApply(TweakState state);
    protected abstract void DoRevert(TweakState state);

    protected void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class HighPerformancePlanTweak : PcTweak
{
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    public override string Id => "high-performance-plan";
    public override string Name => "High Performance power plan";
    public override string Description => "Stops Windows dropping your clock speed when it thinks you are idle.";
    public override string Impact => "Real effect";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        Guid? active = PowerCfg.ActiveScheme();
        return active == null ? null : active == HighPerformance;
    }

    protected override void DoApply(TweakState state)
    {
        Guid? current = PowerCfg.ActiveScheme();
        if (current != null) state.Remember(Id, current.Value.ToString());

        string output = PowerCfg.Run($"/setactive {HighPerformance}");

        if (PowerCfg.ActiveScheme() == HighPerformance) return;

        // Windows 11 hides this plan on many machines. Duplicating it produces a
        // fresh GUID, which is the one that can actually be activated.
        Match copy = Regex.Match(PowerCfg.Run($"-duplicatescheme {HighPerformance}"),
            "([0-9a-fA-F]{8}-[0-9a-fA-F-]{27})");

        if (copy.Success) PowerCfg.Run($"/setactive {copy.Groups[1].Value}");

        if (ReadAppliedByName() != true)
            throw new InvalidOperationException($"Windows would not activate the plan. {output.Trim()}");
    }

    protected override void DoRevert(TweakState state)
    {
        if (state.TryTake(Id, out string? previous) && Guid.TryParse(previous, out Guid guid))
            PowerCfg.Run($"/setactive {guid}");
        else
            PowerCfg.Run("/setactive SCHEME_BALANCED");
    }

    /// <summary>A duplicated plan keeps the name but not the well-known GUID.</summary>
    private static bool? ReadAppliedByName()
    {
        Guid? active = PowerCfg.ActiveScheme();
        if (active == null) return null;
        if (active == HighPerformance) return true;

        return PowerCfg.Run("/getactivescheme").Contains("High performance", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Minimum-processor-cores, driven through the registry rather than
/// powercfg aliases. Windows ships this setting hidden (Attributes = 1), and
/// while hidden `powercfg -q` prints nothing for it — so query-based detection
/// silently reports "unreadable" on a stock machine.
/// </summary>
public sealed class CoreParkingTweak : PcTweak
{
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string MinimumCores = "0cc5b647-c1df-4637-891a-dec35c318583";

    private const string AttributeKey =
        @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\" + SubProcessor + @"\" + MinimumCores;

    public override string Id => "core-parking";
    public override string Name => "Keep all CPU cores awake";
    public override string Description => "Stops Windows parking cores, which can cause a brief stutter when work lands on one. Also unhides the setting in Windows' own power options.";
    public override string Impact => "Real effect";
    public override bool RequiresAdmin => true;

    private static string? SchemeValuePath()
    {
        Guid? scheme = PowerCfg.ActiveScheme();

        return scheme == null
            ? null
            : $@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{scheme}\{SubProcessor}\{MinimumCores}";
    }

    protected override bool? ReadApplied()
    {
        string? path = SchemeValuePath();
        if (path == null) return null;

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path);

        // No override recorded means the scheme is on the Windows default.
        if (key == null) return false;

        return key.GetValue("ACSettingIndex") is int index && index == 100;
    }

    protected override void DoApply(TweakState state)
    {
        string path = SchemeValuePath()
            ?? throw new InvalidOperationException("Could not read the active power scheme.");

        using (RegistryKey? existing = Registry.LocalMachine.OpenSubKey(path))
        {
            state.Remember(Id, existing?.GetValue("ACSettingIndex") is int index ? index.ToString() : null);
        }

        using (RegistryKey? attributes = Registry.LocalMachine.OpenSubKey(AttributeKey, writable: true))
        {
            attributes?.SetValue("Attributes", 2, RegistryValueKind.DWord);
        }

        using RegistryKey key = Registry.LocalMachine.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException("Could not open the power scheme key.");

        key.SetValue("ACSettingIndex", 100, RegistryValueKind.DWord);
        key.SetValue("DCSettingIndex", 100, RegistryValueKind.DWord);

        Reactivate();
    }

    protected override void DoRevert(TweakState state)
    {
        string? path = SchemeValuePath();
        if (path == null) return;

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
        {
            key.SetValue("ACSettingIndex", previous, RegistryValueKind.DWord);
            key.SetValue("DCSettingIndex", previous, RegistryValueKind.DWord);
        }
        else
        {
            // There was no override before, so remove ours instead of writing a
            // zero that was never actually there.
            key.DeleteValue("ACSettingIndex", throwOnMissingValue: false);
            key.DeleteValue("DCSettingIndex", throwOnMissingValue: false);
        }

        Reactivate();
    }

    /// <summary>Power scheme edits only take effect once the scheme is re-applied.</summary>
    private static void Reactivate()
    {
        Guid? scheme = PowerCfg.ActiveScheme();
        if (scheme != null) PowerCfg.Run($"/setactive {scheme}");
    }
}

/// <summary>
/// "Enhance pointer precision". Windows scales cursor travel by how fast the
/// mouse moves, so identical hand movement produces different in-game turns.
/// For anything aim-related this is a correctness problem, not a speed one.
/// </summary>
/// <summary>
/// The two Windows mouse settings that affect whether identical hand movement
/// produces identical in-game turn.
/// </summary>
/// <remarks>
/// Deliberately only two things. Polling rate lives in the mouse firmware or
/// driver, DPI lives in the device, and in-game sensitivity lives in the game —
/// none of the three is reachable from here, whatever other tools imply.
/// </remarks>
public sealed class TrackingHelperTweak : PcTweak
{
    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    /// <summary>The 6/11 notch — the only pointer speed Windows passes through unscaled.</summary>
    private const int LinearPointerSpeed = 10;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoRef(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoPtr(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    public override string Id => "tracking-helper";
    public override string Name => "Tracking Helper";
    public override string Description => "Turns off Enhance pointer precision, and puts pointer speed on the 6/11 notch — the only one Windows passes through 1:1. The same hand movement then always turns the same amount.";
    public override string Impact => "Aim consistency";
    public override bool RequiresAdmin => false;

    private static int[]? ReadAcceleration()
    {
        int[] values = new int[3];
        return SystemParametersInfo(SPI_GETMOUSE, 0, values, 0) ? values : null;
    }

    private static int? ReadSpeed()
    {
        int speed = 0;
        return SystemParametersInfoRef(SPI_GETMOUSESPEED, 0, ref speed, 0) ? speed : null;
    }

    protected override bool? ReadApplied()
    {
        int[]? acceleration = ReadAcceleration();
        int? speed = ReadSpeed();

        if (acceleration == null || speed == null) return null;

        // acceleration[2] is the enable flag; zero means straight 1:1 input.
        return acceleration[2] == 0 && speed == LinearPointerSpeed;
    }

    protected override void DoApply(TweakState state)
    {
        int[] acceleration = ReadAcceleration()
            ?? throw new InvalidOperationException("Could not read the mouse settings.");

        int speed = ReadSpeed() ?? LinearPointerSpeed;

        state.Remember(Id, $"{acceleration[0]},{acceleration[1]},{acceleration[2]},{speed}");

        WriteAcceleration(new[] { 0, 0, 0 });
        WriteSpeed(LinearPointerSpeed);
    }

    protected override void DoRevert(TweakState state)
    {
        // Windows' own defaults, used only if nothing was recorded.
        int[] acceleration = { 6, 10, 1 };
        int speed = LinearPointerSpeed;

        if (state.TryTake(Id, out string? stored) && stored != null)
        {
            string[] parts = stored.Split(',');

            if (parts.Length == 4
                && int.TryParse(parts[0], out int t1)
                && int.TryParse(parts[1], out int t2)
                && int.TryParse(parts[2], out int accel)
                && int.TryParse(parts[3], out int previousSpeed))
            {
                acceleration = new[] { t1, t2, accel };
                speed = previousSpeed;
            }
        }

        WriteAcceleration(acceleration);
        WriteSpeed(speed);
    }

    /// <summary>Writes and applies in one call, so no sign-out is needed.</summary>
    private static void WriteAcceleration(int[] values)
    {
        if (!SystemParametersInfo(SPI_SETMOUSE, 0, values, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
            throw new InvalidOperationException("Windows refused the pointer precision change.");
    }

    private static void WriteSpeed(int speed)
    {
        if (!SystemParametersInfoPtr(SPI_SETMOUSESPEED, 0, new IntPtr(speed),
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
        {
            throw new InvalidOperationException("Windows refused the pointer speed change.");
        }
    }
}

public sealed class GameDvrTweak : PcTweak
{
    private const string ConfigStore = @"System\GameConfigStore";
    private const string CaptureKey = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";

    public override string Id => "game-dvr";
    public override string Name => "Disable Game DVR recording";
    public override string Description => "Turns off the background recording that Windows arms for games. It costs frames on some machines and nothing on others.";
    public override string Impact => "Real effect on some machines";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ConfigStore);
        if (key == null) return false;
        return key.GetValue("GameDVR_Enabled") is int value && value == 0;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey store = Registry.CurrentUser.CreateSubKey(ConfigStore, writable: true)
            ?? throw new InvalidOperationException("Could not open the game configuration key.");

        state.Remember(Id, store.GetValue("GameDVR_Enabled") is int existing ? existing.ToString() : null);
        store.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);

        using RegistryKey capture = Registry.CurrentUser.CreateSubKey(CaptureKey, writable: true)!;
        capture.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? store = Registry.CurrentUser.OpenSubKey(ConfigStore, writable: true);

        if (store != null)
        {
            if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
                store.SetValue("GameDVR_Enabled", previous, RegistryValueKind.DWord);
            else
                store.DeleteValue("GameDVR_Enabled", throwOnMissingValue: false);
        }

        using RegistryKey? capture = Registry.CurrentUser.OpenSubKey(CaptureKey, writable: true);
        capture?.SetValue("AppCaptureEnabled", 1, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Desktop transparency. The compositor blurs whatever sits behind translucent
/// surfaces every frame; switching it off is a small but genuine saving.
/// </summary>
public sealed class TransparencyTweak : PcTweak
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public override string Id => "transparency";
    public override string Name => "Disable transparency effects";
    public override string Description => "Stops Windows blurring behind translucent panels. Small, steady saving — most noticeable on integrated graphics.";
    public override string Impact => "Small but real";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        if (key == null) return false;
        return key.GetValue("EnableTransparency") is int value && value == 0;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the personalisation key.");

        state.Remember(Id, key.GetValue("EnableTransparency") is int existing ? existing.ToString() : null);
        key.SetValue("EnableTransparency", 0, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: true);
        if (key == null) return;

        int previous = state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int value)
            ? value
            : 1;

        key.SetValue("EnableTransparency", previous, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Window animations, shadows and fades. Cheap on a strong GPU, worth having on
/// a weak one, and it takes work off the desktop compositor either way.
/// </summary>
public sealed class VisualEffectsTweak : PcTweak
{
    private const string VisualFxKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string DesktopKey = @"Control Panel\Desktop";
    private const int BestPerformance = 2;

    public override string Id => "visual-effects";
    public override string Name => "Visual effects for best performance";
    public override string Description => "Turns off window animations, fades and shadows. Frees desktop compositor work; the desktop will look plainer.";
    public override string Impact => "Small, helps weak GPUs";
    public override bool RequiresAdmin => false;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(VisualFxKey);
        if (key == null) return false;
        return key.GetValue("VisualFXSetting") is int value && value == BestPerformance;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(VisualFxKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the visual effects key.");

        state.Remember(Id, key.GetValue("VisualFXSetting") is int existing ? existing.ToString() : null);
        key.SetValue("VisualFXSetting", BestPerformance, RegistryValueKind.DWord);

        using RegistryKey desktop = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true)!;
        state.Remember(Id + "-drag", desktop.GetValue("DragFullWindows") as string);
        desktop.SetValue("DragFullWindows", "0", RegistryValueKind.String);
    }

    protected override void DoRevert(TweakState state)
    {
        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(VisualFxKey, writable: true))
        {
            if (key != null)
            {
                // 0 is "let Windows choose", the shipping default.
                int previous = state.TryTake(Id, out string? stored) && stored != null
                               && int.TryParse(stored, out int value)
                    ? value
                    : 0;

                key.SetValue("VisualFXSetting", previous, RegistryValueKind.DWord);
            }
        }

        using RegistryKey? desktop = Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true);
        if (desktop == null) return;

        string restored = state.TryTake(Id + "-drag", out string? dragged) && dragged != null ? dragged : "1";
        desktop.SetValue("DragFullWindows", restored, RegistryValueKind.String);
    }
}

/// <summary>
/// A policy-based QoS rule that marks Roblox's outbound packets as expedited.
/// </summary>
/// <remarks>
/// Honest about what this is: DSCP is a request, not a guarantee. The marking
/// only changes anything if the router — and every hop after it — is configured
/// to honour it, and most consumer gear ignores or strips it outright. Even
/// where honoured it only matters while a link is congested; on an idle
/// connection it cannot lower a ping that has nothing queued behind it.
/// </remarks>
public sealed class QosPolicyTweak : PcTweak
{
    private const string PolicyRoot = @"SOFTWARE\Policies\Microsoft\Windows\QoS";
    private const string PolicyName = "JinxyClicker Roblox";
    private const string RobloxExecutable = "RobloxPlayerBeta.exe";

    /// <summary>46 is Expedited Forwarding, the standard marking for latency-sensitive traffic.</summary>
    private const string ExpeditedForwarding = "46";

    public override string Id => "qos-policy";
    public override string Name => "Prioritise Roblox traffic";
    public override string Description => "Adds a Windows QoS policy marking Roblox's packets as high priority. Only does anything if your router honours the marking and the connection is congested.";
    public override string Impact => "Depends entirely on your router";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{PolicyRoot}\{PolicyName}");
        return key?.GetValue("DSCP Value") as string == ExpeditedForwarding;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey($@"{PolicyRoot}\{PolicyName}", writable: true)
            ?? throw new InvalidOperationException("Could not create the QoS policy key.");

        // Nothing of the user's is being overwritten — the policy is created
        // under our own name — so there is no prior value worth recording.
        state.Remember(Id, null);

        key.SetValue("Version", "1.0", RegistryValueKind.String);
        key.SetValue("Application Name", RobloxExecutable, RegistryValueKind.String);
        key.SetValue("Protocol", "*", RegistryValueKind.String);
        key.SetValue("Local Port", "*", RegistryValueKind.String);
        key.SetValue("Local IP", "*", RegistryValueKind.String);
        key.SetValue("Local IP Prefix Length", "*", RegistryValueKind.String);
        key.SetValue("Remote Port", "*", RegistryValueKind.String);
        key.SetValue("Remote IP", "*", RegistryValueKind.String);
        key.SetValue("Remote IP Prefix Length", "*", RegistryValueKind.String);
        key.SetValue("DSCP Value", ExpeditedForwarding, RegistryValueKind.String);

        // -1 means no bandwidth cap; this policy only marks, never throttles.
        key.SetValue("Throttle Rate", "-1", RegistryValueKind.String);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? root = Registry.LocalMachine.OpenSubKey(PolicyRoot, writable: true);

        try
        {
            root?.DeleteSubKeyTree(PolicyName, throwOnMissingSubKey: false);
        }
        catch
        {
            // Already gone.
        }
    }
}

public sealed class GpuSchedulingTweak : PcTweak
{
    private const string GraphicsKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string ValueName = "HwSchMode";

    public override string Id => "gpu-scheduling";
    public override string Name => "Hardware-accelerated GPU scheduling";
    public override string Description => "Hands more scheduling to the GPU. Results genuinely go both ways depending on card and driver, and it needs a reboot to take effect.";
    public override string Impact => "Mixed — reboot required";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(GraphicsKey);
        if (key == null) return null;
        return key.GetValue(ValueName) is int value && value == 2;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(GraphicsKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the graphics drivers key.");

        state.Remember(Id, key.GetValue(ValueName) is int existing ? existing.ToString() : null);
        key.SetValue(ValueName, 2, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(GraphicsKey, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int previous))
            key.SetValue(ValueName, previous, RegistryValueKind.DWord);
        else
            key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }
}

public sealed class SysMainTweak : PcTweak
{
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\SysMain";
    private const int Disabled = 4;

    public override string Id => "sysmain";
    public override string Name => "Disable Superfetch (SysMain)";
    public override string Description => "Turns off the prefetch service. On an SSD this gains little, and Microsoft recommends leaving it on.";
    public override string Impact => "Little or none on an SSD";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ServiceKey);
        if (key?.GetValue("Start") is not int start) return null;
        return start == Disabled;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKey, writable: true)
            ?? throw new InvalidOperationException("The SysMain service is not present on this machine.");

        if (key.GetValue("Start") is int start) state.Remember(Id, start.ToString());

        key.SetValue("Start", Disabled, RegistryValueKind.DWord);
        RunService("stop");
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(ServiceKey, writable: true)
            ?? throw new InvalidOperationException("The SysMain service is not present on this machine.");

        // 2 is the shipping default: automatic start.
        int previous = state.TryTake(Id, out string? stored) && int.TryParse(stored, out int value) ? value : 2;

        key.SetValue("Start", previous, RegistryValueKind.DWord);
        RunService("start");
    }

    private static void RunService(string verb)
    {
        try
        {
            var info = new ProcessStartInfo("sc", $"{verb} SysMain")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using Process? process = Process.Start(info);
            process?.WaitForExit(10_000);
        }
        catch
        {
            // The registry change is what persists across a reboot; failing to
            // stop or start the service right now is not fatal.
        }
    }
}

public sealed class PowerThrottlingTweak : PcTweak
{
    private const string ThrottleKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string ValueName = "PowerThrottlingOff";

    public override string Id => "power-throttling";
    public override string Name => "Disable power throttling";
    public override string Description => "Stops Windows throttling background apps. A focused game is not being throttled anyway.";
    public override string Impact => "Only affects background apps";
    public override bool RequiresAdmin => true;

    protected override bool? ReadApplied()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ThrottleKey);
        return key?.GetValue(ValueName) is int value && value == 1;
    }

    protected override void DoApply(TweakState state)
    {
        using RegistryKey key = Registry.LocalMachine.CreateSubKey(ThrottleKey, writable: true)
            ?? throw new InvalidOperationException("Could not open the power throttling key.");

        // Null records "this value did not exist", so Revert removes it rather
        // than inventing a zero that was never there.
        state.Remember(Id, key.GetValue(ValueName) is int existing ? existing.ToString() : null);

        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
    }

    protected override void DoRevert(TweakState state)
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(ThrottleKey, writable: true);
        if (key == null) return;

        if (state.TryTake(Id, out string? stored) && stored != null && int.TryParse(stored, out int value))
            key.SetValue(ValueName, value, RegistryValueKind.DWord);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
