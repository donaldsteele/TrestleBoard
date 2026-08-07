# M28 — Reach it from the keyboard

**Delivered 2026-08-07.** §14.5 grouping 5, in part. What is not in it is §4, listed rather than
implied.

---

## 1. Ctrl+V had been dead since M18

M18 made paste mean two things: words into a story, or a picture from the clipboard onto the page.
The catalog was updated to say so (`IsEditingText || HasDocument`), `MainWindow.PastePictureAsync`
was written, and the Edit menu advertised `Ctrl+V`.

The `KeyboardMap` row still said `KeyScope.WhileTyping`.

So the keyboard half of the feature was unreachable **from the day it shipped**. A user who copied a
photograph, clicked a picture frame and pressed Ctrl+V got nothing at all — no picture, no message,
no refusal — while the menu beside them promised the gesture would work.

### Why the audit did not catch it

`KeyboardAuditTests.EveryRegisteredKeyPressReachesItsOwnAction` walks `KeyboardMap` and **skips every
`WhileTyping` row**, as "covered by the editor's own tests". A row scoped too narrowly for the
command it runs was therefore invisible to it by construction.

`NoGestureIsScopedNarrowerThanTheCommandItRuns` is the missing test, and it asks the catalog rather
than a hand-written list: *if a command is available with a newsletter open and no caret in a story,
its gesture may not be scoped to typing.* A future command cannot be forgotten from it.

Its mirror, `EveryShortcutTheCatalogAdvertisesIsInTheTable`, closes the same gap from the other side
— a menu that promises `Ctrl+Shift+N` and a table that has never heard of it is M11's shadowed
`Ctrl+Shift+Y` arriving backwards.

Both were run against the unfixed code and confirmed to fail.

## 2. Backspace was a shortcut nothing knew about

`PageCanvasControl` handled `Key.Delete or Key.Back` in a private `case`. `Delete` was *also* a
`KeyboardMap` row, so it went through the action runner; `Backspace` went nowhere near it.

That meant Backspace could not be refused with a reason, did not appear in the keyboard audit, and
could not be advertised anywhere — three properties M11 makes structural for every other command.

Both keys are rows now, and the canvas `case` is gone. The comment in its place says why, because
"deleting is handled elsewhere" is exactly the kind of thing that gets helpfully re-added.

## 3. Two commands that had no gesture at all

| Command | Gesture | Why this one |
| --- | --- | --- |
| Add a page after this one | `Ctrl+Shift+N` | One of the handful of things done every month while building an issue |
| How things look… | `F10` | The window that makes the interface bigger — the one an elderly user most needs to find, and the only one where "I can't read this" is the reason they are looking |

`F10` rather than a Ctrl chord: nothing else in the app claims a bare function key except F2 and F6,
and a single key is findable by someone hunting with one finger.

The other four page commands stay menu-only, the same trade M21 made for align and distribute: four
more chords nobody can remember would buy nothing.

---

## 4. Still open in grouping 5

- **Marquee selection, add-to-selection (Shift+click), and panning** have no keyboard equivalent.
  Each needs a *new command* with real design behind it — "select everything on this page" is not
  quite a marquee, and "add the next thing" needs an ordering the user can predict. Worth doing;
  not worth guessing at.
- **Alt suppresses snapping** and is advertised nowhere. It needs a home in the interface before it
  needs a shortcut.
- **Tab is swallowed inside a text session**, so leaving a frame for the next one is Esc-then-Tab.
  That is a keyboard path, just a two-step one, and changing what Tab does inside text is a
  decision about text editing rather than about coverage.
- **Pointer-anchored zoom** (Ctrl+wheel) has only a centre-anchored keyboard equivalent. Inherent
  to there being no pointer.
- The remaining ~20 shortcut-less commands are all deliberate: align, distribute, the picture
  geometry commands and the rest of page management are typed into once and left alone.

---

## 5. What guards it

- `KeyboardAuditTests.NoGestureIsScopedNarrowerThanTheCommandItRuns` — the rule, asked of the
  catalog.
- `KeyboardAuditTests.EveryShortcutTheCatalogAdvertisesIsInTheTable` — the same promise, backwards.
- `SaveShellTests.ControlVReachesPasteWithAFrameSelectedRatherThanOnlyWhileTyping` — the actual
  M18 situation, pressed through the real window.
- `EveryAdvertisedGestureIsUniqueAmongTheMenus` (existing) covers the two new gestures for free.

Suite after M28: **1166 passing, 12 skipped**. No snapshot baseline moved.
