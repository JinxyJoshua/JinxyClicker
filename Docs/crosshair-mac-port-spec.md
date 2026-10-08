# Crosshair feature — macOS porting spec

A spec for adding the Windows crosshair feature to the Mac app (JinxyMac) so it
behaves the same. Written from the shipped Windows implementation (v1.4.13+).

The Windows code is the source of truth. Where this doc and the code disagree,
the code wins. Canonical files, all in the Windows repo root:

- `CrosshairStyle.cs` — the geometry of a drawn crosshair (no pixels).
- `CrosshairImage.cs` — turns a style (or an imported image) into a PNG.
- `CrosshairGallery.cs` — the ready-made gallery + the custom-crosshair record.
- `RobloxCursors.cs` — **the core**: writes/restores Roblox's cursor files.
- `CrosshairStore.cs` — stores imported crosshair images.
- `MainWindow.Crosshair.cs` — the page: gallery tiles, size, apply/remove, import.
- `RobloxCursors.Tests.cs`, `CrosshairStyle.Tests.cs` — the behaviour, pinned.

---

## 1. What the feature actually is

It is **not** an overlay window. It **overwrites Roblox's own cursor image
files** with a rendered crosshair PNG, so the crosshair becomes the real cursor
the game draws — including the first-person locked cursor. Everything else is UI
around that one action. Roblox reads the cursor at launch, so every change needs
a Roblox relaunch to show.

Three layers:

1. Draw a crosshair to a PNG (portable math, new image APIs on Mac).
2. Write that PNG over Roblox's cursor files (OS-specific — the hard part).
3. UI to choose/build/import a crosshair (rewrite in the Mac UI framework).

---

## 2. The cursor files (layer 2) — the part that differs on macOS

### Windows (for reference)

Per installed version under `%LOCALAPPDATA%\Roblox\Versions\version-*\`:

| File | Path under the version folder | Pixel size |
|---|---|---|
| ArrowCursor | `content/textures/Cursors/KeyboardMouse/ArrowCursor.png` | 64 |
| ArrowFarCursor | `content/textures/Cursors/KeyboardMouse/ArrowFarCursor.png` | 64 |
| MouseLockedCursor | `content/textures/MouseLockedCursor.png` | 32 |

`IBeamCursor.png` is deliberately **left alone** (it's the text caret; replacing
it makes typing look wrong). Bootstrapper mods (Bloxstrap/Fishstrap/Froststrap)
are also written, into `<strap>/Modifications/content/textures/...` — **not
relevant on macOS** unless an equivalent exists; drop that part.

### macOS — what to resolve FIRST, before any UI work

On macOS the same content tree lives **inside the Roblox app bundle**, not under
a per-version folder. You must confirm on a real Mac:

1. **The exact paths.** Likely `Roblox.app/Contents/Resources/content/textures/
   Cursors/KeyboardMouse/ArrowCursor.png` (+ `ArrowFarCursor.png`) and
   `Roblox.app/Contents/Resources/content/textures/MouseLockedCursor.png`, with
   `Roblox.app` in `/Applications`. **Verify** — do not assume. Also check the
   pixel dimensions of each file and match them (don't hardcode 64/32 if Mac
   differs).
2. **Write permission.** `/Applications/Roblox.app` may not be user-writable;
   you may need the user to grant access, or Roblox may be installed per-user.
3. **Code signing / integrity (the make-or-break unknown).** Roblox.app is
   signed and notarized. Editing a resource inside a signed bundle can invalidate
   the signature — macOS or Roblox may refuse to launch, or Roblox may re-verify
   and re-download, wiping the change. **Test this before building anything:**
   replace one cursor PNG by hand, relaunch Roblox, and confirm (a) it launches
   and (b) the custom cursor shows and (c) it isn't reverted. If it doesn't
   survive, the whole approach needs rethinking on Mac — that's the risk flagged
   to the user.

### Reversibility (identical scheme, port as-is)

See `RobloxCursors.cs`. For every file replaced:

- Before the **first** overwrite of a file, copy the original to a sidecar
  `<file>.jinxybak` **only if that sidecar doesn't already exist** (so switching
  crosshairs many times never overwrites the true original).
- **Remove** restores each `<file>.jinxybak` over its file and deletes the
  sidecar.
- "Is a crosshair applied?" = any `.jinxybak` (or strap marker) exists.
- Apply renders **once per distinct pixel size** and reuses the bytes (arrows
  share 64; the locked cursor is 32) — see `RobloxCursors.Apply` caching.

`RobloxCursors.Tests.cs` pins all of this (discover, backup-once, restore,
render-once-per-size, leave-a-user's-own-file-alone). Port the tests too.

---

## 3. Drawing a crosshair (layer 1) — portable math

### The style model (`CrosshairStyle.cs`)

A crosshair is: `Shape, Size, Thickness, Gap, DotSize, ColorHex, DotColorHex?,
OpacityPercent, Outline`.

Shapes: `Dot, Cross, CrossDot, X, XDot, Circle, CircleDot, TShape`.

Clamps (apply them, a hand-edited settings file must not break rendering):
`Size 0..60`, `Thickness 1..12`, `Gap 0..40`, `DotSize 1..24`, `Opacity 0..100`.

Geometry, from the centre, with `g = Gap`, `far = Gap + Size`:

- **Cross / CrossDot / TShape** — 4 axis arms: up `(0,-g)->(0,-far)`, down
  `(0,g)->(0,far)`, left `(-g,0)->(-far,0)`, right `(g,0)->(far,0)`. **TShape
  drops the up arm** (keeps the sky above your aim clear).
- **X / XDot** — 4 diagonal arms: `(g,g)->(far,far)`, `(-g,-g)->(-far,-far)`,
  `(g,-g)->(far,-far)`, `(-g,g)->(-far,far)`.
- **Dot / CrossDot / XDot / CircleDot** draw a centre dot of radius `DotSize`.
- **Circle / CircleDot** draw a ring of radius `Size`.
- `DotColorHex` (if set) colours the dot a second colour — this is how the
  two-tone "Core" and split styles work; else the dot uses the arm colour.

### Rendering to a PNG (`CrosshairImage.cs`)

- Draw with **round line caps**.
- **Outline:** if `Outline`, draw each arm/ring/dot first in a *contrasting*
  colour, one step thicker, behind the colour pass. Contrast = white when the
  arm colour's luma `(0.299R+0.587G+0.114B)/255 < 0.4`, else black. Thicker by
  `max(2, 2*scale)` for strokes, `+max(1, scale)` radius for the dot. (This is
  what makes a dark "Black Plus" visible and a bright crosshair readable on sky.)
- Apply `Opacity` to the brushes.

### Size → the cursor image dimensions (important — this is a real bug we hit)

Roblox draws the cursor **at the image's own pixel size**. So the size slider
must change the **output image's dimensions**, not just how much of a fixed
square the crosshair fills — otherwise the size looks like it does nothing.

`RenderCursorPng(style, basePixels, sizeFactor)`:

```
outPixels = clamp(round(basePixels * sizeFactor), 16, 160)
draw the crosshair into an outPixels × outPixels transparent image,
  scaling the geometry by (outPixels / 64)   // 64 is the authoring base
