# M100 — Make another page like this one

**Delivered 2026-09-05.** A whole page can be copied, with everything on it.

## What was wrong

A trestle board repeats its own shape. A photo page this month is a photo page next month with
different photographs in it; the announcements page has the same box in the same place every issue.

`item.duplicate` has copied **one thing** since M81. `page.add` adds a **blank** page. Nothing in
the app has ever copied a page, so the committee's way of making the second photo page was to build
it again, frame by frame, from nothing.

## What shipped

`page.duplicate` — "Make ano**t**her page like this one" — in the Page menu, above "Delete this
page". The copy lands directly after the original, and **the app takes you to it**: the copy is the
thing the user is about to work on, and leaving them looking at the original is the commonest way a
copy goes unnoticed.

## How it is built

Every block goes through `BlockCopier`, the type introduced at M91, so every rule that milestone
settled applies here unchanged and untouched:

- a piece of writing gets **a story of its own** holding the same words — never the same story,
  because two blocks on one story is what a *link* is and a copy is not a continuation;
- a picture **shares its bytes**, so copying a photo page does not double the file;
- a block kind nobody has wired up is **refused rather than half-copied** (M72's rule).

Position, size, stacking order and the locked flag are carried across, because a copied page that
arrived rearranged would defeat the entire reason for asking for one.

**Ids are minted against a running set**, which is the M91 defect in its natural habitat: a page of
nine blocks mints nine ids and nine stories in one command, against a document that has not changed
yet. Without the running tally every one of them would be `copy-1`, and `Document.TryFindBlock`
returns the first match, so eight of the nine would be permanently unreachable. A test asserts every
id and every story id is distinct.

**Links are dropped**, deliberately, because `BlockCopier` drops them: the copied page holds the
same words standing on their own.

The whole thing is one `CompositeCommand`, so one Ctrl+Z takes the page, its blocks and its stories
back together.

## A correction to the register

The missing-functionality register recorded "the zoom ladder's floor is 100%, so you cannot zoom
out". **That is wrong**, and this milestone is where it was caught: `ZoomLadder.cs` is the
*photograph positioning* window's ladder, where 100% means "the picture already fits the preview"
and there is genuinely nothing to zoom out to. The canvas has its own ladder,
`MainWindow.ZoomSteps`, which starts at 0.5.

Recorded rather than quietly dropped, because a register that keeps a wrong entry is worse than one
that is shorter. Fit-width zoom is still genuinely absent.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Eight new tests in
`tests/Editing.Tests/DuplicatePageTests.cs`, including the distinct-ids guarantee and the
same-place-same-order property.
