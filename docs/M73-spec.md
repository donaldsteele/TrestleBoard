# M73 — The app does not offer what it cannot do, or claim what it did not

**Delivered 2026-08-10**, across `c221581` (b), `48c26a1` (a, c), `88c3a85` (e, f), `3072f9e` (g),
and the gates and records of (h) and (i). PLAN.md §11 M73; verification gates 26 and 27.

## 1. Why this milestone was the widest of the three

Five owner-reported bugs in one day shared a family, and **three audits in a row each drew a
boundary that excluded the next one**:

- M70 asked *"does the answer reach the user?"* and missed whether the control could work at all —
  next bug, the cover-date button.
- M71 asked *"can this control's catalog action run?"* and recorded *"other dialogs run no catalog
  action, nothing to audit"* — next bug, the memorial, an offer that never touches the catalog.

Both conclusions were true as written. This milestone is scoped by the **user-visible promise**
rather than by any mechanism.

**The invariant, and why it is two and not one.** *Family A* (the corner-drag crop, the chrome
overlap) is "the state was correct and not perceivable" — a rendering property with no shared
testable form; gate 13's by-eye pass is the honest answer there and it has not been re-run since
M16. *Family B*, everything else, is testable:

> Every question the app asks about the document is asked more than once — to decide whether to
> **offer** a thing, to decide whether to **do** it, and to decide what to **say** afterwards — and
> a defect is any pair of those evaluations that can disagree without the later one winning out
> loud.

## 2. (a) The memorial — five defects, not one

Recording a brother as passed offers *"Would you like to write a memorial notice for him?"* and then
the app declined its own offer.

1. **The offer needed a caret and is made from a menu window**, where a caret is the exception
   (`IsActive` is set only by clicking into text or by `SelectRange`). It now writes the notice into
   a new text frame on the current page via a new `AddTextFrameWith` overload — one
   `CompositeCommand`, one Ctrl+Z, following M66 — selects it, and says which page it went on. With
   a caret it still inserts at the caret.
2. **The close path lost the request in total silence.** `_ = OfferAMemorialAsync(name)` was fired
   from a synchronous `Save()` while `MemorialRequestedFor` was assigned only after the card closed,
   so the window closed while its own modal child was being raised. Requests are now queued and
   every exit drains the queue and awaits each card. **This is the correction to M70's clean result
   "no `catch` swallows silently"**: an unobserved `Task` is outside `ActionRunner`'s catch-all, so
   that statement was true of `catch` blocks and one concept too narrow.
3. **A single slot became an append-only list**, so a second brother recorded in one visit no longer
   erases the first.
4. **A null `PhraseLibrary.Find("memorial")` returned with no message at all.** It now says so.
5. **With no newsletter open the refusal was unfollowable** — "click into some writing" cannot be
   obeyed. It now names what to do instead.

**Deviation, recorded deliberately: `ShowPeople` was NOT gated.** `ActionCatalog.cs:451` records an
M64 decision that People and Import *"do not need a newsletter open, and both are most likely to be
reached on a computer that has never had one — the successor's, on their first afternoon"*. Gating
the action would reverse that and would leave `ShowPeople` a `RemedyId` pointing at a blocked
action. **Gate 26 is satisfied at the offer instead**: the card is handed the handler's own
precondition (`new PeopleWindow(Roster, () => CanWriteAMemorial)`), so with no newsletter the card
still appears — his record has been kept is the half that matters — but it explains and offers Close
rather than "Write a memorial".

**A passing test was deleted, and said so in the commit.**
`PassedBrotherShellTests.WithNowhereToWriteItTheAppSaysWhereToStart` asserted the bug as correct
behaviour and was green. It pinned the refusal as the specification when no-caret is the **normal**
state after accepting the offer. Replaced by two tests: the app does what it offered, and the one
honest refusal must be followable. No assertion was flipped.

**Worth recording on its own:** the test seam `MemorialAnswerForTest` had to be made asynchronous.
The old synchronous seam short-circuited before the modal and **would have hidden defect 2
entirely** — the seam was shaped so the bug could not be reproduced through it.

## 3. (b) Three defects that destroy work — shipped ahead of the rest