return the PNG bytes
```

`basePixels` is the file's native size (64 for arrows, 32 for the locked cursor,
or whatever the Mac files actually are). `sizeFactor` = size% / 100, slider range
**40%..160%**, default **100%**. So a 64px arrow at 150% is written as a 96px
image and Roblox shows a bigger cursor; the locked cursor scales in step.

---

## 4. The gallery (`CrosshairGallery.cs`)

Palette: Green `#33FF66`, Cyan `#00E5FF`, Sky `#45B7FF`, Red `#FF3B3B`, Crimson
`#FF2D55`, Pink `#FF57E6`, Yellow `#FFE14D`, Orange `#FF9C3B`, Purple `#A855F7`,
Violet `#8B5CF6`, White `#FFFFFF`, Black `#111111`.

Entries in order — `Name` (Shape, Size, Thickness, Gap, DotSize, Color[, DotColor]):

Crosses: **Green Cross** (Cross,9,3,4,Green) · **Cyan Cross** (Cross,9,3,4,Cyan)
· **Red Plus** (Cross,9,3,4,Red) · **Crimson Plus** (Cross,10,3,3,Crimson) ·
**Black Plus** (Cross,9,3,4,Black) · **Thin Red** (Cross,12,2,5,Red) · **Sniper**
(Cross,20,2,6,Crimson)

Two-tone cores: **White + Purple** (CrossDot,9,3,4,3,White,Purple) · **Red /
Cyan** (CrossDot,9,3,4,3,Red,Cyan) · **Purple Core** (CrossDot,9,3,4,3,Purple,
White) · **Green Core** (…,Green,White) · **Pink Core** (…,Pink,White) ·
**Yellow Core** (…,Yellow,White)

X shapes: **Cyan X** (X,9,3,3,Cyan) · **Red X** (X,9,3,3,Red) · **Green X**
(X,9,3,3,Green) · **Purple X** (X,9,3,3,Violet) · **X + Dot** (XDot,9,3,3,3,White,
Red) · **X Green** (XDot,9,3,3,3,Green,White)

