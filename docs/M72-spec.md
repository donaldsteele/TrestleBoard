# M72 — The emblem stays a drawing all the way to the page

**Delivered 2026-08-10.** PLAN.md §11 M72, §12 gate 25.

## 1. What changed, in one line

An emblem was vector at rest and raster in the document. The path data lived in C# source at
`EmblemLibrary.cs`, `MainWindow.axaml.cs` rasterised it at 2048px, and `PhotoController.InsertPhoto`
put the bytes in the container as `assets/img-N.png`. Now the path data goes into the document, the
renderer draws it at whatever resolution the surface needs, and `SKDocument` receives the curve.

## 2. Why this reverses a written decision

`docs/M65-spec.md` §3 closed this door deliberately, and §9 recorded the closing as a decision
rather than an oversight. Two things it did not know:

**The determinism claim that justified it was false.** M65 rasterised the emblem *because* the
SkiaSharp pipeline was believed byte-identical everywhere. The committed PNG hash held on
windows-latest and ubuntu-latest and failed on macos-latest for fifteen consecutive CI builds. The
variable was never the operating system: macos-latest is arm64, the other two legs are x64.
Antialiased coverage is floating-point arithmetic and arm64 contracts multiply-adds where the x64
baseline cannot; ~840 partially-covered pixels differ by ±1, which re-rolls the PNG filter choice and
the whole deflate stream. **The consequence was not a red build.** The PNG was what a `.tboard`
stored, so a macOS member's newsletter genuinely carried different bytes from a Windows member's, and
no re-recorded hash could have fixed it — one committed hash cannot be true for two architectures.

**The second renderer M65 feared did not have to be built.** `DocumentRenderSource` already drew
antialiased vector shapes onto both the editor canvas and `SKDocument`. `RenderVector` is a sibling
of `RenderShape`, and `SKPath.ParseSvgPathData` was already in the repository. There is now exactly
**one** drawing routine and the picker draws through it — see §5.

## 3. The vector block, and why Core stays BCL-only

`VectorBlock` (discriminator `"vector"`) carries a viewbox, a list of `VectorPart` (path data as a
string, pen width as a double), an ink colour as a `uint`, alt text, a caption and two provenance
fields. Nothing in it is a graphics type. `ShapeBlock` already stored `uint? StrokeArgb` and
`float StrokeWidthPt` with no Skia anywhere near it, and the same holds here: **the parse happens
once, in `Rendering`, where SkiaSharp already lives.** Core's dependency set is unchanged.

`EmblemId` and `EmblemFingerprint` are **provenance only**, on the `ImageFrame.SourcePdfAssetRef`
precedent from M67 — recorded, never read, never written when null.

*Rejected: storing only the emblem id and resolving the geometry at render time.* It needs either
`Rendering → Emblems`, which PLAN.md §9 forbids, or a resolver seam that inverts the dependency —
and it breaks the rule that a document is self-describing, because a later revision to the shelf
would silently change how an existing newsletter prints.

`ICaptionedBlock` is new, in Core: the two things a drawing shares with a photograph are the
sentence a screen reader hears and the words printed underneath. `SetPictureWordsCommand` and
`CaptionLayout.TextOf` were written against `ImageFrame` and now ask for the interface, which is
what makes captions and descriptions work on a drawing without duplicating either.

`Emblems` stays a leaf referenced only by `App`, mirroring the rule that `Editing` must not
reference `Roster`. The conversion from an `Emblem` to document geometry lives in
`App/Emblems/EmblemGeometry`, the one place emblem types touch the document.

## 4. The format bump, which is the risk

`CurrentFormatVersion` had been `1.0.0` since M2 and `MigrationRunner.Chain` was **empty and had
never executed a step**. This is its first live use, and bumping the version without registering a
1.0.0 → 1.1.0 step would have thrown `UnsupportedFormatException` on every newsletter in existence.

**`VectorBlocksMigration` is deliberately a no-op**, and its doc comment says why: 1.1.0 is purely
additive, so a 1.0.0 document *is* a valid 1.1.0 document and touching it would be the bug. A baked
emblem from before M72 stays the ordinary `ImageFrame` it has always been.

**The version is a function of the document, not of the build.** `TboardContainer.Save` computes
both `formatVersion` and `minReaderVersion` from whether any page holds a `VectorBlock`: 1.1.0 if
one does, 1.0.0 otherwise. This is the piece that is design rather than typing, and it exists for
two reasons.

