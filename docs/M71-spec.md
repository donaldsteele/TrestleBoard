# M71 — A button that can never do anything

**Delivered 2026-08-09**, all four items, in `609ffdc`. PLAN.md §11 M71; verification gate 24.

> **Written on 2026-08-10.** See the note at the head of `docs/M69-spec.md`. M71's whole value was
> its (b) audit — an enumeration of every control surface against the states it is drawn in — and
> that enumeration lived only in a commit message until now.

## 1. The question, and how it differs from M70's

M70 asked *"does the answer reach the user?"* This asks a different question about the same
surfaces: **can this control ever do anything at all?**

The owner reported that "Fill in the meeting date on the cover" did nothing on a fresh template. It
was not a broken handler. The suggestion is drawn **only** in the state where its action is
structurally unavailable, so pressing it could never have worked — on any build, since M11.

## 2. The two confirmed dead controls

The panel's "What's next" list renders only when `!context.HasFrameSelection && !context.IsEditingText`
(`ActionPanel.cs:199`). Two of its seven suggestions pointed at actions that require a selection:

| Suggestion | Action | Its rule |
|---|---|---|
| "Fill in the meeting date on the cover" | `ActionId.EditWidget` | needs `Selection == Widget` |
| "Make the writing fit" | `ActionId.AutoFlow` | needs `SelectionIsTextFrame` |

**The correct pattern was already in the repository**, which is what made this bounded. Three of the
other five suggestions carry an explicit second branch for exactly this state — `ReplacePicture` has
`HasPicturePlaceholder && !HasFrameSelection && !IsEditingText`, commented *"reachable the same two
ways and for the same reason"*, and both roster syncs have their own stale-list branch. The
discipline was understood and applied to three of five.

## 3. (a) How the two were fixed

Both follow `ReplacePicture`'s two halves: a second availability branch, and a helper that resolves
the selection or falls back to the obvious target. `WidgetTarget` finds the dateless cover heading;
`FlowTarget` the head of the first overset chain. The fact and the thing the fact is about now come
from one walk, so the button opens the very heading that caused the card to say so.

**Whatever is chosen on the user's behalf is announced, naming the page it was found on.** Selecting
silently and then acting would be M70's defect in a new place.

**An inherited consequence, not an addition.** With nothing selected and a dateless cover,
Edit ▸ "Change what this says…" and its shortcut now enable and pick the cover heading, and the same
for "Make the rest fit" when something is overset. `ReplacePicture` has behaved this way since M18;
the two-branch pattern makes the command reachable everywhere, not only from the suggestion.

## 4. (c) The gate came first, and it caught its author

Written before any fix, as the acceptance demanded. **Its first run flagged four dead suggestions
rather than the two the manual audit found.** Two were false: the test paired "the birthday list has
gone stale" with an *empty* address book, a state the app never produces, because an empty book
offers "Fill in your address book" instead.

A reachability gate fed impossible contexts reports impossible bugs — exactly the noise (d) predicts
will get a gate switched off. So the reasoning was written into the test rather than the fixture
quietly corrected; it is in `WithRoster()`'s doc comment in
`tests/Editing.Tests/ReachabilityTests.cs`. With realistic states it flagged precisely the two real
ones.

The gate is pure — no window, no Avalonia — because `ActionCatalog.Evaluate` and
`WhatsNext.Suggestions` are both pure functions of an `ActionContext`: **the context that produces a
suggestion is exactly the context that suggestion is rendered in.** It carries (d) as its own
assertion (`TheGateDoesNotObjectToAnHonestRefusal`) and an anti-vacuity check
(`TheSuggestionsBeingCheckedAreRealOnes`), because a gate that passes by having nothing to inspect
is how a test dies quietly.

## 5. (b) The audit — every control surface, and its verdict

**This is the exclusion list, and it is the reason this document exists.** For each surface: the set
of contexts it is drawn in, and whether any of them makes its action available.

| Surface | Verdict | Why |
|---|---|---|
| "What's next" suggestions | **two dead**, fixed in (a) | see §2 |
| The action panel's offers | clean **by construction** | `ActionCatalog.ForSelection` drops `NotApplicable` entirely, so a panel offer drawn only where it is refused is *unrepresentable* |
| The toolbar | clean | draws everything always and greys with a reason — M11 working |
| The menu bar | clean | as the toolbar; every greyed item carries the reason in `AutomationProperties.HelpText` |
| The help window's "Take me there" | clean | visible only when its action is available |
| Review remedies | clean | each selects its finding's block before running |
| Every button in a dialog that runs a catalog action | **nothing to audit** | see §6 — this is the boundary that was too narrow |

**Nothing was fixed in (b) and nothing needed to be.** That is the finding, and M70's tradition of
recording clean results is why it is written down.

## 6. The boundary this audit drew, and where it was wrong

M71 (b) concluded that dialogs other than Review and Help **run no catalog action, so there is
nothing to audit**. That was true as written and too narrow, and the next bug the owner found walked
straight through it: recording a brother as passed offers *"Would you like to write a memorial
notice for him?"*, an offer that never touches the catalog at all and whose handler refused on a
precondition the card had never asked about.

The recurrence is recorded here rather than only in M73, because the lesson belongs to the audit
that missed it: **an audit scoped by a mechanism finds only the defects that use that mechanism.**
M70 scoped itself by "the answer's delivery" and missed whether the control could work; M71 scoped
itself by "the catalog action" and missed the offers that make no catalog call. M73 is scoped by the
user-visible promise instead, and gate 26 is the generalisation — *the predicate gating a control
must be the handler's precondition, wherever that handler lives.*

## 7. (d) The three cases, not flattened

Kept as an explicit rule because flattening them is how a gate gets switched off:

1. A control that is **sometimes** unavailable and explains itself — **correct**, M11, must stay.
2. A control that is **never** available where it is drawn — this milestone's bug.
3. A control that is available but whose handler does nothing — M70 (c), already fixed.

## 8. What was not done

- **No dialog was changed.** See §6; that turned out to be the gap, not a saving.
- **No control found dead and left unfixed.** The list of "found dead and not fixed" that the
  acceptance asks for is **empty** — the two named were both fixed.
- **Chrome and catalog only**; nothing in `Core`/`Layout`/`Rendering`/`Export.Pdf`, and no snapshot
  baseline moved.
- **PLAN.md was corrected on the way**: it called the suggestion "Make the rest fit" where the code
  says "Make the writing fit".
