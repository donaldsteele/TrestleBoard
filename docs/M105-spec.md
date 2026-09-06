# M105 — What this newsletter is called

**Delivered 2026-09-05.** The register's "no document-properties surface" row.

## The gap

Three fields on `DocumentMetadata` are read in earnest:

- **`LodgeName`** prints in the line along the bottom of **every page**, goes out as the PDF's
  Author, and is half the email subject.
- **`Title`** is what the app suggests as the file name when the PDF is made.
- **`MeetingRule`** is what `CarryForward.RecomputeMeetingDates` parses when somebody starts next
  month's newsletter from this one.

And the only thing that ever wrote any of them was **the cover heading's wizard** (M75 (a), (d) and
the census pass found the three write-back holes, and fixed them *in that one route*). So a
newsletter with no cover heading on it could not say which lodge it belonged to at all — and
`newsletter.issueDate` is correctly refused there, because it asks on the banner, which meant the
refusal took the other three facts with it.

## What was built

`newsletter.about` — "What this newsletter is called…", in the File menu under the issue-date
question.

**Not "Document properties…".** That names a filing cabinet drawer. Every field behind it is a plain
question about the lodge, and the title says what you will be asked.

**Each field says where it shows up.** A form whose fields do nothing visible is a form people leave
blank — and all three of these have been silently blank on real newsletters, which is how the class
of bug M75 named got as far as it did.

**Available on any open newsletter, cover heading or not.** That is the whole point: these facts
belong to the newsletter, not to a widget on page 1.

**Blank never erases, and everything is trimmed.** A lodge name with a trailing space is a name that
will not match itself in a subject line or a footer, and nobody can see the difference.

## The issue date is shown and not asked

It is answered on the cover heading and mirrored on the banner. A second field here would be a second
source of truth for one fact — the exact shape of defect M75 exists about. The window says which
issue it is and where to change it, and when nobody has said, it says **that**, rather than printing
"January 2000": the model default M75 went to some trouble to stop being read as an answer.

## Verification

`dotnet test TrestleBoard.slnx` — green, no snapshot baseline moved. Five new tests in
`tests/App.HeadlessTests/AboutThisNewsletterTests.cs`; `MetadataCensusTests` continues to hold, since
this adds a writer rather than a field. **Mutation-checked**: gating availability on a cover heading
— the very thing this milestone exists to stop — fails the test that says so.
