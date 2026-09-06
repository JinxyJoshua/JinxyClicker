using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JinxyClicker;

/// <summary>
/// One named auto switcher: two hotbar slots and the timing between them.
/// </summary>
/// <remarks>
/// There was one switcher, configured in place on its own page. People run more
/// than one setup — sword and bow in a fight, pickaxe and gumdrop while
/// gathering — and swapping the numbers by hand between them is the thing this
/// replaces.
///
/// Immutable, like <see cref="KeyMacro"/>, and for the same reason: the list is
/// rebuilt on every edit, so a running switcher can never be holding a
/// half-changed one.
/// </remarks>
public sealed record SwitcherProfile(
    string Name,
    string SlotA,
    string SlotB,
    int HoldFirstMs,
    int HoldSecondMs,
    int EquipMs,
    HotkeyBinding Hotkey,
    bool Enabled = true)
{
    /// <summary>How many clicks a swap waits for before moving on.</summary>
    /// <remarks>
    /// Two, matching the single switcher this replaces. Kept as a name rather
    /// than a literal so the reason it is not one survives: a single click can
    /// land during the equip animation and do nothing.
    /// </remarks>
    public const int Shots = 2;

    /// <summary>
    /// The name the macro engine runs this under.
    /// </summary>
    /// <remarks>
    /// Prefixed with a space, which no typed name can start with once trimmed.
    /// The single switcher already relied on that trick to stay out of the
    /// macro list; with several of them the prefix has to carry the name too,
    /// or two switchers would be one macro overwriting each other.
    /// </remarks>
    public string EngineName => " AutoSwitcher:" + Name;

    /// <summary>Whether this is complete enough to run.</summary>
    public bool IsUsable => Problem() == null;

    /// <summary>
    /// What stops this running, or null when nothing does.
    /// </summary>
    /// <remarks>
    /// Returns the sentence rather than a flag, because the card has to say
    /// which of the three fields is wrong. A profile with an empty slot and a
    /// silent card is the failure this is written to avoid.
    /// </remarks>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "Give this switcher a name.";

        (int[] Keys, string Text)? keys = MacroStore.ParseKeys(SlotA.Trim() + "," + SlotB.Trim());

        if (keys == null || keys.Value.Keys.Length != 2)
            return "Both slots need one letter or digit.";

        if (!InRange(HoldFirstMs) || !InRange(HoldSecondMs))
            return $"Hold times must be between {KeyMacro.MinIntervalMs} and {KeyMacro.MaxIntervalMs} ms.";

        return null;
    }

    private static bool InRange(int ms) => ms >= KeyMacro.MinIntervalMs && ms <= KeyMacro.MaxIntervalMs;

    /// <summary>
    /// This profile as the macro the engine actually runs.
    /// </summary>
    /// <param name="clickPeriodMs">
    /// The clicker's current period. The first hold is raised to whatever
    /// guarantees a shot lands after the equip animation, so changing CPS
    /// changes this without anyone having to know it would.
    /// </param>
    /// <returns>Null when the profile could not run.</returns>
    public KeyMacro? ToMacro(double clickPeriodMs)
    {
        if (Problem() != null) return null;

        (int[] Keys, string Text) keys =
            MacroStore.ParseKeys(SlotA.Trim() + "," + SlotB.Trim())!.Value;

        int firstHold = Math.Max(HoldFirstMs, KeyMacro.MinimumDwellMs(clickPeriodMs, EquipMs));

        return new KeyMacro(
            EngineName, keys.Keys, keys.Text, firstHold,
            new[] { firstHold, HoldSecondMs },
            clicksWanted: Shots,
            equipMs: EquipMs);
    }

    /// <summary>
    /// The first hold this will really use, which can be longer than the one
    /// typed. Shown on the card so the raise is visible rather than surprising.
    /// </summary>
    public int EffectiveFirstHoldMs(double clickPeriodMs) =>
        Math.Max(HoldFirstMs, KeyMacro.MinimumDwellMs(clickPeriodMs, EquipMs));

    /// <summary>A one-line description for the card.</summary>
    public string Summary() =>
        $"{SlotA.Trim()} ↔ {SlotB.Trim()}   ·   {HoldFirstMs} ms then {HoldSecondMs} ms";
}

