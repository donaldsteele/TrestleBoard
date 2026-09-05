# M92 — Choosing several things, and the commands meaning it

**Delivered 2026-09-05.** Every command that acts on "the selection" now acts on all of it, and
every chosen thing is drawn as chosen.

## What was wrong

The app has offered **four** ways to choose more than one thing since M21 — Shift+click, a marquee
drag, "Choose everything on this page" (`edit.selectAllFrames`) and "Also choose the next one"
(`edit.alsoChooseNext`). Of the commands that then act on a selection, **exactly two used it**:
lining up and spreading out, which need two and three respectively.

Everything else read `_selectedBlockId` — the primary alone. Duplicate, lock, border, shade, wrap,
all four z-order commands, nudge and mouse-drag. Choosing five things and pressing Delete left four
of them. (Delete itself was widened at M91, because Cut needed it.)

Worse, `BuildOverlay` returned a single `RectPt`, so the other four were **not drawn as selected at
all**. The user had no way to see what the app thought was chosen — which bites hardest on lining
up, the one command that has always required several.

This was never a missing feature. It was a promise the app made four times and kept twice.

## What shipped

| Command | Now |
|---|---|
| Duplicate | Copies every chosen thing, one undo step, and the copies become the selection |
| Lock / border / shade / wrap | The **primary decides** which way the toggle goes and the rest follow |
| Bring forward / send back / to front / to back | The selection restacks **as a group**, keeping its own order |
| Nudge (arrow keys) | Moves everything chosen by the same amount, one undo step |
| Mouse drag (Body) | Moves everything chosen together, one undo step |
| Mouse drag (a resize handle) | **Unchanged — the primary only** |
| The canvas | Draws every chosen thing |

## The decisions

**A toggle is decided by the primary, not per block.** A mixed selection where each item flipped to
its own opposite would need pressing twice to mean anything, and would leave the page disagreeing
with itself. The primary decides and everything follows, so one press makes them agree.

**A resize handle stays with the primary.** Handles are the edges of one frame, and "drag five
frames by one frame's corner" has no meaning. Moving is the gesture that generalises; resizing is
not.

**A frame kept in place stays put while the rest move**, rather than refusing the gesture for
everything. The point of pinning one thing down is that it does not stop everything else working.
This holds for both the drag and the arrow keys, which is M28's rule: two ways in, one behaviour.

**Restacking moves the group, not each block in turn.** Restacking one at a time shuffles the chosen
blocks against *each other*, so two frames sent to the back would come out in the opposite order
from the one on screen. The chosen blocks are lifted out as a run, the insertion point is computed
against what is left, and the run goes back in whole. For a single selection the arithmetic reduces
exactly to what it was — which is what lets every existing z-order test stand unchanged.

**Companions follow the delta taken *after* snapping**, so the frame being dragged snaps and pulls
the rest into line with it, and the selection keeps its shape.

**Every multi-block gesture is one undo step.** Dragging four frames and pressing Ctrl+Z four times
to put them back is not undoing what the user did.

## Saying it, in the plural

`ActionCatalog.TitleFor` now varies seven commands when `SelectionCount > 1` — "Make another of
each", "Delete these", "Keep them where they are", and so on — and the shell's announcements count
("There are now 3 copies…", "All 3 taken off the page…"). A label promising *less* than the command
does is the M55 defect, and in this direction it is worse than usual: doing more than you said is as
alarming to this audience as doing less.

The catalog's own `Title` stays singular. That is M18's rule — a surface that has not been taught
about multi-selection reads correctly rather than wrongly.

## The overlay

`FrameOverlay` gains `AlsoSelectedRects`, a trailing optional list, drawn as plain outlines **before**
the primary so that where two chosen frames overlap the one carrying the handles is on top. They get
no handles: handles mean "drag this edge", and an edge drag acts on the primary.

The change is additive and no fixture has a multi-selection, so **no snapshot baseline moved** — the
M16 rule holds, and `ChoosingOneThingDrawsExactlyWhatItAlwaysDid` pins it.

## Verification

`dotnet test TrestleBoard.slnx`: 2,199 tests pass across eleven projects. No baseline moved.

**Failing-first, done by reverting rather than by hope.** `FrameEditorController.cs` was checked out
back to the M91 commit with the new tests in place — the overlay's new field is optional, so it still
compiles — and **nine of the twelve behaviour tests failed**. The three that passed are the ones
asserting behaviour that must *not* change: delete (already widened at M91), the single-selection
overlay, and resize touching only the primary. That is the shape you want: the new tests fail without
the change, and the guards pass either way.
