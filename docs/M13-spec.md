# M13 — Roster-driven widgets

Status: derived from the LOCKED `PLAN.md` (§5 roster projection rule, §9, §11-M13, §12 gate 10).
Delivered 2026-07-27.

Acceptance (PLAN §11-M13, §12 gate 10): *birthday re-sync is one undo step and Ctrl+Z restores the
prior payload byte-for-byte; hand-edited rows survive a re-sync; deliberately removed members are not
resurrected; `TemplateTests` passes unchanged.*

**Privacy (PLAN §0 rules 2 and 5).** This milestone connects the one file that holds real people to
the one file the user publishes. The connection is a single pure function that takes the member list
as an explicit parameter; there is still no ambient or static roster accessor anywhere in
`TrestleBoard.Core` or `TrestleBoard.Widgets`, so a widget cannot name a real person unless a person
asked it to. Every fixture in this milestone is fictional and built in the test file that uses it.

---

## 1. What was built, and what deliberately was not

Built, in the order PLAN.md's scope-cut list protects:

- **Birthday generation** — the one thing PLAN.md says never to cut. A pure projection, a
  three-way diff before anything changes, and one undo step when it does.
- **The re-sync diff dialog** — not degraded to "regenerate and confirm".
- **The officers person picker**, with a blank-phone auto-fill and a review-screen write-back.
- **The committee picker**, which needed no schema change at all.

Not built, and PLAN.md says so explicitly: **full officer-table generation**. The roster has an
`office` field, so it looks free, but matching free-text office strings ("Sr. Warden", "SW") to the
twelve fixed positions is a real matching problem, and page 2's officers table is the most
conspicuous place in the newsletter for a wrong name to print. The picker ships; generation waits
until real office strings have settled over a few issues.

---

## 2. The data shape: `dataVersion` 1 → 2, purely additive

`BirthdayListData` gains five fields and `BirthdayEntry` gains two:

```
BirthdayListData  source ("manual"|"roster"), sourceMonth, generatedUtc,
                  rosterFingerprint, removedMemberIds[]
BirthdayEntry     memberId, isManual
```

`Entries` is unchanged, and stays **the printed truth**. A live query would print December's
birthdays when March's issue is reopened in December, so the projection materialises a snapshot and
the document keeps it. The layouter's `(Month, Day, Name)` sort, carry-forward, and PDF export are
therefore all untouched in shape.

**The migration is one line.** A v1 payload gains `source: "manual"`, because that is what every v1
list was. Entries need nothing: a row with no `memberId` is a typed row by definition, and typed rows
are exactly what the projection leaves alone.

**`TemplateTests` passes untouched, as PLAN.md predicted.** It asserts zero birthday entries in
templates; the new fields are additive, templates still ship zero entries, and its `default:` branch
only fires for a *new widget type* — of which M13 adds none.

---

## 3. The projection

`src/TrestleBoard.Widgets/Roster/BirthdayRosterProjection.cs`. `Plan(current, members, month)` is a
pure function returning `{ Additions, Removals, Updates, KeptManual, Result, Fingerprint }`.

PLAN.md lists four lists; there are five. **`Updates` was added deliberately**: a brother renamed in
the address book is neither an addition nor a removal, and without a name for that case the diff
dialog would have had to say "nothing will change" while changing something. It is the same
three-way story with the third case told honestly.

**One rule decides everything.** A row belongs to the projection if and only if it carries a
`memberId` **and** no human has touched it. Everything else is the user's, and the user's rows are
never rewritten and never taken away.

**Provenance is captured in the setter lambdas both editors already share**, one line per bound
field, exactly as PLAN.md specifies — so `WidgetGridWindow` needed no change to honour it. The
equality guard in those setters is load-bearing rather than tidy: both windows write the box's value
back on `LostFocus`, so without it, tabbing through a generated list without typing a character would
mark every row manual and freeze it against every future sync.

**`removedMemberIds` is recorded at the moment of removal, not inferred later.** `RecordListStep`
gained one optional `onRemoveRow` hook; the birthday list uses it to remember the id. Inferring it
instead — "this candidate is missing from a list already generated for this month, so he must have
been deleted" — reads plausibly and is wrong: it cannot tell a deliberate deletion from a brother
added to the address book *after* the list was made, and it would suppress every new arrival.

**Suppressions reset when the issue month moves.** A decision about March's list says nothing about
April's. This is what makes carry-forward behave: after a carry-forward `sourceMonth` no longer
matches `IssueMonth`, the staleness check fires on its own, and the "what's next" card leads with
the offer to update.

**The fingerprint covers only what reaches the page** — who is in this month, what he is called, and
on which day — and excludes anybody already taken off the list. A changed phone number is not a stale
birthday list and must not claim to be.

---

## 4. The one new action, and the second way to reach it

`item.syncBirthdays`, "Bring in birthdays from the address book", `Ctrl+Shift+U`, in the Insert menu
and in the panel's **This item** group beside a selected birthday list.

It is available in **two** contexts, which is a deliberate departure from the shape of every other
item action:

