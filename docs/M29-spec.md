# M29 — The small ones

**Delivered 2026-08-07.** Nine defects from §14.2 and §14.3 that are individually minor, share no
theme beyond being real, and were each cheap to close. Grouped so they are not left to rot as "the
minors" — which is what happens to a list nobody ever picks up.

---

## 1. Recovery wrote its timestamp before its bytes

`FileRecoveryStore.Write` wrote the sidecar (carrying the new `SavedAt`) **before** the document,
with a comment reasoning it through backwards.

Sidecar-first pairs the new timestamp with the *previous* document — so the restore dialog says
"saved 10 seconds ago" over bytes that are a minute old, the user is told their work is current,
accepts it, and loses the difference without ever being shown that there was one. **Overstating
freshness is the direction that costs work.**

Document-first pairs new bytes with a stale timestamp: the dialog understates how fresh the snapshot
is, and the user is offered *more* work than promised — a harmless surprise. A missing sidecar
entirely was already handled.

## 2. 8.5 pt and 85 pt were the same style

`StyleOverrides.Slug` strips everything that is not a letter or digit, so both sizes slugged to
`85`. `TextEditorController.UseFontJustHere` finds a derived style **by name** and does not check
its size — so with an 8.5 pt override already in the document, asking for a banner at 85 pt silently
gave you 8.5 pt.

The decimal point now survives as `p`: `body~lora-8p5` against `body~lora-85`.

## 3. Two fingerprints could not see their own field boundaries

`BirthdayRosterProjection` and `OfficersRosterProjection` hash `Id + Name + Day` and
`Id + Name + Phone` run together with no separator. `"person-12"` + `"Al"` and `"person-1"` +
`"2Al"` produce identical bytes — so a real change to somebody's details can hash the same as the
stored fingerprint, and the list reports itself up to date when it is not.

Fields are now separated by U+001F, which cannot occur in anything the importer produces. (The
birthday projection's `Prints` comparison got the same treatment; it is in-memory only, never
stored, so nothing on disk changes.)

## 4. "Show me where it is" never showed anything

Both sync paths did:

```csharp
_frames?.Select(blockId);
GoToPage(PageOf(blockId));
```

`GoToPage` clears the selection — deliberately, because a selection carried to another page would
make the panel act on something the user cannot see. So the select was destroyed one line later,
every time. The two lines exist to show the user where the list being discussed actually is; they
did nothing. Reordered.

## 5. An interrupted drag was committed later

`PageCanvasControl` had no `OnPointerCaptureLost`. Capture can be taken away mid-gesture — the
window deactivates, a popup opens — leaving `_draggingFrame`, `_panning` or `_marquee` set with no
pointer behind them, so the **next unrelated release committed a drag the user had abandoned**,
moving a frame they were no longer touching.

An interrupted gesture is now cancelled, never committed: the user never let go, so there is no
moment at which they agreed to where the frame had got to.

## 6. The error dialog could only be closed with the mouse

Its single OK button had neither `IsDefault` nor `IsCancel`, so Enter and Esc both did nothing —
in the one dialog a keyboard-only user is most likely to be looking at, since something has just
gone wrong. It now has both.

## 7. Two windows leaked their bitmaps

`PositionPhotoWindow`'s preview is a decoded photograph measured in megabytes, and that window is
opened and closed repeatedly while somebody nudges a picture into place. `RestoreDialog`'s thumbnail
is small but follows the same rule — a leak nobody bothers with is how the rule stops being one.
Both are disposed on `Closed`.

## 8. Two data-layer edges

- **`RecipeCache`** keyed on `assetRef + "|" + …` with no escaping, so an `assetRef` containing a
  bar made `InvalidateAsset`'s prefix match ambiguous. The asset reference is now length-prefixed.
  Asset names are minted by the app and never contain a bar today; this stops that being
  load-bearing.
- **`ChooseHeaderRow`** accepted any non-negative row, including one past the end of the sheet,
  which produced an empty mapping and an empty plan **with no error anywhere** — the import simply
  appeared to find nobody. Out of range now means "no column titles", which at least tries to read
  the file.

## 9. Two accumulations

- **Unreadable roster backups** were skipped by `Backups()`, so they were never counted against
  `BackupsKept` and never deleted — they accumulated for ever in a folder the user never opens. A
  kept copy that cannot be read is not a kept copy, and is now removed.
- **A long committee name ran off the page.** Only the hanging indent was capped (at 40% of the
  column); the first line was given `widthPt - prefixWidthPt`, which goes to zero and then negative
  for a name longer than the column — and a negative available width makes the wrapper place the
  whole line without breaking it, straight across the right margin. There is now a floor, below
  which the first line simply starts on the row after the name, which is what a printed list does.

---

## 10. What guards it

`StyleFontTests.AnOverrideNameCarriesTheSizeOnlyWhenTheSizeDiffers` was **already asserting the
bug** — it expected `body~lora-85` for 8.5 pt. It now asserts the two sizes are distinct, which is
the whole point, and the old expectation is recorded in a comment so it is not "corrected" back.

The rest are structural changes whose correctness is in the code and the comments beside it rather
than in new assertions: an ordering, a missing override, two `Closed` handlers, two flags on a
button. Adding a test for each would mostly test Avalonia. Where a real behaviour changed —
selection surviving a sync, and the header-row clamp — the existing suites cover the paths and
stayed green.

Suite after M29: **1166 passing, 12 skipped**. No snapshot baseline moved.
