# M74 — The final-check findings

**Delivered 2026-08-10**, in v1.3.1. PLAN.md §11 M74. Opened the same day, after v1.3.0 was published.

> **Every finding was fixed, including the two each wave first proposed to leave** — the eleven
> unwidened "replace what is on screen" handlers, and gate 27's `ToggleActionPanel` allow-list entry.
> The allow-list is now empty. Two *limits* remain, recorded in §7: gate 27 cannot see `Task<bool>`
> widenings, and `ShowTextStylesAsync` has no test because its sheet is an awaited modal with no
> override and no seam was invented to manufacture coverage.

## 1. What this milestone is

v1.3.0 shipped M54, M72 and the whole of M73 in a single day. Before calling it production-ready,
four independent reviewers went over it — one each on the newest code, the fix wave, the
data-integrity core, and what the user actually receives. This milestone is what they found.

The reviewers were deliberately given **overlapping** briefs, because the lesson of M69–M73 is that
each audit's boundary excluded the next bug. The one thing every brief demanded was that a finding
be traced to real lines before being reported, and that anything untraced be labelled speculation.
Two reviewers used that label; both times the labelled item turned out to be the minor one.

## 2. The headline: nothing found was a v1.3.0 regression

Worth stating plainly, because it is the question the review existed to answer. The two HIGH
findings are both older than v1.3.0 — the PDF export has never been atomic, and gate 27 was blind
from the moment it was written. The seam most likely to fail in the field was verified **safe**.

## 3. The interop seam, verified rather than assumed

The scenario: member A on v1.3.0 puts an emblem in the newsletter and emails the `.tboard` to member
B, still on v1.2.0. This is the most likely real-world failure of the release, because M72 changed
the document format and committee members share files by email.

The reviewer read **the v1.2.0 tag's own code** rather than reasoning about it:

- v1.3.0 write: `TboardContainer.StampVersion` sets `FormatVersion` *and* `MinReaderVersion` to
  1.1.0 when a page holds a drawing, 1.0.0 when it does not.
- v1.2.0 read: `MigrationRunner` compares `minReader` against its own `CurrentFormatVersion` (1.0.0)
  and throws `UnsupportedFormatException` **before any deserialization**.
- v1.2.0 shell: catches it on both the double-click path and the menu-Open path and shows *"This
  newsletter was saved by a newer version of TrestleBoard. Please update TrestleBoard, then open the
  file again."*

No JSON exception, no crash, no silently dropped drawing. The unknown `"type": "vector"` discriminator
is never reached because the gate throws first. **The ordering is what makes this safe**, and it
pre-dates M72 — that ordering is the single most load-bearing line in the interop story.

## 4. What was fixed, and the one thing each fix teaches

### (a) The PDF export was the only write path without temp-then-rename

Every other writer in the app — `TboardContainer.SaveToFile`, `SuccessorPackContainer`,
`RosterStore.Save`, `FileRecoveryStore.WriteAtomic` — writes to a temp file beside the target,
flushes to disk, and renames over. The PDF export wrote straight into the destination with
truncate-on-open.

The consequence is worse than a lost export: it destroys **last month's** newsletter and leaves a
truncated file with the right name and the right icon, which is exactly the file that then gets
emailed to the lodge. The flagship output of the product had the weakest write discipline in it.

A place with no local path (a cloud location) is now refused in plain language rather than written
through a handle that cannot honour the untouched-file guarantee.

> **The lesson worth keeping:** four correct implementations of a discipline in one codebase did not
> cause the fifth site to adopt it. Consistency is not contagious; it has to be checked for.

### (b) Gate 27 was blind to three quarters of the shell

The gate's type scanner matched `\b(?:class|record|struct)\s+(\w+)` over raw source. A doc comment
reading *"…the **record that** it has now been offered"* parsed as a type declaration named `that`.
Every method below was attributed to the phantom type, receiver resolution failed, and no discard
could be reported there.

The gate shipped one day before this review, in the milestone whose entire subject was records that
overclaim. It reported exactly one discard and was believed.

All four surfaced discards were **fixed rather than allow-listed**, and the allow-list is now empty
— including `ToggleActionPanel`, whose original entry admitted in its own comment that the silent
settings-save was an open question. The census floors were raised to sit deliberately tight: the
poisoned scanner scored 234 resolved calls and the floor is now 240, so this exact collapse cannot
pass the anti-vacuity test again.

> **The lesson:** the anti-vacuity test existed and passed throughout, because 232 calls still
> resolved *somewhere*. An anti-vacuity floor set comfortably below the true number measures
> nothing. Floors must be set tight enough to detect a partial collapse, not just a total one.

### (c) `ActionOutcome`'s coverage was thinner than its own commit admitted