1. **"Stop the import" performed the import.** Automation name *"Stop importing and change
   nothing"*; on the Done step `Render()` never hid it, `Result` had already been set by `Commit()`,
   and the shell read `Result` however the window closed and called `Roster.Replace`. It was
   `IsCancel`, **so Escape did it too**. The target is the real address book.
   **Fixed by removing the button on that step rather than by making it undo** — making Escape undo
   would trade one data loss for a quieter one: work the user explicitly asked for, discarded by a
   keystroke on a report screen. `IsCancel` moves to the remaining button so Escape and the visible
   control still do the same thing.
2. **"Put them all back" claimed success having done nothing.** The footer appears whenever
   `CountFontOverrides > 0`, which is whole-document and needs no caret — but the handler called
   `SelectAll(); ClearFontOverride();`, both of which return early without a caret, both bools
   discarded, and then announced *"N pieces of text were put back. Press Ctrl+Z to undo."*
   **Ctrl+Z would then have taken back an unrelated edit.** `ClearEveryFontOverride` now returns how
   many it actually put back and works across every story in one composite, matching the offer.
3. **The recovery card promised "Nothing has been lost" and then deleted.** `Restore == false` meant
   both "no, throw it away" and "closed the window". `RestoreDialog` now returns
   `PutItBack`/`StartFresh`/`Closed`, and only `StartFresh` deletes.

**Deliberately left, to keep a data-loss fix reviewable:** `RosterImportSession.DoneMessage`
overclaims in the same family; `RestoreDialog` still has no Escape path (now safe to add, but not
asked for); `IRecoveryStore.Delete` was `void` — picked up in (e).

**A fourth test was removed rather than kept**: written for the zero branch of (2), it passed
against the unfixed code, so it reproduced nothing.

## 4. (c) The defect M71 introduced while fixing M71's defect

`RunWizardAsync` is shared by insert and re-edit and said, unconditionally, *"Nothing was filled in
yet. Press Ctrl+Z to take it back off the page."* On re-edit nothing was inserted, so Ctrl+Z took
back an unrelated edit — and M71 multiplied the routes to re-edit, so **the owner's own acceptance
sequence ended in a false instruction**. Re-edit now says *"Nothing was changed. This box is exactly
as it was."* The grid branch's asymmetric silence — an `if` with no `else` at all — is gone.

Gate 24 could not have caught this: it stops at "the action is available" and cannot see the next
line. That is why gate 27 exists.

## 5. (d) The offer/caret shape, once, everywhere

A dialog reached from a *menu* offering something that needs a *caret*. Three instances, all closed:
the memorial (§2), "Copy this into this month" (`LastYearWindow`), and "Put them all back" (§3.2).

## 6. (e) Discarded returns that reach an announcement

The single highest-yield sweep in the milestone.

**Fixed.** `TakeMeToTheWord` returns which page it picked the word out on, so the spelling window's
"Show me where it is" says where it went or that the word has moved. `UseFontJustHere` returns its
bool and the shell distinguishes a caret, a highlight, and a font already in force. `NeverAskAgain`
reads `PersonalDictionary.CouldNotBeSaved`, which nobody read — teaching it eleven surnames on a
locked AppData folder told you eleven times it had worked and asked again next month.
`AppSettings.Save` widened `void → bool`, so "Saved." is said only when it reached the disk.
`DeleteSelectedFrame`, `ToggleWrap`, `DismissCropNotice` and `AutoFlow` now announce from their
returns — `AutoFlow` is M71's own command and was silent on success whenever the user picked the
frame themselves. `IRecoveryStore.Delete` widened `void → bool`.

**Verified ALREADY CORRECT, and not touched — six of the sites PLAN.md listed:** `BeginFrameLink`,
`UnlinkFrames`, `AlignSelection`, `DistributeSelection`, `FixPhoto`, and the
`ClearEveryFontOverride` site, which (b2) had already fixed. **PLAN.md's (e) list was stale on those
six**, and §11 has been corrected. A finding that cannot be reproduced is not a finding.

