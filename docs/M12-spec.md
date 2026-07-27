# M12 — Lodge address book

Status: derived from the LOCKED `PLAN.md` (§0 rule 5, §1, §9, §11-M12, §12 gates 7 and 9).
In progress.

Acceptance (PLAN §11-M12): *import a fictional 100-person list as CSV and as XLSX, get identical
results; re-import changes nothing; export → edit in Excel → re-import moves only the edited fields;
find a person by typing three letters; all of it completable keyboard-only.*

**Privacy (PLAN §0 rule 5, HARD).** This is the milestone that creates the hazard. The file this
code writes — `%AppData%/TrestleBoard/roster.json` and its `roster-backups/` ring — holds real
members' names, birthdays, phone numbers and emails, and is the first such file the app itself
creates. Every rule in §0 rule 5 applies to every commit in this milestone: roster fixtures exist
only in `tests/Roster.Tests` and are fictional; export writes only where the user browses to; no
roster content ever reaches a commit, a test, an issue, or a graphify / llm-wiki run.

---

## 1. The dependency, measured before anything was built

`PLAN.md`'s flagged uncertainty for this milestone was ClosedXML's transitive footprint against §9's
self-contained budget, to be measured *before* the import UI is built. Measured first, therefore.

`dotnet publish src/TrestleBoard.App -c Release -r win-x64 --self-contained`, before and after
adding a `TrestleBoard.Roster` project reference carrying `ClosedXML` 0.105.1:

| | Publish output |
|---|---|
| Before | 217,353,954 bytes |
| After | 227,431,584 bytes |
| **Added** | **10,077,630 bytes — 9.6 MiB per RID, +4.6%** |

What arrives, from `TrestleBoard.Roster.deps.json`: `ClosedXML` 0.105.1, `ClosedXML.Parser` 2.0.0,
`DocumentFormat.OpenXml` 3.1.1 (6.3 MB of it — the bulk of the cost), its `.Framework` 3.1.1,
`ExcelNumberFormat` 1.1.0, `RBush.Signed` 4.0.0, `SixLabors.Fonts` 1.0.0 and
`System.IO.Packaging` 8.0.1. All pure managed with no native assets, so the four RIDs each pay the
same 9.6 MiB and the Velopack packaging is unaffected.

**Verdict: accepted, no fallback needed.** 9.6 MiB against a ~217 MB publish is 4.6%, and the
fallback PLAN.md holds in reserve — a hand-rolled OOXML reader/writer over `System.IO.Compression`
and `System.Xml` — would trade that 4.6% for the whole class of bugs this milestone is least able to
afford, in the one code path that reads a file a lodge secretary made in a program we cannot test
against. The measurement is recorded here so a future reader knows the number was taken rather than
assumed.

`SixLabors.Fonts` arriving transitively is worth one sentence, because §1 rejected *ImageSharp* on
licence-split grounds. This is the fonts package, not the imaging one; it is Apache-2.0, it is used
by ClosedXML only to measure text for column widths, and nothing in TrestleBoard calls it. It does
not touch the bundled-font determinism rule (§1), which is about what the layout engine shapes with.

---

## 2. Where the code lives

`src/TrestleBoard.Roster` is a leaf: BCL plus ClosedXML, and — deliberately — **no reference to
`TrestleBoard.Core`**. PLAN §9 gives the reasoning; the practical effect is that a reviewer can point
at one project and say *"this is the only code that touches real people"*, which is worth a great
deal under §0.

`tests/Roster.Tests` is the only place a roster fixture may exist, and every person in one is
fictional.

---

## 3. The file, and what protects it

`%AppData%/TrestleBoard/roster.json`, beside the settings file, plus a `roster-backups/` ring of ten.
Velopack installs to `%LocalAppData%` — a different root — so an auto-update cannot touch any of it.

Three properties, each with a test:

- **Loading never throws.** A missing, unreadable or garbage file yields an empty book. This is
  `AppSettings.Load`'s contract copied deliberately: refusing to start is never the better failure,
  and that goes double for the file holding the whole lodge.
- **Saving is temp-then-rename**, and *does* throw. A save the user asked for that silently did not
  happen is how an address book gets lost.
