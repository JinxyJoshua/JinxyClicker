# Pro Key System — Design

**Date:** 2026-10-09
**Status:** Design, awaiting review
**Scope:** The license/key system only. Clip Studio and the individual Pro
features get their own specs later. This is the foundation they all plug into.

---

## In one sentence

Let the app tell a paying user from a free user, using a key the buyer pastes
in, that works with no server and can't be faked.

---

## Why this first

Nothing "Pro" can be sold until the app can check whether someone paid. Every
future paid feature — Clip Studio included — hangs off this one gate. It is
small, finishable, and reusable: build it once, and every later feature plugs
in with a single line. So it goes first.

---

## The three things you sell

| Key type | What they get | Price |
|----------|---------------|-------|
| Day pass | Pro for 1 day | $2 |
| Weekend pass | Pro for 3 days | $4 |
| Month pass | Pro for 30 days | $8 |
| Lifetime | Pro forever | $20 |

Lifetime is the real product and is rock-solid. The passes are a cheap on-ramp
and are "good enough" (see Honest limits below).

Note on the month pass: offline, it is **not** an auto-renewing subscription —
there is no processor to charge each month. It is simply a 30-day pass the buyer
rebuys by hand when it runs out. It earns its place by catching the buyer who
won't risk $20 "forever" on a tool that could someday be taken down, but will pay
$8 to use it for a month. Priced at $8 (2.5 months = lifetime) it does not
cannibalize lifetime; $10 would.

---

## How a key works (plain version)

Think of it like a wax seal on a letter.

- You own a **stamp** (a secret "signing key") that only you have. It lives on
  your PC and never ships with the app.
- When you make a key, you stamp it. The stamp proves *you* made it.
- The app carries a **picture of your stamp** (a "public key"). It can *check*
  that a key was stamped by you, but it can't *make* the stamp. So the picture
  is safe to ship — nobody can forge keys from it.

A key is a short string the buyer pastes in:

```
JINXY-LIFE-7K4M-9QR2-X8WD-4FP1
```

Baked inside it, and sealed by the stamp so it can't be edited:

- **Tier** — day, weekend, or lifetime
- **Length** — 1 day / 3 days / forever
- **A unique ID** — so one specific leaked key can be killed later

When the buyer pastes it in, the app checks the seal. Good seal → Pro unlocks.
For passes, the app also notes the date of first use so it can count down.

### How it works (technical version)

- Signing uses **ECDSA on the P-256 curve with SHA-256**. This is built into
  .NET (`System.Security.Cryptography.ECDsa`) — no third-party library.
- The signed payload is a small byte blob: `[version][tier][durationDays][keyId]`.
  `durationDays = 0` means lifetime. `keyId` is 8 random bytes.
- The final key string = `payload + signature`, Base32-encoded (Crockford
  alphabet — no confusable 0/O or 1/I/L), grouped into dash-separated blocks
  with a `JINXY-` prefix for looks.
- The app holds only the **public key** (a constant in source). It verifies the
  signature, rejects anything whose ID is on the blocklist, then reads the tier
  and length out of the verified payload.

---

## Free vs Pro — what's behind the gate

The rule: **never lock what people already use for free** (that's a rug-pull and
loses your userbase). Keep the free floor, sell the Pro ceiling.

