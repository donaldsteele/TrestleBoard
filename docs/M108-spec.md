# M108 — Fit the width

**Delivered 2026-09-06.** The register's "no fit-width" row, and the accessibility one.

## Why this is not just another zoom rung

Fit page is what you use to see the **shape** of a page. Fit width is what you use to **read** it:
the writing comes out as large as it can be while a whole line still fits across, and the bottom of
the page runs off the screen. For the audience PLAN.md §6 is about, that is the difference between
editing a paragraph and squinting at one.

The ladder has had Ctrl+0 (actual size) and Ctrl+1 (fit page) since M2, and there has never been a
way to say "as big as will fit sideways" at all — not by menu, not by key, not through the zoom
chooser M76 added. On paper taller than it is wide, which is every sheet this application prints on,
fit page is limited by the **height**, so the one thing the ladder could not express was the one that
makes the words bigger.

## A mode, not a magnification

`view.fitWidth`, Ctrl+2 — beside Ctrl+1 because it is the same question asked a second way.

It **keeps** fitting as the window is resized, exactly as fit page does. A one-off magnification
would come undone the first time somebody widened the window, which for this audience reads as the
setting not having taken.

Choosing a size by hand — a stepper, the zoom chooser, Ctrl+0 — ends it. Otherwise the next window
resize would silently undo what the user had just chosen, and they would have no idea why.

**The scroll bar is the point of it, not a cost of it.** The page is meant to run off the bottom;
that is what "as wide as the window" buys.

## What the bool became

`_fitToWindow` was a `bool`. Two fits cannot both be on, and a pair of bools that can express an
impossible state is a pair somebody eventually puts into it — so it is now a three-valued
`FitMode` (`None`, `WholePage`, `FullWidth`). `SetZoom(zoom, fit: true)` no longer *sets* the mode,
because the two fits set their own before calling in; it only clears it when `fit` is false.

A newly opened newsletter is shown whole, whatever the last one was shown at: the first thing
somebody wants from a file they have just opened is to see what is in it.

## Verification

`dotnet test TrestleBoard.slnx` — green, no snapshot baseline moved (this is chrome; nothing about
the document changes).

Five new tests in `tests/App.HeadlessTests/FitWidthTests.cs`. The load-bearing one asserts fit width
comes out **strictly bigger** than fit page on the sample newsletter — a "fit width" that gave the
same number as fit page would be a menu item that does nothing, and it is the only assertion here
that could not be satisfied by wiring the command to the wrong method. **Mutation-checked**: with the
width branch removed from `ApplyFitZoom`, that test fails and the other four do not.

The invariant tests caught three things on the way in, which is them working: `Ctrl+2` was advertised
before `KeyboardMap.Describe` could spell `Key.D2`; `fit-width` needed a glyph, because fit-page
carries one and one of a pair having an icon reads as the other being an afterthought; and
"Fit the _width" collided with "Sho_w where fonts were changed" in the View menu.
