using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace JinxyClicker;

/// <summary>
/// A key, or a short cycle of keys, sent over and over on a timer.
/// </summary>
/// <remarks>
/// One shape covers both features people ask for. A macro that spams a single
/// key is the macro creator; a macro that alternates two keys is the inventory
/// switcher — sword to crossbow, pickaxe to gumdrop. Building the general one
/// gets the specific one for nothing, and the alternative was two engines that
/// drift apart.
/// </remarks>
public sealed class KeyMacro
{
    /// <summary>Fastest a macro may repeat. Below this it is a key that never comes up.</summary>
    public const int MinIntervalMs = 5;

    /// <summary>Slowest worth offering — beyond this a hotkey is easier.</summary>
    public const int MaxIntervalMs = 10_000;

    public KeyMacro(string name, IEnumerable<int> keys, string keysText, int intervalMs,
                    int[]? holdsMs = null, int clicksWanted = 0, int equipMs = DefaultEquipMs,
                    HotkeyBinding? hotkey = null)
    {
        Name = name;
        Keys = keys.Where(k => k is > 0 and < 256).ToArray();
        KeysText = keysText;
        IntervalMs = Math.Clamp(intervalMs, MinIntervalMs, MaxIntervalMs);

        HoldsMs = holdsMs?.Select(h => Math.Clamp(h, MinIntervalMs, MaxIntervalMs)).ToArray();

        ClicksWanted = Math.Max(0, clicksWanted);
        EquipMs = Math.Clamp(equipMs, 0, MaxIntervalMs);

        Hotkey = hotkey ?? HotkeyBinding.Unbound;
    }

    /// <summary>
    /// Toggles this macro on and off when pressed. Unbound means no shortcut —
    /// the switch on the card is the only way to start it.
    /// </summary>
    /// <remarks>
    /// The same type the global hotkeys use, so the polling loop, rebind flow,
    /// and collision check all treat a macro's toggle as one more binding
    /// rather than a parallel thing they had to learn about.
    /// </remarks>
    public HotkeyBinding Hotkey { get; }

    /// <summary>
    /// Clicks the first key must actually receive before the cycle moves on.
    /// </summary>
    /// <remarks>
    /// The point this feature turns on. A dwell measured in milliseconds is a
    /// guess about someone else's frame rate, equip animation and click rate,
    /// and it is wrong in both directions at once — too short and the weapon
    /// never fires, too long and it sits in your hand while the sword should
    /// be swinging.
    ///
    /// Counting the clicks the clicker has actually delivered replaces the
    /// guess with the thing the guess was approximating. The dip lasts exactly
    /// as long as it takes to fire and not one tick longer, whatever the CPS
    /// happens to be.
    ///
    /// Zero means time only, which is what an ordinary macro wants.
    /// </remarks>
    public int ClicksWanted { get; }

    /// <summary>Time to allow for the weapon to appear before clicks count.</summary>
    public int EquipMs { get; }

    /// <summary>
    /// How long to stay on each key, when they should not be equal.
    /// </summary>
    /// <remarks>
    /// The switcher needs this and an even rotation cannot give it. Half a
    /// second on the crossbow and half on the sword means the crossbow is in
    /// hand half the time and re-drawn on every return — which is why rotating
    /// fires slower than switching once by hand.
    ///
    /// Measurement turned this around. A recording of it done by hand shows the
    /// sword held for one to two seconds and the crossbow dipped to for about a
    /// sixth of one — the opposite of the guess this was built on. Null means
    /// every key gets IntervalMs, which is what an ordinary macro wants.
    /// </remarks>
    public int[]? HoldsMs { get; }

    /// <summary>How long to wait after pressing the key at this position.</summary>
    public int DwellFor(int index) =>
        HoldsMs != null && index < HoldsMs.Length ? HoldsMs[index] : IntervalMs;

    /// <summary>
    /// How long a weapon must stay in hand to be guaranteed to fire.
    /// </summary>
    /// <remarks>
    /// Swapping to a weapon does not arm it. Roblox plays an equip animation
    /// first, and a click that lands during it does nothing — so a rotation
    /// faster than the animation produces a weapon that is drawn over and over
    /// and never fired.
    ///
    /// Derived rather than guessed. The equip is a property of the game; the
    /// click period is a property of the clicker, which this app already knows.
    /// Two clicks rather than one because the first can land in the same
    /// instant the equip completes and be swallowed by the boundary — the
    /// second is what makes "every time" true instead of "usually".
    /// </remarks>
    public static int MinimumDwellMs(double clickPeriodMs, int equipMs = DefaultEquipMs, int clicks = 2)
    {
        if (double.IsNaN(clickPeriodMs) || clickPeriodMs <= 0) clickPeriodMs = 100;

        double needed = equipMs + clickPeriodMs * clicks;

        return (int)Math.Clamp(Math.Ceiling(needed), MinIntervalMs, MaxIntervalMs);
    }

