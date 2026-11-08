using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>How the rotation's hotkey behaves: a press toggles it, or it runs
/// only while the key is held.</summary>
public enum SwitchTrigger { Toggle, Hold }

/// <summary>One slot in the rotation: a key, and how long to stay on it.</summary>
public sealed record RotationSlot(string Key, int DelayMs);

/// <summary>
/// The auto switcher as a single rotation chain: an ordered list of key+delay
/// slots cycled by one hotkey, with a trigger mode and an optional "stop after N
/// rotations" limit.
/// </summary>
/// <remarks>
/// Replaces the old two-slot A/B swap and the multi-profile list with the one
/// rotation people actually asked for. Immutable like <see cref="KeyMacro"/>: the
/// chain is rebuilt on every edit, so a running rotation never holds a
/// half-changed one. The rotation itself is just a <see cref="KeyMacro"/> with the
/// slot keys and per-slot dwells, so the key-sending engine is shared with macros.
/// </remarks>
public sealed record RotationChain(
    IReadOnlyList<RotationSlot> Slots,
    SwitchTrigger Trigger,
    int StopAfterRotations,
    bool Enabled = true)
{
    /// <summary>The reserved name the macro engine runs the rotation under.</summary>
    public const string EngineName = " AutoSwitcher";

    /// <summary>At least this many slots are needed for a rotation to mean anything.</summary>
    public const int MinSlots = 2;

    public static RotationChain Empty =>
        new(Array.Empty<RotationSlot>(), SwitchTrigger.Toggle, 0);

    /// <summary>The slots that actually carry a key; blank rows are ignored.</summary>
    public IReadOnlyList<RotationSlot> FilledSlots =>
        Slots.Where(s => !string.IsNullOrWhiteSpace(s.Key)).ToList();

    public bool IsUsable => Problem() == null;

    /// <summary>What stops this running, or null when nothing does.</summary>
    public string? Problem()
    {
        List<RotationSlot> filled = FilledSlots.ToList();

        if (filled.Count < MinSlots)
            return $"Add at least {MinSlots} slots, each with a key.";

        foreach (RotationSlot slot in filled)
        {
            (int[] Keys, string Text)? parsed = MacroStore.ParseKeys(slot.Key);
            if (parsed == null || parsed.Value.Keys.Length != 1)
                return "Each slot needs one letter or digit.";

            if (slot.DelayMs < KeyMacro.MinIntervalMs || slot.DelayMs > KeyMacro.MaxIntervalMs)
                return $"Delays must be between {KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.";
        }

        return null;
    }

    /// <summary>This chain as the macro the engine runs, or null when unusable.</summary>
    public KeyMacro? ToMacro()
    {
        if (Problem() != null) return null;

        List<RotationSlot> filled = FilledSlots.ToList();

        int[] keys = filled.Select(s => MacroStore.ParseKeys(s.Key)!.Value.Keys[0]).ToArray();
        string text = string.Join(", ", filled.Select(s => s.Key.Trim().ToUpperInvariant()));
        int[] holds = filled.Select(s => s.DelayMs).ToArray();

        // No hotkey on the macro itself: the rotation is started and stopped by
        // the page's global switcher hotkey, not by the macro-hotkey poll.
        return new KeyMacro(
            EngineName, keys, text,
            intervalMs: holds[0],
            holdsMs: holds,
            maxRotations: Math.Max(0, StopAfterRotations));
    }
}

/// <summary>
/// Where the single rotation chain is kept, in its own small file beside the
/// other lists.
/// </summary>
public static class RotationChainStore
{
    private static readonly string FILE = SettingsPath.For("rotation.json");

    private sealed class StoredSlot
    {
        public string Key { get; set; } = "";
        public int DelayMs { get; set; } = 100;
    }

    private sealed class Stored
    {
        public List<StoredSlot> Slots { get; set; } = new();
        public string Trigger { get; set; } = "Toggle";
        public int StopAfterRotations { get; set; }
        // Stored as "Disabled" so a file written without the field reads as enabled.
        public bool Disabled { get; set; }
    }

    public static bool Exists() => File.Exists(FILE);

    public static RotationChain Load()
    {
        try
        {
            if (!File.Exists(FILE)) return RotationChain.Empty;

            Stored? s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FILE));
            if (s == null) return RotationChain.Empty;

            return new RotationChain(
                s.Slots.Select(x => new RotationSlot(x.Key, x.DelayMs)).ToList(),
                Enum.TryParse(s.Trigger, out SwitchTrigger t) ? t : SwitchTrigger.Toggle,
                Math.Max(0, s.StopAfterRotations),
                Enabled: !s.Disabled);
        }
        catch
        {
            return RotationChain.Empty;
        }
    }

    public static void Save(RotationChain chain)
    {
        try
        {
            var stored = new Stored
            {
                Slots = chain.Slots.Select(s => new StoredSlot { Key = s.Key, DelayMs = s.DelayMs }).ToList(),
                Trigger = chain.Trigger.ToString(),
                StopAfterRotations = chain.StopAfterRotations,
                Disabled = !chain.Enabled
            };

            File.WriteAllText(FILE, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
