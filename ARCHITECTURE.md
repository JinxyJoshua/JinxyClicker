# MyBlinkStyleClicker — Architectural Overview

**Status:** early prototype. One window, one feature (auto-click), everything else is UI scaffolding.
**Stack:** C# / .NET 10 (`net10.0-windows`), WPF, nullable enabled, implicit usings, no third-party packages.
**Entry point:** `App.xaml` → `StartupUri="MainWindow.xaml"` (no explicit `Main`; WPF SDK generates it).

**Purpose.** A configurable auto-clicker for single-player and private-server use. The Clicker page drives a synthetic left-click stream: rate in clicks per second, hold duration as a percentage of each cycle, activated by hotkey in toggle or hold mode. The remaining sidebar pages are unbuilt.

**Domain terms.** *CPS* — clicks per second. *CDC* — Click Duty Cycle, the share of each click period the button stays held; 0% is an instantaneous press-release, 100% holds for the whole period.

---

## 1. File map

| File | Role | LOC |
|---|---|---|
| `MyBlinkStyleClicker.csproj` | SDK-style project. `WinExe`, `UseWPF=true`. No package refs. | 11 |
| `MyBlinkStyleClicker.slnx` | XML solution format (VS 2026). | 3 |
| `App.xaml` / `App.xaml.cs` | Application object. Wires two global exception handlers that show a `MessageBox` with the stack trace. | 8 / 24 |
| `MainWindow.xaml` | The entire UI: sidebar nav + "Clicker" page. Resources (brushes, button styles) are declared window-locally, not in `App.Resources`. | 291 |
| `MainWindow.xaml.cs` | The entire application logic: click engine, hotkey handling, mode state, presets, stats timer, and the Win32 P/Invoke layer. | 319 |
| `HotkeySettings.cs` | Single-value JSON persistence for the activation key. | 43 |

There is no test project, no MVVM layer, no DI, no navigation framework, and no logging.

---

## 2. Layering (as-built)

```
┌──────────────────────────────────────────────────────────┐
│ MainWindow.xaml            declarative UI + theme brushes│
└───────────────┬──────────────────────────────────────────┘
                │ x:Name field access, Click= / KeyDown= handlers
┌───────────────▼──────────────────────────────────────────┐
│ MainWindow.xaml.cs                                       │
│  ├─ input layer     Window_KeyDown, RebindHotkey         │
│  ├─ state           _running _holdMode _powerMode        │
│  ├─ click engine    StartClicking / ClickLoopAsync       │
│  ├─ presets         List<Preset> + 6 Click handlers      │
│  ├─ telemetry       DispatcherTimer → UpdateStats        │
│  └─ interop         SendInput / GetAsyncKeyState + structs│
└───────────────┬──────────────────────────────────────────┘
                │
┌───────────────▼──────────┐   ┌──────────────────────────┐
│ user32.dll (SendInput)   │   │ HotkeySettings (JSON I/O)│
└──────────────────────────┘   └──────────────────────────┘
```

Everything above the interop boundary lives in one partial class. There is one seam: the engine thread reads a `ClickSettings` record rather than the controls, because it must. Everything else still treats UI state as application state — presets write to sliders, `_holdMode` shadows a button's background brush — so there is no model to test or persist independently.

---

## 3. The click engine

`StartClicking()` creates a `CancellationTokenSource` and fires `ClickLoopAsync` without awaiting it (`_ = ...`). The loop:

1. If hold-mode and the physical key is up → `await Task.Delay(5)` and spin.
2. Read CPS from the slider; if below `MinimumCps`, idle and continue (0 CPS means armed but not clicking).
3. Compute `period = 1000.0 / cps`.
4. Optionally add up to `min(3ms, 8% of period)` of jitter when *Shaky Tracking* is checked.
5. Split the period by CDC: `downMs = period × duty`, `upMs = period − downMs`.
6. `SendLeftDown()` → wait `downMs` → `SendLeftUp()` → wait `upMs`.

Press and release are separate `SendInput` calls; a combined call would always yield a zero-length hold and make CDC meaningless. A `finally` block releases the button if cancellation lands between the press and the release — otherwise stopping mid-click would leave the left button logically held across the whole desktop.