| Feature | Free floor | Pro ceiling |
|---------|-----------|-------------|
| Clicker | full speed, always | — (never locked; it's the hook) |
| Kit Wheel | works as now | saved loadouts / extra slots |
| Presets | up to 3 | unlimited |
| Crosshair | basic presets | custom color/glow, import your own, per-kit |
| Recorder | short manual clip | Clip Studio (auto-clip, montage, export) |
| Theme | 1–2 basic | all themes, animated backgrounds, custom colors |

**Open decision (not blocking this spec):** whether to *also* hard-lock the two
brand-new 1.4.15 features — Server/globe and Gaming Mode — fully behind Pro.
They're new enough that locking them causes little backlash, and it gives Pro
two complete features on day one. Recommended, but can be decided when we build
the feature gates.

**Not touched by monetization:** the Clicker core, the Kit Wheel core, and the
Mod page.

---

## How the gate works in the app

**One source of truth.** At startup the app builds a single `License` object
that answers one question: `IsPro`. It works it out once — "is there a stored
key, is its seal valid, is it on the blocklist, and (for passes) has it run
out?" — and every feature just reads that answer. No feature checks keys on its
own.

```
if (License.IsPro) { ...unlock... }
else { ...show locked state... }
```

**Locked, not hidden.** Pro features stay visible but greyed, with a lock and a
"Pro" badge, plus one "Unlock with Pro — $20 / Enter key" bar. Free users see
what they're missing — that's the storefront. Clicking a locked card or the bar
opens the paste-your-key box.

**What's stored** (in the existing `AppSettings`, under `%APPDATA%\JinxyClicker`):

- `LicenseKey` — the raw key string the buyer pasted
- `LicenseFirstUseUtc` — the date a pass was first activated (only used by passes)

Every launch re-checks the seal from scratch. Deleting or editing the stored key
just drops the user back to free — there's nothing to forge.

---

## The key-generator tool (your cash register)

A tiny console program, `KeyGen`, that you run on your own PC. It never ships in
the app and never goes in the public repo.

- **First run — `keygen init`:** makes your stamp. Saves the secret signing key
  to a local file (passphrase-encrypted), and prints the public key once for you
  to paste into the app's source.
- **Make a key — `keygen make --tier lifetime`:** prints a key to send the buyer,
  and appends a row to a local `keys.csv` (id, tier, date) so you can track sales
  and know which ID to kill if one leaks. Also `--tier day` / `--tier weekend`.

**Two things to guard with your life:**

1. **The secret signing key file** — back it up. Lose it and you can't mint new
   keys (old ones keep working; you'd ship a new public key in an update to mint
   a fresh batch).
2. **The `KeyGen` project** — excluded from the installer and publish so it can
   never land on a buyer's PC.

**Graduates later with no rewrite.** When you move to a shop / Discord bot, the
same signing logic gets wrapped to run `make` automatically on payment. The app
and the key format don't change.

---

## Honest limits (no overselling)

- **Lifetime keys are bulletproof.** The seal can't be forged offline.
- **Passes are best-effort.** A pass counts down from a date stored on the
  buyer's PC, so a determined user could roll back their clock or clear that date
  to stretch a $2 pass. Almost nobody will bother; the ones who would were never
  paying. This hardens automatically when you add a server/shop.
- **A leaked key works until you kill it.** Offline, "killing" a key means adding
  its ID to a blocklist that ships in the next app update — so it only stops
  people who update. Each key being unique is what makes this workable.
- **The business ceiling is the audience, not the code.** A free autoclicker
  draws price-sensitive players who are there *because* it's free; converting
  them to $20 is inherently hard. A strong anchor feature (Clip Studio) is what
  pushes past that — not the key system itself.

---

## Build order

1. **Signing core** — a small shared piece that makes and checks keys (so the app
   and `KeyGen` use the exact same logic). Built test-first.
2. **`KeyGen` tool** — `init` and `make`, writing the local log.
3. **`License` object in the app** — reads the stored key, verifies, exposes
   `IsPro`; stores key + first-use date in `AppSettings`.
4. **The "enter key" box + locked-card UI** — paste a key, see it unlock; locked
   state and the unlock bar.
5. **Wire the first gates** — put the cheap existing features (crosshair Pro, the
   bundle) behind `IsPro`.

Clip Studio is a separate spec, built after this and dropped into gate #5.

---

## Components at a glance

| Piece | Lives in | Job |
|-------|----------|-----|
| Signing core | app + shared | make/verify the sealed key blob |
| Public key constant | app source | lets the app check seals |
| `License` | app | one `IsPro` answer for everything |
| Key entry UI + locked cards | app | paste a key, show locked/unlocked |
| `AppSettings` fields | app | remember the key + first-use date |
| Blocklist | app source | refuse known-leaked key IDs |
| `KeyGen` | separate project (not shipped) | mint and log keys |
| Secret signing key file | your PC only | the stamp; never shipped, never committed |

---

## Open questions for review

1. Safe vs aggressive on Server/globe + Gaming Mode — lock them now, or leave free?
2. Key length: the signature makes the key fairly long (~40–50 chars with dashes).
   Acceptable as a paste-in, or do you want it shorter (weaker scheme)?
3. Where buyers are told to paste after paying — a link in the unlock box to
   wherever you sell? (Out of scope to build, but the box should point somewhere.)
