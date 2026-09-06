# M87 — Small enough to email

**Delivered 2026-09-05.** The last planned milestone in the plan. With M86 finished at M102,
**§11 has no undelivered milestone left.**

## What was wrong

The exporter rasters pictures at 300 dpi and re-encodes nothing, which is exactly right for the copy
that gets printed and can be far too big for a member's inbox. M56's send card named a file the app
had **never weighed**.

## What shipped

- **"Make the PDF" weighs the result.** Over 8 MB the card says *"This PDF is 14 MB, which some
  email services will refuse"* and offers **"Make a smaller one for email"**.
- The smaller copy is written beside the first as *`September 2026 (for email).pdf`*, and **the send
  card names that one** when it exists. "Print it" keeps handing over the full-quality copy.
- `view.settings` gains *"Always make the email-sized PDF as well"*, off by default.

## The decisions

**A second export, never a post-process.** Shrinking a finished PDF means decoding what Skia wrote
and writing it again — a second generation of loss on a file the committee may well print. Rendering
again from the same `DocumentRenderSource` costs one more pass and gives one generation, from the
originals.

**150 dpi and JPEG quality 82.** Measured on the fixture, 90 saves about a third and 82 about half,
and the visible difference on a lodge photograph is nil. Not lower, because M90 is the milestone
that exists to record what quality-zero does to a banner with lettering in it.

**8 MB, argued against the mailboxes this newsletter meets.** Gmail refuses over 25 MB and
Outlook.com over 20, but a great many members are on an employer's or a small provider's server
where 10 MB is common and some sit at 5. Eight catches the file that will bounce for a third of the
lodge while staying quiet about the ordinary four-page issue. `TheThresholdAndItsExplanationAgree`
fails if the constant moves without its sentence.

**The card says the trade in words**: *"Pictures will look a little softer on a screen, and
noticeably softer if that copy is printed."* Somebody who prints the emailed copy must not be
surprised by it, and "optimised" would tell them nothing.

## A guard the tests forced into existence

**A "smaller" copy that is not smaller is deleted, and the app says so.**

This was not in the plan. It came out of the fixture work below: JPEG is not smaller than lossless
for every picture. A newsletter of charts, scanned line art, or a plain cover banner can come out
*larger* at 150 dpi JPEG than at 300 dpi lossless — which is M90's finding from the other side,
where a flat fixture encoded losslessly beat the JPEG that was destroying it.

Without the guard the app would leave a second file on the disk that is worse in both directions and
call it the one to email.

## Three fixtures, two of them wrong

The size test failed twice before it meant anything, and both failures were the fixture:

1. **No picture at all.** `SampleIssue.CreatePackage()` takes the photo bytes as an optional
   argument, and without them there is nothing to re-encode — the two exports came out
   byte-identical. A fixture with no pictures proves nothing about a milestone about pictures.
2. **The flat-colour test picture.** The email copy came out *larger*, because lossless encoding of
   flat blocks beats JPEG. The milestone looked broken when the fixture was unrepresentative.
3. **A photograph-like picture** — continuous tone with fine detail everywhere, from a fixed seed so
   the comparison is stable — where JPEG wins the way it wins on a real one.

**A size comparison measures compressibility, not the feature**, unless what is being compressed is
the kind of thing the feature is for. That is the same sentence M90 wrote about its deleted
bytes-per-pixel floor, arrived at again from the opposite direction.

## Verification

`dotnet test TrestleBoard.slnx` — all green, no snapshot baseline moved. Four tests in
`tests/Rendering.SnapshotTests/EmailSizedPdfTests.cs` (the size, the page and annotation counts
matching, the full copy still re-encoding nothing, the threshold agreeing with its sentence) and
three in `tests/App.HeadlessTests/EmailCopyNamingTests.cs`.

The email copy's **bytes are not snapshot-tested across operating systems** — JPEG encoding is not
promised identical between Skia's bundled encoders — so what is asserted between the two copies is
the page count and the M78 annotation count, which is how "nothing but the pictures differs" is
checked without pinning bytes that are allowed to differ.
