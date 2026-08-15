# Accent colour theming — design

**Date:** 2026-08-14
**Status:** implemented
**Scope:** let the user change the app's accent colour from a Theme page

## Goal

The UI is fixed to a single red accent (`#FF4B52`). The user wants to change the
colour of the app. This design covers the accent only — backgrounds, panels and
text stay as they are.

## Decisions

**Accent only, not a full theme editor.** The accent is what reads as "the
colour of the app", and restricting the change to it means no combination the
user picks can produce unreadable text on an unreadable panel. Full per-colour
control was considered and rejected on those grounds.

**A row of fixed swatches, not a picker.** Ten hand-chosen colours, all verified
against the `#0F141D` background. A free picker (hue slider or hex entry) was
considered; the swatch row is smaller to build, needs no validation of user
input, and matches the card aesthetic already used on the Presets page.

## Mechanism

The XAML makes 207 `StaticResource` references to six named brushes and zero
`DynamicResource` references. `StaticResource` is resolved once at load, which
normally rules out runtime theming — but it resolves to a *reference* to the
shared `SolidColorBrush` instance. Moving that brush's colour therefore reaches
every consumer without any of the 207 references changing.

### What does not work: mutating the brush

The obvious form of that idea is to assign the brush's `Color` directly:

```csharp
((SolidColorBrush)Resources["Accent"]).Color = chosen;   // throws
```

This was the design's first mechanism and it is wrong. WPF freezes resource
`Freezable`s wherever it can, and these brushes come back frozen, so the
assignment throws:

> Cannot set a property on object '#FFFF4B52' because it is in a read-only state.

Because `ApplyAccent` runs during `ApplyAppSettings`, the throw landed inside the
constructor — the app showed its constructor error box and died on every launch,
and `_settingsLoaded = true` was never reached, so nothing was ever saved.

### What works: moving the Colour underneath the brush

Each accent brush takes its `Color` from a `DynamicResource` pointing at a
separate `Color` resource:

```xml
<Color x:Key="AccentColor">#FF4B52</Color>
<SolidColorBrush x:Key="Accent" Color="{DynamicResource AccentColor}"/>
```

The indirection does two jobs. A `Freezable` holding an unresolved reference
cannot be frozen, which keeps the brush writable. And swapping the `Color`
resource flows through the brush to every `StaticResource` consumer, so the
repaint is still global and still requires no change to the 207 references:

```csharp
Resources["AccentColor"] = chosen;
```

The ten `FindResource` calls in the code-behind copy the brush into a property
(`StatusDot.Fill`, various `Foreground`s) rather than holding a live reference,
so they show the previous colour until whatever event next refreshes them. The
status dot re-runs on the one-second stats tick and the rest on their next state
change, so this self-corrects within a second and needs no extra wiring.

### Accent-derived literals

Three hardcoded colours are tints of the accent and would stay red while
everything else changed. Each becomes a named brush, set by the same method from
the same RGB:

| Literal | Site | New brush | Derivation |
|---|---|---|---|
| `#33FF4B52` | hover border, `MainWindow.xaml:271` | `AccentSoft` | accent @ alpha `0x33` |
| `#66FF4B52` | focus ring, `MainWindow.xaml:277` | `AccentStrong` | accent @ alpha `0x66` |
| `#2A1D22` | danger panel fill, `MainWindow.xaml:1363` | `AccentWash` | accent @ alpha `0x22` |

`AccentWash` replaces an opaque colour with a translucent one. `#2A1D22` is
approximately the accent at 11–13% over the `#0F141D` background, so alpha
`0x22` reproduces it closely enough to be indistinguishable, and unlike the
opaque original it tracks the accent automatically.

The remaining 19 hex literals in the XAML are greys and neutrals. They stay.

## Palette

| Name | Hex |
|---|---|
| Crimson (default) | `#FF4B52` |
| Ember | `#FF7A45` |
| Amber | `#FFC53D` |
| Mint | `#4BD47B` |
| Teal | `#2FD4A8` |
| Sky | `#45B7FF` |
| Indigo | `#5B7CFA` |
| Violet | `#9B5BFA` |
| Pink | `#FF5BC8` |
| Silver | `#C3CDDD` |

Crimson is the current colour and the default, so an existing install looks
unchanged until the user picks something.

## UI

`PageTheme` (`MainWindow.xaml:1404`) is a "Nothing here yet" placeholder. Its
nav button, `NavTheme_Click` handler, and registration in the `NavButtons` and
`Pages` arrays already exist, so nothing needs wiring.

The placeholder is replaced by one panel: an `ACCENT COLOUR` heading and a
wrapped row of ten swatch chips. The selected chip is ringed, reusing the
visual treatment of `ClickPreset.IsApplied` on the Presets page.

## Persistence

One new property on `AppSettings`:

```csharp
public string AccentColor { get; set; } = "#FF4B52";
```

Read in `ApplyAppSettings`, written in `SaveAppSettings` alongside the existing
fields, and flushed through the existing `_settingsDirty` path on the stats
tick. A missing or malformed value falls back to the default rather than
throwing — the same posture `SelectReplayLength` already takes toward a stored
value it cannot match.

## Files touched

- `MainWindow.xaml` — three new brushes, swatch panel replacing the placeholder
- `MainWindow.xaml.cs` — `ApplyAccent`, the swatch click handler, load and save
- `AppSettings.cs` — one property

No new files. No new dependencies.

## Verification

The project has no test framework and no test project, so verification is
behavioural:

1. `dotnet build` clean.
2. Launch, open Theme, click each of the ten swatches. Buttons, the status dot,
   focus rings and the danger panel all follow on every one.
3. Restart. The chosen colour is still applied.
4. Delete `AccentColor` from `app_settings.json`, restart, confirm it falls back
   to Crimson rather than throwing.

## Out of scope

Per-colour control of the other five brushes; a light mode; hex entry or a hue
slider; preset full-palette themes; theming the window chrome.