/// <summary>
/// Where the named switchers are kept.
/// </summary>
/// <remarks>
/// A file of its own rather than a corner of app_settings.json, matching how
/// macros, presets and saved wheels are stored — a list that grows belongs in
/// its own file, so a parse failure loses the list and not every setting.
/// </remarks>
public static class SwitcherStore
{
    private static readonly string FILE = SettingsPath.For("switchers.json");

    private sealed class Stored
    {
        public string Name { get; set; } = "";
        public string SlotA { get; set; } = "";
        public string SlotB { get; set; } = "";
        public int HoldFirstMs { get; set; } = 21;
        public int HoldSecondMs { get; set; } = 1300;
        public int EquipMs { get; set; } = 5;
        public int HotkeyVk { get; set; }
        public string HotkeyName { get; set; } = "";

        // Stored as "Disabled" so a file written before this existed, where the
        // field is absent and a missing bool reads false, means enabled.
        public bool Disabled { get; set; }
    }

    /// <summary>Nothing. The list starts empty and is filled by migration or by hand.</summary>
    public static List<SwitcherProfile> Defaults() => new();

    /// <summary>Whether anything has been saved here yet.</summary>
    public static bool Exists() => File.Exists(FILE);

    /// <summary>
    /// The single switcher, as the first entry of the list that replaces it.
    /// </summary>
    /// <remarks>
    /// Everyone upgrading has a switcher configured on the old card, and losing
    /// it to a new feature would be the app taking something away in exchange
    /// for the thing it added. Runs once, when no list has been saved yet.
    ///
    /// Its hotkey comes across too, so the key that started the switcher still
    /// starts it — under a name now, but the same key.
    /// </remarks>
    public static SwitcherProfile FromSingle(AppSettings settings, HotkeyBinding hotkey) =>
        new(
            "Switcher",
            settings.SwitcherSlotA,
            settings.SwitcherSlotB,
            settings.SwitcherIntervalMs,
            settings.SwitcherIntervalBMs,
            settings.SwitcherEquipMs,
            hotkey,
            Enabled: !settings.SwitcherDisabled);

    public static List<SwitcherProfile> Load()
    {
        try
        {
            if (!File.Exists(FILE)) return Defaults();

            var stored = JsonSerializer.Deserialize<List<Stored>>(File.ReadAllText(FILE));
            if (stored == null) return Defaults();

            return stored
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new SwitcherProfile(
                    s.Name, s.SlotA, s.SlotB, s.HoldFirstMs, s.HoldSecondMs, s.EquipMs,
                    s.HotkeyVk == 0
                        ? HotkeyBinding.Unbound
                        : new HotkeyBinding(s.HotkeyVk, HotkeyBinding.Describe(s.HotkeyVk)),
                    Enabled: !s.Disabled))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<SwitcherProfile> switchers)
    {
        try
        {
            File.WriteAllText(FILE, JsonSerializer.Serialize(
                switchers.Select(s => new Stored
                {
                    Name = s.Name,
                    SlotA = s.SlotA,
                    SlotB = s.SlotB,
                    HoldFirstMs = s.HoldFirstMs,
                    HoldSecondMs = s.HoldSecondMs,
                    EquipMs = s.EquipMs,
                    HotkeyVk = s.Hotkey.VirtualKey,
                    HotkeyName = s.Hotkey.Name,
                    Disabled = !s.Enabled
                }),
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Adds a switcher, or replaces the one already using that name.
    /// </summary>
    /// <remarks>
    /// Names are the identity here — the engine runs each under its name, and
    /// two switchers sharing one would be a single macro started twice. Saving
    /// over a name is therefore an edit rather than a duplicate, which is also
    /// what somebody typing a name they already used means by it.
    /// </remarks>
    public static void Upsert(List<SwitcherProfile> switchers, SwitcherProfile profile)
    {
        int at = switchers.FindIndex(s =>
            string.Equals(s.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        if (at >= 0) switchers[at] = profile;
        else switchers.Add(profile);
    }

    /// <summary>A name not already taken, for a new switcher.</summary>
    public static string UnusedName(IEnumerable<SwitcherProfile> switchers, string wanted = "Switcher")
    {
        var taken = switchers
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!taken.Contains(wanted)) return wanted;

        for (int n = 2; n < 1000; n++)
        {
            string candidate = $"{wanted} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }

        return wanted + " " + Guid.NewGuid().ToString("N")[..4];
    }
}
