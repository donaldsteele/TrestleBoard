# M90 — Every picture in every PDF, at quality zero

**Status:** implemented. Delivered 2026-09-05, shipped as v1.7.2.

The owner exported an October newsletter and the cover banner came out in coloured blocks and
displaced bands. The text on the same page was perfect.

## What it was

```csharp
var skMetadata = new SKDocumentPdfMetadata
{
    Title = metadata.Title,
    …
    RasterDpi = 300,
};
```

`SKDocumentPdfMetadata` is a **struct**. An object initializer leaves every field it does not mention
at its zero value, and one of those fields is `EncodingQuality` — where **0 means "re-encode every
picture as a JPEG at quality nought"**. Skia's own default is 101, meaning "do not re-encode at
all", and it is reachable only through `SKDocumentPdfMetadata.Default`.

Measured on the owner's own file: the banner was **625 × 253 pixels in 3,057 bytes** — 0.15 bits per
pixel. Every image in every PDF this program has ever exported was destroyed the same way.

Both exporters had it, written the same way, three years apart:
`PdfExporter` and `DocumentPdfExporter`.

## Why nothing caught it

**Text is vector.** The bug could not touch a single glyph, and almost everything that looks at an
exported PDF is looking at text:

- `PdfParityTests` rasterizes the PDF and compares it to the screen render — but box-downsamples
  4× with a channel threshold of 64, a tolerance chosen for antialiasing differences between two
  rasterizers. That is roughly "you may destroy a photograph provided you leave the letters alone".
  It also runs on Linux CI only, since it needs poppler.
- `PdfFontsAreSubsetEmbedded`, `PdfTextIsExtractable` — text.
- The whole PNG snapshot suite renders through `PageRenderer` to a bitmap. It never goes near
  `SKDocument`, so the PDF's own encoding is invisible to it.

And one test was **evidence for the bug rather than against it**.
`IssueExportTests.ThePhotoIsASmallFractionOfTheExportedFile` asserted the photo cost under 100 KB and
recorded approvingly that it had been "under 2 KB when this was written", concluding that Skia's
compression was as good as anything the pipeline could manage. Two kilobytes was the sound of a
photograph being destroyed. The test has been rewritten to say what it is actually for — that
nothing has started embedding pictures at full size — with the old reading preserved in its comment.

## The fix

Start from `SKDocumentPdfMetadata.Default` and override, in both exporters. Never from a bare `new`.

**Lossless rather than a high-quality JPEG**, deliberately. Quality 90 was measured and is visually
fine for photographs — but the picture that exposed this is a *banner with lettering in it*, and
sharp text over flat colour is exactly where JPEG ringing shows. It is also what PLAN.md has said the
exporter does since M87 was written: rasters at 300 dpi and re-encodes nothing. The cost is size, and
size is M87's job, not this one's.

## The tests

`PdfPictureFidelityTests`, in `Rendering.SnapshotTests` but needing no poppler, so they run on all
three operating systems rather than on Linux alone:

1. **`ThePhotographIsNotReEncodedOnItsWayIntoThePdf`** — parses the PDF's image objects and fails if
   any is `/DCTDecode`. Asserts the list is non-empty first, or an export that embedded no pictures
   would pass in silence.
2. **`TheDecodedPictureStillResemblesTheOneThatWasPutOnThePage`** — pulls the largest image back out
   of the PDF, decodes it (JPEG directly; Flate inflated and rebuilt from raw rows), and compares it
   to the source photograph channel by channel. The bug puts it 14.1 levels per channel away; the
   fix, 0.

Both fail against the old code. That was checked by putting the bug back.

**A third test was written and deleted**: "the picture carries at least a quarter of a byte per
pixel". It failed the *fixed* code as loudly as the broken code, because the fixture is flat blocks
of colour and Skia's lossless encoding of it is 0.03 bytes per pixel — nine times smaller than the
quality-zero JPEG it was meant to catch. A size floor measures how compressible a picture is, not
whether it survived. Recorded here because it looked like the obvious test to write.

## What the committee sees

Photographs and banners in an exported newsletter now look like the ones on the screen. Files get
bigger — the fixture's photo went from 2 KB to 129 KB — and a real issue stays well inside the
2.5 MB budget the tests hold it to.