M73 (f) stated the limitation honestly — unwidened handlers still default to `DidSomething` — but
did not enumerate what that covered. It covered three of the four review remedies, so cancelling a
caption dialog had the review window say "Done" about an accessibility worry the user had just
declined to fix, and it covered "Words for hard news", so a grieving committee member who cancelled
was told the memorial words had gone in.

It also produced a live contradiction: Help said "Done:" while `ImportPeopleAsync` simultaneously
announced "The import was stopped. Nothing in your address book was changed." Two live regions
asserting opposite outcomes of one action.

> **The lesson:** "this fix is partial" is not a sufficient disclosure. A partial fix must name the
> remaining surface, because the honest sentence in the commit message did not stop anyone — the
> author included — from assuming the important cases were the ones that got done.

### (d) Three data seams

`RosterStore.ReadBackup` swallowed read failures and returned `Empty` with no flag, and
`RosterService.Restore` wrote that empty book over `roster.json`. This is the **M24 item-4 bug
shape**, on the one reader that never received the `CouldNotBeRead` contract the main load path got.
The bug class was fixed, documented, and regression-tested in M24 — and survived on a sibling reader
for six milestones.

A failed open nulled `DocumentPath` instead of restoring it, so opening a damaged file left the
still-open newsletter with no path: Ctrl+S became a surprise Save-As and crash recovery would have
offered it as "never saved". The picker-driven sibling had the identical defect and was fixed in the
same commit rather than left as a named follow-up.

`RequiredVersionFor` scanned `Pages` but not `PageMasters`, so a drawing on a master would be stamped
1.0.0 and let a pre-M72 reader past the version gate into the crash §3 describes. Latent — no UI
path puts drawings on masters today — and the test says so.

### (e) The M73 (g) fix fought the workflow it was built for

`ReadAloudSession` subscribing to `session.Changed` was right. Re-rendering the whole window on every
keystroke was not: it navigated the canvas back to the sentence being read, moved focus, and
re-announced an unchanged failure sentence. Editing while proofreading is the workflow the
subscription exists to serve, and the subscription degraded it.

> **The lesson:** a fix that makes a window *react* must distinguish who asked. A render triggered by
> the document changing has not earned the right to navigate or move focus; only a render triggered
> by the user pressing something has.

## 5. The clean lists — the verified map

**This section is the reason the milestone has a spec doc.** A clean result nobody wrote down is how
this project ended up needing M73 at all: PLAN.md M70 cited an audit listing ~30 unreachable guards,
and that audit did not exist. What follows was verified against real lines during the final check and
is trustworthy as of 2026-08-10.

### Data integrity — verified sound

- `.tboard` save is atomic: temp beside target, `Flush(flushToDisk: true)`, rename over, temp deleted
  on failure with the original exception preserved. AV holding the target surfaces an IOException with
  the original untouched; a crash mid-save affects only the `.tmp`.
- Backup-ring rotation happens **before** the save, oldest-first, with per-generation failures
  swallowed so a locked `bak-N` cannot abandon the `bak1` copy that matters.
- The M24 `SaveNow`/`Complete` self-destruct pair is verifiably gone — `SaveNow` has zero production
  callers. The "surviving snapshot means a crash" invariant holds.
- The roster **main** path genuinely distinguishes `NoFileYet` / `CouldNotBeRead`, and every write
  funnels through `Apply`/`Undo`, which refuse while unreadable. Two concurrent saves collide on the
  fixed `.tmp` name and the second throws rather than corrupting.
- OfficersSync and BirthdaySync write only into document widgets through undoable commands. They
  never write the roster file.
- Every lazy-capturing command re-captures pre-state on each `Apply`, so the redo cycle is sound.
  Merge updates only the "new" side; merging is blocked across an Undo; composite partial-failure
  rollback is tested.
- Missing or undecodable assets render as a grey placeholder — never a crash, never dropped from the
  container.

### M72 — verified sound

- The migration chain's exact-match loop terminates, runs in the right direction, and the min-reader
  gate precedes typed deserialization (see §3).
- Version transitions are content-derived in both directions: add a drawing → 1.1.0; delete it →
  back to 1.0.0, which correctly re-opens the file to older readers.
- Carry-forward and successor packs round-trip through `TboardContainer.Save`/`Load`, so `VectorBlock`
  is covered by construction. There is no block-level copy/paste or duplicate-page feature that could
  lose a drawing.
- `RenderBlock` is an exhaustive switch that throws on unknown types rather than silently skipping.
- Every `SelectionKind.Photo` comparison in `ActionCatalog.Evaluate` has an explicit, tested Drawing
  decision. `ActionRunner` re-evaluates availability before running, so a keyboard chord cannot
  bypass a refusal.
- Picker thumbnails are tracked and disposed on every refill and on close; all Skia surfaces are
  `using`-disposed.

### Release artifacts — verified sound

