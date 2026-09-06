# M109 — Pull it in from both sides

**Delivered 2026-09-06.** The register's "no left indent, right indent, hanging indent" row.

## The gap

`ParagraphStyleDef` carried exactly one indent: `FirstLineIndentPt`, which marks where a paragraph
*begins*. Nothing anywhere could set a paragraph *apart* from the ones around it — the announcement
pulled in from both sides, the quotation from the Grand Master — and the layout engine had no left or
right indent to honour if anything had asked. Unlike most of this audit's findings, this one was
missing from **every** layer.

Both indents are now on the style, threaded through `ParagraphStyle` into the engine, and applied by
narrowing the column **before** the photographs are subtracted from it — so a pulled-in paragraph
still wraps round a picture, and the wrap and the indent do not fight.

`text.pullItIn` — "Pull it in from both sides", in the alignment submenu under a separator.

## One step in, not a measurement

The engine takes arbitrary numbers. The command offers **one**: two picas on each side, which is what
a printer would use and what looks deliberate rather than accidental at newsletter column widths.

What the committee wants is "set this apart". A pair of boxes to type points into is two questions
asked in order to answer that one — the same reasoning M107 used about page numbering. Choosing it
again puts the paragraph back, so it is a toggle read off the caret, like bold and italic, rather
than two commands that are each wrong half the time.

## The part that needed real design: composition

M96 minted alignment as a derived paragraph style named `body~centred`, with `RoleOf` cutting at the
first `~`. Adding a second variant naively would have broken it in a way nobody would have been told
about: **re-centring a paragraph that had been pulled in would have named a style with no indent in
it, silently putting it back out to the edges.**

So the two variants now share **one naming grammar**, in the way `CharacterStyleResolver` already
owns weight, slant and underline together:

| Alignment | Pulled in | Name |
|---|---|---|
| Left | no | `body` |
| Left | yes | `body~in` |
| Centre | no | `body~centred` |
| Centre | yes | `body~centred-in` |
| Right | no | `body~right` |
| Right | yes | `body~right-in` |

`-in` goes on **last**, after the alignment word, exactly as `-underline` goes on after `-bold` — so
every combination still bases to `body`. Left-and-pulled-in needs the separator of its own (`body~in`
and not `body-in`), because `body-in` would be read as a role of that name and the derived style
would stop deriving.

`AlignmentOf` and `IsPulledIn` read both facts back **off the name**, which is what lets either verb
be applied without consulting the definition it is about to replace.

## Indents that would leave nothing are ignored

A paragraph indented wider than the frame it sits in must still lay out. Text that vanished because
two numbers met in the middle would be a newsletter losing a paragraph silently — worse than one that
ignores a setting somebody can see is not working.

## Verification

`dotnet test TrestleBoard.slnx` — green, **no snapshot baseline moved**: both indents default to
zero, so nothing that existed before lays out differently.

Sixteen new tests across two projects. `tests/Layout.Tests/ParagraphIndentTests.cs` asserts that
**every** line starts further in (a left indent on the first line alone would be the first-line indent
under a new name) and that no line runs past the right edge — the half a line-count assertion cannot
see, since more lines could equally mean bigger words. `tests/Editing.Tests/PullItInTests.cs` asserts
the composition in both directions and walks the whole naming table.

**Mutation-checked**: handing the un-narrowed column to `ComputeSegments` fails three of the layout
tests.