Dots: **Red Dot / Cyan Dot / White Dot / Green Dot / Purple Dot / Pink Dot /
Yellow Dot / Orange Dot / Sky Dot** (Dot, DotSize 4, that colour) · **Tiny Dot**
(Dot,2,White) · **Big Dot** (Dot,7,Red)

Rings + T: **Ring** (Circle,8,3,Cyan) · **Ring + Dot** (CircleDot,9,3,2,Cyan,
White) · **Green Ring** (Circle,8,3,Green) · **Bridge T** (TShape,11,3,4,Yellow)

Default arm colour when unspecified is Green `#33FF66`.

---

## 5. The page UI (layer 3) — behaviour to reproduce

From `MainWindow.Crosshair.cs`. Rebuild in the Mac framework; keep the behaviour:

- **Top card:** live preview, a **SIZE** slider (40–160%, hidden for Default), an
  **Apply to Roblox** button, a **Remove** button, and a status line. Status after
  apply: *"Applied to N cursor file(s) — relaunch Roblox."*; after remove:
  *"Removed — N cursor file(s) back to normal. Relaunch Roblox."*; no Roblox: *"No
  Roblox install found to apply to."*
- **Gallery grid** of tiles, each a rendered icon + name. First tile is
  **Default** (a plain arrow-pointer icon) — selecting it and Applying **restores
  the original cursors** (i.e. Apply routes to Remove) and its size control is
  hidden. Selecting a tile shows *"✓ Switched to X …"*.
- **Per-crosshair size:** each crosshair (built-in, custom, or import) remembers
  its **own** size, kept as a `name → percent` map. Switching crosshairs loads
  that crosshair's size.
- **Make your own:** pick a Shape (Cross, Cross+Dot, X, X+Dot, Dot, Ring,
  Ring+Dot, T), a main colour and a centre colour (with a "None" option); a live
  preview; **Add to gallery** appends a custom crosshair ("Custom N"). Custom
  tiles get a delete ✕.
- **Import image (`CrosshairStore.cs`):** an **Import image…** button opens a
  file picker (PNG/JPG/BMP/GIF); the picked image is **re-encoded to PNG** and
  copied into a `crosshairs/` folder beside the app's settings, stored under a
  random name. It becomes a gallery crosshair ("Import N") with its own tile,
  size and delete ✕. On apply, the imported image is scaled to the cursor size
  the same way (`RenderImageCursorPng`: fit-and-centre into `outPixels`,
  preserving aspect). Deleting it removes the stored file.

### Persistence

