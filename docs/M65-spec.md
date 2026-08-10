# M65 — The emblem shelf

**Delivered 2026-08-09.** PLAN.md §11 M65.

## 1. What it is for

A trestle board carries the craft's emblems. Today a committee wanting the square and compasses on
the cover has to find an image somewhere, judge whether they are allowed to use it, and get it into
the app. Every one of those three steps is a place to go wrong, and the third is where a 90-pixel
JPEG off a search result ends up printed four inches wide.

**Insert → An emblem…** opens the cabinet: nineteen emblems, named, searchable, walkable by
keyboard, each placed as an ordinary picture frame.

## 2. The artwork is geometry in the source

There is no `emblems/` folder of SVG or PNG files. Each emblem is a handful of SVG **path data**
strings in `EmblemLibrary`, drawn by `EmblemRenderer` through SkiaSharp. `SKPath.ParseSvgPathData`
is the only piece of SVG this app understands, and it is parsed by the graphics library rather than
by a document reader — no new dependency, no XML, no parser to keep safe.

That choice is what makes everything else in this milestone simple:

- **It is diffable.** A change to an emblem shows up in review as the curve that moved.
- **It is hashable** the way a bundled font face is, which is what gate 22 asks for (§5).
- **It scales.** Stroke widths scale with the drawing, so an emblem is the same picture at any
  size rather than a spindly one when small.
- **The provenance is simple to state honestly** — nothing was imported, so there is nothing to
  trace back.

Parts are stroked or filled. Most of the craft's tools are lines rather than silhouettes — a plumb
rule *is* a line and a weight — and stroking with round caps is what makes them authorable as
geometry a person can read.

## 3. Rendered once, at insert time, into an ordinary picture

The plan left the choice open: "SVG source rendered through the existing pipeline or pre-rasterized
at bundle time — decided in the spec by what keeps layout deterministic".

**Decided: rasterized once, at insert time, into an ordinary image asset.**

The app has exactly one thing proven byte-identical on Windows, Linux and macOS: its own SkiaSharp
pipeline, held by the snapshot suite. Rendering the emblem through that pipeline once and handing
the PNG to `PhotoController.InsertPhoto` — the single M18 ingest path a photograph goes through —
means the layout engine, the PDF export and the snapshot tests **never learn that emblems exist**.
A vector emblem carried all the way to the page would have been a second renderer, and WYSIWYG is a
promise about there being one.

What falls out of that decision for free, and would each have been work otherwise: the emblem can
be moved, resized, wrapped, cropped, captioned and re-described with commands that already exist;
one `Ctrl+Z` removes it; it lands in the `.tboard` as an ordinary asset with its original bytes; and
`docs/M22`'s picture-resolution warning applies to it like anything else.

`DefaultLongestSide` is 2048 pixels — an emblem placed three inches wide has about 680 pixels to the
inch, past anything the committee's printer can resolve. The cost is a few tens of kilobytes, once.
Black on transparent, so an emblem over a tinted panel does not arrive in a white box.

## 4. What is on the shelf

Nineteen, across four headings, **deliberately wide rather than minimal** — the owner's direction,
and a test asserts the count, because a shelf with three things on it sends people back to the
search engine and defeats the point.

| Heading | Emblems |
| --- | --- |
| The working tools | square and compasses (with and without the G), the square, the compasses, plumb, level, trowel, gavel, twenty-four inch gauge |
| The lodge | the two ashlars, the two pillars, the mosaic pavement |
| Lights and seasons | the all-seeing eye, the blazing star, the sun, the moon and stars |
| Ornaments | a rule with a diamond, a plain rule, a corner ornament |

The compasses, the square and the G are each drawn **once** and composed into the pair, so the
emblem everybody knows can never drift from its own halves.

Every emblem carries a **default description** — the acceptance asks for it, and it means a picture
from the shelf is described before anybody thinks to describe it. The app drew the picture, so it
knows what it is; asking somebody to describe the square and compasses back to the app that just
drew it would be the software pretending not to know something. M23's "Describe this picture" still
changes it.

Three findings from drawing them:

- **The moon was drawn as one outline, not a circle with a circle taken out of it.** The textbook
  way was wrong twice here: an arc between two points bulges where its radius says it must, and what
  came out was a ring with a bite rather than a crescent. Stating both edges directly makes the
  drawing the thing that was intended rather than the outcome of an arithmetic guess.
- **"Dividers" belongs to the compasses, not to the decorative rule** — it is what a draughtsman
  calls them, so the corpus test records that and does not force it the other way.
- **Nothing may reach outside its own viewbox**, pen width included, or it is silently cropped when
  rendered and the crop only shows up on the page. A test walks every path's bounds; it caught two.

## 5. Provenance (PLAN.md gate 22)

The plan records that the owner, as a Mason, holds full authority to use the craft's symbols — and
requires that every shipped artwork *file* still carry written provenance, "because the symbol and a
particular rendering of it are different rights". That distinction is the whole of this section.

