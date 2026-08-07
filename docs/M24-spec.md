# M24 — Keep the work

**Delivered 2026-08-07.** The first milestone drawn from the top-down review recorded in
`PLAN.md` §14. It closes §14.1 — the lost-work cluster — in full, plus the four data-loss items
from §14.2 that belong to the same story.

---

## 1. What was wrong

The review found four defects that only make sense read together. Each is survivable alone; together
they meant **the app could not keep a user's work**.

### 1.1 A clean exit deleted the only saved copy

`MainWindow`'s `Closed` handler, and every path that swapped one newsletter for another, ran:

```csharp
_recovery?.SaveNow();
_recovery?.Complete();
```

with a comment reading "Any unsaved work is written first". `RecoveryService.Complete()` deletes the
snapshot — under the same store id `SaveNow()` had just written it to. So the pair wrote a file and
immediately destroyed it. Work survived **only when the program crashed**, which is the one case the
snapshot was designed for and the one case a user never plans on.

`docs/M9-spec.md` records this as a bug that was found and fixed. The fix that was applied was the
`SaveNow()` call above, which cannot work, because the delete it was added in front of is what makes
a surviving snapshot mean "the app did not close cleanly".

### 1.2 There was no Save command

No `ActionId`, no menu item, no `Ctrl+S` row in `KeyboardMap`, no handler in `ActionRunner`.
`TboardContainer.SaveToFile` existed and was called from exactly two places: the recovery snapshot
and `CarryForward`. `FileRecoveryStore.RotateBackups` was called only from tests. PLAN.md §4
describes a user file with a rotating `.bak` ring as though it existed.

The restore dialog's own success message said **"Your work is back. Save it when you are ready."** —
an instruction naming a command the app did not have.

### 1.3 Nothing asked before discarding

Open, Start-from-a-template, Start-from-last-month, a `.tboard` double-clicked in the file manager,
and closing the window all replaced or dropped the open newsletter without a word.

### 1.4 The address book could overwrite itself with nothing

`RosterStore.Load` returned `RosterBook.Empty` for a missing file **and** for a file it could not
read. `RosterService` loads once at construction, so an antivirus or sync client holding
`roster.json` for a moment at startup produced an empty book — and the next edit wrote that empty
book over the real one. Real names, phone numbers and birthdays, gone silently, recoverable only by
someone who knew the backup ring existed.

---

## 2. What M24 does

### 2.1 Save and Save As

Two new actions, declared once each in the five places M11 requires (`ActionId`, `ActionCatalog`
entry, availability rule, `ActionRunner` handler, `KeyboardMap` row) plus a File-menu item:

| Action | Gesture | Behaviour |
| --- | --- | --- |
| `newsletter.save` | `Ctrl+S` | Writes to the newsletter's own file; asks where if it has none. |
| `newsletter.saveAs` | `Ctrl+Shift+S` | Asks where, writes there, and that file becomes its home. |

`Ctrl+S` is `KeyScope.Always` — reaching for it mid-paragraph is exactly when it is wanted, and a
save does not disturb the text session.

Availability, per M11's rule that nothing becomes unavailable without saying why:

- No newsletter open → blocked, with the standard sentence and a way in.
- Nothing changed since the last save → blocked with
  **"Everything is saved already. Your work is in September 2026.tboard."** That refusal is the
  answer to the question the user was really asking when they reached for `Ctrl+S`.

`ActionCatalog.TitleFor` gives Save a second name: **"Save this newsletter…"** with an ellipsis when
there is no file behind it yet, because then it has to ask. This is the third command with a
state-dependent name, after M18's two picture commands, and it obeys the same rule — the catalog owns
both wordings and no surface invents a third.

The menu mnemonic is `Sa_ve`, not `_Save`: "Open the _sample newsletter" already holds S in the File
menu, and `AccessibilityTests.NoTwoItemsInOneMenuShareAnAccessKey` enforces that.

### 2.2 Knowing when work is unsaved

`MainWindow._unsavedChanges` is set by the document session's own `Changed` event and cleared only by
a successful write. It tracks **the file**, not the undo stack: undoing back to where you started
still leaves the file out of date if something was written in between.

It surfaces three ways:

- **The title bar**, in words: `TrestleBoard — September 2026 — not saved yet`. An asterisk is the
  convention and means nothing to somebody nobody taught it to (PLAN.md §6).
- **`ActionContext.HasUnsavedChanges`**, which is what the Save rule reads.
- **The three-button question**, below.

Two documents start dirty, because they exist in no file anywhere:

- a **carry-forward** issue (`StartFromLastMonth`), and
- work put back by the **recovery dialog** — it was in the recovery store precisely because no file
  held it.

