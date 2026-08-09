# M59 — What we said last year

**Delivered 2026-08-08.** PLAN.md §11 M59.

## 1. What it is for

The monthly cycle has an annual rhythm the product ignored: the picnic announcement, the awards
night, the installation notice. Committee members keep old PDFs open in another window and retype
from them.

`File → "Show me last year's…"` opens the same month of the previous year beside the one being
written, to look at, with a button per article that brings its words across.

## 2. Read-only by construction, not by discipline

PLAN.md's acceptance is that with two documents open, the action catalog, undo stack and autosave
are provably scoped to the editable one. The answer taken here is stronger and simpler: **there are
not two documents open in any sense the app understands.**

`PastIssues` reads a `.tboard` and hands back a package and some strings. `LastYearWindow` is given
that package, a picture and a callback. Neither constructs a `DocumentSession` — so there is no
second undo stack, nothing for autosave to find, and no command that could reach the old file. The
pages are shown as the thumbnail the container already carries: a picture cannot be edited.

Two tests hold it. One opens last year's issue and asserts this month's undo stack and unsaved-changes
flag are exactly as they were. The other is a source scan: neither file may contain `new
DocumentSession` or `CreateEditable`. A future edit that introduced one fails there, before it could
confuse autosave.

The only thing that leaves the window is text, through one callback, and it arrives in this month's
newsletter through `InsertBlock` — M54's one-undo-step composite.

## 3. Finding it

The committee is asked once where old issues live, and the answer is remembered in settings.
"Where do you keep them?" is a question worth answering once a decade, not once a month.

The folder is searched for a `.tboard` whose metadata says that month of the previous year. Every
failure is a sentence rather than an exception — this is a convenience, and a convenience that
throws is worse than one that says "not this time":

- no folder named yet, or the remembered folder has gone → asks again;
- nothing matching → *"looked in that folder and did not find a newsletter for September 2025"*;
- a file that cannot be read, or that a newer TrestleBoard wrote → the **M25 standard**: it names
  the file and says this version cannot read it, and the search carries on past it.

That last one caught a real gap. The first version of the catch list missed
`UnsupportedFormatException` — the very type M25 introduced for honest refusal — so a too-old file in
the folder would have taken the command down instead of being stepped over. The test found it before
the milestone shipped.

## 4. What is offered to copy

One entry per story, in reading order, named by its first few words. Stories still holding a template
prompt are left out: an empty frame from last September is not something anybody wants to copy
across, and offering it would be the app suggesting the user paste a prompt into their newsletter.

## 5. A weak test, found and fixed

The obvious test for "it finds *last* year's" writes September 2025 and September 2026 and asks for
2026. It passes with the year check removed entirely — the files sort oldest-first, so the right
answer is found by accident.

`ThisYearsIssueIsNotMistakenForLastYears` names the current year's file so that it is examined
*first*, so only a real year check can pass it. Verified by breaking the check and watching it fail.

This is the fifth milestone in a row where writing the break first found something: M51 and M53 weak
tests, M55 a defect, M58 dead code, and now a weak test whose weakness was hidden by file ordering.

## 6. What guards it

`tests/App.HeadlessTests/LastYearTests.cs` (11): it finds the same month of the year before; this
year's issue is not mistaken for last year's; the wrong month is not the answer; no folder yet and a
vanished folder both ask rather than fail; a file too old to read says so and names it; something
that is not a newsletter at all is stepped over; a prompt-only article is not offered; each article
is named by its first words; looking at last year does not touch this month's undo stack; and
nothing that opens last year can ever edit it.

## 7. What was NOT done

- **No page-by-page browsing of the old issue.** The front page and the articles are what the
  committee reaches for; a second page viewer is a second canvas to keep in step with the first.
- **No copying of pictures or widgets across.** Text only, deliberately — the same line M66 draws
  for imported documents. A picture would need to enter through M18's ingest path with its caption
  and description asked for, which is a different feature.
- **No search across all old issues.** "Last year's September" is the question the committee
  actually asks; "find me every mention of the picnic" is a different one, and M21's find already
  covers the open newsletter.

Suite after M59: **1430 passing, 12 skipped** (11 new). No baseline moved.
