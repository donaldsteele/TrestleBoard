# M19 — The address book fills in the officers

**Delivered 2026-07-28.** Implements PLAN.md §11 M19. This file records what was built, where it
differs from the plan, and what is still open.

M19 **formally overturns M13's officer-generation deferral**, on the deferral's own stated
condition: the person picker shipped, the office strings have settled, and the owner asked for it by
name on 2026-07-27. `docs/M13-spec.md` §1 now carries the `— OVERTURNED 2026-07-27 by §11-M19`
marker, the existing idiom (cf. `docs/M7-spec.md:1443`).

What the deferral was protecting survives the overturn **as the design rule**, not as a caveat:

> Officer sync never applies without the dialog, never guesses an office match, and never auto-picks
> between two claimants. Silent-wrong beats loud-absent nowhere in this app, and least of all on
> page 2.

Every design decision below is that sentence, mechanised.

---

## 1. `OfficeMatcher` — a table, not a heuristic

`src/TrestleBoard.Widgets/Roster/OfficeMatcher.cs`. Pure and static. `Match(string?)` returns one of
the twelve `OfficersTableData.StandardPositions` or **null**, and null is a first-class answer that
the dialog prints as "We didn't recognise this — the row stays as it is".

Two steps, in this order:

1. **Normalise** — lower-cased invariantly, every non-alphanumeric character treated as a separator,
   runs collapsed to one space, trimmed. So `Sr. Warden`, `SR WARDEN`, `Sr.Warden` and `sr  warden`
   are one key and the table needs one row for the four of them. Invariant culture is not tidiness:
   a Turkish machine lower-casing `I` to a dotless `ı` would give a different answer from every other
   machine, and then the fingerprint would disagree across the committee's laptops.
2. **Look up** — the twelve position names first (matched by their own normalised form, so no
   position name is ever repeated in the alias table — there is a test for that), then the
   abbreviation table.

**There is no fuzzy matching, no prefix matching and no `Contains`.** Those are how a Junior Deacon
quietly becomes a Junior Steward. `Warden`, `Deacon` and `Steward` on their own resolve to nothing,
because they genuinely name nothing.

`Past Master` and `PM` are **absent from the table by decision**, and have their own test named after
the reason: a Past Master is not the Master, and a table that printed him as one would be wrong in
the most embarrassing possible way. `Master` alone does map to Worshipful Master.

`Tyler`/`Tiler` and `Marshal`/`Marshall` are both spelled both ways in lodge minutes and both are
in the table; neither is a typo to be corrected.

The tests run in **both directions**: every string the matcher claims to recognise resolves to one of
the twelve, and every plausible non-office comes back null.

---

## 2. `OfficersRosterProjection` — pure, with the members passed in

`src/TrestleBoard.Widgets/Roster/OfficersRosterProjection.cs`, beside `BirthdayRosterProjection` and
built to the same rules:

- **The member list is an explicit parameter.** There is no ambient roster accessor anywhere in
  `TrestleBoard.Widgets`, which is what makes PLAN.md §0's privacy property structural rather than
  merely tested — and why `WidgetSeed` stays non-personal and `TemplateTests` passes untouched.
- **The result is a materialised snapshot.** What ends up in `OfficersTableData.Officers` is the
  printed truth and stays in the document. A live query would reprint this year's line of officers
  when last year's issue was reopened.
- **`Plan` never mutates what it is given.** It answers "what would a sync do"; the caller decides,
  the user confirms, and the caller commits.

`Plan` returns proposals **in the printed order of the twelve offices**, plus three lists the dialog
shows above them:

| Shape | Means |
| --- | --- |
| `Candidates.Count == 1` | The ordinary case: fill this office in. |
| `Candidates.Count == 0` (`IsVacancy`) | Nobody claims it any more; it goes back to printing `VacantText`. |
| `Candidates.Count >= 2` (`IsAmbiguous`) | Two men claim it. **Never resolved here.** |
| `KeptManual` | Hand-edited rows. Listed so the dialog can promise out loud to leave them alone. |
| `Unrecognised` | Office strings the matcher could not place, by brother's name. |

Four rules live in `Plan` and are each their own test:

- **A hand-edited row is never proposed.** `IsManual` wins over the address book, always.
- **A name with no `MemberId` behind it is never cleared.** The projection only takes back rows it
  put there; a name a human typed is the human's, even when the book puts nobody in that office.
- **An already-correct row produces no proposal** — down to the phone number and the member id. For a
  *contested* office that is what makes "sync twice equals sync once" true: once the user has picked
  between two claimants, the next run stops asking.