**Two tests were dropped for passing before their fix.** Failing-first was demonstrated by reverting
only the announcement logic, because a pre-fix tree cannot compile a test written against a return
value that did not yet exist — so **two of the widenings are guarded rather than
regression-proven**, and that is stated rather than implied.

**Scope note:** `SpellChecker.cs` gained a one-line pass-through property, slightly outside "chrome,
catalog and editing".

## 7. (f) `RunAsync`'s null was a success claim the runner could not make

`ActionRunner.RunAsync` returned `null` for "ran and nothing threw", which covered four different
things: the command did something; it silently early-returned; the user cancelled a picker or a
wizard; and — `TryGetValue` with no `else` — **the action id had no handler at all**. Two windows
read `null` as success, and they are the two where a nervous user is likeliest to cancel: Help's "Do
it for me" said *"Done: Put a picture here…"* after the file picker was cancelled, and every
`ReviewWindow` remedy did the same.

Replaced with `ActionOutcome` — `Refused` / `NothingHappened` / `DidSomething`, carrying the reason
where there is one. `Refused(reason)` throws on an empty reason, mirroring `ActionAvailability`:
**M11's rule that nothing becomes unavailable without saying why now applies to outcomes too.** A
missing handler is loud rather than a silent success.

**Deviation, stated rather than discovered later: the widening is per-handler.** Handlers that were
not individually widened still default to `DidSomething`, so a cancelled Open or Export picker still
reads as done in those two windows. The mechanism exists; only the named handlers are wired to it.

## 8. (g) Staleness from document *mutation*

M70 (g) closed only the document-*switch* case. `FindController` was the **only** `session.Changed`
subscriber in the app and the reference implementation the other five never copied. All four sites
reproduced as real defects; none was already correct.

1. **Read Aloud captured the sentence offsets and a copy of the text once.** Editing a paragraph
   mid-walk had a proofreading tool reading the pre-fix wording back to a committee member while the
   canvas banded the wrong characters — no exception, no clue, because `StoryTextGeometry` clamps.
   **Chosen: re-read, not invalidate.** Editing what you hear IS the workflow here, so discarding
   the walk would end the read-through every time it did its job. **Same words beat same offset**:
   typing ahead of the sentence being read leaves the old offset owned by the new writing. It
   deliberately does not re-speak — a voice starting unasked mid-correction would alarm this
   audience; "Say that again" speaks the new wording on request.
2. **`HelpWindow` computed "Take me there" visibility once in `ShowAnswer`.** M71 (b) recorded that
   surface clean, which was true at render time and false a second later: do what the answer told
   you to do and the button stayed hidden carrying a reason that was no longer true. It now rechecks
   from `RefreshActions`, answering into the window's own live region. The press-time re-check is a
   different moment and is untouched — **it remains the strongest behaviour of its kind in the app**.
3. **`ReviewWindow` narrated "You are on page 4" after a clamped `GoToPage` landed elsewhere.** The
   clamp is what made the lie confident. It now reads the page number back after the clamp and
   returns false when the page moved, so a remedy no longer runs against a page the finding was not
   about.
4. **The spelling guard was occurrence-blind.** It checked that the same characters sat at the
   recorded offset, not that it was the same occurrence, so a realigning edit that slid an identical
   word onto that offset **corrected the wrong copy and reported success** — the only silent
   corruption the audit found. Misspellings now carry an occurrence number counted over all words in
   the paragraph.

**Noted, not fixed:** `SpellingWindow` re-scans only after its own correction, so an edit on the
page behind it leaves its list stale until the next change. No longer dangerous — the guard now
refuses honestly — but it is the one window still lacking the subscription the others have.

## 9. (h) The two gates

### Gate 26 — offer integrity (`tests/App.HeadlessTests/OfferIntegrityTests.cs`)

*A control's gating predicate must be obtained from its handler's precondition rather than restated
beside it.* It is three different strengths of check, labelled rather than blended:

- **Proved by running the app.** The menu bar and the toolbar are walked in four real states — no
  newsletter, a newsletter with nothing selected, a text frame selected, typing in it — and every
  one of the **123 distinct tagged controls** is compared with `ActionCatalog.Evaluate` for the live
  context: **492 comparisons per build**, plus the assertion that every refusal carries its reason
  where a screen reader will find it. The help window is walked across **all 113 answers in the
  index**, asserting "Take me there" is visible exactly when the catalog says yes and that the
  reason is on screen when it says no.