- **`assets-src/emblems/EMBLEMS-PROVENANCE.txt`** states it: the symbols are centuries old and
  unowned; every emblem here was **drawn for TrestleBoard** — no clip art, no traced scan, no file
  from a search result, no stock asset, free or paid; the artwork is released by the project under
  **CC0 1.0**, which covers the artwork alone and makes no claim over the symbols themselves.
- **`assets-src/emblems/emblems.json`** is the manifest, mirroring `fonts.json`: schema version, the
  provenance record, and one entry per emblem with its **SHA-256**.
- Because the artwork is code, what is hashed is the drawing — id, viewbox and every path and pen
  width in order, formatted culture-invariantly so a machine in another country hashes the same.
- The provenance is an **embedded resource** in `TrestleBoard.Emblems`, so it travels with the build
  and not only with the repository — the M14 fonts precedent.

Four tests enforce it: no emblem without a manifest entry; no manifest entry for an emblem that has
gone; no entry whose hash no longer matches the drawing; and the provenance file present, embedded,
and actually saying where the artwork came from.

## 6. Determinism

> **Corrected on 2026-08-09. What this section claimed was false, and it failed in the field.**
> The original text is kept below the line because it is the reasoning M72 overturns.
>
> The committed PNG hash held on windows-latest and ubuntu-latest — two operating systems, two
> native Skia binaries — and failed on macos-latest for fifteen consecutive builds. The variable is
> not the OS: **macos-latest is arm64 and the other two are x64.** Antialiased coverage is computed
> in floating point and arm64 contracts multiply-adds where the x64 baseline cannot, so ~840
> partially-covered pixels differ by ±1, which re-rolls the PNG filter choice and the whole deflate
> stream. One committed hash cannot be true for two architectures.
>
> What *is* identical everywhere is the geometry: `EmblemFingerprint.Of` hashes the path data as a
> string and never invokes Skia, which is why gate 22 passes on arm64. The determinism claim below
> was attached to the wrong artifact.
>
> Consequence for the product, not just the build: an emblem is rasterised at insert time and the
> PNG is what a `.tboard` stores, so a macOS member's newsletter really does carry different bytes
> from a Windows member's. **M72 keeps the emblem vector into the document and the PDF**, which
> removes the raster from the container and makes the sentence below true by construction instead of
> by hoping Skia is bit-stable. See also §9, which closed this door deliberately and recorded the
> cost we are now paying.
>
> ---
>
> ~~`TheSameEmblemRendersToTheSameBytesEveryTimeAndEverywhere` renders one emblem and compares the
> PNG's SHA-256 against a hash committed in the test. Because CI runs the suite on
> windows/ubuntu/macos-latest, that single assertion is the cross-OS claim: **two committee members
> on two platforms produce byte-identical documents.** If it fails after a deliberate redraw, the fix
> is to re-record the hash *and* the manifest entry in one commit, having looked at the picture.~~

No snapshot baseline moved: no fixture uses an emblem.

## 7. Accessibility (§6)

- 22pt search box, 15pt tile labels, 168px tiles — targets far past the 44px minimum.
- Focus lands **in the search box** on open; the count is a polite live region; "nothing matched"
  says what to do next.
- Every tile carries **the name and the sentence** in its automation name, not just the name: the
  person using this window is choosing a picture they cannot see. The name is also written under
  each tile as real text rather than a hover tip — a tip is invisible to a screen reader, to a
  keyboard user, and to anybody who does not know to hover.
- Tiles come out in shelf order, which is category order, so Tab meets the working tools before the
  ornaments. `Escape` closes.
- `EmblemPickerWindow` joins the `AccessibilityTests` walk; `docs/accessibility-test-script.md`
  gains §18, eight manual screen-reader steps.

## 8. Layering

New leaf project `TrestleBoard.Emblems` — SkiaSharp and the BCL, never Core — beside `Imaging`,
`Roster` and `Spelling`. An emblem is geometry and a name; it knows nothing about documents. `App`
references it and turns one into a picture. Nothing else in the dependency flow changed.

The two pack commands aside, the shelf adds one action: `insert.emblem`, in the `Insert` group,
available whenever a newsletter is open and refusing with the standard sentence when none is. It
wears the picture glyph, for `ReplacePicture`'s reason — the user is putting a picture on the page,
and where it came from is not a difference a glyph should try to draw.

## 9. What was NOT done

- **No colour.** Every emblem is black on transparent. Colour would need a palette per emblem, a
  colour picker, and a decision about what happens in the high-contrast theme — and a trestle board
  is printed, usually in black and white.
- **No user-supplied emblems.** "Insert a picture" already does that, and a second import path
  pretending to be a shelf would be two ways to do one thing.
- **No emblem stays a vector on the page.** §3 is the reason, and it is a deliberate closing of the
  door: an emblem is a picture from the moment it lands.
- **No re-render at a larger size later.** The stored PNG is 2048px on its longest side, which is
  past what any printer here resolves. Should that ever prove wrong, the emblem's id is not stored
  with the frame, so re-rendering would need a format change — recorded here as the known cost of
  the simpler design.
- **The seasonal set is thin.** Sun, moon and stars, and that is all. Wreaths, flags and the like
  are what the next pass would add; nothing about the shelf makes that harder than writing more
  path data and re-running the manifest.
