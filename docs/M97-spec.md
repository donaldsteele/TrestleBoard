# M97 — The paper and the margins

**Delivered 2026-09-05.** The newsletter's paper size, orientation and four margins can be changed.

## What was wrong

`PageMaster.Size` and the four margin fields are read in eight places — the layout engine, the snap
engine, the renderer, frame placement, picture placement, widget placement, the body-area
calculation and the margin display M47 added. **Nothing in the application ever wrote any of them.**

US Letter was hardwired by a property default (`new(612f, 792f)`), and the string "A4" did not
appear anywhere in the source. `view.showMargins`, added at M47, only *draws* margins — its own note
records that "the page master has had four margins since M2 and nothing drew them", and drawing them
was as far as it went.

The fifth of the register's thirteen "built, working, unreachable" items to be closed.

## What shipped

`page.setup` — "The paper and the **m**argins…" in the Page menu:

- **Three papers, named**: Letter (8.5 × 11 in), A4 (210 × 297 mm), Legal (8.5 × 14 in).
- **Landscape as a tick box**, not a fourth paper — it is the same sheet turned round, and listing
  six to say three would make the user do arithmetic.
- **Four margins typed in inches**, the same shape and the same validation as M94's position window.

## The decisions

**Paper is named, margins are typed.** There are three papers a lodge newsletter is ever printed on
and naming them is plainer than four numbers. Margins are a real measurement with no small set of
right answers, so they are boxes.

**Every master at once**, like `ShowPageFooterCommand` and for the same reason: the paper is a fact
about the newsletter, not about one page, and a document whose pages were different sizes is not
something this app can produce or the committee can print. The revert still restores **each master
to what it was** rather than to one answer for all of them, so a document that arrived with a
mixture keeps its mixture — the same rule the footer command follows, and a test builds a
two-master document to hold it.

**Nothing is moved to fit.** Making the paper smaller can leave a frame hanging off the edge, and
this deliberately does not shuffle the committee's layout to prevent it: a dozen frames quietly
moving is a bigger surprise than one that needs dragging. Instead `BlocksOffThePaper()` counts them
afterwards and the announcement says how many, so pressing Ctrl+Z is an informed choice rather than
a guess. The dialog says this before the change, too.

**Margins that meet in the middle are refused with the arithmetic spelled out** — "on this paper the
left and right together have to be under 8 inches". Below that there is no room to write in and
every downstream width calculation starts returning a negative number.

**Nothing is shown as chosen when the paper matches none of the three**, for the same reason M93's
spacing window shows no choice for an unrecognised setting: picking the nearest would be the app
telling the user something untrue about their own file.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved (no fixture changes its
paper). Seven new tests in `tests/Editing.Tests/PageSetupTests.cs`, including the two-master revert
and the "smaller paper leaves the layout alone" guarantee, plus `SetPageSetup` identity coverage in
`CommandTests`.
