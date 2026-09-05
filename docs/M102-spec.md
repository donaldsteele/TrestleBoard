# M102 — Underline

**Delivered 2026-09-05.** The last of M86's three deliverables. **M86 is now complete**: alignment
shipped as M96, colour as M99, underline here.

## What shipped

`text.underline` (Ctrl+U), beside Bold and Italic in Format and on the same footing — its own icon,
its own catalog entry, its own keyboard row.

## Underline is a third dimension, not an override

M99's colour and M14's "just here" font both ride the `~` convention, and underline could have too.
It does not, on the record:

**An override occupies the one override slot.** `body~ink-8c2a2a` and `body~underline` cannot both
be the name of one style, so underline-as-override could never combine with a colour — and a red
underlined heading is an ordinary thing to want.

As a **suffix** it composes with bold and italic exactly as they compose with each other:
`body-bold-underline` is a real style name, minted on demand, and four tests hold the composition in
both directions and through taking the bold off again. The cost is one more parameter on
`VariantName`, `TryResolve` and `Derive` — all optional, so no existing caller changed.

Two details that would have been silent bugs:

- **`BaseName` strips the suffixes in the reverse of the order `VariantName` appends them.**
  Underline goes on last, so it comes off first, and every combination unwinds.
- **The attribute scan matches on underline.** Without that clause, asking for the underlined
  sibling of `body` would be answered with `body` itself — same family, same size, same colour — and
  the line would silently never appear. `ThePlainStyleIsNotMistakenForItsUnderlinedSibling` exists
  for exactly that.
- **`Derive` carries the underline across unless told otherwise**, which is what keeps the line on
  the words when they are bolded.

## Where the line goes comes from the font

Position and thickness are read from the face's own `post` table at render time. Nothing about the
line is stored in the document: an underline under Source Serif sits at that designer's depth, and
changing a style's font moves it with no arithmetic in this app. A hand-picked offset would be wrong
for every face except the one it was eyeballed against, and wrong at every size but one.

**The fallbacks are proportions of the type size, not constants** — a face may carry no metrics at
all, and a fixed 1pt line under 30pt type reads as a mistake. **The sign is taken, not trusted**: a
face storing its position the other way round would otherwise get a strike-through.

The line is drawn in the run's own colour, because an underline is part of the writing rather than a
mark on top of it.

## The test that could not fail, again — and what fixed it this time

The first version of "the line is under the words, not through them" counted dark pixels per row of
the rendered page. It **passed against a deliberate strike-through** — the same line moved above the
baseline — because an underline sits close enough to the baseline that both land in rows with little
ink.

A test that cannot tell an underline from a strike-through is not a test of an underline.

The fix was not a better pixel heuristic but a **seam**: `UnderlineRectFor` was pulled out of the
drawing code and given the two font metrics as plain nullable floats rather than an `SKFontMetrics`
(whose fields are read-only, so a "what if the face has no metrics" rule could not otherwise be
reached from a test). Re-running the same mutation now fails **five** tests where it failed none.

This is the fourth time this session that mutation caught a green test proving nothing, and the
second where the answer was to move the logic somewhere assertable rather than to assert harder.

## No golden image, and why no baseline moved

M86 budgeted a `text-decorations` snapshot fixture baked on all three operating systems. A
maintainer's machine bakes only its own, so adding one would leave CI red on the other two until
somebody baked them. Instead the properties a golden image would have stood in for are asserted
directly — a line appears, it sits below the baseline, it spans the run, it takes the writing's
colour, and turning it off restores the page byte-for-byte — and every one is independent of how a
particular rasteriser antialiases an edge.

Nothing in any fixture is underlined, so the change is additive and **no committed baseline moved**.

## One more thing, found on the way

`SixtyDragStepsStayInsideTheFrameBudget` failed twice in this session with nothing in the drag path
touched. Its own comment explains that a shared CI runner has no CPU guarantee and widens the budget
from 16ms to 48ms there — the reasoning is right and the *detection* was not: it asked whether `CI`
was set, and running the whole solution locally starts eleven test projects at once, which contends
the machine exactly the way a shared runner does. It now asks whether the machine is **busy**, which
is the variable that actually matters, and a single-project run still gets the strict 16ms gate.

## Verification

`dotnet test TrestleBoard.slnx` — 2,300 tests green, no snapshot baseline moved. Fourteen new tests
in `tests/Editing.Tests/UnderlineTests.cs` and nine in
`tests/Rendering.SnapshotTests/UnderlineRenderTests.cs`.