- **Proved elsewhere.** The action panel is gate 24's and `ActionSurfaceTests`'; the memorial card is
  `PassedBrotherShellTests.WithNoNewsletterOpenTheCardDoesNotOfferAMemorial`. Gate 26 asserts the
  *wiring* those depend on — that the shell hands the card `() => CanWriteAMemorial` and the handler
  refuses on that same member — which is a source-text check and proves only that the two names
  match.
- **Not proved, held.** For a dialog that gates a button on something other than the catalog there
  is no pure function to compare against. So the check is that **every such control is enumerated
  from the source and classified** as `FromTheCatalog`, `FromTheOwner` or `ItsOwnState`. Eighteen
  are classified today. It cannot tell a correct predicate from an incorrect one; it can, and does,
  refuse to let a new gated control appear without somebody answering the question — which is the
  step that was skipped all three times. It fails in both directions, so a stale entry is a failure
  too.

**The action panel is deliberately excluded from the enabled-state comparison**, and its absence is
the M11 rule rather than an oversight: nothing in the panel is ever greyed. Comparing its
`IsEnabled` against the catalog would flag M11 working correctly.

### Gate 27 — outcome honesty (`tests/App.HeadlessTests/OutcomeHonestyGateTests.cs`)

*No method may announce an outcome that is not a function of a value returned by the operation it
announces*, checked as PLAN.md proposes: no discarded `bool`/`int` return in a method that reaches
an `Announce`/`Tell`/`_status.Text`/`StatusMessage =`.

**It is a ratchet, not a net, and this is the honest finding of the milestone.** The analyser was
run against the tree as it stood before M73 (`60bd8d7`) and **found nothing** — not because that
tree was clean, but because the defining feature of (b2), (e) and (f) was that the operation
returned *nothing at all*: `SelectAll` was `void`, `AppSettings.Save` was `void` with an empty
catch, `IRecoveryStore.Delete` was `void`. A sentence cannot be a function of a value that does not
exist, and no static check over source text can see the absence of a concept. **M73 (e) and (f)
created those return values; this gate is what stops them being discarded again.**

Because the defect it was written for no longer exists in the tree and cannot be found in the tree
that had it, "the gate fails before the fix" is demonstrated the only honest way available: the
analyser is run against the (b2) defect written out as source, and must report both discards.

Its other limits are in its doc comment: it reads text, not a compilation; it resolves a call's
receiver only through field declarations, so a call on a local or a parameter is invisible; it
matches by name within the resolved type, so overloads are indistinguishable; and "in the same
method" is the whole body, not a real control-flow path.

**Census on the current tree:** 89 source files, 149 value-returning methods, 127 methods that
announce, 232 statement-level calls resolved to a declared type, **1 discard**. Each of those counts
is asserted with a floor, so an enumeration that silently empties fails rather than passes.

**The one discard, accepted with its reason:** `MainWindow.ToggleActionPanel` discards
`AppSettings.Save`. It is a false positive of the proxy rather than a violation of the rule — the
sentence this method says is about the panel and *is* a function of the operation it announces (M70
(d) fixed that); the discarded bool belongs to a second, unannounced operation, remembering the
choice for next time. **That silence is a real question and is listed in §11 as a follow-up.**

**Both gates carry the M71 (d) guard** — `TheGateDoesNotObjectToAnHonestRefusal` — and an
anti-vacuity test, exactly as gate 24 does.

## 10. (i) Two records of ours that overclaimed

1. **The missing spec docs.** `docs/M69-spec.md`, `docs/M70-spec.md` and `docs/M71-spec.md` now
   exist. `M70-spec.md` §7 is the part that matters: PLAN.md M70's claim that roughly thirty "can
   never fire" guards "are listed in the audit" is **admitted as an unrecoverable gap** rather than
   backfilled with a plausible list, because the next person would trust an invented one and would
   either "fix" a load-bearing guard or leave a genuinely dead one. What is recoverable — the rule
   they were recorded for, the fact that "can never fire" is conditional on the catalog, and a
   method for regenerating the list — is written down instead.
