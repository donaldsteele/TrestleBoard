# M67 — Bring in a page from a PDF

**Delivered 2026-08-09.** PLAN.md §11 M67.

## 1. What it is for

Lodges receive finished things as PDF: the Grand Lodge announcement, the district calendar page, a
flyer for a degree night. The committee wants the page **as it is** — not retyped, not rebuilt.
**Insert → A page from a PDF…** shows the pages of a chosen file and puts the one they pick on the
newsletter as a picture.

## 2. Converted once, at import, and never again

The plan settled this and the code holds it: the page is rendered to a raster **at import time** and
stored as an ordinary image asset. The layout engine, the PDF export and the snapshot suite never
see a PDF.

That is M65's reasoning for the emblems, applied to a second source of pictures, and it is the same
one: determinism is a property of there being **one** rendering path, and PDFium is not it. A page
carried as a live PDF to the page would mean the editor canvas and the exported PDF each asking a
native library to draw it, and the WYSIWYG guarantee would rest on those two agreeing forever.

Consequences that come free rather than being built: the page can be moved, sized, wrapped,
captioned, positioned and re-described with the commands that already exist; one `Ctrl+Z` removes
it; it round-trips through the container like any picture; and `docs/M22`'s resolution warning
applies to it.

`DefaultLongestSide` is 2200 pixels — a US Letter page at roughly 260 pixels to the inch.

## 3. The original PDF is kept beside it

Gate 7's discipline, applied to a new kind of original: the PDF's bytes go into the container next
to the rendered picture, and `ImageFrame` gains two optional notes — `SourcePdfAssetRef` and
`SourcePdfPage`.

The **name** is settled before the insert command runs, because the frame records it, but the bytes
go in only once the picture is actually on the page. Nothing ever paints the PDF, so there is no
reason to register it early — and registering early is how a failed insert leaves a megabyte of
orphan in somebody's newsletter for ever. A test asserts the container is unchanged when the picker
is closed without choosing.

It costs a few hundred kilobytes and it buys the one thing the raster cannot: a later version can
re-render the page sharper, years after whoever emailed the file has left the committee. "Past what
this printer resolves" is a claim with a shelf life; the original is the hedge against it.

