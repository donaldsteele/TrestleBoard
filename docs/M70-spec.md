# M70 — The answer has to land where they are looking

**Delivered 2026-08-09**, all eight items, across `8325602`, `e8f5273`, `22d1ad3`, `e5712b9` and
`2e80112`. PLAN.md §11 M70; verification gate 23.

> **Written on 2026-08-10.** See the note at the head of `docs/M69-spec.md`: this is one of the
> three spec documents that were skipped, and PLAN.md M70 says roughly thirty "can never fire"
> guards "are listed in the audit" when no audit document existed. §7 is the honest account of what
> can and cannot be recovered.

## 1. Why this milestone existed

Four defects in one day shared a root: **the app answered, and the answer arrived somewhere the user
was not.** Not one was a broken mechanism. For an audience that is elderly and in part using a
screen reader, an answer that cannot be perceived is identical to no answer at all — the same claim
§6 already makes about a control that cannot be reached.

Three audits of the whole app were run on 2026-08-09. This milestone is their findings, and every
item was a cited finding rather than a suspicion.

## 2. What each audit examined

- **The delivery of a sentence**: `MainWindow.UpdateStatus`, `Announce`, every controller
  `StatusMessage`, and `ActionRunner.RunAsync`'s refresh on every path.
- **Every non-modal window** and what it does with an answer: Find, Spelling, Help, Review,
  ReadAloud, LastYear.
- **Every catalog command's do-nothing path**: can it complete having done nothing, and does it say
  so.
- **Every wholesale container rebuild** and what happens to focus.
- **Every live region declared in the app**, and every dialog that can refuse to close.
- **Document lifetime**: what happens to an open window when the newsletter under it is swapped.

## 3. The eight items, and what each found

**(a) The status bar could not be relied on, and everything else rested on it.** `UpdateStatus`
composed the controllers' `StatusMessage` *before* `_announcement`, and the runner refreshes after
every command — so a lingering controller message silently discarded the sentence just announced,
**including every catalog refusal**. `PhotoController.StatusMessage` was never cleared, so one
"Picture fixed" could shadow every announcement for the rest of the session. Separately, pressing
the same blocked action twice assigned an identical string, Avalonia raised no property change, and
the live region stayed silent on the repeat. M11's central promise was being made by the catalog and
broken at the point of delivery.

**(b) Five non-modal windows answered into a status bar the user could not see.** Each gained its
own polite live region — the pattern `FindWindow` and (since `8a73929`) `SpellingWindow` already
used, so this was the third instance of an existing shape rather than a new invention. The worst was
`ReadAloudWindow`, which discarded `ISpeaker.Say`'s `false`: the heading read "Reading" to somebody
hearing nothing.

