# MyBlinkStyleClicker — Architectural Overview

**Status:** four working pages (Clicker, Presets, Tweaks, Optimizations) and three unbuilt ones (History, Theme, Settings).
**Stack:** C# / .NET 10 (`net10.0-windows`), WPF, nullable enabled, implicit usings, no third-party packages.
**Entry point:** `App.xaml` → `StartupUri="MainWindow.xaml"` (no explicit `Main`; WPF SDK generates it).

**Purpose.** A configurable auto-clicker aimed at single-player and private-server use, with aim-training support. The Clicker page drives a synthetic left-click stream — rate in clicks per second, hold duration as a percentage of each cycle, activated by hotkey in toggle or hold mode — and can add bounded camera shake to make tracking practice harder. The remaining pages manage saved profiles and a small set of Windows settings.

**Domain terms.**
*CPS* — clicks per second.
*CDC* — Click Duty Cycle, the share of each click period the button stays held; 0% is an instantaneous press-release, 100% holds for the whole period.
*Shaky Tracking* — synthetic camera movement, not timing jitter. "Tracking" is the aim-trainer sense: following a moving target.

---

## 1. File map

| File | Role | Lines |
|---|---|---|
| `MyBlinkStyleClicker.csproj` | SDK-style project. `WinExe`, `UseWPF=true`. No package refs. | 9 |
| `MyBlinkStyleClicker.slnx` | XML solution format (VS 2026). | 3 |
| `App.xaml` / `App.xaml.cs` | Application object. Two global exception handlers that show a `MessageBox` with the stack trace. | 7 / 20 |
| `MainWindow.xaml` | The entire UI: window-local resources, sidebar nav, and all seven pages stacked in one `Grid`. | 878 |
| `MainWindow.xaml.cs` | Click engine, shake engine, hotkey polling, navigation, presets, tweaks and clean-up handlers, plus the Win32 P/Invoke layer. | 1367 |
| `HotkeySettings.cs` | `HotkeyBinding` (virtual-key + display name) and its JSON persistence. | 82 |
| `AppSettings.cs` | Clicker page state — sliders, shake directions, toggles, click mode. | 53 |
| `ClickPreset.cs` | `ClickPreset` plus `PresetStore`, the CPS/CDC profile list. | 97 |
| `PcTweaks.cs` | `PcTweak` base and nine concrete Windows tweaks, `TweakState`, elevation helpers. | 604 |
| `SystemCleanup.cs` | Temp-file scanner/cleaner and memory helpers. | 206 |

There is no test project, no MVVM layer, no DI, and no logging. Verification during development was done with throwaway console harnesses compiled against these files directly.

---

## 2. Layering (as-built)

```
┌────────────────────────────────────────────────────────────────┐
│ MainWindow.xaml     resources, sidebar, seven stacked pages    │
└───────────────┬────────────────────────────────────────────────┘
                │ x:Name access, Click= handlers, DataTemplates
┌───────────────▼────────────────────────────────────────────────┐
│ MainWindow.xaml.cs                                             │
│  ├─ navigation      ShowPage + Tag-driven sidebar selection    │
│  ├─ input           HotkeyTimer_Tick (polled), rebind capture  │
│  ├─ click engine    ClickLoop  on its own thread               │
│  ├─ shake engine    ShakeLoop  on its own thread               │
│  ├─ telemetry       DispatcherTimer → stats, ping, autosave    │
│  └─ interop         SendInput / GetAsyncKeyState / GetCursorInfo│
└──┬──────────┬──────────┬───────────┬───────────────────────────┘
   │          │          │           │
┌──▼───────┐ ┌▼────────┐ ┌▼────────┐ ┌▼──────────────┐
│ Hotkey   │ │ App     │ │ Click   │ │ PcTweaks      │
│ Settings │ │ Settings│ │ Preset  │ │ SystemCleanup │
└──────────┘ └─────────┘ └─────────┘ └───────────────┘
```

The engines read an immutable `ClickSettings` snapshot published by the UI thread through a `volatile` field, because UI controls have thread affinity. Everything else still treats UI state as application state — presets write to sliders, `_holdMode` shadows a button's background brush — so there is no view model to test independently.

