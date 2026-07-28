# M21 — Everyday conveniences other publishing apps have

**Delivered 2026-07-28.** Implements PLAN.md §11 M21, the last of the five hands-on milestones. This
file records what was built, where it differs from the plan, and what is still open.

M21 is the milestone of things whose absence a user notices without being able to name them. Nothing
here is a new capability of the newsletter: every one of these commands moves, finds or shows
something the app could already have done by hand. What they buy is the difference between an editor
that can be driven and one that can only be operated.

**Nothing was cut**, including the marquee, which the plan named as the cut candidate.

> **No pixel of the printed page moved.** M21 opened no file in `TrestleBoard.Layout`,
> `TrestleBoard.Rendering` or `TrestleBoard.Export.Pdf` — the restriction PLAN.md §11 M18 placed on
> M17–M21 — and the snapshot suite re-ran **unchanged**: 162 passed, 12 skipped (the Linux-only
> `pdftoppm` parity tests). The new on-page chrome (the multi-selection outline and the marquee) is
> drawn by `PageCanvasControl` with Avalonia's own primitives, exactly as M17's hints are, which is
> what makes that true by construction rather than by luck.

---

## 1. The four deliverables

| | Deliverable | Where it lives |
|---|---|---|
| 1 | **Ctrl+wheel zoom about the pointer**, and middle-drag / Space+drag pan | `PageCanvasControl` reports, `MainWindow` answers |
| 2 | **Find (Ctrl+F) and replace (Ctrl+H)** across stories and linked chains | `StoryFinder` (Core), `FindController` (Editing), `FindWindow` (App) |
| 3 | **Multi-select, with six align and two distribute commands** | `FrameAlignment` + `FrameEditorController` (Editing) |
| 4 | **The text session survives losing focus** — M17's deferral | `PageCanvasControl.OnLostFocus` |

---

## 2. Zoom about the pointer (never-cut)

`PageCanvasControl` raises `ZoomAtPointerRequested` with the pointer position and a direction;
`MainWindow.ZoomAtPointer` owns the arithmetic, because holding a point still is a question about
the **scroller**, which the canvas cannot see:

1. read the page point under the pointer at the old zoom;
2. step the zoom ladder (the existing `view.zoomIn`/`view.zoomOut` path — no second ladder);
3. `UpdateLayout()`, because the canvas has just changed size and the extent with it;
4. work the same page point back into control pixels and shift the scroller by the difference.

`ConveniencesShellTests.ControlWheelZoomKeepsThePointUnderThePointerStill` measures exactly that,
to within a pixel — the scroller snaps its offset to whole pixels, so "still" can mean no finer.

**At an edge it clamps, and that is the right answer.** With the view already at offset zero,
zooming out cannot hold a point that would need a negative offset. The test scrolls away from the
corner first and says so, rather than asserting something that is not true.

Panning is the same division of labour: the canvas knows the gesture (middle button, or Space held
with the left button), the shell moves the scroller. The pan anchor deliberately does **not** move
between moves — scrolling drags the canvas out from under the pointer, so the next position measured
against the control comes back to the anchor by itself, and re-anchoring would double every delta.

Space arms panning only when the user is **not** typing. Inside a text session a space is a space,
and always will be.

---

## 3. Find and replace

**Three layers, and the split is the point.**

- `TrestleBoard.Core/Text/StoryFinder.cs` — pure, BCL-only: story order, every hit, the next hit
  after a given one, and wrapping. It searches **stories**, in the order a reader meets them.
- `TrestleBoard.Editing/FindController.cs` — what is being looked for, where the last hit was, the
  sentences the user reads, and the commands a replacement becomes.
- `TrestleBoard.App/Dialogs/FindWindow.cs` — the window.

