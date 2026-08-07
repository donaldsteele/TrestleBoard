# M35 — Six more small ones

**Delivered 2026-08-07.** Six §14.2 items, none individually large, all real.

---

1. **`FileRecoveryStore` caught `IOException` and let `UnauthorizedAccessException` through.** A
   read-only file, an ACL change or an antivirus hold escaped — and from `FindRecoverable` that
   aborted the whole startup scan, **hiding every other recoverable snapshot**, while from `Remove`
   it escaped `RecoveryService.Complete()` on the clean-close path. Neither is worth failing over.

2. **Re-selecting the link-mode source frame left link mode armed.** `Select` early-returns when the
   block is already selected, which skipped the reset below it — so the "now click the frame to
   continue into" prompt stayed in the status bar with nothing to say it was still live. Clicking
   what is already selected is a way of saying "never mind", and now means it.

3. **`ActionCatalog.TryGet` lied about its nullability.** The signature promised a non-null
   `EditorAction` on every path and returned null on the false one, with `out action!` telling the
   compiler to stop noticing. A caller that ignored the bool got a `NullReferenceException` later
   instead of a warning here. It is `NotNullWhen(true)` with a nullable out now.

4. **The district calendar indented under a separator it had not printed.** `JoinPresent` omits the
   `" — "` when the date is blank, but the hanging indent was measured with it regardless, so a
   date-less event's continuation lines hung in mid-air under nothing.

5. **One Esc could stack two confirm dialogs in the wizard.** Esc reaches `CancelWithConfirmation`
   twice — through `OnKeyDown` and through the Cancel button's `IsCancel` — so answering the
   question left another copy of it waiting behind. The guard is on the method rather than on either
   caller, because both are legitimate and neither should have to know about the other.

6. **The grid editor threw away typed answers without asking.** `WidgetGridWindow` and
   `WizardWindow` edit **one** `WizardSession` — "Show all at once" swaps between them mid-edit —
   and only the wizard confirmed. The fast path was the one that lost more: a user who chose the
   grid has typed twelve rows into it, and Esc discarded the lot without a word. It now asks the
   same question, in the same words.

---

## What guards it

These are structural: widened catch clauses, a condition, an attribute, a measurement, and two
guarded dialogs. The existing suites cover the paths and stayed green.

Items 5 and 6 are modal-dialog behaviour, which the headless session cannot drive — the same reason
M24's save-first question is answered through `SaveFirstAnswerForTest`. They are guarded by the
comments at their call sites and by the symmetry between the two windows being stated in both.

Suite after M35: **1175 passing, 12 skipped**. No snapshot baseline moved.
