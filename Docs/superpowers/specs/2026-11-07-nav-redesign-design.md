# Nav redesign: tab merges + sidebar customization

**Status:** draft for review (2026-11-07). Two parts — the tab merges (bounded)
and the sidebar customization (architectural). Nothing here is coded yet.

**Origin:** community/user feedback (Discord screenshots) that the app has too
many tabs ("there is too much"), plus a request to "rename and move stuff in
settings with customization."

**Decisions locked with the user:**
- Customization depth: **reorder + hide + rename** (the full version).
- Approach the merges first, then the customization.

**Open — needs the user's confirmation before the plan is written:**
1. The exact merge map in §2 (groupings).
2. Unrelated: the "add this with the recording" item from the 3rd screenshot
   (tracked elsewhere, not part of this doc).

---

## 1. Goals

- Cut the number of sidebar rows by merging related tabs into one page with
  sub-tabs (no feature removed — just fewer top-level rows).
- Let the user **reorder**, **hide**, and **rename** sidebar items, saved across
  restarts, edited from a panel in Settings.
- Keep every existing feature, handler, Plus-gate and deep-link working.

## 2. Part A — Tab merges (proposed)

Each surviving page gains a sub-tab bar (the existing generic `PageTab_Checked`
pattern: a `UniformGrid` of `RadioButton`s whose `Tag` is the panel name). The
moved page's content becomes a sub-panel; its controls keep their `x:Name`s so
handlers bind unchanged.

| Surviving page | Sub-tabs | Removes |
|---|---|---|
| **Clicker** | Clicker · Shaky Tracking · Hit Registration | NavShake, NavHitReg |
| **Automation** (was Macros) | Macros · Auto Switcher | NavSwitcher |
| **Performance** (new) | Optimizations · Tweaks · Lower Ping | NavTweaks/NavOptimizations/NavLowerPing → one row |

Net: ~5 fewer sidebar rows.

**Gating:** Hit Registration and Lower Ping are Plus. As sub-tabs they stay
visible so Free users see them, and selecting one while Free routes to the key
page exactly as the standalone tabs do now (reuse each page's existing
`ApplyXPro()` bounce).

**Risk:** the merged pages mix a Free page (e.g. Optimizations) with a Plus page
(Lower Ping) under one row. The row itself must be reachable by Free users (so it
can't be Plus-gated as a whole); only the Plus sub-tab gates. Confirm this is the
intended behaviour.

## 3. Part B — Sidebar customization

### 3.1 Approach: reorder existing buttons, don't regenerate them

The sidebar is currently static XAML: each nav item is an `x:Name`d `Button`
referenced by code-behind handlers and by the `NavButtons`/`Pages` arrays.
Regenerating the nav from data would break all of that.

Instead, keep the static buttons and **apply a saved layout to them at runtime**:
- set each button's `Content` to its custom label (or its default),
- set `Visibility` to Collapsed for hidden items,
- reorder the buttons within/between the group `StackPanel`s per the saved order.

This reuses every button, handler, gate and the arrays untouched. The layout is
data; the buttons stay the source of identity.

### 3.2 Data model

A new `NavLayout` persisted in `AppSettings` (one JSON string, parsed into a list):

```
record NavEntry(string Id, int Order, bool Hidden, string? CustomLabel);
```

- `Id` — a stable key per nav item (its `x:Name`, e.g. "NavClicker").
- `Order` — global order; the sidebar sorts by it.
- `Hidden` — collapses the row (and, if it's the current page, falls back to
  Clicker).
- `CustomLabel` — overrides the displayed text; null = use the default.

Unknown ids in a saved layout are ignored; nav items with no saved entry fall to
their default order/label/visible (so a new build's new tab just appears).

### 3.3 Grouping

Keep the group headers (CLICKING / GAME / PERFORMANCE / APP / PLUS). v1:
reordering and hiding happen **within the existing groups**; moving an item to a
different group is out of scope for v1 (revisit if asked). Rename applies to any
item. *(Confirm: is within-group reorder enough, or do you want to drag items
between groups too?)*

### 3.4 The editor (in Settings)

A new **"Layout"** sub-tab on the Settings page (alongside General / … /
Feedback). It lists every nav item grouped as in the sidebar, each row with:
- up/down arrows (or drag) to reorder within its group,
- a hide/show toggle,
- an inline editable label (empty = reset to default),
- a "Reset all" button.

Changes apply live to the sidebar and save immediately. Reset returns an item (or
all) to defaults.

Rationale for arrows over drag for v1: WPF drag-reorder is fiddly and error-prone;
up/down arrows are simpler, accessible, and fully cover "move stuff." Drag can be
a later polish. *(Confirm you're OK with arrows for v1.)*

### 3.5 What stays fixed

- Get Plus and Join Discord (bottom-docked) are not customizable — they're
  pinned chrome, not tabs.
- Group headers themselves aren't renamable in v1.

## 4. Sequencing

1. **Phase 1 — merges** (§2): mechanical, testable, removes the clutter now.
2. **Phase 2 — customization** (§3): the model + persistence + runtime apply +
   the Settings editor.

Each phase ships and is reviewed on its own.

## 5. Testing

- Merges: the moved sub-panels keep their control names, so existing behaviour
  tests are unaffected; add a test that each merged page's sub-tab map is wired
  (bar `Tag` → an existing panel name).
- Customization: unit-test the `NavLayout` model — parse/serialize round-trip,
  unknown-id tolerance, missing-entry defaults, order stability, label reset. The
  runtime apply (reparenting buttons) is UI-timing code tested by hand in the app.

## 6. Non-goals (v1)

- Moving items between groups (within-group only).
- Renaming/reordering group headers.
- Per-profile or cloud-synced layouts (local only).
- Drag-and-drop (arrows instead).
