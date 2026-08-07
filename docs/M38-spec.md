# M38 — Two picture verbs, not four

**Delivered 2026-08-07.** The second of §14.4's two owner decisions. Decided: **merge the duplicate,
rename the survivors.**

---

## 1. The finding was understated

The review called them "four near-synonym picture verbs in one panel". Reading the code, it was
worse than synonymy — **two of the four opened the same window**:

```csharp
internal async Task AdjustPhotoAsync() => await ShowAdjustWindowAsync(startOnTrim: false);
internal async Task TrimPictureAsync()  => await ShowAdjustWindowAsync(startOnTrim: true);
```

Identical dialog. The only difference is which control gets focus.

So "This picture" offered five geometry-and-appearance entries that were really **three things**:

| Entry | Reality |
| --- | --- |
| Swap this picture… | file picker |
| Fix this picture | instant: auto-crop to frame + auto-levels, one undo step |
| Adjust the picture… | `PhotoAdjustWindow`, focus on brightness |
| Trim the edges… | **the same `PhotoAdjustWindow`**, focus on trim |
| Position the picture in its frame… | `PositionPhotoWindow` — *also* reachable from a button inside the Adjust window |

That window already contains brightness, contrast, colour, rotate, all four trim sliders **and** a
button through to Position. Every one of the verbs lands inside it or beside it.

## 2. What changed

**"Trim the edges…" is gone as a command.** It was a second door onto a window that already has a
"Trim the edges" heading inside it, so the words are still there for anyone who scans for them —
they are just no longer a separate thing to choose between.

**The survivors are named for the outcome, not the operation.** "Fix" and "Adjust" are near-synonyms
in English and gave no clue which did what; they now read as a pair.

| Was | Is | Why |
| --- | --- | --- |
| Adjust the picture… | **Change how it looks…** | brightness, colour, turning, trimming — all of it is how it *looks* |
| Position the picture in its frame… | **Choose which part shows…** | says what the user gets, not what the app does to a crop rectangle |
| Fix this picture | *unchanged* | it is the one-press path, and "Fix" is right for it |

The wording follows through into the windows themselves, so nothing is called two things:
`PhotoAdjustWindow`'s title becomes "Change how the picture looks", its Position button becomes
"Which part shows…", and `PositionPhotoWindow`'s title becomes "Which part of the picture shows".

Panel: **five entries down to four**, and the two that remain do visibly different jobs.

**M22's decision stands.** Position is still a top-level command with its own window — that
milestone's whole point was that positioning a crop is not resizing one, and nothing here
contradicts it.

## 3. The access-key trap

`Choose which part _shows…` clashed with `_Swap this picture…` on **S** — and the clash is only
visible at runtime, because that header is rewritten by `RefreshActions` from "Put a picture here…"
to "Swap this picture…" when the frame holds a photograph.

`AccessibilityTests.NoTwoItemsInOneMenuShareAnAccessKey` caught it, which is what that test is for.
The key moved to **W**.

## 4. What happened to the id

`ActionId.TrimPicture` is **kept as a constant** and documented as retired. No `.tboard` ever
referenced it and no user setting holds it, so deleting it would be safe — but deleting an id
outright makes the history unreadable to the next person who greps for `picture.trim` and finds
nothing at all.

Removed from: the catalog's entry list, `ActionRunner`'s handler map, `ActionIcons`' icon-less
record, and the availability rule that grouped it with Fix and Adjust.

## 5. What guards it

- `IconTests.EveryActionEitherHasAnIconOrIsListedAsIconLess` — the two icon sets must partition the
  catalog exactly, so removing a command from the catalog without removing its icon record fails.
- `ActionSurfaceTests.EveryActionThePanelOffersIsEitherPerformableOrExplained` — every offered
  action must have a handler, so removing the handler without removing the offer fails.
- `AccessibilityTests.NoTwoItemsInOneMenuShareAnAccessKey` — caught the S clash.
- `ActionCatalogTests.AnEmptyPictureFrameCanBeFilledButNotAdjusted` updated to the four survivors.

Screenshots regenerated: `action-panel-photo`, `fix-photo`, `position-photo`.

Suite after M38: **1185 passing, 12 skipped**. No snapshot baseline moved.