**Why the linked chain needs no special case.** A chain is one story flowing through several frames,
so a hit in the second frame is simply a hit further down the same paragraph list. The frame is read
back afterwards, from the caret geometry, by the one new entry point into a text session:
`TextEditorController.SelectRange`. That is the whole of M21's acceptance criterion "find reaches the
second frame of a linked chain", and `FindControllerTests.FindReachesTheSecondFrameOfALinkedChain`
builds a two-page chain to prove it.

**Widget payloads are not searched**, as the plan scoped pass one — and the empty-result message says
so out loud:

> "zamboni" is not in the writing on the page. TrestleBoard does not look inside the lists it fills
> in for you, such as the officers table or the birthday list.

Half-searching them would have been worse than not searching them: matching inside the officers table
without being able to select or replace there teaches the user a rule that is not true. Saying it is
the cheap, honest version, and `FindController.WidgetsNotSearched` is one string with one test on it.

**The window is deliberately not modal**, alone among this app's dialogs. It exists to point at
something on the page behind it. That is only possible because of deliverable 4 — before M21, opening
any window threw away the text session it was about to move.

**Undo granularity.** Replace-this-one is one `CompositeCommand` (delete + insert), so one Ctrl+Z puts
the old words back. **Replace-all is also one step**, deliberately: replacing forty words is one thing
the user did, and forty Ctrl+Zs to take it back would be a punishment for using the command. The
children are built back to front, because each replacement moves the offsets after it and none of the
offsets before it.

**A stale hit never bites.** The controller forgets where it was whenever the document changes, and
re-checks that the remembered hit still holds the searched-for words before replacing it.

---

## 4. Multi-select, align and distribute

`FrameEditorController` keeps its primary selection exactly as M5 left it — handles, drag, nudge,
link, delete all act on it — and adds a list of blocks chosen **alongside** it. Multi-select is
therefore a capability added rather than a model rewritten, and every M5 test still describes the
truth.

- **Shift+click** adds, or takes out again. Un-choosing the primary promotes the next one rather than
  emptying the selection.
- **A marquee drag** on bare page chooses everything it touches. A box smaller than 4pt is a click
  that missed, and chooses nothing.
- Everything chosen must be **on one page**, and the refusal says why. Lining up two frames the user
  cannot see at once would move something off-screen with nothing said about it.

`FrameAlignment` is pure: rectangles in, rectangles out. Two decisions worth naming:

- **Aligning is measured against the chosen frames' own bounding box**, never the page and never one
  "key" frame. So nothing jumps to a margin the user was not looking at, and **aligning twice changes
  nothing the second time** (`LiningUpTwiceChangesNothingTheSecondTime`).
- **Distributing equalises the GAPS, not the centres.** With frames of different sizes, equal centres
  leaves gaps that visibly are not equal — which is the thing the user was asking for.

Nothing resizes: every command produces `MoveBlockCommand`s, so a lined-up frame keeps the width its
words were laid out for. Frames already in place are left out of the composite, so lining up an
already-lined-up selection puts **no empty step** on the undo stack and says "They are already lined
up."

**Eight new commands, no new chords.** The Arrange menu's own mnemonics are the keyboard path;
`MenuIndexTests` and `KeyboardAuditTests` cover the eight automatically because they walk the catalog.
Eight more unmemorable Ctrl+Shift chords would have bought nothing — M19 made the same trade in the
other direction and said so.

**With one thing chosen they are `NotApplicable`**, which means absent from the panel rather than
greyed in it (the M11 rule). That is also why **M21 re-bakes no screenshot**: the panel for a single
selection looks exactly as it did before the milestone, and no committed image poses a multi-selection
or an open menu. `WithOneThingChosenNothingAboutLiningUpIsOffered` is that claim as an assertion.

**Deliberately out of scope:** delete still acts on the primary frame alone. Deleting a set correctly
means deciding, per frame, whether its story is still shown anywhere else — a real piece of work that
belongs to whoever asks for it, not smuggled into a milestone about lining things up. Group/ungroup
stays excluded for the reason PLAN.md §13 already gives.

