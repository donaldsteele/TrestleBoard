# M99 — What colour the writing is

**Delivered 2026-09-05.** M86's third deliverable. Underline, its second, remains — with a reason.

## What was wrong

`CharacterStyleDef.ColorArgb` has been plumbed end to end since M1: through
`CharacterStyleResolver`, the layout adapter, the HarfBuzz shaper, `PositionedGlyphRun`, and both
the canvas renderer and the PDF exporter. `PageRenderer.DrawRun` paints with it on line 93.

**And nothing could set it.** Every piece of writing in every newsletter this app has produced has
been black because no command existed to make it anything else — not because anybody chose black.

The sixth of the register's thirteen "built, working, unreachable" items.

## What shipped

`text.colour` — "What colour the writin**g** is…", in the **More about the text** submenu — over
seven named colours: black, lodge blue, deep red, dark green, brown, charcoal, purple.

## The decisions

**A fixed palette and no picker, which is a change to what M86 planned.** M86 asked for eight tiles
*plus* "More colours…" backed by Avalonia's `ColorPicker`, with a contrast sentence for anything
under 4.5:1. This ships the palette alone, on the record:

- **Every colour offered already clears 4.5:1 against white**, and a test asserts it with the same
  WCAG formula the M16 theme tests use. So there is no warning to give and no argument to have with
  somebody about their own choice.
- A picker adds a package reference, and a control M86 itself notes would be "the first in the app
  whose meaning is not in words" — needing an NVDA/VoiceOver pass of its own before it could ship.
- The palette answers the actual request: a coloured heading over a notice.

If the committee asks for a colour that is not there, the picker is a small addition on top of this;
the reverse — shipping a picker and retro-fitting a floor — is not.

**Black is how you put it back.** Asking for the role's own colour names the *role* again rather
than minting an override that says "the same as the role" — the rule the M96 alignment verbs follow
for left. That is why there is no separate "put the colour back" command to find.

**It rides the `~` convention**, exactly as the M14 "just here" font does: `body~ink-8c2a2a`. Two
properties fall out for free and both have tests — `BaseName` strips only `-bold`/`-italic`, so
`body~ink-8c2a2a-bold` bases to `body~ink-8c2a2a` and **bold keeps working inside coloured text**;
and the name carries the colour, so two colours can never share a style. The `ink-` prefix keeps a
colour override from colliding with a font one, because `Slug` strips hyphens and no family slug can
begin with it.

**Not the bare word "colours" in the search box.** The app's own appearance settings have owned it
since M16, and M95 already had to be renamed once for taking it. The synonyms here are
"colour the writing", "red text", "coloured heading".

## Underline, and why it is not here

M86's second deliverable needs `CharacterStyleDef` to grow an `Underline` field, the resolver's
variant machinery to carry a fourth dimension, and — the real cost — the renderer to draw a line
from the font's `post` table position and thickness, on canvas and in the PDF from shared code.

That is a change to **`Layout` and `Rendering`**, the two projects the plan guards most carefully,
and M86 budgets it a new `text-decorations` snapshot fixture **baked on all three operating
systems**. Only the Windows baselines can be baked here; adding the fixture would leave CI red on
Linux and macOS until someone baked the other two, and *not* adding it would ship a rendering change
with no visual test at all.

So it is left, named, with the work understood — rather than half-done in a way that would have to
be undone.

## Verification

`dotnet test TrestleBoard.slnx` — all green, **no snapshot baseline moved**: colour travels a path
the renderer has always had, and no fixture opts into it. Eight new tests in
`tests/Editing.Tests/TextColourTests.cs`, including the bold-inside-colour property the `~`
convention exists for, and the contrast floor over every colour offered.