- **An inactive member holds no office** as far as the newsletter is concerned.

`DefaultDecisions` is what the dialog starts with ticked: every unambiguous proposal, and no
contested one. `Apply(current, decisions, fingerprint)` builds the table — and refuses to overwrite a
hand-edited row **even if a caller made up a decision for it**, which is belt and braces rather than
a duplicated rule.

**The fingerprint** hashes exactly what reaches the page: for each of the twelve offices, the id,
name and phone of everyone claiming it. A changed birthday is not a stale officers table and does not
claim to be. Member order does not affect it (candidates are ordered by display name, then id), so
two machines agree.

---

## 3. `OfficersTableData` at `dataVersion` 2, purely additive

```
OfficersTableData  source ("manual"|"roster"), generatedUtc, rosterFingerprint
OfficerEntry       memberId, isManual
```

`Officers` is unchanged and stays the printed truth; the layouter, the twelve-row structure, the
`VacantText` vacancy and the PDF export are untouched in shape, and **no snapshot baseline moved**.

The migration is one line — `source: "manual"` — because that is what every v1 table was. Rows need
nothing: a row with no `memberId` is a typed row by definition, and typed rows are exactly what the
projection leaves alone. A v2 payload opened in a pre-M19 build is move/resize/delete-only, which is
designed behaviour, and `WidgetController.CanEdit` refuses a newer version before the address book is
even consulted (`ActionCatalogTests` asserts the refusal names "newer TrestleBoard").

**Provenance is captured in the wizard's setters**, the same trick M13 used: `SetName` and `SetPhone`
mark a row `IsManual`, and the step wizard and `WidgetGridWindow` drive the same bindings, so one line
per bound field marks a row as the user's without either window learning anything new. The equality
guard in those setters is load-bearing — both windows write the box's value back on LostFocus, so
walking the twelve screens without typing a character would otherwise mark every row manual and
freeze the whole table against every future re-sync. `OnlyTheRowTheUserActuallyTypedIntoBecomesHisOwn`
drives the real wizard through all twelve screens to prove it.

---

## 4. `OfficersSyncDialog` — three refusals, made visible

Modelled line for line on `BirthdaySyncDialog`: M12's import-wizard voice, 20pt, plain sentences, and
*nothing changes until you press the button* written where the user is looking.

Above the fold, in this order — the two things only a human can settle come first:

1. **"We didn't recognise these"**, with the brother's name and his office in his own words.
2. **"You will need to choose"** — one office, two men, presented as radio buttons with **neither
   selected**. The row's accept box is *disabled* until a choice is made, and ticks itself the moment
   one is: the user has just answered the question the row was asking.
3. **"These will be filled in"** — one tick box per office, on by default, reading
   `Worshipful Master: A. Placeholder → Aaron Placeholder`.
4. **"These are yours, and will be left alone"** — the hand-edited rows, named.

**Per-row accept was not cut.** The officers table is page 2 of a newsletter that goes to the whole
lodge; all-or-nothing would make one wrong row a reason to abandon the other eleven.

The **phone write-back** carries over from M13 with one addition of its own. When the table holds a
number the address book does not, the row offers *"Also keep the phone number on the page for … in
your address book?"* — off by default. Ticking it does two things, not one: the number goes back to
the book as **its own address-book change with its own undo** (Ctrl+Z never crosses the
roster/document boundary), *and* the accepted decision keeps the page's number rather than the book's.
Without that second half the write-back would push one number into the book while the table printed
the other — the exact quiet disagreement this milestone exists to prevent.

---

## 5. `item.syncOfficers`, and the slot M17 reserved

One `ActionId`, one catalog entry, one availability rule, one runner handler, one `KeyboardMap` row —
the M11 contract, and the audit tests fail without any of them.

- **Insert ▸ "Fill in the officers from the address book…"** takes the slot `MainWindow.axaml`
  reserved at M17, directly under "Lodge officers", exactly as the birthday sync sits under
  "Birthdays". One item added; no menu restructured.
- **Ctrl+Shift+B.** Every letter in "officers" and "fill in" was already spoken for (`O` is
  replace-picture, `F` is fix-this-picture), so the mnemonic was not available. An unmemorable chord
  is a smaller sin than a stolen one, and the menu item carries the words.
- **Reachable two ways**, like M13's: with the table selected, and — because that is where the user
  is standing when the card tells them the table is out of date — from the "what's next" card with
  nothing selected, where the shell finds the table and shows them where it is.
