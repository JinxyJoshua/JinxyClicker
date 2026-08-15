# Jinxy Clicker

A Windows autoclicker and tuning utility, written in C# and WPF. Original work —
it contains no third-party application's source.

## Install

Run `JinxyClicker-Setup-1.0.0.exe`. Nothing else is needed — .NET and ffmpeg are
both included, and it installs per-user, so there is no admin prompt.

Windows 10 or 11, 64-bit. ARM machines are not supported.

## Hotkeys

Bindings are stored as Windows virtual-key codes, so mouse side buttons bind the
same way keys do. Rebind by clicking a hotkey button and pressing the key.

| Action | Default |
|---|---|
| Start / stop clicking | `F6` |
| Toggle shake | `F7` |
| Save instant replay | `F8` |
| Start / stop recording | unbound |

They are polled system-wide, so they fire while a game has focus — and equally
while you are typing elsewhere. Settings has a master switch that silences all
four without unbinding them.

Two ways to stop the clicker beyond its hotkey:

- **Escape**, while the window has focus.
- **Move the pointer into any corner of the desktop.** This works regardless of
  focus and regardless of the master hotkey switch, so it stays available when
  everything else is off. Corners rather than edges, because on a multi-monitor
  setup edges are crossed constantly in normal use.

## Pages

**Clicker** — CPS and duty cycle sliders, toggle or hold mode, precision
options, and shake. Shake nudges the pointer within per-direction pixel bounds
to make tracking practice harder; it only engages while Roblox is in the
foreground and holding the mouse. Values can be masked so they cannot be read
off the screen.

**Presets** — named click configurations. A preset carries the whole setup —
rates, mode, precision and shake bounds — not just the two numbers.

**Tweaks** — Windows performance settings: high performance power plan, core
parking, Game DVR, transparency, visual effects, GPU scheduling, SysMain and
power throttling. Each reports its current state and can be reverted.

**Optimizations** — temporary file cleanup, mouse tracking helper (pointer
acceleration) and a network QoS policy.

**Mod** — Roblox client settings. Only flags on Roblox's published allowlist are
offered; since that allowlist landed, anything outside it is ignored by the
client anyway.

**Recorder** — records the screen to MP4 and can upload a clip for sharing.
Choose which monitor to capture on a multi-display setup. Instant replay keeps
the last minute rolling in the background so a hotkey saves something that has
already happened, without recording continuously to disk.

**History** — time spent clicking and running totals.

**Theme** — accent colour, window opacity, and a dark or light palette.

**Settings** — master hotkey switch, clip folder and capture framerate.

## Files it writes

Generated on first run in `%APPDATA%\JinxyClicker`:

- `app_settings.json` — sliders, toggles, theme, window placement
- `click_presets.json` — saved presets
- `hotkey_settings.json` — key bindings
- `tweak_state.json` — which tweaks are applied
- `history.json` — running click totals

Clips default to `Videos\JinxyClicker` and the path is configurable in Settings.
The replay buffer uses a temporary folder and is bounded in size.

## Architecture

`ARCHITECTURE.md` covers the internals — the click engine's timing, the polled
hotkey thread, and the Win32 surface the app depends on.