- **M61's rule.** Stamping 1.1.0 on every save would change the manifest of every newsletter the
  committee owns, the first time each one was opened and saved by this build. `AnOldNewsletterSavesBackByteUnchanged`
  is the test.
- **Honesty to older readers.** A build that predates M72 binds `"type": "vector"` to nothing. A
  document that contains one says `minReaderVersion: 1.1.0` so that build says *"This newsletter was
  saved by a newer version of TrestleBoard. Please update"* rather than quietly dropping an emblem
  off somebody's cover. A document that contains none keeps saying 1.0.0, so older builds keep
  opening it.

**Old documents are NOT upgraded, and this is deliberate.** The emblem's id was not stored with the
baked frame (docs/M65-spec.md §9), so recognising one would need pixel matching — and getting it
wrong would silently rewrite a user's document. A baked emblem remains an ordinary picture forever.

`tests/Core.Tests/VectorBlockFormatTests.cs` holds nine tests on this, which is more than the
renderer got, because this is where the milestone could have destroyed data.

## 5. One draw routine, not two

`Rendering/VectorArtRenderer` is the only thing in the app that turns path data into marks. It takes
path strings and numbers rather than any document or emblem type, so both callers reach it without
`Rendering` learning that emblems exist:

- `DocumentRenderSource.RenderVector` — the page, and therefore the PDF.
- `EmblemPickerWindow.Thumbnail` — the tiles, through `VectorArtRenderer.ToPng`.

`EmblemRenderer` is **deleted**. Had it survived for thumbnails, the milestone would have built
M65's feared second renderer and merely relocated it. `TrestleBoard.Emblems` consequently drops its
SkiaSharp package references and becomes a BCL-only data leaf like `Roster`; `Emblems.Tests` keeps
SkiaSharp as a test-only reference, because "can Skia read this path" and "does anything reach
outside its viewbox" are questions only a path parser can answer.

**An unreadable path draws nothing rather than throwing.** Path data now arrives from a saved
document, so it can have been written by a later build, hand-edited or damaged. A newsletter that
cannot be opened because one ornament has a typo in it would be a far worse failure than an ornament
that does not appear.

## 6. What the app offers a drawing, and what it refuses

`SelectionKind.Drawing` is a new kind rather than a reuse of `Shape`, because
`ActionContextFactory` **defaults any unrecognised block to `Shape`**. A new block type nobody wires
up therefore *appears to work* while announcing "A shape is selected" and offering the shape group —
a silent wrong answer of exactly the family M73 was written about.

| Command | On a drawing | Why |
| --- | --- | --- |
| Move, resize, wrap, align, delete, z-order | Granted | A drawing is a thing on a page. |
| Corner drag keeps its shape (M69) | Granted | The *mechanism* has gone — a drawing cannot be cropped by its frame — but the gesture is the point: a square and compasses squashed by a stray corner drag is a mutilated symbol. |
| Write a caption… | Granted | A caption is about the page, not about pixels. |
| Describe this picture… | Granted | It arrives described; this is how that is corrected. Withholding it would put the one thing a screen-reader user depends on out of reach. |
| Fix this picture / Change how it looks… / Choose which part shows… | Refused | They act on an `ImageRecipe` a drawing does not have. |
| Put a picture here… | Refused | A drawing is not a frame with something in it — it *is* the thing. |

Each refusal carries its own sentence — `ActionAvailability` refuses an empty reason at
construction — and the refusals deliberately do **not** say "this needs a picture", which would be
untrue of the thing the user just chose. What is true is that those commands are about
*photographs*.

One shell-level trap was found and closed while doing this. `PictureTarget()` falls through to "the
first empty picture frame in the newsletter" when what is selected is not a photograph, which is
right for "Put a picture here…" and would have been badly wrong for the two granted commands: the
catalog offers them with a drawing chosen, and the shell would have gone off and described something
else on another page. `WordsTarget()` answers with the chosen drawing first. That is precisely the
offer-time / do-time disagreement gate 26 exists for.

## 7. What it cost and what it bought, measured

| | Before (raster) | After (vector) |
| --- | --- | --- |
| Square and compasses in a `.tboard` | 73,140 bytes of PNG | 170 characters of path data |
| The same emblem in a PDF | a raster at ~680dpi | path operators |
| Bytes differ between x64 and arm64 | **yes** | no |

