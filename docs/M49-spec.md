# M49 — The keyboard gets the marquee, the pan, and the truth about undo

**Delivered 2026-08-08.** Closes the remaining keyboard-coverage gaps in §14.3 and the two-undo-stacks
disclosure beside them.

---

## 1. A marquee cannot be drawn with a keyboard — so don't try

The review asks for a keyboard equivalent for marquee selection, and notes that "select everything on
this page" is not quite a marquee. That is true, and it is also the answer: **what a marquee is
nearly always for in this app is taking hold of several things at once to line them up**, which is
what M21's align and distribute commands then act on.

So the keyboard gets the outcome rather than a simulated rubber band. Edit ▸ **"Choose everything on
this page"** selects every block on the page, announces how many, and points at Arrange.

**No chord, deliberately.** Ctrl+A was the obvious choice — the same key, the other side of the mode,
and `KeyboardMap`'s scope column could separate the two rows cleanly. I built it that way, and two
existing tests rejected it: `ActionIdsAndGesturesAreUnique` and
`EveryAdvertisedGestureIsUniqueAmongTheMenus`. They are right. The Edit menu would then show two
items both advertising Ctrl+A, and a user who cannot tell which one they are about to get is exactly
the confusion this whole review is about. PLAN.md §6 asks that every command be reachable from the
keyboard, not that every command have a chord — Alt+E then E reaches this one, like the twenty other
commands that carry no gesture.

## 2. Panning had no keyboard equivalent at all

The view could only be moved by dragging, which needs a mouse. **With nothing chosen the arrow keys
are free** — the nudge case owns them only while something is selected — so they now move the view,
which is the one thing arrows could mean when there is nothing on the page to nudge. Shift makes the
step large.

Nudging keeps the arrows the moment anything is selected. That is the older meaning and the more
important one, and the test asserts it still wins.

## 3. Two undo stacks, one of them a secret

This app has two: the newsletter's and the address book's. The second was disclosed only inside the
People menu. So somebody who corrected a telephone number and reached for Ctrl+Z was told:

> There is nothing to take back yet.

— which was true of the newsletter and **false about the thing they had just done**. The refusal now
names the other stack and offers it as the remedy, which is what M11's machinery is for:

> There is nothing to take back in this newsletter. Your last change to the address book can be taken
> back from the People menu.

## 4. What guards it

- `FrameShellTests.EverythingOnThePageCanBeChosenWithoutAMouse` — every block chosen, and the align
  commands available afterwards, which is the point of the gesture.
- `FrameShellTests.ArrowKeysMoveTheViewWhenNothingIsChosen` — two pans in the right directions, and
  none once a frame is selected. Verified failing (0 pans) against the pre-M49 canvas.
- `ActionCatalogTests.UndoWithNothingToTakeBackNamesTheAddressBookIfThatIsWhereTheChangeWas` —
  verified failing against the old refusal.

## 5. What was NOT done

**Add-to-selection from the keyboard.** Shift+click adds one thing at a time; the keyboard's version
of that would need a "cursor" separate from the selection, which is a real feature and a different
milestone. "Choose everything on this page" covers the case that actually comes up.

**Pointer-anchored zoom from the keyboard.** Without a pointer there is no anchor, and inventing one
(the centre? the selection?) would be a different feature wearing the same name. Ctrl+= / Ctrl+− zoom
about the centre already.

**Selection is still cleared when the page changes**, and that stays. The comment on `GoToPage` has
said why since M8 — a selection carried to another page makes the action panel act on something the
user cannot see — and the M29 fix to the sync commands works by going to the page *first*. The review
listed it as an observation; this is the answer to it.

Suite after M49: **1213 passing, 12 skipped**. No baseline moved, no screenshot re-baked.