- **Every save keeps the version it replaced.** This file's contents exist nowhere else — no
  `.tboard` carries them, no command stack spans sessions — so the ring is the only protection there
  is. Ten copies, named by UTC timestamp so the ring survives a copy that loses file times.

`Member` has seven fields the user ever types and three deliberate absences: **no birth year**
(matching the month/day-only rule the birthday widget already keeps), **one date plus a kind**
rather than separate raised/initiated fields, and **`office` as free text rather than an enum**,
because titles drift and a value the app refuses to store is a value the user retypes somewhere
worse. `[JsonExtensionData]` on both `Member` and `RosterBook` preserves whatever a newer
TrestleBoard wrote.

**Ids are highest-plus-one, never lowest-free.** A reused id would let a spreadsheet exported last
month re-import onto whoever now holds that number, and the ID column is exactly what makes
export → edit in Excel → re-import lossless.

### Undo, and the boundary it must not cross

> **Ctrl+Z never crosses the roster/document boundary.**

`RosterService` keeps one snapshot and offers one "Undo the last change"; anything older is
"Restore an earlier version…" over the ring. PLAN §11-M12 gives four reasons this is not an
`IDocumentCommand`, and the decisive one is behavioural rather than architectural: sharing
`DocumentSession` would make Ctrl+Z in the newsletter undo an address-book edit — precisely the
class of surprise M11 exists to eliminate.

Restoring is itself an ordinary save, so restoring the wrong version is undoable too.

### `AppPaths`, and why it lands here rather than in M15

M15's screenshot harness runs on the maintainer's own machine, where the roster is real, and a
single capture of the People window would put real personal data in a public repository. PLAN.md
says plainly that the guard belongs to the milestone that *creates* the hazard, not the one that
trips over it. So `src/TrestleBoard.App/Settings/AppPaths.cs` arrives with the roster: one place
naming the settings file, the recovery folder and the roster file, with a **settable root**. A
harness pointed at a temporary folder is structurally unable to read the real address book. A rule
in a document does not prevent that; not being able to see the file does.

The default path is unchanged, so an existing installation finds its settings and its recovery
snapshots exactly where M9 and M10 left them.

---

## 4. Reading the file the committee already keeps

Everything the import flow does after this point works on a `TableWorkbook` — sheets of plain text,
and nothing that knows what a member is. That is what makes the acceptance criterion *"import the
same list as CSV and as XLSX and get identical results"* a property of the design rather than a
coincidence: after the reader there is only one problem left.

PLAN.md lists the hazards; each is a committed fixture, and each is something a real file does:

| Hazard | Fixture | What would happen without it |
|---|---|---|
| UTF-8 BOM | `members-hazards.csv` | The first header reads `﻿Name` and matches nothing |
| CRLF, embedded newlines, doubled quotes, quoted commas | same | Rows split in the wrong places |
| Excel's `="555-0100"` idiom | same | The phone number is stored with its equals sign and quotes |
| Semicolon delimiter (European Excel) | `members-semicolon.csv` | The whole file reads as one column |
| No header row, no final newline | `members-headerless.csv` | The last person is dropped |
| **Real date cells** | `excel-dates.xlsx` | A birthday is stored as `45123` |
| Old binary `.xls` | `old-format.xls` | An obscure failure instead of "save it as .xlsx" |
| Password-protected `.xlsx` | `password-protected.xlsx` | The same obscure failure |

The last two are the same eight bytes — both are OLE compound files — and get **different
sentences**, because the user's next action differs.

**The date-serial hazard is the one PLAN.md singles out, and it is worth being exact about.** A
spreadsheet's Birthday column is usually a real date cell: it stores days since 1899-12-30 and merely
*shows* `7/4`. Reading the displayed text would make the import depend on the reader's locale;
reading the number would print `45123` in the newsletter. So a date cell is converted once, in the
reader, to an ISO string, and everything downstream sees text. `FieldValues.TryReadBirthday` then
also accepts a bare serial, because that is what lands in a CSV exported from such a column.

Excel's 1900 leap-year mistake is handled rather than ignored: serial 60 is a day that never
existed, so serials below it use an epoch one day later. The test table includes 59, 60 and 61.

