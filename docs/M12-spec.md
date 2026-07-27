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