PDF size, against PLAN.md's 1.3–1.8 MB target for a real issue and M8's 2.5 MB ceiling: the
five-page sample issue exports at **720,566 bytes**, and adding the nineteen-part corner ornament to
every one of its five pages takes it to **724,175 bytes** — **3,609 bytes for five ornaments**,
about 722 bytes each. The plan's warning that "a 19-part ornament repeated on six pages is not free
as content-stream operators" is correct in principle and, at this scale, immaterial: five ornaments
cost half a percent of one page's text. `OrnamentsOnEveryPageStayWellInsideTheSizeBudget` records
both numbers and fails if the per-ornament cost grows by an order of magnitude.

The emblem also stops inheriting the photo toolkit it never wanted, and `ReviewChecklist` stops
asking whether a decorative rule has "nothing printed under it" — its picture findings are reached
through `case ImageFrame`, and a drawing is not one.

## 8. Snapshot baselines are per-OS but **not** per-architecture

`SnapshotInfra.BaselineDir` selects a baseline by operating system — `windows`, `linux`, `macos` —
and by nothing else. Until now that was defensible: the per-OS split existed for **glyph** scalers
(DirectWrite / FreeType / CoreText), and the fixtures' non-text content was axis-aligned fills and
rules that land on few partial-coverage pixels.

`vector-emblems-page1.png` is the first fixture whose content is antialiased **curves**, which is
the case where architectures measurably disagree. Because macos-latest is arm64 and the other two
legs are x64, **the macOS baseline is implicitly an arm64 artifact and must be baked on arm64.**
Recorded here, and in the test's own doc comment, so the next person does not lose a day to it.

**Only the Windows baseline is baked in this milestone.** It was baked on this machine (x64,
Windows 11) and verified to contain no `tEXt`/`iTXt`/`zTXt` chunks, per the privacy rule for
committed images. The `linux` and `macos` baselines are **not present**; the test *skips* with a
message naming the missing path rather than failing, so the first CI run on each of those legs
reports a skip and the baseline is baked from that machine. That is a deliberate hole in coverage
until it is filled, and it is the honest state of the milestone.

## 9. Tests rewritten rather than deleted

- **`EmblemTests`.** Its four `OfType<ImageFrame>()` assertions asserted the claim M72 reverses.
  They are rewritten around `VectorBlock`, and two tests are added: the provenance fields, and a
  newsletter with an emblem adding no asset and saving to identical bytes twice.
- **`EmblemTests.OneUndoTakesItBackOff`'s doc comment** explicitly warned against "somebody later
  optimising the emblem into a frame type of its own". It is rewritten to say that the warning was
  sound and has been *honoured* — `InsertVector` reuses the same rectangle, z-order and composite
  command precisely so one undo still works — and that what the warning could not weigh is that the
  determinism it was protecting did not exist.
- **`PictureResizeTests`'s three emblem tests.** The corner-lock test is now about a drawing and is
  the test that would notice the new block type being left out of M69's rule.
  `AnEmblemIsNeverCroppedToItsFrame` read `ImageFit.Contain` back off the frame; a drawing has no
  fit mode, so reading one back would assert nothing. The claim is made in the two places it can now
  be made honestly: at the app level, that widening the frame leaves the drawing's shape untouched
  and that the crop commands are refused with reasons; and in `VectorArtTests`, against the pixels.
- **`Emblems.Tests`.** Four PNG tests were removed rather than moved, and the file says why in
  place: three were about an emblem PNG and no emblem PNG goes into a document any more, and
  `AnEmblemPngCarriesNoTextChunks` guarded a PNG that got sent to the lodge inside a `.tboard`.
  There is no longer such a PNG. The ink check moved to `VectorArtTests`, against the routine that
  actually draws.

## 10. What was NOT done

- **No colour, still.** `InkArgb` exists on the block and is always the default black. A colour
  picker is a feature, not a consequence of this one.
- **No user-supplied SVG.** `ParseSvgPathData` is a path-data parser, not an SVG reader, and nothing
  here changes that. Letting a document carry arbitrary imported artwork is a different milestone
  with a different threat model.
- **No upgrade of old baked emblems.** §4. Deliberate, and the most important omission in the list.
- **Linux and macOS baselines for the new fixture.** §8. They can only be baked on those machines.
- **The `.tboard` container's `assets/` handling is untouched.** A newsletter that contains an
  emblem *and* a photograph still carries the photograph's original bytes, exactly as before; what
  the emblem no longer contributes is anything at all.