`WaitAsync` skips the `await` entirely for sub-millisecond spans. At CDC 0 or 100 one half of the period is empty, but the other half always yields, so the loop cannot spin hot.

**The loop runs on its own thread** — a background `Thread` at `AboveNormal` priority, named `ClickEngine`. It cannot run on the dispatcher, because Ultra Accuracy spins and would freeze rendering. Since UI controls have thread affinity, the engine never reads them; it reads an immutable `ClickSettings` snapshot published by the UI thread through a `volatile` field.

**Timing.** Three things make the requested rate achievable:

1. `timeBeginPeriod(1)` for the life of the loop, taking the system timer from ~15.6 ms to ~1 ms. Without it nothing above ~32 CPS is reachable, whatever the slider says.
2. Absolute deadline accumulation. Each half-cycle advances a timestamp by exactly its share of the period, so time spent in `SendInput` and loop overhead cannot compound into drift. A resync guard drops the deadline forward if the loop falls more than 100 ms behind, so it never bursts to catch up.
3. Ultra Accuracy, optional, spinning out the last 2 ms instead of sleeping.

Measured on a 16-core machine, mirroring this code with `SendInput` removed:

| target CPS | rate (off) | jitter (off) | CPU (off) | rate (on) | jitter (on) | CPU (on) |
|---|---|---|---|---|---|---|
| 10 | 10.0 | 0.50 ms | ~0 | 10.0 | 0.00 ms | 0.03 core |
| 60 | 60.0 | 0.51 ms | ~0 | 60.0 | 0.05 ms | 0.13 core |
| 100 | 100.0 | 0.33 ms | ~0 | 100.0 | 0.04 ms | 0.28 core |
| 150 | 150.0 | 0.53 ms | 0.01 core | 150.0 | 0.02 ms | 0.26 core |

The rate is exact in both modes. Ultra Accuracy buys *evenness between individual clicks*, not throughput — roughly a 10–25× jitter reduction for up to a third of a core. It matters most at high CPS, where 0.5 ms is a larger share of a short period.

These are clean-room figures; the real engine also calls `SendInput` twice per cycle and shares the machine with whatever else is running.

Cancellation is cooperative: `StopClicking()` cancels and disposes the CTS; the loop catches `OperationCanceledException` and exits. Any other exception routes to `Dispatcher.InvokeAsync(StopClicking)`.

---

## 4. Input / hotkey model

Activation is handled by **`Window_KeyDown`**, a routed WPF event. This means the hotkey only works while the window has keyboard focus. There is no `RegisterHotKey` and no low-level keyboard hook, so the clicker cannot be triggered from inside the target application — which is the usual use case for this class of tool. Adding a global hotkey is the largest missing architectural piece.

`Window_KeyDown` discards auto-repeat first (`e.IsRepeat`), then splits four ways:

- **Rebind capture** — when `_rebindingHotkey` is set, the next key press is stored, persisted, mirrored into the UI, and pushed into the engine snapshot.
- **Escape** — unconditional emergency stop, checked before everything else so it survives being bound as the hotkey and works while a value box has focus.
- **Text entry guard** — returns if a `TextBox` has focus, so typing a number cannot trigger the hotkey.
- **Activation** — `e.Key == _hotkeySettings.HotkeyKey`; toggles, or in hold mode starts.

Hold mode additionally uses `Window_KeyUp` to stop, and `Window_Deactivated` as a backstop — losing focus mid-hold means `KeyUp` never arrives, which would otherwise leave the clicker running with no key held. Toggle mode deliberately ignores both: continuing to click after you switch away is the point.

The engine's own hold gate reads `ClickSettings.HotkeyVk`, produced by `KeyInterop.VirtualKeyFromKey`. That translation is what keeps hold mode pointed at the bound key; the gate previously used a hardcoded `VK_F6` and silently ignored rebinds.

### Status

`RefreshStatus` derives the pill and the start-button label from `_running`, `_holdMode`, and the live key state. Hold mode has two live states — **ARMED** (started, waiting on the key) and **RUNNING** (actually clicking) — and conflating them is what made the old status misleading. It is re-evaluated on every stats tick, so ARMED cannot go stale.

### Persistence