**The XLSX fixtures are produced by real tools**, per PLAN.md: `members-100.xlsx` by LibreOffice,
and `excel-dates.xlsx` hand-written as OOXML in the shape Excel writes (a shared-string table and
numeric cells carrying a date number format). Round-tripping our own writer would prove nothing
about the reader. *Open, honestly:* no fixture has yet been produced by Excel itself — no machine
here has it — so "Excel *and* LibreOffice" is half met, with the hand-written OOXML standing in for
the other half.

**A phone number is kept exactly as it was written.** Reformatting somebody's phone number is not
the app's business, and a number that reads differently from the sheet it came from is a number the
user stops trusting. The one exception is scientific notation, which is a spreadsheet's damage
rather than the user's writing.

---

## 5. The merge policy, which is the milestone

Everything else here is plumbing. This is the part that can quietly ruin an address book.

**Match in strict order:** exact `TrestleBoard ID` → exact normalised email → exact normalised name
→ otherwise a new person. Names are normalised the way people actually write them: trimmed,
whitespace collapsed, case-folded, punctuation dropped, `Jr`/`Sr`/`II`/`III` removed, and
`Last, First` turned round into `First Last`.

Then:

- **Update by match; never replace the file; never delete.** An import has no code path that removes
  anybody. A list missing this year's new members is a perfectly ordinary thing to import, and it
  must not empty the book. Deletion is a deliberate per-person act in the People window.
- **Mapped columns overwrite; unmapped fields are left alone** — so a phone-only import cannot wipe
  everyone's birthdays (PLAN §12 gate 9). **An empty cell in a mapped column also leaves the value
  alone**, which is the same rule one level down: a column of blanks is not an instruction to clear
  a field for the whole lodge.
- **Auto-merge on exact match only.** Anything merely similar becomes a question —
  *"Are these the same person?"* with **[Same person] / [Different person]** — and until it is
  answered, that row does nothing at all.

**Idempotence is the key test**, and it is written against the hundred-person fixture: import,
import again, assert `ChangesAnything` is false and the member list is equal. That single property
guards the whole rule set, because every way of getting the matching wrong shows up as a second
import doing something.