2. **`SilentNoOpTests.AnAvailableCommandAlwaysLeavesSomethingToRead` was widened rather than
   renamed.** It called itself *"the rule behind (c), asserted once rather than command by command"*
   while iterating a hardcoded two-element array, so every finding in (e) was invisible to it. It
   now enumerates the catalog: every command in the groups that act on the page, minus those whose
   title ends in "…" or "▸" — the app's own mark for "this opens something and asks you", so both
   halves of the filter are read off the catalog and a new command joins the test by existing. **It
   runs 24 commands where it ran 2, and found no new defects.** The groups left out — `Newsletter`,
   `Page`, `People`, `Everything`, `Help` — each open a window, a picker or a confirmation, and a
   window IS an answer, so silence is not the failure mode there; that gap is stated in the test's
   own doc comment.

## 11. Clean results, and findings deliberately not fixed

**Recorded because a clean result nobody wrote down is why this milestone exists.**

- **The catalog surfaces are clean and are now checked on every build**: 123 tagged controls × 4
  states, and 113 help answers (§9). *PLAN.md's acceptance asks for "roughly forty verified-correct
  offers". That figure came from the audit narrative and the individual list was never written down;
  it is not reconstructed here. What replaces it is the mechanical enumeration above, which is
  larger, is checked rather than remembered, and cannot go stale.*
- **The non-modal windows are exactly six**: Find, Spelling, Help, Review, ReadAloud, LastYear.
  `FindWindow` is clean in every dimension the three audits looked at — its own live region, a
  status-bar mirror, closed on a document switch, and the only `session.Changed` subscriber.
- **Document-switch handling was closed by M70 (g)** and re-confirmed: Review, ReadAloud, LastYear
  and Spelling close; Help stays open because it holds no copy of anything; bringing in a
  predecessor's pack is not a switch at all.
- **`HelpWindow`'s press-time re-check is the strongest behaviour of its kind in the app** and was
  not touched by (g), which fixed only the display half.
- **Six of (e)'s listed sites were already correct** (§6), and PLAN.md §11 has been corrected.
- **The unreachable-guard list** — PLAN.md M70's "roughly thirty" — could not be reconstructed. See
  `docs/M70-spec.md` §7. What this milestone adds to it: (e)'s six already-correct sites and M70
  (c)'s two unreachable no-op paths are the concrete, recorded members of the "do not fix this into
  noise" family.

**Not fixed, listed with the reason:**

| Finding | Why not now |
|---|---|
| `RosterImportSession.DoneMessage` overclaims | same family; kept out of a data-loss fix to keep it reviewable |
| `RestoreDialog` has no Escape path | now safe to add, but not asked for |
| `SpellingWindow` has no `session.Changed` subscription | no longer dangerous — its guard refuses honestly (§8.4) |
| `ToggleActionPanel` discards `AppSettings.Save` | §9; the sentence is not about the save, but the save's silence is real |
| `RunAsync`'s widening is per-handler | §7; unwidened handlers still default to `DidSomething` |

## 12. What this milestone cannot do

Every gate here is static: it reads source or evaluates pure functions. Gate 24 was only possible
because `ActionCatalog.Evaluate` is a pure function of context, which is luck the dialog and canvas
surfaces do not share, so checks there are weaker by nature — and gate 27 is a ratchet that could
not have caught the defects it was written for (§9).

**All five bugs were found by the owner using the app.** Gate 23's screen-reader pass
(`docs/accessibility-test-script.md` §21, sixteen steps, rows 21.1–21.16 still blank) remains
unwalked, and gate 13's by-eye pass has not been re-run since M16 — *Family A* has no other answer.
**A hands-on pass is worth more than another audit round**, and this milestone is not a substitute
for one.

## 13. Acceptance

Chrome, catalog and editing only; nothing in `Core`, `Layout`, `Rendering` or `Export.Pdf`, and no
snapshot baseline moved. Every fix in (a)–(g) was verified failing before its fix except where §6
states otherwise.
