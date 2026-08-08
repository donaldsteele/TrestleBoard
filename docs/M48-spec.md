# M48 — Four small ways out

**Delivered 2026-08-08.** Closes the wizard finding in §14.4 and two of the §14.2 minors that were
still standing.

---

## 1. Cancel sat 12 pixels from "Save it"

On the review screen the wizard's footer pair becomes **Cancel** and **Save it**, and twelve pixels
apart they are one slip of the hand from discarding fourteen answers. The gap is now 28 pixels
wider, and this is the only place in the app that needs it — everywhere else the pair is "go back"
and "go on", where a misclick costs a step rather than a session.

**The review's open question resolves in the app's favour.** It could not tell from the code whether
Cancel confirms; it does, and has since M28. The distance is belt and braces on top of that.

## 2. Fourteen screens with a hatch nobody notices

"Show all at once" sits at the bottom left of every step and says nothing about itself. The first
screen now mentions it, in the help line the window already has:

> If you would rather see every question on one page, press "Show all at once".

**Only the first.** Repeating it on all fourteen would be nagging.

## 3. The start screen had no way out but the mouse

`StartDialog` had no Escape path at all — and it is the first window a user ever meets. Every other
dialog in the app closes on Esc; this one had to be answered by clicking. It now leaves with
`StartChoice.Nothing`, which is what the shell has always treated as "they did not choose", so the
new exit lands in a state the caller already handles.

## 4. Two update checks could run at once

Opening the app and immediately choosing Help ▸ "Check for an update" started two — the startup
check and the menu one — and two downloads of the same release is wasted bandwidth on a lodge's
connection.

The second caller is **told the truth rather than silently dropped**: somebody who pressed a menu
item is owed an answer, and "TrestleBoard is already looking for an update" is the answer.

## 5. What guards it

- `PackagingTests.TwoUpdateChecksCannotRunAtOnce` — a channel that stays in flight until the test
  releases it; the second call reaches no channel and says "already looking"; after it completes the
  gate is open again. The last assertion is about **what the user is told**, not a call count:
  whether the channel is consulted twice is `UpdateCoordinator`'s business, since it already knows
  the answer.
- `AccessibilityTests.TheStartScreenCanBeLeftWithEscape`.
- `AccessibilityTests.TheWizardsFirstScreenMentionsShowingEveryQuestionAtOnce`.

The first two were verified failing against the pre-M48 source.

## 6. What was NOT done

The wizard's **"Show all at once" versus "Save it"** as two commit verbs (the jargon list's last
line) is untouched. They are not two verbs for one act: one changes how the questions are shown and
the other finishes. Renaming either would make the pair *look* consistent while describing two
different things, which is worse than the inconsistency.

Three screenshots re-baked. Suite after M48: **1209 passing, 12 skipped**.
