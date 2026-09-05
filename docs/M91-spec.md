# M91 — Copy it, and put it on another page

**Delivered 2026-09-05.** Cut, copy and paste stopped being only about words; two new commands move
what is chosen to the next page or the one before.

## What was wrong

Anything on a page was stuck there. `item.duplicate` ("Make another like this", Ctrl+D) always put
the copy on the **same** page, because `FrameEditorController.DuplicateSelected` took its target
from `document.TryFindBlock(blockId, out Page? page, …)` and added straight back to it. And **no
operation anywhere in `src/` relocated a block between pages** — `MoveBlockCommand` carries only a
`RectPt`.

`edit.cut` and `edit.copy` were words only: they refused outside a story with "No words are
highlighted", and `MainWindow.CutAsync`/`CopyAsync` did nothing at all unless a text session was
open. So "this notice belongs on page 4" meant deleting it and building it again, re-answering every
question in the widget wizard.

## What shipped

- **Ctrl+C / Ctrl+X** now take the highlighted words inside a piece of writing, exactly as since M4,
  and the chosen thing outside one. **Ctrl+V** puts it on whichever page is being looked at.
- **`item.moveToNextPage` / `item.moveToPreviousPage`** (Ctrl+Shift+PageDown / PageUp) move what is
  chosen and follow it there.
- All of it acts on **the whole selection**, in one undo step.

**No new `IDocumentCommand` type.** Everything is built from `AddBlockCommand`, `RemoveBlockCommand`,
`AddStoryCommand`, `SetZOrderCommand` and `CompositeCommand`. Nothing touched `Layout`, `Rendering`
or `Export.Pdf`, and **no snapshot baseline moved**.

## The four decisions