---

## 3. The click engine

`StartClicking()` creates a `CancellationTokenSource` and starts `ClickLoop` on a dedicated background `Thread` at `AboveNormal` priority, named `ClickEngine`. It cannot run on the dispatcher: Ultra Accuracy spins, which would freeze rendering.

Each iteration:

1. If hold-mode and the bound key is up → sleep 5 ms, resync the deadline, continue.
2. Read CPS from the snapshot; below `MinimumCps` the engine is armed but idle.
3. `period = 1000.0 / cps`, split by CDC into `downMs` and `upMs`.
4. `SendLeftDown()` → wait → `SendLeftUp()` → count the click → wait.

Press and release are separate `SendInput` calls; a combined call always yields a zero-length hold and makes CDC meaningless. A `finally` releases the button if cancellation lands between press and release — otherwise stopping mid-click leaves the left button logically held across the whole desktop.

**Timing.** Three mechanisms make the requested rate achievable:

1. `timeBeginPeriod(1)` for the life of the loop, taking the system timer from ~15.6 ms to ~1 ms. Without it nothing above ~32 CPS is reachable whatever the slider says.
2. Absolute deadline accumulation — each half-cycle advances a timestamp by exactly its share of the period, so `SendInput` and loop overhead cannot compound into drift. A resync guard drops the deadline forward if the loop falls more than 100 ms behind, so it never bursts to catch up.
3. Ultra Accuracy, optional, spinning out the last 2 ms instead of sleeping.

`WaitUntil` slices its coarse sleep into 20 ms chunks. At 1 CPS a single hold runs to hundreds of milliseconds, and an uninterruptible sleep there would keep the mouse button physically down that long after a stop. Measured cancellation latency is 2–5 ms.

Measured on a 16-core machine, mirroring this code with `SendInput` removed:

| target CPS | rate (off) | jitter (off) | CPU (off) | rate (on) | jitter (on) | CPU (on) |
|---|---|---|---|---|---|---|
| 10 | 10.0 | 0.50 ms | ~0 | 10.0 | 0.00 ms | 0.03 core |
| 60 | 60.0 | 0.51 ms | ~0 | 60.0 | 0.05 ms | 0.13 core |
| 100 | 100.0 | 0.33 ms | ~0 | 100.0 | 0.04 ms | 0.28 core |
| 150 | 150.0 | 0.53 ms | 0.01 core | 150.0 | 0.02 ms | 0.26 core |

The rate is exact in both modes. Ultra Accuracy buys *evenness between individual clicks*, not throughput — roughly a 10–25× jitter reduction for up to a third of a core. These are clean-room figures; the real engine also calls `SendInput` twice per cycle.

---

## 4. The shake engine

`ShakeLoop` runs on its own background thread for the **lifetime of the window**, not the lifetime of a clicking session — aim practice does not require auto-clicking, so the two are independent. It is gated inside the loop by the checkbox rather than by thread lifetime.

Each tick picks a fresh absolute offset within the configured range and moves the cursor by the difference:

```
targetX = random in [-Left,  +Right]
targetY = random in [-Up,    +Down]     // Windows dy is positive-down
SendMouseMove(targetX - offsetX, targetY - offsetY)
```

**This is deliberately not a random walk.** Taking an independent random step each tick accumulates: simulated over 10 seconds at 30 Hz, independent steps drifted 79 px off origin with an 82 px maximum excursion, while the bounded form stayed within ±4 px. For aim training a crosshair that slides off-screen is not a handicap, it is a fault. The outstanding offset is also undone when the gate closes or the thread exits, so the cursor ends where it began.

The four directions are magnitudes — sign is ignored, so `-8` and `8` both mean eight pixels that way. Asymmetric ranges bias the resting position, which is inherent rather than a bug.

