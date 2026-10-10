# Clip Studio v1 — Design

**Date:** 2026-10-10
**Status:** Design, awaiting review
**Scope:** The first Pro feature behind the key gate. Hotkey-driven clipping plus a
one-button montage. No auto-detection (see "Why no auto-clip" below).

---

## In one sentence

You press a key to clip your best moments while you play; one button stitches all
your clips into a finished, music-backed montage you can post.

---

## Why this (and why no auto-clip)

The hook for Pro was always "one-button montage of your best moments." The
tempting extra — auto-detecting those moments — turns out not to be buildable
cleanly: the app can't tell a fight from a bridge, because the autoclicker fires
during both, and the only way to really know is to read the game, which is the
cheat line we don't cross. So the moments are chosen by **you** (a hotkey), and
the montage is the automatic part. This is actually better: every clip is a real
moment, no bridging junk to sift through.

It also builds on what already exists — the app already has a rolling replay
buffer (`ReplayBuffer`) that saves the last N seconds to an MP4 on a hotkey, and
ffmpeg is already shipped. Clip Studio is the buffer + a library + a montage
maker, wrapped in a Pro page.

---

## What it does

### 1. Clip (hotkey)
- Clip Studio keeps the rolling buffer running while you play.
- You tap the **clip hotkey**; it saves the **last ~20 seconds** as a clip into
  your clip library. (Length is a setting; 20s default.)
- This reuses the existing `ReplayBuffer.SaveLastAsync` — the capture engine is
  already built and tested.

### 2. Clip library
- A list in the Clip Studio page of the clips you've saved — newest first, each
  with its time and a way to **play**, **delete**, or **open the folder**.
- Clips live in your clip folder (the existing `ClipFolder` setting, defaulting to
  Videos).

### 3. Montage (the hook)
- One button: **"Make montage."** It takes the clips you choose (default: all of
  them), joins them in order with a clean hard cut between each, lays **background
  music** over the whole thing, and exports one finished video.
- Built with ffmpeg (already shipped). Clips are normalized to a common
  size/framerate, concatenated, and the music is mixed in (trimmed or looped to
  the montage length).
- A simple **progress indicator** while it renders (montaging several clips takes
  a few seconds to a minute).

### 4. Export shape
- **Landscape** (16:9) by default — for YouTube.
- A **vertical (9:16) toggle** — for TikTok / Shorts / Reels. On export it reframes
  to vertical (center-crop) rather than letterboxing.

---

## Music (decision for review)

Recommended: **ship 1–2 genuinely free-for-commercial-use (CC0) tracks** so the
montage has music out of the box, **plus** a "use my own audio file" picker.

- The bundled tracks must be real CC0 (e.g. from Pixabay Music) — never ripped
  songs, which would be a copyright problem and could get the app taken down.
- "Use my own file" lets a creator drop in whatever they want (their own risk).

Alternative if you'd rather keep it simplest: **own-file only** for v1, add
bundled tracks later. Pick one at review.

---

## Where it lives

- A new **CLIP STUDIO** page in the sidebar's **CAPTURE** group, locked behind
  `IsPro` (the gate we already built).
- Free users keep the basic manual replay-buffer clip they have today; **auto-
  nothing changes for them**. The Clip Studio page (library + montage + vertical
  export) is the Pro part.

---

## How Pro-gating works here

- The CLIP STUDIO page reads the existing `IsPro` switch.
- When not Pro: the page shows the feature list and a "Unlock with Pro" prompt
  (the locked-state pattern), and the montage/clip controls are disabled.
- When Pro: everything is live.

---

## What v1 deliberately leaves out

- Auto-clip / moment detection (not buildable cleanly — see above).
- Beat-synced music, transitions, effects.
- Kill-counters / overlays / intros / outros.
- Thumbnail maker.
- Watermark-on-free-clips upsell (a later monetization lever).
- Picking a subset of clips with reordering UI — v1 montages **all** clips in the
  library in saved order; "delete" is how you leave one out.

---

## The honest caveats

- Montage quality is "clean cuts + music," not a hand-edited edit. Postable, not
  Premiere.
- Rendering a montage re-encodes video, so it uses the CPU/GPU for a few seconds
  to a minute depending on clip count — done when you ask for it, not while you
  play.
- Vertical export center-crops, so action at the far edges of a landscape clip can
  get cut off. That's inherent to reframing 16:9 into 9:16.

---

## Components at a glance

| Piece | New or existing | Job |
|-------|-----------------|-----|
| `ReplayBuffer` | existing | rolling buffer + save last N seconds |
| Clip hotkey | existing hotkey system, new binding | triggers a save |
| Clip library | new | list, play, delete, open folder |
| `MontageMaker` | new | ffmpeg: normalize → concat → music → export (landscape/vertical) |
| CLIP STUDIO page | new (XAML + handlers) | the Pro UI for all of the above |
| `IsPro` gate | existing | locks the page for free users |

---

## Open questions for review

1. **Music:** ship 1–2 CC0 tracks + own-file (recommended), or own-file only?
2. **Clip length:** 20s default good, or different? (It's a setting either way.)
3. **Montage = all clips** in saved order for v1 — OK, or do you want tick-to-
   include even in v1?
