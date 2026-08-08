# M53 — A draft for the Master, then the real thing

**Delivered 2026-08-08.** PLAN.md §11 M53. Closes the approval step and the print step, which are
the two places the committee's month left the app entirely.

## 1. The draft copy

Until now the copy the Master reviewed and the copy sixty people received were the same bytes with
different filenames, and the only thing standing between the two was somebody remembering which was
which. A page saying **DRAFT — not for sending** across it cannot be sent by mistake.

`WatermarkRenderer` draws it through HarfBuzz from a bundled face, with the same deterministic
`SKFont` settings `PageRenderer` uses. It is **not chrome**: it is meant to print, and it is the one
thing in this project deliberately added to an exported page — so it has to be as reproducible as
the newsletter under it.

Four decisions inside it:

- **Sans Bold**, because a display face at that size turns two words into a pattern, and because it
  is being read through a page of body text.
- **Grey, not red.** A draft is not an error, and a red page reads as one.
- **Over the page, not under it.** A mark beneath the text would be hidden by every picture and half
  the widgets — exactly where a reader has stopped looking.
- **Size derived from the page's diagonal**, by exact arithmetic from a single nominal shaping pass
  rather than a search that could land differently on another machine. A consequence worth naming:
  fewer words are set *larger*, not quieter. A one-word draft still spans the sheet.

`RenderWatermark` is a separate call on `DocumentRenderSource`, not a flag on `RenderPage`, so no
ordinary render can grow a watermark by accident: a page gets one only where somebody asked for one
in as many words. `DocumentPdfExporter.Export` gained one optional parameter, defaulted to null.

## 2. "Print it"

**No print subsystem is built, and that is the design rather than a shortcut.** The machine already
has a program that prints PDFs, already knows the default printer, and already has the dialog the
user has seen a hundred times. Reimplementing that would mean a page-setup dialog, a printer list
and a driver conversation — three new places to get printing subtly wrong for an audience with no
way to tell what had gone wrong.

`PrintService.Print` uses the shell's own `print` verb on Windows (what right-clicking the file in
Explorer does) and `lp`, then `lpr`, elsewhere. It never throws: this is the last step of an
evening's work, the file is safely on disk either way, and the worst outcome allowed here is a
sentence.

**Every path degrades to something honest.** If no print path answers, the PDF is *opened* and the
card says "press Ctrl+P in that window". If nothing opens it either, the card names the file and the
folder. `PrintOutcome` has three values because there are three genuinely different things that can
have happened, and a test asserts that neither fallback sentence contains the word "printed".

After a successful export the app now offers, once, on a card: **Print it** / **Not now**. Before
this, "Make the PDF" ended with a file somewhere on the disk and a user who had to go and find it.
The stream is closed before the offer appears — handing a half-written file to a printer would be a
worse bug than not offering at all.

`newsletter.print` is refused in words until there is something to print, and the reason names
`newsletter.exportPdf` as the way out (M11's rule). A picker that hands back a file with no local
path — a cloud location — is handled by saying so rather than by a command that silently does
nothing.

## 3. Testing: why there is no new pixel baseline

PLAN.md says *"pixel-deterministic across OSes (same renderer, same bundled fonts; snapshot-tested)"*.
The determinism is delivered; the snapshot baseline deliberately is not, on the reasoning
`OversetLabelTests` already recorded for the overset badge.

A baseline would have to be baked on three operating systems before it could fail honestly, and
what matters here is not which pixels the letters land on — that is the rasteriser's business and it
differs per OS — but that the words are drawn, in the same place every time, on a draft and on
nothing else. The stronger cross-OS guarantee is the one this project has always relied on and which
`SnapshotInfra`'s own comment states: the mark is shaped by HarfBuzz from a bundled face at an
arithmetically-derived size, so glyphs and positions are identical everywhere; only the
anti-aliasing of their edges is not.

`WatermarkTests` counts ink instead, which answers all three questions on any machine.

## 4. What guards it

- **`tests/Rendering.SnapshotTests/WatermarkTests.cs`** (7): the mark puts ink on the page; it
  reaches both edges; the same inputs give the same bytes twice; an ordinary page is untouched;
  different wording changes the page; fewer words are set larger rather than smaller; a store
  missing the bundled sans face **refuses** rather than exporting a draft with no diagonal — a
  draft without its mark is a draft that can be emailed to sixty people.
- **`tests/App.HeadlessTests/PrintAndDraftTests.cs`** (5): a draft PDF carries more than the real
  one; the real export is unchanged by the draft machinery; the wording says both what it is and
  what not to do with it; "Print it" is refused in words with the right remedy until a PDF exists;
  a missing file reports that nothing answered; and neither fallback sentence claims anything was
  printed.
- The fifteen existing per-OS document baselines did not move, which is the other half of "the real
  export is untouched".

### Failure-first evidence

Shrinking the watermark to a twentieth of its size failed `TheDraftMarkPutsInkOnThePage` and
`TheMarkCrossesTheWholePage`; making the fallback claim "printed successfully" failed
`TheFallbackCardNamesTheFileAndTheFolder`. Named, and only those.

**One test was found to be weaker than it looked, and was strengthened.** The first version of
`TheMarkCrossesTheWholePage` compared the four page *quadrants* — which meet at the centre, so the
shrunken watermark still put ink in all four and the test passed against the deliberate break. It
was checking a property the defect could satisfy. It now compares the outer fifth of the left and
right edges, which cannot be reached without actually spanning the sheet. This is the third
milestone in a row where writing the break first found a test that was not testing what its name
claimed.

## 5. What was NOT done

- **No bleed, imposition or print-shop features.** Out of scope, matching the declined ground of
  M47.
- **No print preview.** The app is WYSIWYG by construction; a preview of the preview is a second
  thing to keep in step.
- **No printer choice, no copies box, no page range.** All three exist in the dialog the OS is about
  to show.
- **The draft is not offered the review first.** M51's checklist asks before "Make the PDF"; a draft
  is by definition going to somebody who will read it, and stopping the user on the way to a review
  copy would be the app getting in the way of the very thing it is for.

Suite after M53: **1316 passing, 12 skipped** (12 new). No baseline moved, no screenshot re-baked.