Settings fields (see `AppSettings.cs`): `CrosshairName` (selected), `CrosshairSizes`
(`name → percent`), `CustomCrosshairs` (list of `{Name, Shape, Color, DotColor?,
ImageFile?}`; `ImageFile` set ⇒ it's an import). "Is it applied?" is read from the
cursor files on disk, not from settings.

---

## 6. Custom crosshairs — build one, or import a picture

Two ways a person ends up with a crosshair that is not in the gallery. Both end
up as ordinary gallery tiles: they sit after the ready-made ones, they each keep
their own size, and each carries a delete ✕. Everything in §2 (backup, restore,
render once per pixel size) applies to them unchanged — by the time a crosshair
reaches `RobloxCursors`, it is just PNG bytes.

### 6.1 The record

`CustomCrosshair` (in `CrosshairGallery.cs`) covers both kinds:

| Field | Meaning |
|---|---|
| `Name` | Tile label and the key into the per-crosshair size map |
| `Shape` | Shape name, e.g. `Cross`, `XDot` |
| `Color` | Arm colour, hex |
| `DotColor` | Centre colour, hex, or null for "same as the arms" |
| `ImageFile` | Bare file name of an imported picture, else null |

`ImageFile` set is what makes it an import (`IsImage`). Deliberately plain
strings rather than a `CrosshairStyle`: the settings file stays readable, and an
unknown shape or a bad colour falls back to something drawable instead of
throwing. `ToStyle()` fills in proportions that suit a cursor — Size 9,
Thickness 3, Gap 4, DotSize 3 — so a built crosshair is a colour and a shape
decision only. Size is **not** stored here; it lives in the shared
`name → percent` map with every other crosshair's.

### 6.2 Build your own

Controls: a shape (Cross, Cross+Dot, X, X+Dot, Dot, Ring, Ring+Dot, T), a main
colour, a centre colour including a **None** option, and a live preview that
redraws on every change. **Add to gallery** appends the record and selects it.

Names are `Custom 1`, `Custom 2`, … — the first number not already taken, so
deleting `Custom 2` and adding again does not produce two tiles with one name.
Names are the key to the size map, so they must be unique.

### 6.3 Import a picture

The Mac has every piece of this already; use the existing ones rather than
inventing new:

1. **Pick.** `StorageProvider.OpenFilePickerAsync` with an image
   `FilePickerFileType` — the same call and the same file type the wallpaper
   picker uses in `MainWindow.axaml.cs`. Take `TryGetLocalPath()`; a picked file
   with no local path (a cloud item) is a decline, not a crash.
2. **Copy it in, immediately.** Never keep the path the user picked. Copy the
   file into a `crosshairs/` folder beside the settings — on macOS that is
   `~/Library/Application Support/JinxyMac/crosshairs` via
   `SettingsPath.Folder`. `Core/Wallpaper.cs` is the precedent for copying a
   chosen picture into the app's own folder; follow its shape.
   A referenced file would break the moment it was renamed, moved or deleted,
   and an uninstall would leave it behind.
3. **Re-encode to PNG on the way in**, and store under a GUID name. Re-encoding
   means a JPEG, an odd colour profile or a progressive file cannot trip up the
   cursor writing later, when failing would leave Roblox's cursors half
   replaced. The GUID means two imports both called `crosshair.png` cannot
   collide.
4. **Formats:** whatever the decoder actually handles. On Windows that is PNG,
   JPG, BMP and GIF; on macOS, Avalonia decodes through Skia, so follow
   `Wallpaper.Allowed` (`.png .jpg .jpeg .bmp .webp`) rather than copying the
   Windows list. An animated GIF decodes to its first frame — fine for a cursor,
   worth knowing before someone reports it.
5. **Unreadable file:** store nothing, add no tile, and say so in the status
   line ("That file could not be read. Try a PNG or JPEG."). The wallpaper
   picker already words it that way.

On apply, an import goes through `RenderImageCursorPng(path, basePixels,
sizeFactor)`: same `outPixels = clamp(round(basePixels * sizeFactor), 16, 160)`
as a drawn crosshair, the picture fitted and centred into that square preserving
aspect, on a transparent background. If it cannot be read at apply time — the
user deleted it from the folder by hand — fall back to rendering the default
style rather than writing nothing.

Note what this means for a big source image: a 1024px picture becomes a 64px
cursor. Scaling down is the normal case, and scaling up a tiny picture will look
soft. Say it in the UI rather than filtering sizes.

### 6.4 Deleting

The ✕ on a custom tile removes the record, its entry in the size map, and — for
an import — the stored file. A leftover file is harmless and must never take the
app down, so deletion failure is swallowed. If the deleted crosshair was the
selected one, fall back to **Default**.

Deleting a custom crosshair does **not** restore Roblox's cursors. If it was
applied, the cursor files stay as they are until Remove is pressed; the backups
from §2 are what makes that safe.

### 6.5 Persistence

`CustomCrosshairs` in the settings, a list of the records above, written
whenever one is added or deleted. Combined with `CrosshairName` and
`CrosshairSizes`, a restart puts the gallery, the selection and every
per-crosshair size back exactly as they were.

### 6.6 What to test

Port or write, all of it pure and Mac-testable without Roblox:

- Store re-encodes to PNG, returns a name that did not exist before, and two
  imports of the same file get different names.
- Store on an unreadable file returns null and writes nothing.
- Delete removes the file; deleting a missing file is a no-op, not a throw.
- `ToStyle()` maps an unknown shape and a bad colour to something drawable.
- Naming picks the first free number after a delete.
- `RenderImageCursorPng` returns an image of the expected side for a size
  factor, and centres a non-square source without stretching it.

### 6.7 One Mac-specific risk

The app is unsigned and not sandboxed, so an ordinary file path works and no
security-scoped bookmark is needed. If JinxyMac is ever sandboxed or notarised
with a hardened runtime, reading the picked file would need a bookmark — but
because the file is copied in at import and never re-read from its original
location, only the import itself would need revisiting.

---

## 7. Suggested build order on Mac

1. **Prove layer 2 by hand first** (§2): confirm the Mac cursor paths, replace one
   PNG manually, relaunch Roblox, confirm it shows and survives. If it doesn't,
   stop and rethink — the rest depends on it.
2. Port `RobloxCursors` (paths + backup/restore) and its tests.
3. Port `CrosshairStyle` geometry + tests (pure, easy).
4. Port `CrosshairImage` rendering (CoreGraphics/`NSImage`), including the
   size→dimensions rule.
5. Port `CrosshairGallery` data, including the `CustomCrosshair` record (§6.1).
6. Build the page UI and wire Apply/Remove/Default/per-size.
7. Add the custom crosshairs last (§6): build-your-own first, since it needs no
   file handling, then import. Neither changes how cursors are written, so both
   can be built on top of a feature that already works.

Keep the same relaunch-Roblox messaging and the same reversibility guarantees —
those are what make it feel identical.
