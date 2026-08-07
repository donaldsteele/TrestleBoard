# M34 — Selection chrome that follows the theme

**Delivered 2026-08-07.** One item from §14.3, and an accessibility defect rather than a
preference: it is the part of the interface that stopped working for the users who most needed it.

---

## 1. What was wrong

`FrameOverlayRenderer` held five `const uint` colour literals — the selection outline (a mid blue),
the resize handles (white), the snap guides (a mid pink), the overset badge (a mid red) and the link
target wash.

Everything else in the window moved to palette tokens at M16, for the reason PLAN.md §6 gives: High
Contrast raises every contrast floor to 7:1. These five did not move. So with High Contrast on — on
a black page — the outline that says **what is selected** was still a mid blue, and the snap guides
still a mid pink.

A user turns High Contrast on because they cannot see the interface. What they could not see
afterwards was the selection.

## 2. The fix

`FrameOverlayColours` is a record struct of the five, with two sets:

- **`Default`** — Light and Dark, which share these because the page under them is white in both.
- **`HighContrast`** — black and white only. 21:1 against each other, comfortably past the 7:1
  floor, and nothing is lost by dropping the hue because **every one of these marks is a shape as
  well as a colour**: a rectangle, eight squares, a dashed line, a badge with a glyph in it. That is
  PLAN.md §6's "colour is never the only signal" doing the work it was written for.

The handles **invert** — black fill inside the white outline — because eight solid white squares
sitting on a white outline read as one thick line rather than as grab points.

The overset badge's glyph reuses `HandleFill`, which is exactly the colour that contrasts with a
filled mark in both sets: white against the red badge in Light and Dark, black against the white one
in High Contrast. Reusing it keeps the pair inverting together.

## 3. Where the decision lives

The **shell** chooses the set, not the renderer. `TrestleBoard.Rendering` knows nothing about
Avalonia or about themes and PLAN.md §1 keeps it that way, so `PageCanvasControl.OverlayColours()`
reads the theme variant and passes the answer in.

It is decided by the variant rather than by reading individual palette tokens, deliberately: these
are not tokens but a **set that has to stay internally consistent** — the handles invert against the
outline — and picking them one at a time out of the palette would let that relationship drift
without anything noticing.

---

## 4. What guards it

`ThemeCompositionTests.TheSelectionChromeIsBlackAndWhiteInHighContrast` — every High Contrast colour
is pure black or pure white, the handles differ from the outline, and the set genuinely differs from
the ordinary one.

No snapshot baseline moved: the overlay is editor chrome and is never on the export path.

Suite after M34: **1175 passing, 12 skipped**.