`HotkeySettings` serializes `{ "HotkeyKey": "F6" }` to `hotkey_settings.json` using a **relative path**, so the file lands in the process working directory — the build output folder under F5, but wherever the shell happens to point when launched otherwise. Both `Save` and `Load` swallow all exceptions silently. Migrating to `%APPDATA%` and a typed settings record is a small, contained change.

---

## 5. State and modes

| Field | Written by | Read by |
|---|---|---|
| `_running` | Start/Stop | `ToggleRunning`, `StartClicking` |
| `_holdMode` | Toggle/Hold buttons | key handler, click loop |
| `_rebindingHotkey` | `RebindHotkey` | key handler |
| `_clickCts` | Start/Stop | click loop |

Click mode (Toggle/Hold) uses a field plus a button `Background` brush set imperatively, with each handler clearing the other via `ClearValue`. It works, but it duplicates state. The `SegmentButton` style is deliberately kept even though Power Mode's removal left it unreferenced — it is a mutually-exclusive segmented control looking for exactly this control to adopt it.

---

## 6. UI surface vs. implemented behavior

The XAML advertises considerably more than the engine implements. Inventory:

| Control | Wired to logic? |
|---|---|
| CPS slider + value box | **Yes** — read every loop iteration, fractional |
| CDC slider + value box | **Yes** — splits each period into hold and release |
| Shaky Tracking | **Yes** — adds jitter |
| Click mode Toggle/Hold | **Yes** |
| Ultra Accuracy | **Yes** — gates the spin-wait in `WaitUntil` |
| CPU stat tile | **Yes** — `GetSystemTimes` delta at 1 Hz, machine-wide |
| RAM stat tile | **Yes** — `Process.WorkingSet64` at 1 Hz, this process only |
| Measured rate | **Yes** — delivered clicks per second, Stopwatch-timed |
| Ping Sync | **Yes** — measures latency, forces the spin-wait above the threshold |
| HitFix | No |
| Nav: Presets…Theme | No — `MessageBox` placeholders |
| Nav: Settings | Partial — hotkey rebind prompt |

*Frame Sync* was removed from the Precision panel rather than left inert. *Ultra Accuracy* was removed and then restored once it had something real to gate. *Power Mode* was removed outright — every meaning it plausibly had was either already occupied by Ultra Accuracy (CPU-for-accuracy tradeoff) or by the presets (rate tiers), and the one it did not claim, Windows power plans, was explicitly disclaimed by a comment in the original source. CDC now spans the full width it vacated.

### Ping Sync

One ICMP probe per stats tick toward `www.roblox.com`, issued only while the toggle is on, and skipped whenever the previous probe is still outstanding. Above `HighPingMs` it forces the spin-wait on regardless of the Ultra Accuracy checkbox, so `UpdateEngineSettings` publishes `spin = UltraAccuracy || (PingSync && ping >= threshold)`.

The rationale is *minimise the error we control*: under a degraded connection, don't add local timing jitter on top of the network's.

Two limits worth stating plainly for anyone extending this:

- **It is not Roblox's in-game ping.** That figure is measured application-side inside the client. An ICMP round trip to Roblox's front end is a different quantity that moves in sympathy, not a substitute. Reading the real one means reading a Hyperion-protected process — out of scope by design.
- **The effect is small by construction.** Local jitter is ~0.5 ms with the spin-wait off and ~0.03 ms with it on. When ping is high enough to trigger this, network variance is tens of milliseconds and dominates both. Latency cannot be compensated for in a local click stream — a constant delay shifts every click equally and so preserves the intervals between them, and the part that does distort arrival timing, jitter, is unpredictable by definition. This feature reduces the one term it can reach.

Dead code that remains: `PresetFast_Click` and `PresetMax_Click` (no XAML binds them — the "Fast" preset at index 3 is unreachable).

---

## 7. Interop layer

Standard `SendInput` marshalling, declared as private nested types at the bottom of `MainWindow`:

- `INPUT` (sequential) → `InputUnion` (explicit, `FieldOffset(0)`) → `MOUSEINPUT` (sequential).
- The union declares only the `mi` member. Because `INPUT`'s size must match the OS expectation, this works on x64 by luck of layout — `MOUSEINPUT` is the largest member of the real union, so `Marshal.SizeOf<INPUT>()` yields the correct 40 bytes. Adding keyboard support means adding `KEYBDINPUT`/`HARDWAREINPUT` to the union, not just a new method.
- `SendMouseEvent` sends exactly one event; `SendLeftDown` and `SendLeftUp` wrap it. Its return value is discarded, so injection blocked by UIPI (target window running elevated while this app is not) fails silently and looks like a dead hotkey.
- `GetSystemTimes` (kernel32) backs the CPU tile, using a private `FileTime` struct — named to stay distinct from `System.Runtime.InteropServices.ComTypes.FILETIME`.

This is the one part of the codebase with a clean seam. Extracting it to an `IInputSender` / `Win32InputSender` pair would make the click loop testable without a display.

---

## 8. Error handling

Three layers, all terminal-user-facing:

1. `MainWindow` constructor wraps `InitializeComponent` in try/catch → `MessageBox` → rethrow.
2. `App.DispatcherUnhandledException` → `MessageBox` with stack trace, then `e.Handled = false` (the app still dies).
3. `AppDomain.UnhandledException` → same.

Plus silent `catch { }` in `HotkeySettings` and in `UpdateStats`. There is no log file, so anything swallowed is unrecoverable after the fact.

---

## 9. Notable defects found during review

Ordered by user impact.

1. **No global hotkey.** `Window_KeyDown` is a routed WPF event, so activation requires window focus (section 4). Hold mode is therefore unusable from inside another application, and toggle mode cannot be stopped by hotkey once you switch away — only by returning to the window or pressing Escape there.
2. **Settings path is working-directory-relative** (section 4).
3. **`SendInput` failures are invisible** (section 7).

### Fixed since the first review

- Escape ordering — the emergency stop was in an `else if` after the hotkey comparison, unreachable if the hotkey was rebound to Escape. It is now checked first, before the hotkey and before the text-box focus guard.
- Power-mode buttons accumulating highlights — replaced by a radio group (section 5).
- Stuck mouse button — introduced with the duty cycle and closed in the same change by the `finally` release (section 3).
- Requested CPS not being delivered — the loop topped out near 32 CPS and ran 20% low even at the default 10. Fixed by the timer resolution and deadline accumulation in section 3, and now observable in the UI via the measured-rate readout.
- Hold mode never stopping — releasing the key left `_running` true and the status stuck on RUNNING, with the hotkey a no-op thereafter. `Window_KeyUp` now stops it, `Window_Deactivated` backstops lost focus, and ARMED distinguishes waiting from clicking (section 4).
- Auto-repeat thrashing the toggle — holding the hotkey started and stopped the loop tens of times per second. `e.IsRepeat` is discarded first.
- Rebind not reaching the hold gate — the engine watched a hardcoded `VK_F6`. It now reads a translated virtual-key from the settings snapshot, and the sidebar badge and start button follow the bound key instead of reading "F6" forever.

None of the remaining items are architectural dead ends. But 1–3 are the ones a user hits in the first minute.

---

## 10. Where the seams should go

If this grows past the prototype, in rough priority order:

1. **Finish extracting the click engine.** The thread, the `ClickSettings` snapshot, and the timing are already separated; what remains is lifting them out of `MainWindow` into a `ClickEngine` class taking an `IInputSender`, which would make the timing logic unit-testable without a display.
2. **Global hotkey** via `RegisterHotKey` on the window handle (`HwndSource.AddHook`), with a `Key` → virtual-key translation shared by both the toggle and hold paths.
4. **Introduce a view model** for the Clicker page so the engine stops reading `Slider.Value`, and mode selection stops living in `Background` brushes.
5. **Real navigation** — the eight sidebar buttons imply a `ContentControl` host and one `UserControl` per page; the `MessageBox` placeholders are a stand-in for that shell.
6. **Widen `HotkeySettings` into an app-settings store** under `%APPDATA%`, persisting CPS, CDC, mode, and the precision toggles.

Steps 1 and 3 are prerequisites for the rest being worth doing; 4 and 5 are the ones that stop `MainWindow.xaml.cs` from being the only place code can go.