**Gating.** Shake only runs while Roblox owns the foreground window *and* the system cursor is hidden (`GetForegroundWindow` → pid → process name, plus `GetCursorInfo`). That is the closest an external process can get to "is the player in first person" without reading the game. It is a superset: third-person shift-lock and holding right-mouse to rotate lock the cursor the same way. Distinguishing them would mean reading a Hyperion-protected process, which this app does not do. The pid→name lookup is cached until the pid changes, since the loop runs every 20–40 ms.

---

## 5. Input / hotkey model

**Hotkeys are polled, not routed.** A `DispatcherTimer` at 15 ms reads `GetAsyncKeyState` for both bindings and acts on edges. Routed `KeyDown` only arrives while this window has focus — which is exactly when a game does not — and mouse side buttons never route to the window at all. Polling reads the same global state for keys and mouse buttons alike, without hooks or injection.

A binding is a `HotkeyBinding` — a virtual-key code plus a display name — so a keyboard key and a mouse side button are the same kind of thing. `HotkeyBinding.FromKey` uses `KeyInterop.VirtualKeyFromKey`; `FromMouse` maps `XButton1`/`XButton2` to `0x05`/`0x06` and returns null for everything else. Left is deliberately unbindable: it is the button this app synthesises.

Two bindings exist: start/stop (default F6) and Shaky Tracking (default F7).

**Rebinding** is inline — the button becomes "Select A Hotkey" and captures the next input. Capture runs on `PreviewKeyDown`, ahead of the focused control, because a `Button` treats Space and Enter as activation during `KeyDown`; capturing there would let the rebind button re-trigger itself. Escape cancels. A key already bound to the other action is refused. After capture the edge detectors are primed from live key state, or releasing the just-bound key would read as a fresh press.

**Escape stays a routed window event on purpose.** Polling it would make Escape stop the clicker from inside any application.

`Window_Deactivated` was removed. It used to stop hold mode when focus was lost, as a backstop for a `KeyUp` that would never arrive; with polling that backstop became actively harmful, since it would kill the clicker the instant you alt-tabbed into the game.

### Status

`RefreshStatus` derives the pill and the start-button label from `_running`, `_holdMode`, and live key state. Hold mode has two live states — **ARMED** (started, waiting on the key) and **RUNNING** (actually clicking). Re-evaluated on every stats tick, so ARMED cannot go stale.

---

## 6. Navigation

Seven pages live as sibling `StackPanel`s in one `Grid` inside the page `ScrollViewer`, all collapsed except the active one. `ShowPage` clears `Tag` on every nav button and sets `Tag="Selected"` on the one being opened, which drives the accent rail and lifted background through a template trigger. Selection keys off the page that opened rather than the button that was clicked, so the highlight cannot point at a page that failed to load.

The status pill and start button are collapsed on every page except Clicker — they belong to the clicker, not the shell. Hotkeys remain global, so clicking can still be started and stopped from any tab.

Tweaks and Optimizations re-read system state on entry, since a setting may have changed outside the app.

> **Resource ordering matters.** The shared `TweakRow` `DataTemplate` uses `{StaticResource Badge}`, and WPF cannot resolve a forward `StaticResource` reference. Because template content is deferred, a forward reference compiles cleanly and only throws when the page first renders. `TweakRow` must stay below the badge styles.

---

## 7. Presets

`ClickPreset` is an observable name/CPS/CDC triple; `PresetStore` persists the **whole list**, not just user-created entries. Every preset is deletable, so the defaults have to be able to stay deleted — regenerating them from code each launch would resurrect what the user removed. A missing file seeds the defaults; a file holding an empty list is respected as "the user deleted everything". **Restore defaults** adds back only what is missing.

Cards show the numbers and a fill bar scaled against 150 CPS, so relative speed is legible before reading any digits. `IsApplied` is derived from the *slider values*, not from the last card clicked, so editing CPS by hand clears the highlight rather than leaving a stale claim on screen.

Numeric fields reject anything that would not parse, on typing and on paste, by testing the resulting string rather than the keystroke — so a second decimal point is refused while the first is allowed. `NumberStyles.AllowDecimalPoint | AllowLeadingSign` is used deliberately in place of `NumberStyles.Float`, which would also admit exponents and whitespace.

---

## 8. Windows tweaks