**1. One newsletter, one run of the app.** What is copied lives in a field on
`FrameEditorController`, not on the system clipboard. A held picture names an asset in *one*
newsletter's package, so it must never outlive that newsletter — and because the shell builds a new
controller for every newsletter it opens, that guarantee is **structural** rather than something a
close handler has to remember. A `ForgetHeldFrames()` method was written and then deleted for
exactly this reason: it had no production caller, and a zero-caller surface does not survive the
milestone (M18's rule).

**2. Moving is a command, not a drag.** The canvas draws exactly one page —
`MeasureOverride` sizes to one, `Render` paints one, `ToClampedPagePoint` clamps to it — so there is
no page edge to drag across. Rejected on top of that: a long, precise drag onto a rail thumbnail is
the fine-motor gesture §6 exists to avoid, and a "Move to page…" picker dialog adds a question to a
command that already knows the answer.

**3. Ctrl+C/X/V were widened, not duplicated.** `ActionCatalogTests.ActionIdsAndGesturesAreUnique`
forbids two catalog actions advertising one gesture, so a second `item.copy` could never have
carried Ctrl+C. The `KeyboardMap` rows dropped `KeyScope.WhileTyping` for `Always`, which
`NoGestureIsScopedNarrowerThanTheCommandItRuns` then ties to the catalog's widened answer.

**4. Paste onto a different page keeps the exact position.** That is the whole feature: "the same box
in the same place on page 4". Only a paste back onto the page it came from is offset, so a
paste-in-place is visibly a copy rather than a page that appears not to have changed (M81's reason).

## The three defects the design had to avoid, each with a test that proves it

**A batch of pastes minting one id.** `NextId` answers "what is free" by scanning the document, and
the document is not mutated until the composite runs — so pasting three frames minted `copy-4` three
times. `Document.TryFindBlock` returns the **first** match, so from then on the wrong block would be
edited, forever, silently. `CopyOnto` therefore threads a `HashSet<string> taken` through the batch.
*(`EveryThingPastedAtOnceGetsAnIdOfItsOwn`.)*

**Chain healing computed one block at a time.** Cutting A and B out of A→B→C→D must leave A's
predecessor pointing at **D** — the first frame that is *staying*. Healing per block points it at a
frame that is about to stop existing; nulling it instead breaks the invariant the whole flow model
rests on, that a story has exactly one head, and the article is then drawn twice from its first
paragraph in two places. `SurvivingContinuationOf` walks past every id in the batch.
*(`CuttingTwoFramesOutOfOneArticleJoinsUpWhatIsLeft`.)*

**A clipboard holding live references.** What is held is copied at the moment of copying. A reference
would mean editing the original silently changed what pasted — and after a **cut** there would be
nothing to reference at all. *(`ChangingTheOriginalAfterCopyingDoesNotChangeWhatIsPutDown`.)*

## Move: why the command order is the whole correctness argument

A composite reverts its children **in reverse**, so `MoveSelectionToPage` emits every
`RemoveBlockCommand` first, then the `AddBlockCommand`s, then the `SetZOrderCommand`s. It works only
because the **same `Block` instance** is handed to Add: `AddBlockCommand.Revert` removes by
reference, while `RemoveBlockCommand.Revert` re-inserts at its captured index. One Ctrl+Z therefore
puts every block back on its own page at the index it held.

**Links are deliberately untouched.** A chain is a list of ids and has always crossed pages —
`MovePageCommand` does not touch `linkNext` either, and auto-flow creates linked frames on other
pages as its ordinary business. A moved frame keeps flowing, because the user moved a frame and not
a story. A *copy* must break the link; a *move* must not. Both directions are tested.

## Paste ordering, said out loud

Paste is a three-way branch: a text session takes words; otherwise held frames; otherwise the
picture path (M18). Once something on the page has been copied, **Ctrl+V puts that down and stops
reaching for a picture on the system clipboard** for as long as the newsletter stays open. That is
the right default — copying a frame is the more recent deliberate act — and a picture is still one
click away under Insert ▸ A picture…. The cost is named in a comment rather than left to be
discovered.

## Two things found on the way

**`DeleteSelected` only ever deleted one thing.** The app offers four ways to choose several objects
(Shift+click, marquee, "Choose everything on this page", "Also choose the next one") and of the
commands acting on a selection, only align and distribute used it. Delete has been widened here
because Cut needed it and because Delete and Cut disagreeing about what "chosen" means is the M55
defect. **The rest of that gap is still open** — duplicate, lock, border, shade, wrap, z-order, nudge
and drag remain primary-only, and secondary selections are not drawn as chosen. It is written up as
item A0 of the missing-functionality register and is not part of this milestone.

**A regression test that could not fail.** `CuttingWhileTypingStillTakesTheWordsAndNotTheBox` was
written to prove the old text path survived, and it passed against deliberately broken code — twice,
for two different reasons. First it asserted on the wrong frame: it clicked at the corner of a frame
it had just added, the click landed in a higher block, and the story it checked was empty from the
start. Then, repaired, it still passed — because it was written as
`await Session.Dispatch(async () => …)`, the exact trap M39 documented and left a warning about in
`HeadlessSession.cs`: the inner task is dropped and every assertion in it vanishes. It now asks the
editor which block it is actually in, asserts there are words to lose *before* trying to lose them,
and uses `HeadlessSession.DispatchAsync`. Both failures were found by mutation — putting the bug in
and checking the test noticed. The second one would have been caught anyway:
`DispatchDisciplineTests` scans every file in the project for that exact pattern and would have
failed on the next full run. The lesson is the gap before that run — **a brand-new test that passes
tells you nothing until you have watched it fail.**

## Menu placement

The two move commands live in the **Page** menu, not beside "Make another like this" in Arrange.
Arrange had no access key left (`NoTwoItemsInOneMenuShareAnAccessKey` failed on both), and somebody
hunting for this is thinking about pages. Their headers say "Move **what is chosen** to the next
page" so they can never be read as moving the page itself, which is the item directly above. Both
still reach the action panel and the right-click list on their own, from `ActionGroup.Item`.

Neither gets an icon, and the reason is recorded in `ActionIcons.cs`: an arrow, in a group already
four arrows deep, pointing the same two directions while meaning something else — which takes
discrimination away from the four that are there.

## Verification

`dotnet build TrestleBoard.slnx` and `dotnet test TrestleBoard.slnx`: 2,181 tests pass across all
eleven projects. No snapshot baseline moved. `tests/Editing.Tests/DuplicateAndLockTests.cs` passes
**unedited**, which is the proof that lifting the per-type copy switch out of `DuplicateSelected`
into `BlockCopier` changed nothing.

Four mutations were put into the implementation and each was caught by the intended test: dropping
the running id set, healing the chain per block, holding live references in the clipboard, and
handing `AddBlockCommand` a different instance from the one `RemoveBlockCommand` captured.
