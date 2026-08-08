# M45 — Tooltips, at last

**Delivered 2026-08-08.** Closes the "no tooltips exist anywhere in the App project" half of §14.3's
assorted list.

---

## 1. Zero, across the whole project

`grep -rn "ToolTip" src/TrestleBoard.App/` returned nothing at all. Not a sparse set — none.

The toolbar is where that costs the most: nine buttons that are a glyph and one or two words, and a
user who wonders what "Fit page" means has nowhere to find out short of pressing it.

## 2. Nothing new was written

The catalog has held a sentence per command since M11 — `ShortDescription`, written in this
audience's words, and already read aloud to screen-reader users. The tooltip is that sentence,
finally shown to people who use a mouse:

> **Save** — Keeps your work in its file so you can come back to it. (Ctrl+S)

**The shortcut is included deliberately.** A tooltip is read by somebody who is already using the
mouse, and it is the one moment they are looking straight at a place that can teach them the
keyboard instead.

Because the text comes from the catalog rather than being authored beside it, there is no second
copy to drift — the test asserts the tooltip *contains* the catalog's own sentence.

## 3. The 16pt floor reaches tooltips too

PLAN.md §6's floor applies here like everywhere else: a hint that explains a button, in text smaller
than the button, would be the one part of the window this audience cannot read. Tooltips are styled
at 16pt and wrap at 420px, because the catalog's descriptions are sentences rather than labels.

## 4. What guards it

`PlainLanguageTests.EveryToolbarButtonExplainsItselfOnHover` — every toolbar button carrying an
action id has a tip, the tip contains the catalog's description, and where the command has a gesture
the tip names it. Verified failing against the pre-M45 shell, listing all nine buttons by id.

## 5. What was NOT done

**No tooltips on menu items.** A menu item is already a full sentence in this app, and it carries
the reason it is unavailable in `HelpText` (M11). A hover hint over a menu would repeat the label.

**No tooltips on the action panel.** The panel already prints the description under the title — the
tooltip would be the text directly above it.

**No tooltips on the canvas.** The finding groups them with rulers and guides, and those are a
different milestone's worth of work; hover text over a page is also the wrong shape for it.

Suite after M45: **1204 passing, 12 skipped**.