**The near-match rule is deliberately generous**, since its only cost is a question the user answers
once. Two names count as worth asking about when they share a family name and a compatible first
name (`A. Placeholder` against `Aaron Placeholder` — half a lodge's records are written that way),
or when they are within two edits of each other. That second clause is loose enough that two
*fictional surnames* two edits apart would trip it, which is why the 100-person fixture's five
surnames are deliberately unlike one another: the fixture should exercise the merge policy, not the
question-asking.

### The screens

`RosterImportSession` holds every piece of state and the window merely renders it, which is the one
architectural trick borrowed from `WizardSession` — and the reason every sentence a user reads
during an import is asserted in `tests/Roster.Tests` without an Avalonia session. It does **not**
go through `WizardDefinition`: the mapping step is discovered at runtime and the preview is a table,
so bending the widget wizard to fit would make both worse.

1. **"Where is the list?"** — and the promise the whole flow rests on: *"Nothing changes until you
   say so at the end."*
2. **"Which sheet?"** — skipped when there is only one, because a screen with one possible answer
   wastes the user's time.
3. **"Which row has the column titles?"** — the first eight rows, one big radio each, plus "There
   are no column titles". Guessed by how title-like a row is rather than by assuming row one:
   guessing wrong on a headerless list quietly loses the first person.
4. **"Match up the columns."** — inverted from the usual import grid: one question per *lodge*
   field, in the user's words, each dropdown item showing column letter, title **and the first two
   values**, so the user recognises their data instead of decoding a header. Only Name is required,
   and the reason is said out loud rather than leaving a dead Next button.
5. **"Have a look before we add these."** — the plan in plain counts, the "Leave them alone instead"
   toggle, the duplicate questions, and the unusable rows with a **Save these to a file** button so
   nothing is silently lost.
6. **"Done."** — *"Your address book now has 100 people."*

---

## 6. Export, and the round trip it exists for

`Lodge-address-book-YYYY-MM-DD.xlsx`, one sheet named "People", nine columns.

**Every cell is written as text.** A real date cell forces a year onto a birthday that has none and
shows it as `7/4/1900`; a numeric phone number becomes `8.03555E+09`. Both are damage the user would
have to undo by hand in a file they opened only to check a phone number. ClosedXML's
`SetValue(string)` is not enough on its own — a string that parses as a date is converted on the way
in unless the cell's number format says `@` — so both are set.

**The ID column is written**, which is what makes export → edit in Excel → re-import lossless even
when a name changed in between. That is the workflow a lodge secretary will actually use, and it is
why the importer recognises "TrestleBoard ID" wherever it sits and never offers it as one of the
user's own fields.

One bug worth recording, because the round-trip test is what caught it: our own "Raised or
initiated" header matched the *degree date* hint `raised` before the degree-kind row was reached, so
re-importing our own export stored the word "Raised" as somebody's degree date. The hint list now
names the full header first. A round trip through one's own writer proves little about a reader, but
it proves a great deal about a writer and a guesser together.

Export writes only to a path the user browsed to (PLAN.md §0 rule 5). There is no default location
beside the repository or the newsletter.

---

## 7. The People menu, and the three windows

**A top-level `Peop_le` menu**, not an item buried in File: the address book becomes one of the five
things this app does. Five items — People… (`Ctrl+Shift+R`), Import from a file…, Save as a
spreadsheet…, Undo the last change, Restore an earlier version… — each of them a catalog entry, an
availability rule, a runner handler and (for the one with a shortcut) a `KeyboardMap` row, exactly as
M11's rule requires. The access key is `l`, because `P` already belongs to Page.

Two of the five can be unavailable, and both say why rather than only going grey: saving a spreadsheet
when the book is empty points at Import as its remedy, and the undo item names what it will take back
(`_Undo Add A. Placeholder`) — **a separate sentence from the newsletter's Undo, because Ctrl+Z never
crosses that boundary.**

**`PeopleWindow`** is shaped around what somebody is actually doing when they open it, which is
almost never "browse the membership": it is *"look somebody up and fix their phone number"*. Focus
lands in a 24pt search box; the result count is a polite live region, because a search that silently
finds nothing looks exactly like a search that is still thinking; rows read `Name — office —
birthday` so the useful facts need no click; and the editor is a **single page**, not a wizard. The
wizard shape is right for data you do not have yet and wrong for correcting one field of data you do.
The same form is reused for Add a person, so it is learned once. Deleting asks, and the confirm
answers the question the user is actually worried about — *"Newsletters you already made will not
change."*

It carries one sentence that exists nowhere else in the app: the address book is **kept on this
computer only**, and the spreadsheet is how you share it. PLAN.md's flagged uncertainty says to say
so in the People window rather than only in the plan; this is that.

**`RosterImportWindow`** renders `RosterImportSession` and nothing else — no decision, no count and
no sentence lives in the window. **`RosterRestoreDialog`** lists the ring by *when it was taken and
how many people were in it*, which is how somebody recognises the version they want.

### Where the address book is read from

`MainWindow.Roster` is created lazily but read during the first `RefreshActions`, which is near
enough to eager — and that is the right trade. The alternative, deferring until the People window is
opened, would leave "Save as a spreadsheet…" greyed with the wrong reason until somebody happened to
look, which is exactly the silent wrongness M11 exists to remove. The one thing that is *not* done
per refresh is listing the backup directory: `RosterService.HasEarlierVersions` is tracked instead.

### The headless suite now runs on a temporary app-state root

`HeadlessSession` sets `AppPaths.Root` to a per-process temporary folder before Avalonia starts. The
point is not tidiness. From M12 `%AppData%/TrestleBoard` holds real members' names, birthdays,
telephone numbers and emails, and a test suite that *could* read that file could print it in an
assertion message on a public CI log. Pointed at a temporary folder, it cannot.

`PeopleShellTests` and `AccessibilityTests` read the roster fixtures by **linking** them from
`tests/Roster.Tests/Fixtures` rather than copying them, so §0 rule 5's "fixtures exist in exactly one
place" stays literally true of the repository.
