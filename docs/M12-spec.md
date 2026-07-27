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
