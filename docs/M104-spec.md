# M104 — What colour the emblem is

**Delivered 2026-09-05.** The register's `VectorBlock.InkArgb` row, closed.

## The gap

`VectorBlock.InkArgb` has been on the block since M65. `BlockCopier` copies it. `DocumentRenderSource`
paints **every part** of the drawing with it, and the PDF gets path operators in that colour rather
than a raster. And the only thing that ever wrote it was the moment of insertion — an emblem put on
the page in black stayed black for the life of the newsletter.

The fifteenth instance of the category, and the same shape as all of them: the model expresses it,
the engine honours it, no command reaches it.

## What was built

`SetEmblemInkCommand` in `TrestleBoard.Core/Commands/BlockCommands.cs`, beside `SetShapeLookCommand`
for the same reason that one exists: the colour lives **on the block**, so no derived-style machinery
is needed and this is the half that costs nothing.

`item.emblemColour` — "Change what colour the emblem is…", in the Arrange menu under the box's
colours.

**One colour for the whole drawing, not one per part.** That is not a simplification made here — a
`VectorBlock` has a single ink and the renderer takes no other colour, because a trestle board emblem
is a line drawing meant to print in one colour on a page that is usually photocopied. Per-part colour
would be a different feature and a different model.

**The same seven colours as the writing.** Which colours print clearly on white paper does not depend
on whether the ink is making a letter or a square and compasses, and a second palette would be a
second thing to learn for nothing. Black is how you put it back, so there is no separate "undo the
colour" command to hunt for.

**No merging.** Two recolourings of one emblem are two decisions somebody made and looked at; folding
them together would make one Ctrl+Z jump past a colour the user chose.

## Two refusals, not one

A box refuses **towards** `item.shapeColours`; anything else refuses towards `insert.emblem`. The two
commands answer different questions, and somebody who picked the wrong one must not be left guessing
which they wanted.

## What the invariant test made us change

`EveryCommandTypeHasIdentityCoverage` demands a factory for every `IDocumentCommand`, and the factory
needs a drawing to recolour. **The drawing is added by the factory, not to the shared fixture** — a
document containing a `VectorBlock` is schema **1.1.0**, and `VectorBlockFormatTests` asserts that
the version follows what is in the document, so adding one to `Fixtures.BuildDocument` quietly made
three other assertions about a different document. All three failed, which is the system working.

That in turn moved one line in `ApplyRevertIsIdentity`: the command is now built **before** the
baseline snapshot is taken. A factory may have to put the thing it acts on onto the page, and that
setup is not the change under test — snapshotting first would demand the setup be undone as well.

## Verification

`dotnet test TrestleBoard.slnx` — green, no snapshot baseline moved. Seven new tests in
`tests/Editing.Tests/EmblemColourTests.cs`. **Mutation-checked**: with `Apply` no longer writing the
ink, two of them fail.
