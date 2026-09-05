# M93 — How spaced out the writing is

**Delivered 2026-09-05.** Line spacing, the gap above and below a paragraph, and the first-line
indent became reachable for the first time.

## What was wrong

`ParagraphStyleDef.LineSpacing`, `.SpaceBeforePt`, `.SpaceAfterPt` and `.FirstLineIndentPt` have
been honoured by `TextLayoutEngine` since M1. Every template sets them. They serialize, they
round-trip, they are covered by the golden LineBox tests. **And no command in the application could
change any of them.**

That is the shape of gap the missing-functionality register calls "built, working, and unreachable":
the hard half is finished and tested, and only the verb is missing. It is also the easiest kind to
miss, because every layer reports itself as done. Twelve more of these are on the register.

## What shipped

`text.writingLook` — "How sp**a**ced out the writing is…" in the Format menu — opening a small
window with **three named choices and one tick box**:

| Choice | Line spacing | Gap after a paragraph |
|---|---|---|
| Closer together | 1.15 | 4pt |
| Normal | 1.3 | 7pt |
| More spread out | 1.5 | 11pt |

plus "Start each paragraph pushed in a little" (14pt, or none).

## The decisions

**Three names, not four numbers.** The fields behind this are a multiplier and three point values,
and §6 is explicit that this audience should not be asked to operate spinners over those to say a
thing they can say in a word. The word the committee says when a page looks cramped is "spread out".
The app already prefers naming outcomes over numbers — "Make it fit what is in it", "Make the rest
fit". The numbers live in one place, `WritingLookController.NumbersFor`, where they can be read and
argued with.

**Each choice shows what it does.** Three lines of real text at each setting, with the line height
and the paragraph gap drawn to scale, because "1.15" means nothing to somebody who has never had to
know what it means and everything is guessable from looking.

**Normal is exactly what `StandardStyles` ships** (1.3 / 7pt). So choosing Normal on an untouched
newsletter changes nothing, and a user who wanders through all three and comes back is where they
started without needing Ctrl+Z. A test pins the two together, so a future change to the template
defaults cannot silently make "Normal" mean something the templates do not do.

**It edits the style, so it changes the whole newsletter** — the locked constraint (§1, M14) is that
nothing carries direct formatting. That is also what is meant: nobody asks for the third paragraph
on page four to be roomier than the rest. The dialog says so in a sentence rather than letting it be
discovered.

**Not in the fonts window.** `TextStylesWindow`'s own doc comment says it is "deliberately NOT a
general style editor — that is a scope trap", and that boundary is worth more than the convenience
of one more control on an existing window.

**Nothing is shown as chosen when the newsletter matches none of the three.** A file written by a
later version, or edited by hand, legitimately sits between them; picking the nearest would be the
app telling the user something untrue about their own file.

## The command

`SetParagraphSpacingCommand` takes four nullable floats, where null means "leave this one alone" —
the same shape as `SetCharacterStyleFontCommand`. That is not decoration: the dialog carries two
independent decisions, and a command that restored all four fields from one snapshot would look
correct while quietly resetting the three the caller never asked about. Two identity-coverage
entries in `CommandTests` exist for exactly that reason, and the mutation that made `null` reset to
a default was caught by `TurningTheIndentOnLeavesTheSpacingAlone`.

Asking for what is already set returns false and pushes nothing, so the shell says "The writing is
already like that, so nothing has changed" rather than announcing a change and leaving an empty step
on the undo stack (M70(c)).

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Eight new tests in
`tests/Editing.Tests/WritingLookTests.cs`; the null-means-leave-alone contract was mutated and the
intended test failed.
