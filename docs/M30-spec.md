# M30 — The layout hang

**Delivered 2026-08-07.** One defect, upgraded from PLAUSIBLE to **confirmed** by building the case
the review described and watching the engine stop.

---

## 1. What happens

`TextLayoutEngine.LayOut` has two loops that advance down the frame by the paragraph's line height,
hunting for a band with usable segments:

```csharp
while (true)
{
    if (y + para.LineHeight > frame.Rect.Bottom + Epsilon) { frameFull = true; break; }
    segments = ComputeSegments(frame, y, y + para.LineHeight, minSegWidth);
    if (segments.Count > 0) { break; }
    y = y + para.LineHeight;
}
```

At `LineHeight == 0` the exit test can never become true and `y` never moves. Negative is worse:
`y` walks upward for ever. Either way the app locks up **inside its first paint**, with no
exception, no message and nothing on screen to explain it.

`ParagraphStyleDef.LineSpacing` is a plain settable float with no validation anywhere, and it is
deserialised straight out of the file. A hand-edited or corrupted `.tboard` carrying
`"lineSpacing": 0` is enough.

## 2. Why it took a deliberate setup to see

The loop is only entered when `ComputeSegments` comes back **empty** — that is, when the band is
fully blocked by a wrap exclusion. With any ordinary layout the first call succeeds and the loop
breaks immediately, so a zero line height is harmless and the bug is invisible.

The first version of the regression test used the existing fixture and **passed without the fix**,
which is the only reason it was noticed that the test proved nothing. It now widens the fixture's
picture to span the whole column, so every band is blocked and the engine falls into the loop it
cannot leave. With that in place, two of the four cases hang; the test's 20-second join fails them.

A hang has no exception to catch, so bounding the wait *is* the assertion — without it the test does
not fail, it never returns.

## 3. The fix

A floor on the computed line height, in the one place it is computed:

```csharp
float naturalHeight = maxAscent + maxDescent + maxLeading;
float lineHeight = Math.Max(
    paragraph.Style.LineSpacing * naturalHeight,
    Math.Max(naturalHeight * MinimumLineSpacing, MinimumLineHeightPt));
```

`MinimumLineSpacing` is 0.25 — tighter than any typographer would set and far tighter than anything
this app's own styles use. It is a backstop against a corrupt file, not a design choice.

`MinimumLineHeightPt` (0.5 pt) covers the degenerate case where the *natural* height is itself zero
— a font with no metrics, or a run at size zero — because the multiple of zero is zero too.

Clamping here rather than at the model means one line covers every path into layout, including
documents already saved with a bad value. Validating on load would leave those documents broken.

No snapshot baseline moved: every real style in the app is far above the floor, so nothing that
laid out before lays out differently.

---

## 4. What guards it

`DocumentRelayoutTests.ACorruptLineSpacingLaysOutInsteadOfHanging`, four cases: `0`, `-1`,
`-0.0001` and `float.Epsilon`. Confirmed to hang the engine without the floor.

Suite after M30: **1170 passing, 12 skipped**.