- Velopack compares its own package metadata, never the assembly version, so the `<Version>0.1.0</Version>`
  left in the csproj cannot cause a spurious update decision. The only in-app version readers get the
  injected tag version.
- Bundled-fonts-only is **structural**: all 59 faces are embedded resources, so packaging cannot omit
  them, and `FontStore` throws rather than touching a system font. The dictionary is embedded the same
  way.
- A v1.2.0 `settings.json` with no `sicknessContactOffice` loads cleanly and gets the Secretary
  default, including for a whitespace value.
- The update check reports failure honestly, including the timeout case, and applies only on exit. A
  portable copy that cannot self-update says so in plain language rather than failing.
- Fonts, dictionary, emblems and PolyForm all have manifest entries or embedded licences, surfaced in
  Help and test-enforced.

### M73 — verified sound

- `RosterImportWindow`: `Result` is set only by "Add these people"; Escape is remapped with the Stop
  button; X or Escape on earlier steps leaves `Result` null and produces the honest announcement.
- `RestoreDialog`: `Choice` defaults to `Closed`; X, Alt+F4 and app-quit all keep the snapshot; the
  delete's bool is read before "thrown away" is claimed.
- The spelling `Occurrence` counter is stable under corrections that change word counts, because the
  window re-scans after every successful change. Hostile mid-wizard edits fail safe — a refusal,
  never a wrong-copy correction.
- `ReadAloudSession` disposal is clean: the subscribing overload is constructed in one place, the
  window unsubscribes on close, and a document switch closes the window. No leak.
- The M54 phrase work handles the emptied pre-filled box (falls back to the default in both preview
  and inserted text) and special characters (no regex anywhere, so braces land literally).
- Gate 26's 492-comparison menu/toolbar walk and 113-answer help walk are real proofs, and its
  action-panel exclusion masks nothing beyond what its comment says.

## 6. Two traps that have now bitten twice

Recorded because each cost a reviewer or an implementer real time, and both are invisible in a green
suite.

**`Session.Dispatch` with an async lambda.** The first version of the export-atomicity test used it
and "passed" by not awaiting. `HeadlessSession`'s own doc comment warns about this — it is the M39
trap — and it still happened. Use `DispatchAsync`.

**A regression test that reproduces nothing.** Three times in two days a test was written, passed
against the unfixed code, and was correctly discarded. A fourth nearly shipped: gate 27's proof test
originally placed the phantom-making prose *above* the class declaration, where it passed even on the
broken regex. Demanding failing-first is what caught all four.

## 7. What is deliberately not fixed

**Two limits of the fixes themselves, found while making them:**

- **Gate 27 cannot see a `Task<bool>` discard.** Its value-returning regex matches bare `bool` and
  `int`, so every handler widened in (c) and (f) — thirty-odd of them — is outside its view. The gate
  protects the synchronous discards it was built for and no others. Widening the regex is a
  follow-up; doing it during a release candidate, immediately after the same gate proved to have been
  blind for a day, was the wrong moment.
- **`ShowTextStylesAsync` is the one widened handler with no test.** Its sheet is
  `await window.ShowDialog(this)` with no test override, so a headless run would block on a modal
  nothing can close. No seam was invented to manufacture coverage — an untested widening that is
  honest about being untested is better than a test that proves the seam works.

**Older items, unchanged by this milestone:**

- The remaining `_settings.Save()` discards in methods that announce nothing (`ClaimTheTour`,
  `OfferTheReviewAsync`, two test seams) are outside gate 27's rule, which is about announcements
  resting on discards. Noted, not fixed.
- A zip-bomb-shaped document is loaded fully into memory with no cap; worst case is an OOM while
  opening, with nothing on screen to lose.
- Duplicate block ids are never validated; commands act on the first match. Odd undo behaviour on a
  hand-edited file, no crash, no silent drop.
- `ReplaceImageCommand` does not carry `SourcePdfAssetRef` through, so swapping a picture brought in
  from a PDF leaves stale provenance. Nothing reads that field today; it is a trap for a future
  "re-fetch from the PDF" feature.
- `CommandTests.EveryCommandTypeHasIdentityCoverage` reflects over the Core assembly only. Coverage is
  complete today because every command lives in Core; a command added in `Editing` tomorrow would
  escape the gate.

## 8. What still needs a person

Unchanged by this milestone, and the reason a hands-on pass is scheduled: the NVDA passes
(`docs/accessibility-test-script.md` §21, sixteen steps, still blank), the by-eye theme checks at 100%
and 200%, clean-machine installs on fresh Windows and macOS, the printed captioned-photo check, the
auto-update round trip, and the July 2026 real-world issue recreation.

One item from this milestone specifically needs those hands: **whether ReadAloud's per-keystroke
`_heading.Focus()` was actually stealing focus across windows under a real screen reader.** The
mechanism is fixed either way, but the severity was never confirmed — a headless harness cannot
answer it.
