# M37 — Grey stops meaning two things

**Delivered 2026-08-07.** §14.4's largest visual item, and the one held back through M27 as the
owner's decision rather than a coding one. The decision taken was **"border on pressable buttons"**.

---

## 1. What was measured

Sampled from the shipped `hero-issue-page1.png`, where Undo, Redo and Previous page are genuinely
disabled (fresh document, page 1 of 5):

| Button | State | Fill |
| --- | --- | --- |
| Open | enabled | `#BEBFC2` |
| Undo | **disabled** | `#BEBFC2` |
| Redo | **disabled** | `#BEBFC2` |
| Previous page | **disabled** | `#BEBFC2` |
| Next page | enabled | `#BEBFC2` |

**Identical.** The only thing separating "you can press this" from "you cannot" was the label:
**11.42:1** enabled against **2.61:1** disabled.

That second number is the sharper problem for this audience. It is not a weak signal, it is an
unreadable one — an elderly user could not make out *what the button they cannot press even says*,
and the reason it is unavailable lives in its `HelpText` (M11), where a sighted user never looks.

Meanwhile the action panel's items — which by M11 are **never disabled**, because they explain
themselves instead — were the same grey slab. So one appearance meant "press me" and "you cannot"
at the same time, in one window.

## 2. What changed

`TrestleBoard.ActionButton`, a `ControlTheme` opted into by class:

- **Enabled**: a border in `Chrome.Foreground`, thickness from the `Border.Thickness` token so High
  Contrast steps it up like every other rule in the app. Label at full contrast.
- **Disabled**: no border **and no fill**. The button sits flat on the chrome. Removing the fill
  matters as much as removing the border — a dead control that still has a slab under it is still
  competing for attention it has not earned.

Measured after:

| | Before | After |
| --- | --- | --- |
| Enabled fill | `#BEBFC2` | `#BEBFC2`, with a `#14181F` border |
| Disabled fill | `#BEBFC2` | `#EDEFF2` — the chrome itself, no border |
| Disabled label | 2.61:1 | **6.54:1** |

`Chrome.Border` could not be the border: 2.36:1 against the button fill, under §6's 3:1 floor for a
boundary that must be perceived. `Chrome.Foreground` is 9.68:1.

The border is a **shape**, not only a colour, so §6's "colour is never the only signal" holds — and
High Contrast gains the most, where white-bordered buttons now stand out sharply on black.

## 3. Why it is opted into rather than automatic

`Theme/Controls.axaml` already documents why a bare `Style Selector="Button"` is unavailable: a
ScrollBar's repeat buttons and a ComboBox's drop-down toggle **are** Buttons and would take any
border with them.

So `Tokens.Action()` is the one way to ask, mirroring the existing `Tokens.Primary()`. Forty-odd
call sites is exactly the kind of list that gets one missed — and a missed one looks *disabled*,
which is the bug.

**The test is what makes it safe.**
`ActionSurfaceTests.EveryButtonTheAppMadeSaysWhetherItCanBePressed` walks the window and fails on
any button carrying neither treatment. `TemplatedParent is null` is the discriminator: a button the
app constructed has none; one Fluent built inside a template has its host. It found all sixteen
unmarked buttons on the first run, which is how the toolbar and panel came to be covered at their
two existing loops rather than one button at a time.

## 4. Scope, stated honestly

The automated guard covers **MainWindow** — the toolbar, the action panel and the collapsed-panel
button, which is where the user spends the whole session. The dialogs were marked by hand, at their
`MakeButton` helpers where they had one and at each construction where they did not. A future dialog
that forgets `.Action()` will not be caught by a test unless it is added to that walk.

Screenshots regenerated: `start-screen`, `hero-issue-page1`, `high-contrast`.

Suite after M37: **1185 passing, 12 skipped**. No snapshot baseline moved — this is chrome, and
never reaches the export path.