    /// <summary>
    /// How long Roblox takes to put a weapon in your hand.
    /// </summary>
    /// <remarks>
    /// Measured off a recording of someone doing it by hand: the crossbow was
    /// dipped to for 130 to 200 milliseconds and fired reliably every time, so
    /// the equip has to cost well under that. An earlier guess of 250 was wrong
    /// by roughly four times and would have forced a rotation slower than the
    /// hand it was meant to copy.
    /// </remarks>
    public const int DefaultEquipMs = 60;

    public string Name { get; }

    /// <summary>Virtual key codes, sent in order and then from the top again.</summary>
    public int[] Keys { get; }

    /// <summary>The keys as the user typed them, for the card and for editing.</summary>
    public string KeysText { get; }

    public int IntervalMs { get; }

    public bool IsUsable => Keys.Length > 0 && Name.Trim().Length > 0;

    /// <summary>What the card says under the name.</summary>
    public string RateText => IntervalMs >= 1000
        ? $"every {IntervalMs / 1000.0:0.##}s"
        : $"every {IntervalMs} ms";

    public string SummaryText => Keys.Length > 1
        ? $"{KeysText}  ·  {RateText}  ·  cycles"
        : $"{KeysText}  ·  {RateText}";
}

/// <summary>
/// Sends the keys, on its own thread, until told to stop.
/// </summary>
/// <remarks>
/// A thread per running macro rather than one scheduler. Nobody runs twenty of
/// these — two is the realistic maximum — and a scheduler would be more code
/// to get wrong for no benefit anyone would notice.
/// </remarks>
public sealed class MacroRunner : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _running = new();

    private long _sent;

    public bool IsRunning(string name) => _running.ContainsKey(name);

    public int RunningCount => _running.Count;

    /// <summary>How many key presses have actually gone out.</summary>
    /// <remarks>
    /// Counts sends, not ticks. A macro that is running but suppressed reads
    /// zero here, which is the difference between "it is not working" and "it
    /// is working and you are looking at the wrong window".
    /// </remarks>
    public long Sent => Interlocked.Read(ref _sent);

    /// <summary>
    /// Asked before every press. True means skip this one.
    /// </summary>
    /// <remarks>
    /// This exists because a macro types into whatever is focused, and while
    /// somebody is setting one up that is this application — the keys land in
    /// the very boxes being edited, whose change handler restarts the macro,
    /// and it spends its life fighting itself instead of reaching the game.
    ///
    /// Called on the macro thread, so whatever is behind it must be safe to
    /// call from anywhere.
    /// </remarks>
    public Func<bool>? Suppressed { get; set; }

    /// <summary>
    /// The clicker's input gate, so a key never lands inside a click.
    /// </summary>
    /// <remarks>
    /// The same lock the shake engine takes, and for the same reason. The
    /// clicker holds it between a mouse-down and its release; anything injected
    /// in that window arrives mid-click, and the game sees an interrupted press
    /// rather than a click and a keystroke.
    ///
    /// It is why switching by hand fires fast and switching automatically does
    /// not: a human presses the key between clicks by luck of timing, and a
    /// timer lands wherever it lands.
    /// </remarks>
    public object? InputGate { get; set; }

    /// <summary>
    /// How long a key is held down.
    /// </summary>
    /// <remarks>
    /// The same problem HitFix solves for the mouse. A press that goes down and
    /// up in the same instant falls between two frames of a game reading input
    /// once a frame, and is never observed at all — the switch either does not
    /// happen or happens unreliably. Fifteen milliseconds clears a 60fps frame
    /// and is far too short to notice.
    /// </remarks>
    private const int HoldMs = 15;

    /// <summary>Longest to wait for a click to finish before going anyway.</summary>
    /// <remarks>
    /// Sent regardless on timeout, deliberately. A split click costs one click;
    /// a missed switch leaves the wrong weapon in hand, which costs the fight.
    /// </remarks>
    private const int GateWaitMs = 250;

    public void Start(KeyMacro macro)
    {
        if (!macro.IsUsable || _running.ContainsKey(macro.Name)) return;

        var cts = new CancellationTokenSource();
        _running[macro.Name] = cts;

        CancellationToken token = cts.Token;

        new Thread(() => Loop(macro, token))
        {
            IsBackground = true,
            Name = "Macro:" + macro.Name
        }.Start();
    }

    public void Stop(string name)
    {
        if (!_running.TryGetValue(name, out CancellationTokenSource? cts)) return;

        cts.Cancel();
        _running.Remove(name);
    }

    public void StopAll()
    {
        foreach (CancellationTokenSource cts in _running.Values) cts.Cancel();

        _running.Clear();
    }

    private void Loop(KeyMacro macro, CancellationToken token)
    {
        int at = 0;

        // Same 15.6 ms -> 1 ms move the click loop makes, and for the same
        // reason. Without it a sleep cannot land nearer than a scheduler tick,
        // which is longer than the whole crossbow dip this is trying to time.
        // Raised here rather than borrowed from the clicker so a rotation is
        // accurate whether or not clicking happens to be running.
        bool raisedTimer = TimeBeginPeriod(TimerResolutionMs) == 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Suppressed rather than paused: the cycle still advances, so
                // alt-tabbing away and back does not leave it stuck on one key.
                if (Suppressed?.Invoke() != true)
                {
                    SendGated(macro.Keys[at], token);
                    Interlocked.Increment(ref _sent);
                }

                int dwell = macro.DwellFor(at);
                bool firing = at == 0 && macro.ClicksWanted > 0 && Clicks != null;

                at = (at + 1) % macro.Keys.Length;

                if (firing) WaitForShots(macro, dwell, token);
                else Wait(dwell, token);
            }
        }
        catch
        {
            // A failed send must not take the thread — or the app — with it.
        }
        finally
        {
            if (raisedTimer) TimeEndPeriod(TimerResolutionMs);
        }
    }

    /// <summary>Milliseconds of system timer resolution asked for while running.</summary>
    private const uint TimerResolutionMs = 1;

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    /// <summary>
    /// The clicker's running total of delivered clicks.
    /// </summary>
    /// <remarks>
    /// Read, never written. It is what lets a dip end when the weapon has
    /// actually fired rather than when a stopwatch says it probably has.
    /// </remarks>
    public Func<long>? Clicks { get; set; }

    /// <summary>
    /// Waits out a span accurately, rather than approximately.
    /// </summary>
    /// <remarks>
    /// Thread.Sleep returns when the scheduler next gets round to it, not when
    /// the time is up. Even with the timer at 1 ms each call overshoots, and a
    /// dwell slept in slices compounds every one of them — a 150 ms crossbow
    /// dip built from thirty 5 ms sleeps measured 165-180 ms, long enough to
    /// cost the swing it was supposed to leave time for.
    ///
    /// So the bulk is slept a millisecond at a time and the last stretch is
    /// spun. How long that stretch needs to be depends on the system timer,
    /// which this cannot assume anything about — the loop raises it to 1 ms,
    /// but a caller outside that, a test included, gets the ~15.6 ms default.
    /// So the tail is not a constant: each sleep is measured, and the longest
    /// one seen becomes the distance at which sleeping stops being safe. A
    /// coarse timer teaches it on the first sleep and costs one overshoot.
    /// </remarks>
    /// <returns>False if cancellation was observed.</returns>
    internal static bool Wait(double ms, CancellationToken token)
    {
        if (ms <= 0) return !token.IsCancellationRequested;

        long freq = Stopwatch.Frequency;
        long deadline = Stopwatch.GetTimestamp() + (long)(ms * freq / 1000.0);
        double tail = SpinTailMs;

        while (true)
        {
            if (token.IsCancellationRequested) return false;

            long now = Stopwatch.GetTimestamp();
            double remaining = (deadline - now) * 1000.0 / freq;
            if (remaining <= 0) return true;

            if (remaining <= tail)
            {
                Thread.SpinWait(40);
                continue;
            }

            Thread.Sleep(1);

            // What that sleep actually cost, which is the floor on how close a
            // sleep can get us. Anything nearer than this has to be spun.
            double slept = (Stopwatch.GetTimestamp() - now) * 1000.0 / freq;
            if (slept > tail) tail = slept;
        }
    }

    /// <summary>
    /// Shortest stretch spun rather than slept, before measurement widens it.
    /// </summary>
    private const double SpinTailMs = 1.2;

    /// <summary>
    /// Stays on the weapon until it has actually been clicked, then leaves.
    /// </summary>
    /// <remarks>
    /// As short as it can be while still firing, which is the whole point. Time
    /// on the crossbow is time the sword is not swinging, and this game is
    /// scored in hits — so the dip ends on the click that fires it rather than
    /// on a timer that has to be generous to be safe.
    ///
    /// The configured hold is a ceiling, not a target. If the clicks never
    /// arrive — clicker switched off, a click rate slower than the hold — it
    /// gives up and moves on rather than parking on one weapon for ever.
    /// </remarks>
    private void WaitForShots(KeyMacro macro, int ceilingMs, CancellationToken token)
    {
        if (!Wait(macro.EquipMs, token)) return;

        long from = Clicks?.Invoke() ?? 0;
        int remaining = Math.Max(0, ceilingMs - macro.EquipMs);
        long deadline = Stopwatch.GetTimestamp() + (long)(remaining * Stopwatch.Frequency / 1000.0);

        // Polled fine enough that the dip ends on the click rather than up to a
        // slice after it. At 40 clicks a second they arrive 25 ms apart, so a
        // millisecond of detection lag is the difference between leaving on the
        // shot and leaving a slice late, every single swap.
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (token.IsCancellationRequested) return;

            if ((Clicks?.Invoke() ?? 0) - from >= macro.ClicksWanted) return;

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Presses the key without splitting a click in half.
    /// </summary>
    /// <remarks>
    /// Takes the clicker's gate if there is one, so the whole press and release
    /// happens between two clicks rather than inside one. Waiting costs a few
    /// milliseconds on a switch that happens twice a second; not waiting costs
    /// the click it lands in the middle of.
    /// </remarks>
    private void SendGated(int virtualKey, CancellationToken token)
    {
        uint scan = MapVirtualKey((uint)virtualKey, MapVkToVsc);

        Gated(() => Send(virtualKey, scan, up: false));

        // Outside the gate. The clicker is free to click while a key is held —
        // that is just clicking with a key down, which is what a hand does.
        Wait(HoldMs, token);

        Gated(() => Send(virtualKey, scan, up: true));
    }

    /// <summary>
    /// Runs one send between clicks rather than inside one.
    /// </summary>
    /// <remarks>
    /// Around each individual event, never across the hold between them. Held
    /// for the whole press this blocks the click loop for the full hold time on
    /// every swap — three percent of clicks at a half-second rotation, and a
    /// quarter of them at the fast rates the switch technique actually wants.
    /// A feature that costs hits in a game measured in hits is worse than no
    /// feature, so the lock is taken for microseconds and released.
    /// </remarks>
    private void Gated(Action send)
    {
        object? gate = InputGate;

        if (gate == null)
        {
            send();
            return;
        }

        bool held = Monitor.TryEnter(gate, GateWaitMs);

        try
        {
            send();
        }
        finally
        {
            if (held) Monitor.Exit(gate);
        }
    }

    /// <summary>Presses and releases one key.</summary>
    /// <remarks>
    /// Scan code alongside the virtual key. Games routinely read scan codes
    /// rather than virtual keys, and an event carrying only the latter is
    /// ignored by exactly the software this exists for.
    ///
    /// Held for a real moment between the two, rather than sent as one pair.
    /// A key that goes down and up in the same instant lands between two frames
    /// of a game polling input once a frame, and is never seen.
    /// </remarks>
    private static void Press(int virtualKey, CancellationToken token)
    {
        uint scan = MapVirtualKey((uint)virtualKey, MapVkToVsc);

        Send(virtualKey, scan, up: false);

        // Cancellable, so stopping mid-press does not wait out the hold — and
        // the key is released either way by what follows.
        Wait(HoldMs, token);

        Send(virtualKey, scan, up: true);
    }

    private static void Send(int virtualKey, uint scan, bool up)
    {
        var input = new INPUT[1];

        input[0].type = InputKeyboard;
        input[0].U.ki = new KEYBDINPUT
        {
            wVk = (ushort)virtualKey,
            wScan = (ushort)scan,
            dwFlags = up ? KeyEventUp : 0
        };

        SendInput((uint)input.Length, input, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => StopAll();

    private const uint InputKeyboard = 1;
    private const uint KeyEventUp = 0x0002;
    private const uint MapVkToVsc = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <remarks>
    /// MOUSEINPUT is declared here despite nothing using it, and it is
    /// load-bearing. A union is as large as its largest member, and SendInput
    /// rejects the call outright if the size it is handed does not match the
    /// real INPUT — silently, by returning zero. Leaving the mouse member out
    /// makes the struct eight bytes short on x64 and nothing is ever sent.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

/// <summary>
/// The saved macros, kept beside the click presets.
/// </summary>
public static class MacroStore
{
    private static readonly string MACROS_FILE = SettingsPath.For("macros.json");

    private sealed class StoredMacro
    {
        public string Name { get; set; } = "";
        public int[] Keys { get; set; } = Array.Empty<int>();
        public string KeysText { get; set; } = "";
        public int IntervalMs { get; set; } = 100;
        // Zero means unbound. Absent from files written before this feature
        // shipped, which defaults them to unbound on load — no migration.
        public int HotkeyVk { get; set; }
        public string HotkeyName { get; set; } = "";
    }

    /// <summary>
    /// Nothing. A fresh install starts with an empty list.
    /// </summary>
    /// <remarks>
    /// Shipping example macros was a mistake worth naming: they arrive looking
    /// like features the app provides rather than things the user made, and the
    /// first instinct is to delete them. A macro is only useful if it is one
    /// somebody chose.
    /// </remarks>
    public static List<KeyMacro> Defaults() => new();

    /// <summary>
    /// The auto switcher used to live in this list before it became its own
    /// page. Anyone who ran that build has it saved, and it would show up twice.
    /// </summary>
    private static bool IsLegacySwitcher(string name) =>
        name.Trim().Equals("Auto Switcher", StringComparison.OrdinalIgnoreCase);

    public static List<KeyMacro> Load()
    {
        try
        {
            if (!File.Exists(MACROS_FILE)) return Defaults();

            var stored = JsonSerializer.Deserialize<List<StoredMacro>>(File.ReadAllText(MACROS_FILE));
            if (stored == null) return Defaults();

            return stored
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Where(m => !IsLegacySwitcher(m.Name))
                .Select(m => new KeyMacro(
                    m.Name, m.Keys, m.KeysText, m.IntervalMs,
                    hotkey: m.HotkeyVk > 0 ? new HotkeyBinding(m.HotkeyVk, m.HotkeyName) : HotkeyBinding.Unbound))
                .ToList();
        }
        catch
        {
            return Defaults();
        }
    }

    public static void Save(IEnumerable<KeyMacro> macros)
    {
        try
        {
            var stored = macros
                .Select(m => new StoredMacro
                {
                    Name = m.Name,
                    Keys = m.Keys,
                    KeysText = m.KeysText,
                    IntervalMs = m.IntervalMs,
                    HotkeyVk = m.Hotkey.VirtualKey,
                    HotkeyName = m.Hotkey.Name
                })
                .ToList();

            File.WriteAllText(MACROS_FILE,
                JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Adds a macro, or replaces the one that already has that name.</summary>
    public static void Upsert(List<KeyMacro> macros, KeyMacro macro)
    {
        int existing = macros.FindIndex(m =>
            string.Equals(m.Name, macro.Name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) macros[existing] = macro;
        else macros.Add(macro);
    }

    /// <summary>
    /// Reads keys typed as "1, 2" or "R" into virtual key codes.
    /// </summary>
    /// <remarks>
    /// Letters and digits only, which covers every hotbar slot and every action
    /// key in the game this is for. Accepting the whole keyboard would mean
    /// parsing "Left Shift" and deciding what a macro that holds a modifier
    /// even means.
    /// </remarks>
    public static (int[] Keys, string Text)? ParseKeys(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return null;

        var keys = new List<int>();
        var names = new List<string>();

        // The separators as an explicit array. Written as Split(',', ' ', options)
        // it binds to the (char, int count, options) overload instead — a space
        // converts to int silently — and splits on commas only, with a limit of
        // thirty-two. It compiles, and "1 2" quietly parses as one key.
        char[] separators = { ',', ' ' };

        foreach (string piece in typed.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string one = piece.Trim().ToUpperInvariant();

            if (one.Length != 1) return null;

            char c = one[0];

            if (!char.IsLetterOrDigit(c)) return null;

            keys.Add(c);
            names.Add(one);
        }

        return keys.Count == 0 ? null : (keys.ToArray(), string.Join(", ", names));
    }

    /// <summary>Reads an interval, or null when it is not one.</summary>
    public static int? ParseInterval(string? typed)
    {
        if (!int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int ms)
            && !int.TryParse(typed?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms))
        {
            return null;
        }

        return ms is < KeyMacro.MinIntervalMs or > KeyMacro.MaxIntervalMs ? null : ms;
    }
}
