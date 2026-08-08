# M39 — The backup ring, and the test bug that hid behind it

**Delivered 2026-08-08.** Closes §14.6 item 7 (the `.bak` ring beside the user's file and its
"Restore an earlier version" menu, PLAN.md §4). It also uncovered two defects that had nothing to do
with backups, one of them in the test suite itself.

---

## 1. The ring existed and protected nobody

`FileRecoveryStore.RotateBackups` was written at M9. PLAN.md §4 asks for it in as many words:

> Also rotate last 5 autosaves as `.bak` beside the user's file ("Restore earlier version" menu) —
> protects users from their own mistakes, not just crashes.

Its only caller was its own unit test. Nothing in the app has ever rotated a backup, and nothing
could list one back.

It was harmless while it lasted, in the narrow sense that the app had no Save command either — there
was no way to overwrite a newsletter, so there was nothing to keep a copy of. **M24 is what made the
omission reachable.** From that milestone until this one, saving overwrote the user's only copy with
no way back, which is the precise mistake §4 wrote the ring to prevent.

Two lines close the writing half:

```csharp
FileRecoveryStore.RotateBackups(path);
TboardContainer.SaveToFile(package, path);
```

Rotate then write, never the other way round: rotating after the write would make `.bak1` a copy of
the version just saved, so "go back to an earlier version" would hand back exactly the thing the
user wanted undone.

## 2. Giving it back

`FindBackups` lists the generations newest-first, skipping any zero-length one — an interrupted copy
would otherwise put an empty window in front of somebody who came to get their work back.

`ActionId.RestoreDocument` → **"Go back to an earlier version…"**, in the File menu under Save. The
refusal has two forms, because they send the user somewhere different:

| State | What it says |
| --- | --- |
| Never saved | "This newsletter has not been saved yet, so there are no earlier versions of it." → offers Save As |
| Saved exactly once | "You have saved this newsletter once, so there is nothing earlier to go back to yet." |

The dialog is modelled on `RosterRestoreDialog`, because it is the same question — but the promise
underneath is different and the wording has to say so. **The address book's restore is undoable;
this one is not.** Its safety net is that nothing is written:

> The older version opens on screen. Your file is not changed until you save — so if it is the wrong
> one, close september.tboard without saving and nothing is lost.

Generations are named in words — "The last one you saved over", "The one before that", "4 saves
ago" — not as `.bak3`, which is a file extension this audience has no reason to have met.

`DocumentPath` became a property rather than a field, because M39 hung a second fact off it (does
the ring hold anything) and seven places set the path. Six would have been right and one forgotten.
The fact is tracked rather than re-scanned for the same reason `RosterHasEarlierVersions` is:
`RefreshActions` runs on every keystroke, and a directory listing per keystroke is not a menu item's
enabled state.

## 3. The test bug

The regression test was written first and run against the unwired code, as every test in M24–M38
was. It failed — but on the wrong line, several assertions past the one that should have caught it.

**`Session.Dispatch(async () => …)` drops the body.** Avalonia offers `Dispatch(Action)`,
`Dispatch<T>(Func<T>)` and `Dispatch<T>(Func<Task<T>>)`, and **no `Func<Task>` overload at all**. An
`async` lambda therefore binds to the middle one with `T = Task`; the call returns `Task<Task>`, and
awaiting it waits only for the lambda to reach its first suspension point. Everything after the
first `await` — every assertion, every exception — runs unobserved.

Eleven lambdas across six files were in that state: roughly every asynchronous shell test the
project had. `HeadlessSession.DispatchAsync` returns a value from the inner lambda so the call lands
on the `Func<Task<T>>` overload, which is the one that awaits the body.

Awaiting the `Task<Task>` a second time is **not** the fix, and it is worth recording why: Avalonia
tears the `Application` down when the task it was given completes, so under the `Func<T>` overload
teardown starts at the body's first `await` and the rest of the body runs against a dead session.
That attempt turned 2 failures into 115, all of them inside the font manager.

`DispatchDisciplineTests` is a string search over the suite's own sources. The failure mode is a
compiler-legal overload choice, so there is nothing else left to check it with.

### What it had been hiding

Two real failures surfaced the moment the bodies were awaited:

- **`PackagingTests` looked up a menu item by an `x:Name` no control has ever had** —
  `"CheckForUpdatesMenuItem"`. The `.Single(…)` threw on every run since the test was written. It
  now matches on `Tag`, which is how every surface in the shell names the action it performs.
- **Removing the page the caret is standing on crashed the app.** The text session stayed open over
  a frame deleted with its page, so the next `RefreshActions` asked
  `DocumentRenderSource.IsTextBlock` about a block the document no longer held — and that method
  answered by throwing `KeyNotFoundException`, from inside `BuildContext`, outside any handler's
  try/catch.

The crash gets both halves fixed, because either alone leaves the other wrong: `RemovePage` ends the
text session before the page goes, and `IsTextBlock`/`IsWidgetBlock`/`IsImageBlock` now answer "no"
for a block that is not there. They ask a question; a question about a vanished block has an answer.

## 4. What guards it

- `RecoveryServiceTests` — the ring lists newest-first, skips empty generations, and reports none
  for a file saved once.
- `SaveShellTests.SavingOverANewsletterKeepsTheVersionItReplaced` — verified failing against the
  unwired code (empty ring after the second save).
- `SaveShellTests.AnEarlierVersionIsOfferedBackAndOpeningOneLeavesTheFileAlone` — the safety
  argument itself: after restoring, the file's length is unchanged.
- `PageShellTests.RemovingThePageTheCaretIsOnDoesNotCrash` — verified failing against both fixes
  reverted (`KeyNotFoundException: Block not found in document: frame-essay-1`).
- `DispatchDisciplineTests.NoTestHandsAnAsyncLambdaStraightToDispatch` — verified failing by planting
  an offending line.
- `IconTests` and `ActionSurfaceTests` — the new action needed its icon-less record and its handler,
  and the two partition tests are what said so.

## 5. What was NOT done

- **No shortcut.** Going back a version is a deliberate, rare act, and it replaces what is on
  screen. A chord would make it reachable by accident.
- **No thumbnail in the dialog.** `RestoreDialog` shows page 1 of a crash snapshot, and the same
  would help here — but the ring holds up to five, and five decoded page images to answer one
  question is a cost this milestone did not take on. Recorded, not hidden.
- **No pruning of `.bak` files when a newsletter is deleted or renamed.** They sit beside a file the
  app no longer knows about. Harmless, and cleaning up files the user did not ask about is worse.

Suite after M39: **1192 passing, 12 skipped**. No snapshot baseline moved; no screenshot re-baked.