1. a birthday list is selected — the ordinary case;
2. **nothing is selected and a birthday list on the page is stale** — because that is exactly where
   the user is standing when the "what's next" card tells them the list is out of date. The shell
   then finds the stale list, selects it, and turns to its page, so the user watches it happen.

Two refusals rather than one grey button, each naming its own way out: an empty address book wants
importing (`people.import`), and a month nobody was born in is not a fault — it points at the People
window so the missing dates can be filled in.

**Nothing to show is not nothing to do.** If the diff is empty but the list is still recorded against
last month, the action refreshes the provenance without a dialog nobody needs and says so in the
status bar. Left undone, the "what's next" card would nag forever about a list that is already right.

> **The hard rule holds: staleness never mutates the document.** Opening a newsletter only to look
> at it changes nothing. The nudge is a "what's next" row and a status sentence, and there is a
> headless test whose whole job is to prove it.

---

## 5. Insert: one extra screen, and only when there is something to offer

Inserting a birthday list with birthdays in the issue month asks *"We found 7 birthdays in July in
your address book. Add them?"* before the wizard opens. The answer **seeds the wizard** rather than
committing on its own, so filling the list in and pressing "Save it" is still a single undo step.
With an empty address book, or a quiet month, the user is asked nothing and the wizard is exactly
what it has always been.

---

## 6. Officers and committees

**`WizardFieldKind.Person`** renders as an editable `AutoCompleteBox` over the address book in
**both** windows — the grid is a second view of the same wizard, not a laxer one. Free typing always
works, so a brother who is not in the book is never a dead end. The officers wizard stays fourteen
screens, a deliberate M7 design; each screen simply becomes a pick.

**Picking fills a blank phone box** from the book, and never one the user has already typed.

**The write-back is one question, off by default.** If the typed number disagrees with the book's,
the review screen offers *"Also update A. Placeholder's phone in your address book?"*. Ticking it
makes the shell save that member — as its own address-book change with its own undo, because
**Ctrl+Z never crosses the roster/document boundary** (M12). It is only ever offered about somebody
the user actually picked: a name typed freehand has no address-book entry to write back to.

**Committees needed no schema change.** `CommitteeEntry.Members` stays `List<string>`; the field
gained `AllowsPeoplePicker`, and the button appends a line. No migration, no layouter change.

---

## 7. Two robustness fixes that fell out of the tests

Both are in `WizardWindow`, both pre-date M13, and both were invisible until a test rendered two
screens without a dispatcher turn between them:

- **Controls now bind to the step they were built for.** Avalonia raises `TextChanged` through the
  dispatcher, so a box from the screen the user has just left can still fire — and a write aimed at a
  step that no longer has that field in it throws `KeyNotFoundException` out of the middle of a
  wizard. Capturing the owning step at build time and using the step-scoped session API makes a late
  event a harmless no-op.
- **The person picker listens on `TextProperty`, not `TextChanged`.** `AutoCompleteBox` raises that
  event from its templated inner text box, so it says nothing at all until the control has been shown.

---

## 8. Tests

- `Widgets.Tests/BirthdayRosterProjectionTests` — idempotence (apply twice = apply once), manual rows
  survive, removed members are never resurrected, stored order is untouched and new people go on the
  end, a renamed brother is updated in place rather than re-added, suppressions reset with the month,
  the fingerprint changes if and only if a contributing field changes, and a v1 payload migrates to a
  typed list.
- `Editing.Tests/ActionCatalogTests` — the new action is about the birthday list and nothing else, is
  reachable from the "what's next" card, and gives two different refusals with two different ways out.
- `App.HeadlessTests/BirthdaySyncShellTests` — §12 gate 10 through the real shell: one undo step,
  Ctrl+Z restores the payload byte-for-byte, saying no changes nothing, a stale list is nudged and
  never quietly changed.
- `App.HeadlessTests/OfficersPickerShellTests` — the pick, the auto-fill, the number that is never
  overwritten, and the write-back that is only offered about somebody who was actually picked.
- `App.HeadlessTests/WizardFieldKindTests` — every `WizardFieldKind` gets the control it was invented
  for in **both** windows, against a declared table that a new kind cannot be added to silently.
  This is the test PLAN.md asks for; the failure it catches is a missed case degrading to a plain
  `TextBox` while the wizard keeps working.

The headless tests build and render their windows but never show them, for the reason
`WidgetShellTests` already gives: a shown headless window runs a real layout pass, and this suite's
platform has no font manager behind it.

---

## 9. Open items

- **A keyboard-only run of the birthday sync and the officers picker has not been done by a person.**
  Every control is keyboard-operable by construction — an `AutoCompleteBox`, a list box, buttons, no
  drag anywhere — and the headless tests drive the state machine end to end, but "completable
  keyboard-only" is a claim a person should make after doing it. Added to
  `docs/accessibility-test-script.md` alongside M11's outstanding NVDA pass and M12's import run.
- **The mid-rollout `dataVersion` 2 case is designed behaviour, not a defect.** A v2 birthday list
  opened in a pre-M13 build is move/resize/delete-only with the "newer version" message. PLAN.md
  flags it; it is recorded here so nobody files it as a bug during a Velopack rollout.
