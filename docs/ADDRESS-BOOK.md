# The address book

The lodge's member list, kept once by the program and used by everything that prints a name. It
belongs to TrestleBoard rather than to any single newsletter, so it is still there next month, and
next year, and for whoever takes the secretary's chair after you.

This page describes what it holds, what fills itself in from it, and what it will and will not do
with your members' details.

---

## What it holds about a brother

Everything a lodge's own membership system usually holds. The form is in two tabs, and the split is
deliberate: the first tab is what somebody opening the window in a hurry came for, and the second is
everything else.

**The basics**

| Field | Notes |
|---|---|
| Name | The only field that is required. Everything else is optional. |
| Birthday | A month and a day — `7/4`, `July 4`, `1968-07-04` are all understood. |
| Year he was born | Stored, and **never printed** anywhere. See [Birthdays and the year](#birthdays-and-the-year). |
| Telephone number | **The one printed in the newsletter.** His other numbers are on the second tab. |
| Email address | Used to address the newsletter when you send it by email. |
| Lodge office | Free text. "Worshipful Master", "Junior Deacon", "Musician" — whatever your lodge calls it. |
| Highest degree | Entered Apprentice, Fellowcraft or Master Mason. |
| The date of that degree | Written as you like; stored so a spreadsheet cannot re-interpret it. |
| Still a member | Ticked for everyone on the rolls. |
| Passed to the Celestial Lodge | A tick and a date. Nothing the app generates mentions him again. |
| Groups | Tick boxes — who gets it by email, who gets a printed copy, and any list your committee invents. |

**Address and more**

| Field | Notes |
|---|---|
| Lodge member number | The number your lodge gave him. Also how the import recognises him — see [Bringing in a list](#bringing-in-a-list-of-members). |
| Letters after his name | "PM", "PDDGM". Free text. |
| Street address, second line, town, state, ZIP | The postal address, as your own records have it. |
| Post comes back undelivered | A tick, for the address you know is stale. |
| Home, mobile and work telephone | All three kept separately, because most committees have at least two numbers for somebody. |
| His wife's name, email and telephone | Fields on **his** card. A spouse is never a separate person in the book, so she can never appear in the birthday list, the officers table or a mailing count. |
| Notes | Anything else. "Prefers a telephone call." "Fifty years a Mason in 2022." |

## Finding somebody

Type into the box at the top of the window. Three letters of a name is usually enough — but the
search looks at more than the name, so any of these finds a man:

- his name, however it is written (`Placeholder, A.` finds `A. Placeholder`)
- his **lodge member number**
- any of his **telephone numbers**
- his **email address**
- his **street or town**
- his **lodge office**
- his **wife's name**

The line under the box says how many were found, and says it out loud to a screen reader.

## Bringing in a list of members

**People → Import from a file…** reads a spreadsheet saved as `.xlsx` or `.xlsm`, or a list
saved as `.csv`, `.txt` or `.tsv`.

The import asks **one question per piece of lodge information** — *"Birthday — which column has
it?"* — rather than showing a grid of your headings and hoping you can decode them. Each answer
offers the column letter, its heading, and the first couple of values in it, so you recognise the
data rather than the label. It guesses every answer first and says so: *"We guessed these. Change any
that are wrong."*

Then it shows you what it is about to do, in plain counts, and **nothing is written until you press
the button on that screen**.

### What it copes with

- **Excel dates.** A birthday column is usually a real date cell, which stores `45123` and merely
  *shows* `7/4`. Exported carelessly, that number is what lands in the file. TrestleBoard reads it
  back as a date rather than storing a number.
- **The same man on several rows.** Some membership systems emit one row per report item, so a
  brother appears two or three times. Rows carrying the same member number are recognised as one man.
- **Spouses in the same sheet.** A row marked as a spouse never becomes a member. Where it names the
  member she belongs to, her details go onto his card, and the review screen says how many rows that
  was.
- **A father and a son.** "John Smith Jr." and "John Smith Sr." are the same name to any matcher that
  ignores the suffix — and most do. TrestleBoard will not merge two men who carry different lodge
  member numbers, whatever their names do.
- **Three telephone columns and no plain one.** If the file has Home, Mobile and Work but no single
  "Phone", the newsletter's telephone number is filled from the mobile (or the home number), and the
  review screen counts that as a change rather than doing it quietly.
- **A workbook with a pivot table in it.** Membership systems often export one. If the spreadsheet
  reader refuses such a file, TrestleBoard reads it a second, simpler way rather than telling you to
  repair a file that is not broken.
- **Rows that are somebody's child.** They are not members and there is nowhere to put them, so they
  are reported rather than added to the roll.
- **Anything it cannot use.** Every unusable row is listed, one plain sentence each, and can be saved
  to a file. Nothing is dropped in silence.

### The rules it will not break

1. **An import never removes anybody.** A list that is missing this year's new members is a normal
   thing to import; it must not empty your book. Deletion is a deliberate act, one person at a time.
2. **A column you did not map is left alone.** Importing a telephone-only list cannot wipe everyone's
   birthdays.
3. **An empty cell in a mapped column is also left alone**, for the same reason: one column of blanks
   should not clear a field for the whole lodge.
4. **Two people are only merged on an exact match.** Anything close is a **question** — *"Are these
   the same person?"* — never an action.

## Sending it back out

**People → Save as a spreadsheet…** writes `Lodge-address-book-YYYY-MM-DD.xlsx`: one sheet, one row
per member, every field the book holds.

Every cell is written as **text**, deliberately. A real date cell would print a birthday as
`7/4/1900`, and a numeric telephone number would come back as `8.03555E+09`.

The file carries a TrestleBoard ID column, which is what makes the round trip lossless: **export it,
edit it in Excel, bring it back, and only the cells you changed move** — including when what you
changed was somebody's name.

That is also how a committee shares the list. Send the spreadsheet; the other person brings it in.

## What fills itself in

- **The birthday list** knows which month the issue is for and takes the brethren whose birthdays
  fall in it. Nobody who has passed, and nobody off the rolls.
- **The officers table** offers the brethren whose office matches each position, with the telephone
  number you told it to print.
- **Sending the newsletter** fills the BCC line from the members in the *Gets the newsletter by
  email* group, and tells you how many printed copies to run off from *Gets a printed copy*.

Every one of these **shows you every change before it makes it**. Filling a list from the address
book is never something that happens behind you.

## Birthdays and the year

The newsletter prints a month and a day and never a year. If your lodge's records carry a full birth
date, TrestleBoard keeps the year — it is your data, and losing it would mean re-importing to get it
back — but nothing that reaches a page can even see it.

There is a test that fails the build if the year is so much as *mentioned* in the code that produces
pages.

## Where it is kept, and who can see it

On your computer, in the program's own folder, and nowhere else. There is no account, no server and
no synchronisation. Nothing about your members leaves the machine you typed them into unless you send
a newsletter, save a spreadsheet, or hand on a pack yourself.

**A copy is kept every time the list changes** — ten of them, rotating. So:

- **People → Undo the last change** puts back what you had a moment ago.
- **People → Restore an earlier version…** lists the kept copies with the date and how many
  people were in each, for when the mistake was several steps ago.

If the file cannot be read at all — a sync client holding it, a permissions change — TrestleBoard
says so and **refuses to write over it**, rather than starting an empty book on top of your data.

## Handing over to the next secretary

**Pack everything up for whoever comes next…** puts the address book, your templates, your standing
paragraphs and your settings into one file. The person taking over opens it with **Bring in a pack…**,
which shows exactly what it would replace before it replaces anything.

The address book travels inside the pack byte for byte. It is a removal van, not an editor.

## Two versions on two laptops

If a book written by a **newer** TrestleBoard is opened by an older one, nothing is lost: fields the
older version does not know about are carried through untouched and written back exactly as they
came. The older copy says so in a sentence rather than showing you a column that looks empty.

---

*Related: [`docs/M12-spec.md`](M12-spec.md) for the original address book and its import flow;
[`docs/M88-spec.md`](M88-spec.md) for the milestone that widened it to what a lodge's own membership
system holds.*
