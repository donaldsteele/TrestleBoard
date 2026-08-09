# M60 — A list of your own

**Delivered 2026-08-08.** PLAN.md §11 M60. The seventh widget.

## 1. The headline: this was not the expensive milestone

PLAN.md scheduled M60 near the end because *"a new widget type in the document model means a format
migration and possibly moved snapshot baselines — the expensive kind of change"*. Both halves turned
out to be false, and checking before building saved the whole cost.

**No migration.** `MigrationRunner`'s chain is empty and it fires only on `formatVersion` /
`minReaderVersion`. A widget's payload lives in `WidgetBlock.Data` as **opaque JSON**, so a new
widget type is new *data*, not a new format. The polymorphic block plus opaque payload was designed
at M2 to absorb exactly this, and `WidgetContractTests.AnUnknownWidgetTypeIsRefusedQuietlyRatherThanThrown`
already proved the forward-compatibility half: an older TrestleBoard opening a document containing a
`simpleList` paints the neutral placeholder, pixel-identical to the no-provider path.

**No baselines moved.** `widgets-gallery-page1` is a **hand-written list of six widgets**, not an
enumeration of the registry, so a seventh does not touch it. Verified rather than assumed: baking
with `TRESTLEBOARD_UPDATE_BASELINES=1` after the change produced no diff in any of the fifteen
Windows baselines.

That last point mattered practically. Baselines are per-OS and only Windows can be baked here;
Linux and macOS need the manual `bake-baselines` workflow and the maintainer promoting artifacts. A
milestone that moved them would have left CI red on two platforms until the owner intervened.

## 2. What it is

Content that is table-shaped but is not one of the six lists TrestleBoard fills in — Eastern Star
news, a degree schedule, a dinner menu — was being hand-typed into free text frames with tabs and
spaces, losing re-edit, carry-forward and alignment. Hand-aligning a table with spaces is exactly
the fine-motor, spatial task §6 exists to avoid.

The officers wizard already proved this audience can build a table one question at a time. This
generalises that proof to a table nobody wrote a wizard for.

## 3. Three columns, capped on the question

There are three heading boxes and **no way to ask for a fourth**. The cap is on the question rather
than on the answer, which is what keeps "one question at a time" true for the rows: three cells is
the most that still reads as three questions rather than as a grid.

`FieldsStep` throws at construction above three fields, so the wizard could not have been built with
a fourth column even by accident — the cap is enforced by the framework, not by care.

**The column count is derived, never stored.** `ColumnCount` is "how many headings were filled in".
A stored count beside a set of headings is two things that can disagree, and the first disagreement
prints the wrong table. Same reasoning as M55's status-as-a-date.

## 4. Layout

Everything goes through the shared `TableLayouter` — the same columns, row rules and wrapping the
officers table and district calendar use. A seventh widget drawing its own table would be a seventh
set of decisions about column gaps and rule weights, and the page would show it.

Columns share the width **evenly**, and that is a deliberate difference from the officers table. That
one measures its columns from its data because it knows what is in them — names and telephone
numbers, one much wider than the other. This widget knows nothing about its own content, so it
cannot measure with any authority, and an even split is the honest answer rather than a guess
dressed up as one.

## 5. The icon

A new glyph rather than a borrowed one: a header row with a rule under it, then two rows of two
cells. It reads as a **table**, which is what distinguishes it from M61's paragraph lists — the two
arrive one milestone apart and must not look like the same command.

## 6. Two hard-coded counts turned into derived ones

Adding the widget broke three tests that had `6` written into them. Two of those — the widget count
in `WidgetShellTests` and the icon-map count in `IconTests` — now read
`BuiltInWidgets.All.Count` instead. They were asserting "there are six widgets", which is not a
property worth defending; what they meant was "the shell and the icon map agree with the registry",
and now they say so. M65 adds emblems and would have broken them again.

The third, `TheSixV1WidgetsAreRegisteredInMenuOrder`, keeps its explicit list and gains an entry —
that one *is* pinning something real: the order widgets appear in the menu.

## 7. What guards it

`tests/Widgets.Tests/SimpleListTests.cs` (17): one, two and three columns; clearing a heading
clearing the column; no way to a fourth; a blank row being a stray Enter rather than an entry; a row
keeping only as many cells as there are columns; the wizard asking rows one at a time; an inserted
list being empty and saying so; the payload surviving a write/read; writing what was read changing
nothing (gate 9's shape); a field from a newer TrestleBoard being kept rather than dropped; the
header row and one row per entry; the two empty-prompt wordings; no cell silently dropped; and the
same list drawing the same thing twice.

Layout is held by golden assertions on the draw list rather than by pixels, for the reason
`SnapshotInfra` states itself: the geometry is identical on every OS and is the real cross-OS
guarantee; only glyph rasterisation differs.

## 8. What was NOT done

- **Not added to the gallery fixture.** Deliberate: it would move `widgets-gallery-page1` on three
  platforms for no test value the golden draw-list assertions do not already give.
- **No column width control.** See §4 — the app cannot measure content it knows nothing about, and a
  width slider is the fine-motor work this widget exists to remove.
- **No per-cell styling, no merged cells, no sorting.** Each is a spreadsheet feature, and §1's
  non-goals rule out becoming one.
- **No import from a spreadsheet.** M55's roster import exists because the address book is a
  database; a dinner menu is not, and the wizard is faster than a column-mapping screen for six rows.

Suite after M60: **1455 passing, 12 skipped** (17 new). **No baseline moved, no migration, no
format-version change.**
