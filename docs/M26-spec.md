# M26 — Import honestly

**Delivered 2026-08-07.** §14.5 grouping 3. Everything here is about the app either reading what a
file actually says, or admitting it cannot — instead of guessing and printing the guess.

---

## 1. The serial floor said 61 in prose and 1 in code

`FieldValues.SmallestPlausibleSerial` carried a doc comment explaining that Excel serials below 61
are unusable ("below 61 the sheet and the calendar disagree", "'7' alone is not a birthday") and was
then set to `1`. The code won.

So every small number in a birthday column was read as a date:

| Cell | Was imported as | Is now |
| --- | --- | --- |
| `7` | 7 January | not a birthday |
| `42` | 11 February | not a birthday |
| `1968` | 21 May 1905 | not a birthday |

It reached further than the rows themselves. `ColumnGuesser` decides which spreadsheet column is
which by asking this parser whether a column's values look like birthdays — so a column of ages,
dues or degree years scored as a Birthday column, and could push the real one out of the mapping the
user is then shown as "we guessed these".

**Two changes**, because the floor alone was not enough:

- The floor is now 61, as documented. Nothing real is lost: serial 61 is 1 March 1900, and a real
  birthday written as a serial is five figures.
- A bare number in the **calendar-year range 1800–2200 is read as a year, not a serial.** The floor
  does not settle this on its own — serial 1968 is a genuine date, 20 May 1905 — but the two
  readings only collide for dates in 1904–1906, and nobody on a lodge roster was born or raised
  then, while columns of four-digit years are everywhere.

Anything else that is a bare number now stops there rather than falling through to the looser
parsers below, which would happily read `"1968"` as the first of January 1968.

`ReadDate` keeps a bare year **verbatim** — `"1968"` stays `"1968"`. That method's contract already
says unrecognised text is preserved as the user wrote it, and `"1968"` is true where
`"1968-01-01"` invents a day nobody claimed.

## 2. 29 February was dropped in three years out of four

The year-less birthday formats (`"MMMM d"`, `"MMM d"`, `"d MMMM"`, `"d MMM"`) were given to
`TryParseExact`, which fills a missing year with **today's**. In a non-leap year `"February 29"`
therefore failed to parse and the row was silently dropped — a real brother, with a real birthday,
missing from the list.

The bug fixed itself for one year in four, which is the worst way for a bug to behave: it is
unreproducible for anyone who looks at the wrong time.

Year-less shapes are now parsed against a fixed leap year.

## 3. Days that do not exist were printed as birthdays

`WizardValidators.TryParseMonthDay` checked `day is >= 1 and <= 31` regardless of the month, so
`2/30`, `2/31`, `4/31`, `6/31`, `9/31` and `11/31` were accepted, stored, and printed on the page
that goes out to the whole lodge.

February is 29 days here on purpose. 29 February is somebody's actual birthday, and a month-and-day
with no year behind it has no business ruling it out.

## 4. The officers dialog promised changes it did not make

`OfficersRosterProjection.Plan` proposes across all twelve `StandardPositions`, whether or not the
table holds a row for each. `Apply` walked `current.Officers` and rewrote matching rows — so a
decision for an office with **no row yet** was silently dropped. The dialog listed the change, the
user ticked it, and nothing happened.

Reachable from any document whose officers list is short of twelve: an older format, a hand edit, a
table trimmed by a newer build.

`Apply` now adds a row for any decision it did not consume, inserted in printed order rather than
appended to the bottom — offices this build does not recognise sort last, which is where a user who
added one of their own will have put it. "Sync twice equals sync once" is asserted to still hold.

---

## 5. Considered and deliberately unchanged

**Month-first date reading** (`"4/7"` → 4 July, not 7 April). The review flagged this as a possible
d/M bug. It is a decision, not an oversight, and now says so in the code: this lodge is in South
Carolina and writes dates the American way. There is nothing inside a two-number cell that can tell
the two readings apart, and a d/M default would silently swap every day-of-month at or below twelve
for the people this app is actually for.

**CSV encoding** (`CsvTableReader` decodes BOM-less files as UTF-8, so a Windows-1252 export lands
accented names as U+FFFD). Still open, and still worth doing — it is left for a milestone that can
carry the encoding-detection decision properly rather than bolting a guess onto this one.

Also still open from §14.2's Roster block: the header-hint misfires ("Member Number" → Name),
`ChooseHeaderRow` accepting a row past the end, and unparsable backups never being trimmed.

---

## 6. What guards it

- `FieldValueTests.ASmallBareNumberIsNotABirthday` (6 cases: 7, 12, 42, 60, 1968, 2026)
- `FieldValueTests.ARealSpreadsheetSerialStillReads` — the floor is 1900-03-01, not "no serials"
- `FieldValueTests.TheTwentyNinthOfFebruaryIsABirthdayInEveryYear` (4 spellings)
- `FieldValueTests.ADegreeDateIsStoredUnambiguously` extended with the bare-year case
- `BirthdayListTests.ADayThatDoesNotExistInItsMonthIsRefused` (6 cases) and
  `TheDaysThatDoExistAreStillAccepted` (4, including 2/29)
- `OfficersRosterProjectionTests.AnOfficeWithNoRowYetIsAddedRatherThanSilentlyDropped` — the row is
  added, filled, in printed order, and a second sync changes nothing

Every one of these was **run against the unfixed code and confirmed to fail** first.

Suite after M26: **1160 passing, 12 skipped**. No snapshot baseline moved.
