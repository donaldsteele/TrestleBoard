# M89 — What the real file taught

**Status:** implemented. Delivered 2026-09-04, shipped as v1.7.1.

M88 was built against the lodge's own export and a fictional fixture in its shape. Then the export
was actually run through the app, and **five defects came out of a single attempt** — four of which
no test could have caught, because the fixture was a description of the file written from a summary
of it rather than from the file itself.

This document is short on purpose. Its value is the list.

---

## 1. The app could not read the file at all

`TableFileReader` refused it outright:

> TrestleBoard could not read that spreadsheet. If it opens in Excel, try File, then Save As, then
> Excel Workbook (.xlsx), and use that copy.

The workbook has a **pivot table** in it. ClosedXML reads every part of a workbook — pivot caches
included — and its cache reader threw before a single row was read. The file opens perfectly in
Excel. So the app's advice was both wrong and useless: it asked a committee to repair a file that
was not broken, and the likely outcome was a secretary retyping 112 names.

**`XlsxRawReader`** is the answer: a second attempt, pure BCL, that opens only four parts — the
workbook, its relationships, the shared strings and the sheets — and is deliberately incurious about
everything else. An unknown part cannot break a reader that never opens it. It reads `styles.xml`
too, so a date-styled serial still comes back as an ISO date rather than as `30149`.

It runs **only** when ClosedXML has already failed. The first reader stays the one that matters; this
is a rescue, and a test asserts an ordinary workbook still goes the usual way.

**The test for it was vacuous twice before it was real.** The first fabricated pivot workbook was
accepted by ClosedXML, so the test passed with the fallback disabled. The second — with the cache
wired into `workbook.xml` so ClosedXML actually opened it — also passed with the fallback disabled.
Only the third, whose records carry two values per row over a cache declaring one field, failed
correctly. Each version was checked by turning the fallback off and re-running; that is the only
reason the emptiness was noticed.

## 2. The name column was the wrong one

`Name` mapped to **"First Name"**, and 112 brethren imported under their first names alone.

The file has a `FullName` column, and the hint `name` does not match it: word-boundary matching sees
the `N` flanked by a letter. So the only columns that matched were "First Name", "Middle Name" and
"Last Name" — and the first of them won.

Two fixes. `fullname` is now a hint. And, more generally, **a hint that accounts for the whole header
beats a longer hint that accounts for part of one** (scored as double its length). No partial match
can be twice the length of the header it sits inside, so the rule is a rule rather than a nudge.

## 3. Every wife imported as a member

`RowKind` mapped to **"Item Type"** — a column holding sentences like *"Actual Birthday for July:
7/17"* — because that scored longer than the column actually headed **"Type"**, which holds "Member"
or "Spouse of …". Dropping `item type` as a hint and the whole-header rule above both point at the
right column now.

**The fixture had modelled the wrong column.** It called the marker column "Item Type", because that
is what a summary of the file said. The real export has both columns, side by side, and the fixture
now has both too.

## 4. Ten of fifteen wives were filed against nobody

Even with the right column, the import reported *"…is a spouse of member number 91916, who is not in
your list"* for ten women whose husbands were in the very same file — because a spouse row was filed
the moment it was read, and those husbands sat further down the sheet.

Every row that is not a member is now **held back until every member has been read**. The fixture
gained a wife listed above her husband, which is the only arrangement that can fail.

## 5. A child joined the lodge

The export carries one row marked **"Child of …"**. It is not a member and not a spouse, so it fell
through to the ordinary path and was added as a brother — on the roll, in the birthday list, offered
as an officer.

The address book has nowhere to put a child and does not invent one. The row is reported in a
sentence, which is what every other row the import cannot use gets:

> "…" is recorded as somebody's child. The address book holds the lodge's members and their wives, so
> this row was not added.

## 6. And one found by looking at the result

The imported book was written back still calling itself **schema version 1**, because `Normalised()`
only ever repaired a version of zero. A file holding every M88 field while claiming to predate them
is a file describing itself wrongly — and that number is the one thing another copy of TrestleBoard
uses to work out which of a committee's two laptops is behind.

`RosterStore.Save` now stamps the current version, and **never lowers a higher one**: a book from a
newer TrestleBoard keeps its marker.

---

## What the real import does now

Against the lodge's actual export, in a dry run that wrote nothing:

| | |
|---|---|
| Rows in the sheet | 239 |
| People added | **112** |
| Rows recognised as the same man again | 111 |
| Wives filed onto husbands' cards | **15** |
| Rows reported, not added | 1 (the child) |
| Postal address / member number / degree | 112 of 112 |
| Birthday, and the year behind it | 111 of 112 |
| Printed telephone filled from a mobile | 79 |
| …from a home number | 27 |
| Names that would have merged before M88 | 2 pairs, kept apart by member number |
| Importing the same file twice | changes nothing |

## The lesson, for the record

**A fixture written from a description of a file is a description, not a file.** `members-full.csv`
was built from a summary of the real export — right about the headers it knew, silent about the
column it did not, and wrong about which column carried the marker. Four of the five defects above
sat in exactly the gap between the summary and the sheet.

The fixture is now shaped from the file itself: both `Type` and `Item Type`, a wife above her
husband, a child, an orphaned spouse, a repeated member row, and a Jr./Sr. pair.
