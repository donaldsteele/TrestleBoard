# M57 — Keep this design for next time

**Delivered 2026-08-08.** PLAN.md §11 M57.

## 1. What it is for

"Start from last month" serves the steady state. A special issue — an installation of officers, a
past masters' night, a memorial issue — meant rebuilding a layout or overwriting the working
lineage; and a committee that had evolved its layout had no way to bless the result as the new
starting point.

In a volunteer committee of elderly members, succession matters. A template is the only thing here
that outlives the person who made it, which is why it can also be handed to a file.

## 2. The flag the format promised and the app never kept

`TboardManifest.IsTemplate` has been in the file format since M2. Nothing in the repository has ever
set it — a repo-wide search finds the declaration, three mentions in PLAN.md, and nothing else. Not
even the three built-in templates set it, because they are code rather than files.

`NewsletterTemplate.From` sets it, and `ThePackageSaysItIsATemplate` holds it. This is the same class
of finding as M55's write-only "Active" column: a promise made in one place and kept in none.

## 3. The reset is shared, not copied

PLAN.md's acceptance says the reset pipeline is shared with carry-forward. It is: `DeepCopy`,
`ResetArticleProse` and `ClearMeetingDates` became `internal` on `CarryForward` and
`NewsletterTemplate.From` calls all three.

The reason is not tidiness. The day carry-forward learns to clear something new and a duplicated
template path does not, a template starts carrying last month's words into **every** issue built
from it — a single mistake reproducing itself monthly. `TheResetIsTheSameOneCarryForwardUses`
asserts the two agree about the thing that matters, and it fails if the article reset is dropped
from either.

What is *not* shared: `BumpIssueDate` and `RecomputeMeetingDates` are about next month specifically.
A template has no month.

## 4. Clearing the issue date, and why it is the defaults

`DocumentMetadata.IssueMonth` and `IssueYear` are plain `int`s defaulting to 1 and 2000. There is no
null to write, so "cleared" means "back to the model's own defaults".

Making them nullable would be a format change reaching every widget, every projection and every
snapshot baseline, for the sake of a state only a template is ever in — and the template asks for a
date the moment somebody starts an issue from it. Recorded here rather than left as a puzzle for the
next reader.

## 5. Where they live, and what they carry

One `.tboard` per template in `%AppData%/TrestleBoard/templates`, with a small JSON sidecar holding
the name and the date. The shape is `FileRecoveryStore`'s; the class deliberately is not, because
recovery snapshots are the app's business and templates are the user's, and the two have different
lifetimes and different words.

**Every path is computed from `AppPaths`.** That is what makes the screenshot harness's temporary
app-state root cover this store too (§0 rule 6) — the harness assigns `AppPaths.Root` once and
anything derived follows. `FileRecoveryStore` keeps a hard-coded fallback that would bypass the
redirect; this does not repeat it, and `TheTemplateStoreFollowsTheAppStateRoot` says so.

The tile picture is free: the app has written `thumbnails/page-1.png` into every saved `.tboard`
since the recovery work, so a template arrives with a picture of its first page already in it. The
thumbnail is refreshed before saving, so the tile shows the layout as it is now rather than as it
was at the last save.

## 6. Two places, on purpose

- **The Start screen lists them**, above the three built-ins and below the three big verbs — a lodge
  that has saved a template means to use it, but eleven months out of twelve still start from last
  month. One press starts a newsletter from one.
- **"My templates…" manages them** — rename, remove, hand on. Deliberately a separate window:
  putting a Remove button on the Start screen's tiles would mean somebody reaching for "start from
  this" and finding "delete this" under their finger.

Removal is confirmed in the established plain-language shape, with the safe answer as the default,
and the confirm says what removal does *not* touch: newsletters already made from it. It also names
the way to keep a copy first.

## 7. Handing one on (§0 rule 7)

A template carries the officers table and the cover, so on a real machine it carries real names.
"Hand it on…" writes it **only where the user browsed to** via the save dialog — no default location
beside the repository or the newsletter, exactly as roster export does it. Templates are personal
files while they stay in AppData (§0 rule 4); the moment one leaves, it leaves by the user's hand.

## 8. What guards it

`tests/App.HeadlessTests/MyTemplatesTests.cs` (15): the layout, widgets and pictures are kept and
the writing is not; the issue date is cleared; the manifest says it is a template; the newsletter it
came from is untouched; the reset agrees with carry-forward; a template is there next time; saving
twice under one name replaces rather than twins; four awkward names — including `..\..\escape` and
`:::` — save inside the folder; renaming keeps the template; removing takes both files; a template
whose sidecar is gone keeps its existence if not its name; a missing folder is not an error; the
thumbnail round-trips; and the store follows the app-state root.

### Failure-first evidence

Dropping the article reset and the date clear from `NewsletterTemplate.From` failed
`TheLayoutIsKeptAndTheWritingIsNot`, `TheIssueDateIsCleared` and
`TheResetIsTheSameOneCarryForwardUses`. Three named failures, and only those.

## 9. What was NOT done

- **No thumbnail on the Start screen tiles.** The manage window shows the picture; the Start screen
  shows the name. A 150px image on a 96px-tall tile would either shrink the target or grow the
  screen past the point where three verbs and a list still fit on a laptop.
- **No import of a handed-on template.** The recipient opens the `.tboard` as a newsletter and saves
  it as one of their own — two steps that already exist, rather than a third command that does both.
  M64's successor pack is where the round trip properly belongs.
- **No editing a template in place.** Open it, change it, save it under the same name; that is the
  same act with one fewer command to explain.

Suite after M57: **1387 passing, 12 skipped** (18 new). No baseline moved.