**This is not a new frame type.** The acceptance asks that the document-model change be "an image
like any other", and two optional properties on the frame that already exists is exactly that.
Nothing reads them yet; nothing about the picture depends on them; they are null on every picture
that did not come from a PDF and are never written when null, so a newsletter saved by an older
TrestleBoard opens and saves back byte-unchanged (M61's rule).

## 4. Thumbnails, because a page number is not a page

The committee is handed a twenty-page district bulletin and told "the calendar is in there
somewhere". Asking them to type a page number would be asking them to open the file in another
program first — the round trip this milestone exists to remove.

The picker renders every page small, once, and throws the thumbnails away with the window. The
chosen page alone is re-rendered at printing size; a twenty-page bulletin at full size would be
eighty megabytes to look at a menu.

A page that will not draw shows an empty tile rather than taking the file down with it. A bulletin
with one bad page in it is still a bulletin, and refusing the whole thing would be the app deciding
for the committee that they cannot have the other nineteen.

## 5. The platform that cannot

PDFium is a native library, shipped per RID through `Docnet.Core` — already a package in this repo,
used by `tools/TrestleBoard.PdfImport` since M21, so nothing new had to be vendored. A platform
where it will not load is a real possibility, and the plan required an explicit fallback rather than
a half-working feature.

`PdfPageRasterizer.IsAvailable` answers it once, by loading the library and handing it four bytes of
nonsense. The distinction being drawn is not "did that work" — it will not — but **"did the library
reject my bytes" (fine) versus "there is no library" (not fine)**: `DllNotFoundException`,
`BadImageFormatException`, `TypeInitializationException` and their kin mean no, and everything else
means the library is present and merely unimpressed.

That answer reaches the catalog through a new `ActionContext.CanReadPdfs`, filled in by the App —
the Editing layer cannot reach a native library and should not learn that native libraries exist.
**It defaults to false**, so a context built without thinking about it refuses the feature; the
thing it guards against is a crash, and that is the safe direction to fail.

The refusal order matters and is deliberate: no newsletter open is answered first with the ordinary
sentence, and only once that is out of the way does the app mention the missing reader. Telling
somebody with no newsletter open about a native library would be answering a question they did not
ask.

## 6. A file from outside

The PDF arrives by email from the district secretary. Every way PDFium can say no becomes a
`PdfPageException` carrying a sentence — damaged, not-a-PDF, password-protected (PDFium reports the
last two the same way, so the message names both possibilities and says what to do about either).
Page counts are capped at 400.

The page is composited **onto white**. A PDF page is ink on whatever it is printed on; on
transparency it would arrive as black text on nothing and print as a smear over a coloured panel.

Skia gets a *copy* of PDFium's pixels (`SKImage.FromPixelCopy`) rather than a pointer into a managed
array it does not own — the kind of shortcut that shows up as one corrupt page on one machine,
months later.

## 7. What it cannot do, and says so

The page arrives described as *"Page 3 of district-bulletin.pdf."* — honest and nearly useless. So
the app says out loud, at the moment it lands, that a screen reader cannot read a picture of writing
and points at "Describe this picture". That is the whole accessibility answer available here: a
rasterized page is opaque, and pretending otherwise by extracting text and hiding it in the alt text
would produce a description nobody checked.

## 8. What guards it

`PdfPages.Tests` — the rasterizer, with no app. The fixtures are PDFs written byte by byte in the
test file: fictional by construction, small enough to read in a diff, and the only way to write the
damaged cases at all.

| Test | Holds |
| --- | --- |
| `ThisComputerSaysWhetherItCanReadPdfsAtAllWithoutThrowing` | the probe never throws — the catalog asks it on every refresh |
| `TheRefusalSentenceSaysWhatToDoInstead` | the fallback is useful, not just polite |
| `EveryPageInTheFileIsListedWithItsShape` | page table, portrait and landscape |
| `APageComesOutAsAPngOfAboutTheSizeAskedFor`, `TheSecondPageIsTheSecondPage` | rendering |
| `ThePageArrivesOnWhiteRatherThanOnNothing` | ink needs paper |
| `APageThatIsNotInTheFileIsRefusedByNumber`, `AFileThatIsNotAPdf…`, `ATruncatedPdf…`, `AnEmptyFileIsRefused` | sentences, never raw throws |

**Nothing asserts what a rendered page looks like.** PDFium's output may differ between platforms
and between its own versions, and the whole design is that its output never reaches the layout
engine. Asserting pixels would be asserting a promise the app deliberately does not make — the
opposite of M65, where a committed hash *is* the promise.

`App.HeadlessTests/PdfPageTests` — the newsletter: an ordinary `ImageFrame` and one undo; the PDF
kept byte-for-byte in the container with the page number; described as the page it is; a cancelled
picker leaves no orphan asset behind; a file that is not a PDF refuses in a sentence and changes
nothing; the picker's tiles name the page **and its shape**; and both catalog refusals, in order.

`pdf-page-picker` joins the screenshot harness with `MilestoneGate` naming M67;
`docs/accessibility-test-script.md` gains §20 — including step 20.0, where a dimmed menu item with a
reason is recorded as a **pass**, not a failure, on a machine that cannot read PDFs.

## 9. What was NOT done

- **No text extraction.** The page is a picture. Pulling the words out and putting them in the alt
  text would produce a description nobody read; pulling them out as editable writing is M66's job,
  and a PDF is a far worse source for it than the Word document the same information usually
  arrives in.
- **No re-rendering of an existing page.** The provenance notes are written and nothing reads them
  yet. That is the point of writing them now: the door is left open, and opening it later needs no
  format change.
- **No multi-page import.** One page, one picture, one decision. "Bring in all six pages" would put
  six pictures on one newsletter page and leave the committee to sort it out.
- **No cropping at import.** The whole page comes in; M22's "Position the picture" already trims it,
  and a second cropping surface at import would be a worse version of one that exists.
- **PDFium is not bundled per RID by hand.** It arrives through `Docnet.Core`'s own native assets,
  which is the same mechanism SkiaSharp's natives already use. If a future RID is not covered, the
  fallback in §5 is what the committee sees, and it is tested.