`PcTweak` is an abstract base: read current state, apply, revert, with prior values recorded to `tweak_state.json` **before** anything is written. Revert restores what the machine actually had rather than an assumed default. A null recorded value means "did not exist", so revert deletes the value instead of inventing a zero.

Nine tweaks, each carrying an honest `Impact` string rather than marketing:

| Tweak | Mechanism | Admin |
|---|---|---|
| High Performance power plan | `powercfg /setactive`, duplicating the scheme if Windows hides it | No |
| Keep all CPU cores awake | scheme registry `CPMINCORES` = 100, and unhides the setting | Yes |
| Disable Game DVR recording | `HKCU\System\GameConfigStore` | No |
| Disable transparency effects | `HKCU\…\Themes\Personalize` | No |
| Visual effects for best performance | `HKCU\…\Explorer\VisualEffects` | No |
| Hardware-accelerated GPU scheduling | `HKLM\…\GraphicsDrivers\HwSchMode` | Yes |
| Disable Superfetch (SysMain) | service `Start` = 4 | Yes |
| Disable power throttling | `HKLM\…\Power\PowerThrottling` | Yes |
| Turn off mouse acceleration | `SystemParametersInfo(SPI_SETMOUSE)` | No |

Mouse acceleration lives on **Optimizations**, not Tweaks: it is about input fidelity, not machine performance, and it is the most consequential setting here for an aim-training tool. It uses `SystemParametersInfo` rather than raw registry writes so the change applies immediately with no sign-out.

Core parking is driven through the registry rather than `powercfg` aliases for a specific reason: Windows ships `CPMINCORES` hidden (`Attributes = 1`), and while hidden `powercfg -q` prints nothing for it even with explicit GUIDs. Query-based detection silently reports "unreadable" on a stock machine.

**There is no Apply All.** One click silently changing several system settings, two of them registry writes, is how people end up unable to undo something. Revert All exists — it only moves in the safe direction.

---

## 9. Optimizations

**Temp cleaner.** Scans `%TEMP%` and `%SystemRoot%\Temp` and nowhere else. Files touched within the last 24 hours are left alone, so an in-flight installer keeps its working set. Locked files are skipped and counted rather than aborting the run. The walk **refuses to cross a reparse point** — a junction inside temp can point anywhere, and following one is how a temp cleaner becomes a disaster; a directory whose attributes cannot be read is treated as one. Deleting is irreversible, so this is the one action in the app that keeps a confirmation dialog.

Measured on a development machine: 181,189 files / 3,186 MB eligible, 1.5 s to walk — hence the scan runs off the UI thread.

**Memory.** `GlobalMemoryStatusEx` for the readout; `EmptyWorkingSet` per process for the Free RAM button. That button is included because it was requested, and labelled for what it does: trimming working sets pushes pages onto the standby list, so the available figure rises and then falls again as processes fault them back in. The result message reports the before/after numbers and says to expect drift, rather than claiming a benefit. The temp cleaner is the accent button; Free RAM is secondary — the visual hierarchy reflects which one does something.

---

## 10. Persistence

Four JSON files, all written beside the executable via relative paths, all gitignored as user state:

| File | Contents |
|---|---|
| `hotkey_settings.json` | Both bindings, as virtual-key + name |
| `app_settings.json` | CPS, CDC, four shake directions, saved shake snapshot, toggles, click mode |
| `click_presets.json` | The full preset list |
| `tweak_state.json` | Prior values for applied tweaks |

`AppSettings` saves automatically: every change point already routes through `UpdateEngineSettings`, so that is the single place marking the config dirty, and the 1 Hz stats tick flushes it. Dragging a slider therefore does not write the file on every pixel. Closing the window catches anything from the last second.

Every loader degrades to defaults on missing or corrupt input, and `HotkeySettings` additionally falls back to the older "name of a WPF `Key`" format so files written by earlier builds still load.

Relative paths mean the files land in the process working directory — the build output under F5, but wherever the shell points otherwise. Moving to `%APPDATA%` remains a small, contained change.

---

## 11. Interop layer

