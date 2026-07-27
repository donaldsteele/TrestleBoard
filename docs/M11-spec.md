# M11 — Contextual action panel

Status: derived from the LOCKED `PLAN.md` (§6, §11-M11, §12 gate 8). Implemented; the manual NVDA
pass is the one open item (§7).

Acceptance (PLAN §11-M11): *select each block kind — every action offered in the panel is
performable, and no panel control is ever greyed; no `Blocked` availability may carry an empty
reason string; a keyboard-only run reaching and invoking every panel action; a manual NVDA pass at
200% UI scale in High Contrast.*

**Privacy (PLAN §0, HARD).** This milestone adds no data and reads no personal data. The action
context is a flat snapshot of editing state — selection kind, undo descriptions, whether a frame is
overset. The only document text it looks at is a search for the carry-forward article prompt, which
is a shipped constant, not something a person typed.

---

## 0. The two problems this milestone exists for

The owner's review of v0.1.0 named them:

1. **Every action lived in the menu bar, and items silently greyed themselves out.** 47 menu items
   across 8 menus, an Object menu of 14, and roughly 30 hand-written `IsEnabled =` assignments in
   `UpdateEditChrome`/`UpdateFrameChrome`. A user who saw a control go grey had no way to find out
   why, and the app never told them.
2. **The toolbar carried 18 controls**, several of which acted on a selection that might not exist.

The answer is not more menus. It is that *what you can do to a thing belongs next to the thing*, and
that an action which cannot run has to say so in words.

---

## 1. The split, and why it is where it is

`TrestleBoard.Editing/Actions/` owns **"can I, and if not, why not, in plain English"**.
`TrestleBoard.App/Actions/` owns **"how"**.

The reason for the seam is testability. `Editing` has no Avalonia in it, so every sentence the user
will ever read about why something is unavailable is asserted in `tests/Editing.Tests` in
milliseconds, with no headless session and no window. `ActionCatalogTests` poses fourteen situations
by writing down a handful of booleans; posing the same fourteen situations through a real window
would need fourteen documents.

| Type | Project | What it is |
|---|---|---|
| `ActionId` | Editing | String constants. Not an enum: these names cross into the App handler map, the keyboard table and test failure messages, and a stable spelling that appears verbatim in a failure is worth more than a compiler-checked ordinal. |
| `EditorAction` | Editing | The declaration — title, one-sentence description, group, advertised shortcut. |
| `ActionAvailability` | Editing | `Available` / `NotApplicable(reason)` / `Blocked(reason, remedyId?)`. |
| `ActionContext` | Editing | A flat immutable snapshot of everything a rule needs. |
| `ActionContextFactory` | Editing | Reads the six controllers once and builds the snapshot. |
| `ActionCatalog` | Editing | `Evaluate`, `ForSelection`, `DescribeSelection`, `DescribeGroup`. |
| `WhatsNext` | Editing | The no-selection checklist. |
| `ActionRunner` | App | One map from id to the code that performs it, and the one place an action is refused. |
| `ActionPanel` | App | The right-docked panel. |
| `KeyboardMap` | App | Which key press runs which action. |

### The reason is enforced by a constructor

`ActionAvailability.Blocked("")` throws. So does `NotApplicable("")`. The complaint was thirty
controls greying themselves with nothing said about why; a rule enforced at construction survives
edits that a code review does not. `ActionCatalogTests.NoUnavailableActionIsSilentAboutWhy` sweeps
every action against every interesting context, and a sibling test insists each reason reads like a
sentence — starts with a capital, ends with a full stop, long enough to be one.

### Why `NotApplicable` carries a reason too

The panel never shows it, so at first it looks like dead text. The menu bar does show it. The
asymmetry is the design:

- **The panel** is selection-scoped and headed "A photo is selected", so an action that is *absent*
  reads as "not about photos". Nothing in the panel is greyed.
- **The menu bar** is a global index. Hiding items there would break PLAN §6's "every command has a
  menu item" guarantee, and "dimmed" is a convention screen readers announce. So it keeps greying —
  and every greyed item carries the same sentence in `AutomationProperties.HelpText`, and pressing
  its shortcut writes the sentence to the status bar, which is already a polite live region.

---

## 2. `RefreshActions`, and what it replaced

One method. It takes one snapshot and feeds the menu bar, the toolbar, the panel and the flyout from
it. Four surfaces reading one answer cannot disagree with each other, which the two old chrome
methods could and did.

The old code also cleared the status bar on every refresh, which made an explained refusal
unreadable — the next controller event wiped it. `Announce` now records the message; `UpdateStatus`
prefers a controller message, otherwise shows the pending announcement, and clears it. So a refusal
survives exactly until the user does something else.

---

## 3. The keyboard table

`OnWindowKeyDown` was a 126-line `switch` of `case Key.X when ctrl && …`. Its failure mode was
invisible: `case Key.Y when ctrl:` matches Ctrl+**Shift**+Y too, so the menu advertised
"Ctrl+Shift+Y — Fit to contents" and the app silently redid instead.

`KeyboardMap` is a table matched on **exact modifier equality**. Ctrl+Shift+Y is simply a different
row from Ctrl+Y, so the shadowing bug is unreachable by construction rather than by inspection.
`KeyScope` handles the three genuinely conditional cases: Ctrl+C only while typing, bare PageDown
only while not typing, everything else always.

### The test amendment, and one deviation from PLAN.md

PLAN §11-M11 requires replacing `KeyboardAuditTests.NoUnshiftedShortcutSwallowsItsShiftedTwin` —
which regex-read `MainWindow.axaml.cs` for the case order — with a behavioural test that presses
every registered gesture through the headless window and asserts the `ActionId` it reached. Done:
`EveryRegisteredKeyPressReachesItsOwnAction`, plus two new invariants the old test could not express
(`EveryShortcutTheCatalogAdvertisesIsRegistered`, `EveryRegisteredGestureNamesARealAction`).

