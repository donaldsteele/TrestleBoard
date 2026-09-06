# M106 — The newsletters you had open

**Delivered 2026-09-05.** The register's "recent files is not an MRU" row.

## The gap, and why the old list is not it

M76 (h) put a recent list on the start screen. It is built by scanning `AppSettings.OldIssuesFolder`
by **last write time**, and the reasoning was written down at the time: no second store, and "last
saved" is the fact we actually have.

That list answers a different question, and three things follow from it:

- It is **empty until somebody has nominated a folder** — which most committees never do.
- It **cannot see a newsletter kept anywhere else**: the desktop, a memory stick, a mail download.
- It orders by when a file was **written**, so a file touched by a backup tool climbs it, and a
  newsletter opened and read climbs nothing.

And it appears on the **start screen only** — so once you are in the app, the way back to what you
had open on Tuesday is the file dialog.

## What was built

`AppSettings.RecentNewsletters`, capped at eight, most recent first, each path once
(case-insensitively — the same file in different capitals is the same file, and a list that showed
it twice is a list somebody stops trusting). The rule is a pure function on the record, like
`WithPictureUsed` beside it, so it is testable without a window and without a disk.

**Recorded in the `DocumentPath` setter.** That property is a property for exactly this reason: M39
hung a second fact off it because seven places set the path and six of them would have been right.
This is the third fact, and it cannot be forgotten by the eighth call site. Opening and Save As both
go through it.

`newsletter.recent` — "Newsletters you had open…", in the File menu.

**Not "Recent files".** A file is a thing the computer has; a newsletter is a thing the committee
made, and this audience has been told which of those they are looking at everywhere else in the app.

**Both lists are kept.** The start screen keeps the folder scan, which is how a committee finds an
issue from three years ago; this is what the File menu offers, which is how somebody gets back to
last Tuesday.

## Two decisions worth the words

**Available with nothing open.** That is when it is most wanted, and it is the only command in its
group whose whole purpose survives an empty window. An empty list is answered **by the window** —
"You have not had a newsletter open on this computer yet", with the file dialog beside it — rather
than by a greyed-out menu item whose reason is "you have not opened anything yet", which tells a new
user off for being new.

**A path that no longer leads anywhere is dropped only when somebody asks for it.** The list is not
swept at load: a newsletter on a memory stick that is not plugged in today has not stopped existing,
and forgetting it on that basis would be the list quietly deciding something the user did not. When
the file really is gone, the app says which one, says it has been taken off, and says what to do if
it is on a stick.

**Shown by name, never by path** (§0). The folder appears as its own name only — "In Trestle Boards"
— which is enough to tell two Septembers apart without spelling out the user's filing in a window.

## Verification

`dotnet test TrestleBoard.slnx` — green, no snapshot baseline moved. Nine new tests in
`tests/App.HeadlessTests/RecentNewslettersTests.cs`, one of which opens a real newsletter and looks
at the saved settings: every other test is about the rule, and that one says the rule is ever
applied — which is the entire defect class this audit is about. **Mutation-checked**: with the
recording removed from the setter, that test fails and the other eight do not.