A freshly opened newsletter and an untouched template start clean, so closing one of those asks
nothing.

### 2.3 Nothing is discarded without asking

`ConfirmSaveFirstAsync` puts one three-button dialog in front of every path that replaces or drops the
open newsletter: **Save it** / **Do not save it** / **Go back**. It names what the changes would be
lost *to* ("If you carry on and open another newsletter…") rather than asking in the abstract, and it
says whether a saved copy exists at all.

"Go back" is `IsCancel` and is what Esc and the title-bar close mean — the answer that loses nothing.
A "Save it" that then **fails** returns `Stay`, not `Save`: a save the user asked for and that did not
happen is never permission to discard.

Guarded paths: `OpenNewsletterAsync`, `NewFromTemplateAsync`, `StartFromLastMonthAsync`,
`OpenDocumentFromPathAsync` (the file-association and drop-a-second-file landing point), and
`Window.Closing`.

The closing guard is the standard Avalonia shape — cancel the close, ask, close again once the answer
is in — with `_closeAgreed` stopping the question being asked twice and a `catch` that keeps the
window (and the work) alive if anything goes wrong on the way out.

**Headless runs** answer through `SuppressStartupForTest`, the flag that already keeps the start
screen and the update check out of the test session; a test that wants the question asked answers it
with `SaveFirstAnswerForTest`.

### 2.4 The snapshot is dropped only when the work is elsewhere

`RecoveryService.Complete()` keeps its meaning — a clean close deletes the snapshot, so a surviving
snapshot means an unclean one — but is now called only where that is true:

- **after a successful save**, because the user's own file now holds what the snapshot held;
- **on close and on document swap**, both of which are now behind the save-first question.

The two `SaveNow()` calls that stood in front of `Complete()` are gone. They wrote files this line
deleted.

### 2.5 Four data-loss fixes in the layers below

| Fix | File | What it prevents |
| --- | --- | --- |
| `Load(out RosterLoadState)` distinguishes `NoFileYet` from `CouldNotBeRead`; `RosterService` refuses every write while unreadable, and `TryReadAgain()` is the way out without a restart | `RosterStore`, `RosterService` | The empty-book overwrite of §1.4 |
| `Apply` writes **before** it mutates | `RosterService` | A failed save leaving the People window showing an edit that reached no disk and raised no `Changed` |
| `Flush(flushToDisk: true)` before the rename | `RosterStore.Save`, `TboardContainer.SaveToFile` | A power cut persisting the rename but not the bytes — the exact corruption temp-then-rename exists to prevent |
| The temp file is deleted when the write fails | `TboardContainer.SaveToFile` | `Newsletter.tboard.tmp` abandoned beside the user's document for ever, then silently overwritten by a later save |

A saved `.tboard` also now carries a current page-1 thumbnail, from the same `RefreshThumbnail` the
crash snapshot uses (PLAN.md §2).

---

## 3. Not in this milestone

- **The rotating `.bak` ring beside the user's file** and its "Restore an earlier version" menu
  (PLAN.md §4). The ring without the restore path is half a feature; both belong together, and the
  roster's ring (M12) is the working model for it.
- **Print.** Still absent, still listed in §14.
- Everything else in §14.2–§14.4. §14.5 groupings 2–5 remain unscheduled.

---

## 4. What guards it

New tests, all cross-platform:

- `SaveShellTests.ControlSReachesTheSaveCommand` — both gestures land on their own action.
- `SaveShellTests.EditingMarksTheNewsletterUnsavedAndSavingClearsIt` — the flag, the title, the
  status line, the "already saved" refusal, and a real file that loads again afterwards.
- `SaveShellTests.TheCrashSnapshotIsOnlyDroppedOnceTheWorkIsInTheUsersFile` — **the regression test
  for §1.1**, using a recovery store that counts its own deletes.
- `SaveShellTests.NothingReplacesUnsavedWorkWithoutAsking` — all four swap paths plus the window
  close, with "Go back" leaving the newsletter exactly where it was.
- `SaveShellTests.CarryForwardStartsUnsaved`.
- `RosterUnreadableTests` (3) — the three load states, the refusal to write over an unreadable book
  (asserted byte-for-byte against the file), and the write-before-believe ordering.
- `ContainerTests.AFailedSaveLeavesNoTempFileBehind`.
- `ActionCatalogTests.TheReplaceCommandIsNamedForWhatIsInTheFrame` extended with Save's two names.
- `IconTests` — the 25th glyph, a floppy disk, which this audience reads instantly.

Suite after M24: **1132 passing, 12 skipped** (the `pdftoppm` parity tests, Linux-CI-only by design).
No snapshot baseline moved; no file in `Layout`, `Rendering` or `Export.Pdf` was opened.