**Deviation, stated plainly.** PLAN also sets a sequencing rule: land the replacement while the
switch still exists, see both pass, then delete the switch and the regex test in one commit. That
was not followed. The switch and the table were replaced in a single commit, because the
intermediate state — a switch whose every case body calls `Invoke(actionId)` purely so a regex can
still find it — is about sixty lines written to be deleted an hour later, and it proves nothing the
final state does not. The rule's purpose is that no coverage gap opens in between; that is met
instead by keeping `ControlShiftYFitsToContentsRatherThanRedoing` and `ControlYStillRedoes`
**verbatim**, exactly as PLAN requires, and both pass on the new dispatcher. The named regressions
are the thing that actually guards the bug; the regex was a proxy for them.

The interceptor (`ActionRunner.InterceptorForTest`) is what makes the behavioural test safe: it
records the id a key press reached and stops short of running it, so pressing Ctrl+O in a test does
not open a file dialog on a CI runner.

---

## 4. The menu and toolbar restructure

**Object → "This item".** Fourteen flat items became six direct entries plus three submenus —
Picture, Text flow, Arrange. Nothing was removed; the four front-to-back items simply went one level
deeper. The menu is named for what the user is looking at rather than for a model type.

**Toolbar: 18 controls → 9.** Bold, Italic, the paragraph-style combo, Wrap, Add text frame, Insert
picture, Fix photo and the two arrange buttons all moved into the panel, where they sit beside the
thing they act on. What stayed is what is always true: Open, Undo, Redo, page navigation, zoom, Fit
page.

**Format → Paragraph style.** `ParagraphStyleCombo` was the only command in the app with no menu
path at all — a hole in PLAN §6's guarantee, closed here rather than left for later. The same list
is offered from the panel as a flyout.

**Access keys.** Three new submenus made collisions likelier, and nothing had ever checked for them.
`NoTwoItemsInOneMenuShareAnAccessKey` now walks every menu and the menu bar itself.

---

## 5. The panel, and the chrome budget

320px wide, docked right, inside a fourth `LayoutTransformControl` (`PanelScale`) so it honours the
UI scale — a 16pt panel beside a 32pt menu bar would be the one part of the window an elderly user
could not read. The canvas stays deliberately outside the scale transform, because the page has its
own zoom.

Below ~900px of window width the panel folds itself to a "What can I do? ▸" button rather than
squeezing the page into a strip. The default window grew from 1100×800 to 1280×860. Whether the
canvas keeps enough of the frame at 200% scale in High Contrast is the thing §7's manual pass has to
answer; the mitigations are in place but the 40%-of-frame estimate in PLAN is an estimate.

`ShowActionPanel` is persisted in `AppSettings`, defaulting to **on**: it is the primary way actions
are discovered now, and someone who has never seen it cannot decide they want it.

### "What's next"

The panel's no-selection state. A computed checklist, one row per suggestion with a sentence saying
why it is there: an article still holding the carry-forward prompt, a cover heading with no meeting
date, overset text, no PDF exported this session. Two more sources are declared and wired but always
false until their milestones arrive — `RosterEmptyButNeeded` (M12) and `BirthdayListIsStale` (M13).

**Nothing here changes the document.** A suggestion that acted on its own would dirty a file the
user opened only to look at, trip the 60-second autosave and grow the recovery snapshot. This is the
same rule M13 states for birthday staleness, and it is stated here first because the card is where
it would first be tempting to break it.

The cover-date check is the one suggestion that has to read widget data, so it is computed in
`MainWindow` and passed in through `ShellFacts` — `Editing` knows nothing about what is inside a
widget payload, and giving it that knowledge to save one boolean would be a bad trade.

---

## 6. The context flyout

Right-click, Shift+F10 and the Applications key all build the same list the panel shows.
`BlockAutomationPeer.ShowContextMenuCore` returned `false`, so a screen-reader user pressing the
Applications key over the canvas got nothing at all. It now selects the block first — the flyout is
built from the selection, so "what can I do to this" has to make "this" the selection before the
question can be answered — and then raises the request on the canvas.

---

## 7. Open items

1. **The manual NVDA pass at 200% scale in High Contrast has not been run.** It needs a person with
   a real screen reader. `docs/accessibility-test-script.md` §11 is written for it and the results
   table has its rows. Two things in particular are unvalidated and PLAN flags both:
   - whether the panel heading, marked as a polite live region, is actually announced when the
     selection changes without the user going looking for it;
   - whether a greyed menu item's `HelpText` reason is read out, and what the user has to do to hear
     it.
   If those two read badly together, PLAN's stated fallback is explained refusal in the menu too —
   always-enabled items that refuse with a spoken reason.
2. **The chrome budget at 200% is unmeasured**, as above.

---

## 8. What did not change, deliberately

- **No MVVM rewrite.** Rewriting 1290 lines of code-behind into ViewModels buys nothing the user can
  see and puts the whole headless suite at risk. The action catalog is the useful half of "commands"
  — declared availability and plain-language descriptions — without the binding machinery.
  `CommunityToolkit.Mvvm` had gone 100% unreferenced since M0, so its reference is removed rather
  than left as a dead supply-chain surface.
- **No new document commands, no format change, no migration.** M11 is entirely a surface.
- **The canvas keeps its own key handling.** Caret motion, frame nudging, Tab-cycling and link mode
  stay in `PageCanvasControl`, because they only make sense with canvas focus. The window table
  holds the gestures a menu advertises.