**(c) Ten commands could complete having done nothing and say nothing** — restack on the frontmost
frame, "Make it fit" straight after a widget edit, Paste with an empty clipboard, Zoom at either end
of the ladder, Bold/Italic with a caret, "Go back to an earlier version" with a pruned ring, F6 with
no focusable region, Undo/Redo (which announced nothing although the catalog knows the step's name),
and a cancelled update check that ignored `userAsked`.

**(d) Two commands said something happened when nothing did** — `ToggleActionPanel` announcing the
panel was showing when a narrow window had refused to show it, and `ClearFontOverrideHere`
announcing "Put back to the usual font" before knowing whether anything was put back.

**(e) Focus was dropped by wholesale rebuilds.** Five containers are cleared and rebuilt; three put
focus back. `WidgetGridWindow` had no `.Focus()` call anywhere in the class; the action panel — the
most frequent instance, rebuilt after every command — destroyed the button a keyboard user was
standing on, although `PanelButtonFor` had existed since M69's flyout fix and was exactly what was
needed.

**(f) Screen-reader gaps.** `WidgetGridWindow` was the worst case in the app: press "Save it" with a
validation error and a screen-reader user was told nothing at all. It now uses three techniques
together — a polite live-region summary, a window rename, and focus onto the first offending input
with the reason in its `HelpText`.

**(g) Non-modal windows survived a document switch.** Only Find was closed.

**(h) One residual edge in M69's own fix**, where `ShowParagraphStyles` fell back to the detached
control the fix existed to avoid.

## 4. What the work corrected in the audits themselves

Recorded because three of the eight found the defect *again* while fixing it, and because two of the
audit's own statements turned out to be wrong:

- **Draining controller messages instead of polling them was tried and reverted.** Polling is why a
  link-mode instruction disappears when link mode ends; the real bug was one-shot sentences stored
  in a state-shaped field with nothing to reset them. `FrameShellTests` caught the regression.
- **`LastYearWindow` is not merely read-only**, so it closes on a document switch — the brief's
  suggestion that it might survive was wrong. Its copy button is a live edit that would drop a June
  article into a December newsletter.
- **`SpellingWindow` was already closed on a switch**, since M52. What it never did was say so.
- **Bringing in a predecessor's pack is not a document switch at all**: M64 restores stores, not the
  newsletter.
- **`Help` is kept open on a switch**, and is the right one to keep: it holds no copy of anything.
  Only the answer already drawn was stale.
- **Found on the way, unrelated to the brief:** `RetargetSpans` executed its composite even when
  every span already had the style it was being given — a command that rewrote each run as itself,
  costing an undo step and marking the newsletter unsaved for no visible change.

## 5. The clean results — what the audits examined and found correct

These are recorded so nobody re-audits them:

- **The flyout and anchor surface is clean.** Two `ShowAt` sites, both anchored to stable controls;
  no `ContextFlyout` and no `Popup` anywhere in the app; and no handler captures a `Control` across
  an `await`. M69's flyout was the only instance.
- **No `catch` reachable from a user-invoked action swallows an exception silently.** *(Corrected by
  M73: this clean result never covered `PeopleWindow`'s `_ = OfferAMemorialAsync(name)`, an
  unobserved `Task` outside `ActionRunner`'s catch-all. The statement was true of `catch` blocks and
  the boundary was drawn one concept too narrow — see `docs/M73-spec.md` §2.)*
- **The status bar, `SaveStateLabel`, `PageLabel`, `ZoomLabel`, the whole step-wizard family and
  nineteen other live regions are correctly declared.** The four in item (f) were the exceptions.
- **`ShowPackage` is genuinely the common funnel for a document switch** — checked rather than
  assumed: every controller field in the shell is assigned in exactly one place, and all nine switch
  paths reach it (recovery, open, drop, file association, both template routes, carry-forward,
  restore, and the test entry points).
- **Two of item (c)'s eleven no-op paths are unreachable** — one unreachable through the catalog,
  one arithmetically unreachable — and both were left **silent** rather than given a sentence nobody
  could ever see. That was the noise risk in the item, and it was checked rather than assumed.

## 6. What is NOT closed

Gate 23's last clause is a human with a screen reader. **The tests prove a live region is declared,
not that it is spoken**, and two of them read source text because constructing those dialogs needs a
live document. `docs/accessibility-test-script.md` §21 is the sixteen steps that actually close it,
rows 21.1–21.16, and **it has not been walked**.

## 7. The thirty "can never fire" guards — the gap in this record

PLAN.md M70 says: *"Roughly thirty null-and-state guards **can never fire** because `ActionCatalog`
already makes the state impossible; they are defensive, they are listed in the audit, and they must
not be 'fixed' into noise."*

**That list does not exist and cannot be honestly reconstructed.** The audits were run in a working
session on 2026-08-09 and their output was summarised into PLAN.md rather than committed. No commit,
test, or comment in the tree enumerates the thirty. Writing a plausible list here would be worse
than admitting the gap, because the next person would trust it and would "fix" a guard that is load
bearing — or leave one that is genuinely dead.

What *is* recorded, and is all that can be relied on:

1. The count, "roughly thirty", and the reason they exist: `ActionCatalog` refuses the state before
   the handler is reached, so the handler's own null check is unreachable **through the catalog**.
2. The rule they were recorded for: **do not "fix" them into noise.** A guard that cannot fire must
   not be given a sentence nobody can ever read — that is item (c)'s two unreachable paths, above,
   and M73 (e)'s six sites that turned out already correct.
3. That the property is *conditional on the catalog*. A guard unreachable through the catalog is
   reachable from a test seam, a keyboard shortcut path, or any future caller that does not consult
   the catalog first. "Can never fire" was always shorthand for "cannot fire from the surfaces we
   have today".

**How to regenerate it if it is ever needed.** `ActionCatalog.Evaluate` is a pure function of an
`ActionContext` (this is what makes gate 24 possible), so for each `ActionId` the set of contexts in
which the action is available is enumerable, and a handler's guard is dead if no available context
can reach it. That is a real piece of work and it is not pretended to have been done here.

## 8. Acceptance, as it stood

Chrome only: **no file in `Core`, `Layout`, `Rendering` or `Export.Pdf` was opened across all eight
items, and no snapshot baseline moved** — the acceptance's own test of whether the milestone
overreached. Tests: `AnswerDeliveryTests`, `SilentNoOpTests`, `WindowAnswerTests`,
`FocusAndAnnouncementTests`, `StaleWindowTests`, plus `docs/accessibility-test-script.md` §21.
