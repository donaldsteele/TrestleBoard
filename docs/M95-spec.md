# M95 — A box to set something apart

**Delivered 2026-09-05.** A coloured panel can be put on the page, and a box or line can be
recoloured.

## What was wrong

`ShapeKind.Box` has existed in the model since M2. `DocumentRenderSource.RenderShape` draws a
filled and stroked rectangle from the block's own `FillArgb`, `StrokeArgb` and `StrokeWidthPt` —
any colour at all, already. And the **only** thing in the entire application that ever made a
`ShapeBlock` was `AddRuleAcrossThePage`, which hardcodes `ShapeKind.Rule`. `ShapeKind.Decoration`
was constructed nowhere.

So a committee wanting a shaded panel behind a notice had to shade a *box of writing* instead, and
take whichever of `PageLooks`' three fixed looks they were given — one grey, one width, one tint.

## What shipped

- **`insert.box`** — "A box to **s**et something apart" — asks what colour, then puts a panel on
  the page.
- **`item.shapeColours`** — "Change wha**t** colour the box is…" — recolours a box or a line
  already there.
- **`PageLooks.BoxColours`** — seven named colours: pale grey, grey, cream, lodge blue, lodge gold,
  deep red, black.

## The decisions

**Seven named colours, not a colour wheel.** Every one prints legibly on white and sits with the
M16 palette; a freely chosen colour can be trusted to do neither. §6 would rather this audience
picked "cream" from seven than mixed one from a picker. The full picker arrives with M86, for text,
where the case is stronger and a contrast warning is part of the deliverable.

**A colour is named as well as shown.** Colour is never the only signal (§6): somebody who cannot
tell two swatches apart still has the words, and so does a screen reader.

**A box lands behind everything else.** A panel is a thing other things sit on; one that arrived in
front would hide the notice it was drawn for.

**Fill and outline are asked separately, and either may be nothing** — but not both, because a
see-through box with no outline cannot be seen and the user would be recolouring something they
cannot find. That pair is refused with a sentence.

**Only a box or a line can be recoloured, and the refusal says why.** A text frame's look lives in
a *named* `FrameStyleDef`, of which the app mints exactly three; arbitrary colours there need the
derived-style machinery M86 is bringing. This milestone closes the half that needs nothing new, and
points the other half at "Put a border round it" and "Shade it".

## Three things the invariant tests caught

**`Brushes.Gray` on the swatch border.** `NoChromeControlPaintsItselfWithALiteralColour` was right
about the outline and wrong about the fill — a swatch's fill *is* the document colour being chosen,
and a palette token there would make it show the wrong one. The test learned a **per-line opt-out**
that has to say `document colour, not chrome` in a comment within five lines. Narrow on purpose:
the next literal in the same file is still caught. The outline moved to `Tokens.ChromeBorder`.

**Two access-key clashes.** "Change its colo**u**rs" collided with "P**u**t a border round it", and
Arrange had only J and Q free. The item moved to Format, beside the Picture submenu that already
makes that menu about more than words.

**"colours" was stolen from Settings.** `HelpIndexTests` asserts that typing the bare word finds the
app's own appearance settings, as it has since M16. The new command's title contained it and won.
Renamed to "Change what colour the box is…" rather than flipping a documented decision — the same
file's note about "mistake" is the precedent for leaving an ambiguous word where it was.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Eight new tests in
`tests/Editing.Tests/BoxTests.cs`, plus `SetShapeLook` identity coverage in `CommandTests`.