- **Two different refusals with two different ways out**: an empty address book offers Import; a book
  where nobody's office field is filled in offers the People window. Neither is a grey button.
- **App invokes the projection and passes the members.** `TrestleBoard.Editing` still does not
  reference `TrestleBoard.Roster`, and `ActionCatalog` knows the officers table only by its stable
  type-id string, the same way it has known the birthday list since M13.

At **insert** time the wizard gains M13's extra first screen: with an address book that has offices
in it, inserting an officers table offers to fill it in, and the answer *seeds the wizard* rather
than committing on its own — so filling it in and pressing "Save it" is still one undo step.

---

## 6. Visible linkage — the half of the complaint that was true

The owner's complaint was that the widgets are not driven by the address book. Half of that was
false (M12 shipped the picker, M13 the birthday projection) and the half that was true is that
**nothing anywhere said so**. Four places now say it, from one sentence in one function
(`ActionCatalog.DescribeFilledIn`):

- the **panel caption** on a generated widget — "Filled in from your address book on 14 July 2026.";
- the **same banner** in both widget editors, the step wizard and the grid;
- **staleness** in `ActionContext.OfficersTableIsStale`;
- a **"what's next" step** — "Update the officers table — somebody's details changed in your address
  book since the officers table was filled in."

The date is dropped rather than left blank when the stamp is unreadable: a sentence with a gap in it
reads worse than no date. A table somebody typed says nothing at all, which is what makes the caption
honest rather than decorative.

**M13's rule holds verbatim: staleness never mutates the document.** Applying on open would dirty a
newsletter the user opened only to look at, trip the 60-second autosave and grow the recovery
snapshot. `AStaleTableIsNudgedButNeverQuietlyChangedByOpeningTheNewsletter` asserts the payload is
byte-identical after the book moves under it.

---

## 7. What M19 did not do

**Committees stay picker-only, and the reason is on record:** `Member` has no committee field, so
generating a committee list needs a roster schema change. That is its own small milestone if the
owner wants it; PLAN.md §13's unscheduled list carries it.

**No layouter, no template and no baseline changed.** The three officers screenshots re-baked
byte-identically — the roster banner is collapsed, not absent, when a list was typed — which is the
honest confirmation that the chrome addition costs nothing on a hand-typed table.

---

## 8. Files

| Path | What |
| --- | --- |
| `src/TrestleBoard.Widgets/Roster/OfficeMatcher.cs` | New. Free text → one of twelve, or null. |
| `src/TrestleBoard.Widgets/Roster/OfficersRosterProjection.cs` | New. `Plan`, `Apply`, `IsStale`, `Fingerprint`, `CountFor`. |
| `src/TrestleBoard.Widgets/Builtins/OfficersTable/OfficersTableData.cs` | `OfficersTableSource`, three table fields, two row fields. |
| `src/TrestleBoard.Widgets/Builtins/OfficersTable/OfficersTableDefinition.cs` | `dataVersion` 2, the migration, the provenance setters. |
| `src/TrestleBoard.App/Dialogs/OfficersSyncDialog.cs` | New. The per-office diff, per-row accept, the contest. |
| `src/TrestleBoard.App/MainWindow.axaml.cs` | `SyncOfficersAsync`, the insert-time offer, the banner, the shell facts. |
| `src/TrestleBoard.App/Dialogs/WizardWindow.cs`, `WidgetGridWindow.cs` | The roster banner, shared. |
| `src/TrestleBoard.Editing/Actions/*` | `SyncOfficers`, its rule, three context fields, the card step. |
| `tools/TrestleBoard.Screenshots/*` | The `officers-sync` shot and its fictional awkward cases. |
| `tests/Widgets.Tests/OfficeMatcherTests.cs` | Both directions, plus the Past Master test. |
| `tests/Widgets.Tests/OfficersRosterProjectionTests.cs` | Idempotence, hand-edits, vacancies, contests, fingerprint, migration. |
| `tests/App.HeadlessTests/OfficersSyncShellTests.cs` | The whole thing through the real shell, on a fictional book. |
| `tests/Editing.Tests/ActionCatalogTests.cs` | The availability rules and the panel caption. |

**Privacy (PLAN.md §0):** every person in every fixture here is fictional and built in the test file
or in `tools/TrestleBoard.Screenshots/Fixtures.cs`; the headless tests point the window at a roster
they create under a temporary state root, so none of them can read the real address book even by
accident.
