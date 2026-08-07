# M25 — Don't crash, don't corrupt

**Delivered 2026-08-07.** The second milestone from the review recorded in `PLAN.md` §14 —
grouping 2 of §14.5. Where M24 made the user's work keepable, this stops it being destroyed or
thrown on the floor by a crash.

---

## 1. Two confirmed defects, each with a demonstrated failure

### 1.1 Undo, straight after clicking into a new text frame, took the app down

`TextEditorController.OnDocumentChanged` clamped the selection through `CurrentStory()`, which is
`Document.GetStory(...)` — and that throws `KeyNotFoundException` for a story that is not there.

"Add a text frame" is one **composite** command over a story *and* a block, so its revert removes
both. Click into the frame you just added, press Ctrl+Z, and the change notification arrived at a
session pointing at a story that no longer existed. The exception came out of an event handler, so
there was nothing to catch it: the process went down, taking the newsletter with it.

`FrameEditorController.OnDocumentChanged` has handled exactly this case for its *block* since M5.
This was the other half of the same pair, never written.

**Fix.** `Document.TryGetStory` — the non-throwing counterpart of the existing `TryFindBlock`, added
for the same stated reason. The session ends when its story has gone (or has no paragraphs left),
which is the only honest thing a session over a deleted story can do. `Clamp` became static over a
story that has already been proved to exist.

### 1.2 Deleting the middle frame of a linked chain printed the article twice

`FrameEditorController.DeleteSelected` nulled the predecessor's `LinkNext` unconditionally. For a
chain A→B→C with B deleted, that left **A and C both pointing at nothing while both still
referenced the same story** — two heads on one story, which the entire linking model forbids. The
article was laid out from its first paragraph in two places on the page.

It also permanently disabled the story-cleanup guard for that story, because
`AnyOtherBlockUsesStory` then always answered true.

**Fix.** The chain heals across the gap: A→C. One line — the predecessor is pointed at the deleted
frame's continuation rather than at null — but it restores the invariant `Unlink()` guards from the
other direction by minting a new story for the frame it detaches.

---

## 2. Crashes that had nowhere to be reported

| Where | What happened | Now |
| --- | --- | --- |
| `ActionRunner.RunAsync` | Every surface starts a command with `_ = RunAsync(...)`, so an exception from **any** handler went into a discarded Task: the button did nothing, the app said nothing, and half-finished work stayed half-finished | Caught at the command/app boundary and reported in a sentence, with the window and the newsletter still there |
| `Opened += async` | The startup flow reads the recovery directory, parses a snapshot and opens whatever the command line named — all disk work. A failure took the app down **at launch**: a window that flashed and vanished, with no way to find out why | Lands on the start screen and says what happened |
| `OnCanvasDrop` | `async void`, because Avalonia's drop event has no other shape — so an unreadable dropped file or a picture Skia refused went straight to the process | Guarded end to end; the newsletter is left unchanged |
| `PageDrawOperation.Render` | Runs on the **compositor thread** against a render source the UI thread owns and disposes. Open another newsletter, or remove the page a queued operation was told to draw, and it threw there | The stale frame is skipped. A dropped frame is invisible — the repaint that follows the change draws the right thing — and a render-thread exception is not |

The `ActionRunner` catch is deliberately broad, and says so in the code: this is the boundary
between "a command" and "the app", and on the far side of it there is no caller left to handle
anything. The alternative is not a better error; it is the process going down.

---

## 3. A damaged file now reads as damaged

`TboardContainer` and `MigrationRunner` promise a plain-language `UnsupportedFormatException` for a
file this build cannot open, and the shell catches exactly that plus `InvalidDataException`. But an
unparseable `formatVersion` (`"1.0.0-beta"`, a number, an empty string) escaped as `FormatException`,
and a truncated entry as `JsonException`. So the corrupt file — the one case the contract exists for
— was the only case that produced an unhandled-exception dialog.

Both are now caught and rewritten as sentences that name the damaged part and suggest an earlier
copy. `UnsupportedFormatException` gained an optional inner exception so the original is not thrown
away.

---

## 4. One leak

`PeopleWindow` subscribed to `RosterService.Changed` and never unsubscribed, from a service that
lives as long as the app. Every People window ever opened stayed alive for the session, and every
roster change ran `RefreshList` on all the dead ones. Now unsubscribed on `Closed`, with a named
handler rather than a lambda so there is something to detach.

---

## 5. Investigated and NOT changed

**`RecipeCache` eviction (§14.2, Major PLAUSIBLE).** The review argued that `Trim()` disposing the
LRU tail immediately could dispose an image the renderer was still about to draw. Reading the only
call site — `DocumentRenderSource.RenderImage` — the image is fetched and drawn in the same few
lines and is never held across another `GetOrAdd`. The failure is not reachable in the current call
pattern, and inventing a deferred-disposal mechanism for it would add a concurrency concept to a
class whose whole design note is that it is single-threaded and UI-thread owned.

Recorded rather than "fixed", because the constraint that makes it safe is a property of the caller
and could quietly stop being true. If a future renderer ever holds cached images across fetches —
a display list, a batched draw — this becomes live.

---

## 6. What guards it

- `TextEditorControllerTests.UndoingTheFrameTheCaretIsInEndsTheSessionInsteadOfThrowing`
- `FrameEditorControllerTests.DeletingTheMiddleOfAChainJoinsTheTwoEndsRatherThanSplittingTheStory` —
  asserts one head, one story, two frames of geometry, and that undo restores the chain
- `ContainerTests.ADamagedVersionStringIsReportedInPlainLanguage` (3 cases) and
  `ATruncatedEntryIsReportedInPlainLanguage`

Both regression tests were **run against the unfixed code and confirmed to fail**, so neither is a
test that would have passed anyway.

Suite after M25: **1138 passing, 12 skipped**. No snapshot baseline moved.