Private nested types and `DllImport`s at the bottom of `MainWindow`, plus a few in `PcTweaks` and `SystemCleanup`:

- `INPUT` (sequential) → `InputUnion` (explicit, `FieldOffset(0)`) → `MOUSEINPUT` (sequential). The union declares only `mi`; `MOUSEINPUT` is the largest member of the real union, so `Marshal.SizeOf<INPUT>()` yields the correct size. Adding keyboard support means extending the union, not just adding a method.
- `SendMouseEvent` sends exactly one event; `SendLeftDown`/`SendLeftUp`/`SendMouseMove` wrap it. Return values are discarded, so injection blocked by UIPI fails silently.
- `GetAsyncKeyState` — hotkey polling, keyboard and mouse buttons alike.
- `GetSystemTimes` — the CPU tile, via a private `FileTime` struct named to stay distinct from `ComTypes.FILETIME`.
- `GetForegroundWindow` / `GetWindowThreadProcessId` / `GetCursorInfo` — the shake gate.
- `timeBeginPeriod` / `timeEndPeriod` (winmm) — per-process since Windows 10 2004, balanced in the loop's `finally`.
- `SystemParametersInfo` — mouse acceleration.
- `GlobalMemoryStatusEx` / `EmptyWorkingSet` — the memory panel.

Extracting the input calls to an `IInputSender` would make the click loop testable without a display. That is still the cleanest available seam.

---

## 12. Error handling

1. `MainWindow` constructor wraps `InitializeComponent` in try/catch → `MessageBox` → rethrow.
2. `App.DispatcherUnhandledException` → `MessageBox` with stack trace, then `e.Handled = false` (the app still dies).
3. `AppDomain.UnhandledException` → same.

Engine threads swallow their own exceptions so a failed input call cannot take the app down; the click loop routes anything unexpected to `Dispatcher.InvokeAsync(StopClicking)`. Persistence and tweak detection degrade silently to defaults. There is no log file, so anything swallowed is unrecoverable after the fact — the largest remaining gap in diagnosability.

---

## 13. Known defects

1. **Settings paths are working-directory-relative** (section 10).
2. **`SendInput` failures are invisible** (section 11) — a blocked injection looks identical to a dead hotkey.
3. **HitFix is inert** — a labelled toggle with no implementation and no agreed meaning.
4. **Rapid start/stop can overlap engine threads**, so an outgoing loop's release can land between a new loop's press and release, producing one short click. Self-correcting within a cycle.
5. **`StopClicking` disposes the `CancellationTokenSource`** while engine threads may still read the token. Benign in practice — `IsCancellationRequested` reads a field and does not throw after dispose — but it is the documented anti-pattern.

### Fixed during review

Escape unreachable behind the hotkey comparison · power-mode buttons accumulating highlights · stuck mouse button on mid-click cancellation · requested CPS not delivered (capped near 32) · hold mode never releasing · auto-repeat thrashing the toggle · rebind not reaching the hold gate · no global hotkey (now polled) · `_shakeActive` never cleared, leaving a stale "Active" label · shake value stored, displayed and reported as three different numbers · uninterruptible sleep holding the button down for seconds after a stop at low CPS · `TweakRow` declared above the resources it referenced, crashing the Tweaks page on first render.

---

## 14. Where the seams should go

1. **Finish extracting the click engine.** The thread, the snapshot and the timing are already separated; what remains is lifting them out of `MainWindow` into a `ClickEngine` taking an `IInputSender`, making the timing logic testable without a display.
2. **Add a log file.** Every layer currently swallows exceptions silently, which was tolerable at prototype scale and is not once the app writes to `HKLM`.
3. **Introduce a view model** for the Clicker page so the engines stop reading `Slider.Value` and click mode stops living in `Background` brushes. `MainWindow.xaml.cs` is past 1,300 lines and is the only place code can go.
4. **Split `MainWindow.xaml` per page.** Seven pages in one 878-line file is the same problem in the other language.
5. **Move persistence to `%APPDATA%`** behind a single settings service, rather than four classes each doing their own relative-path file I/O.