---

## 5. The text session survives losing focus (M17's deferral)

`docs/M4-spec.md` §7.5 said "v1: focus loss ends the session — simplest and clearest". It was
simplest, and it was wrong by M11: with a panel full of buttons and a menu bar full of commands about
the text you are typing, ending the session on focus loss means every one of them silently throws the
caret and the highlight away.

Now the session survives. **The caret stops blinking and hides**, because a caret blinking in a window
that does not have focus is a lie about where typing will go; the **highlight stays**, because it is
what the find window, the font window and the paragraph-style flyout are all about. `OnGotFocus`
starts the blink again.

This is what made the non-modal find window possible, and it is the reason the two deliverables landed
in the same milestone rather than in the order the plan listed them.

---

## 6. What the plan asked for and did not get

Nothing was cut. The scope-cut order (replace → distribute → marquee → pan) was never reached; all
four shipped, including the marquee the multi-select bullet named as its cut candidate.

Two divergences, both deliberate:

1. **Replace-all is one undo step, not one per replacement.** The plan said "each replacement is one
   `IDocumentCommand`", which this keeps — every replacement is a command, and they are children of
   one composite. The alternative reading (forty separate undo steps) would make the command a trap.
2. **The find window is not modal.** Every other dialog in the app is; §4 of this document says why
   this one cannot be.

---

## 7. Still open, and owned by a person

- **A manual NVDA/Orca pass over §15 of `docs/accessibility-test-script.md`** — the eleven new steps
  written for this milestone: the find window's live region, the "3 things are selected" heading, the
  eight new Arrange items announcing their refusal, and the survival of the text session across a
  focus change. The tests prove the strings exist and the state is right; only a person with a screen
  reader can prove they are heard.
- **Step 15.11 needs a mouse** and is the only step in the whole script that does. Ctrl+wheel and
  Space+drag are pointer gestures by definition; every command they reach also has a keyboard path
  (`view.zoomIn`, `view.zoomOut`, and the scroll bars), so nothing is keyboard-unreachable — but the
  gestures themselves want a hand on a mouse to confirm.

---

## 8. Files

| File | What |
|---|---|
| `src/TrestleBoard.Core/Text/StoryFinder.cs` | new: story order, every hit, next-with-wrap |
| `src/TrestleBoard.Editing/FindController.cs` | new: the search state, the sentences, the commands |
| `src/TrestleBoard.Editing/FrameAlignment.cs` | new: align and distribute, pure |
| `src/TrestleBoard.Editing/FrameEditorController.cs` | multi-selection, `Align`, `Distribute` |
| `src/TrestleBoard.Editing/TextEditorController.cs` | `SelectRange` — the one entry that is not a click |
| `src/TrestleBoard.Editing/Actions/*` | ten new `ActionId`s, their entries, rules and `SelectionCount` |
| `src/TrestleBoard.App/Dialogs/FindWindow.cs` | new: one window, two modes |
| `src/TrestleBoard.App/Canvas/PageCanvasControl.cs` | wheel, pan, Shift+click, marquee, focus |
| `src/TrestleBoard.App/MainWindow.axaml{,.cs}` | ten menu items, `ZoomAtPointer`, `PanBy`, find wiring |
| `src/TrestleBoard.App/Theme/ActionIcons.cs` | ten icon decisions, on the record |
| `tests/Core.Tests/StoryFinderTests.cs` | new: reading order, wrap, case, orphan stories |
| `tests/Editing.Tests/FindControllerTests.cs` | new: the linked chain, undo granularity, the messages |
| `tests/Editing.Tests/FrameAlignmentTests.cs` | new: the arithmetic, and one undo step |
| `tests/App.HeadlessTests/ConveniencesShellTests.cs` | new: M21's gate through the real window |
| `docs/accessibility-test-script.md` | §15, the Edit and Arrange menu lists, eleven result rows |
