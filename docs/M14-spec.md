# M14 — More fonts, and choosing them

**Delivered 2026-07-27.** Implements PLAN.md §11 M14. This file records what was built, the
decisions that differ from the plan, and what is still open.

Before M14 the entire typographic surface was Bold, Italic and a paragraph-style combo that listed
only the styles a document already contained — which, for a template-created document, was exactly
one. The committee could not change a typeface or a text size at all.

---

## 1. The catalog — 20 families, 59 faces

`assets-src/fonts/fonts.json` is the source of truth: one row per **face**, carrying the upstream
repository pinned to a tag or commit, the variable axes it was instanced at, the subset spec, the
licence file, and the SHA-256 of the produced bytes.

| Group | Families |
| --- | --- |
| Serif text (7) | Source Serif 4, EB Garamond, Crimson Pro, Libre Baskerville, PT Serif, Lora, Libre Caslon Text |
| Sans text (5) | Source Sans 3, Open Sans, Lato, Libre Franklin, PT Sans |
| Display (5) | Cinzel, Playfair Display, Cormorant Garamond, Bitter, Marcellus |
| Accent (3) | Cinzel Decorative, Great Vibes, UnifrakturMaguntia |

**Deviation from the plan: 59 faces, not 58.** The plan's arithmetic assumed Libre Caslon Text had
no bold italic. It does — `ofl/librecaslontext/LibreCaslonText-Italic[wght].ttf` covers 400–700 —
so the family ships all four. Two families ship fewer faces than four for reasons that are upstream
facts rather than choices: **Libre Baskerville** has no bold italic, and **Marcellus** has only one
weight. Display families ship regular and bold only (a masthead needs no italic) and accent families
ship one face each.

Total on disk: **4.4 MB** for 59 faces, of which ~1.85 MB is the three frozen families' unsubsetted
bytes and ~2.55 MB is the 52 new subsetted faces.

## 2. The three founding families are frozen

`Source Serif 4`, `Source Sans 3` and `Cinzel` keep their exact upstream bytes. Subsetting renumbers
glyph ids, `WidgetDrawListDump` records glyph ids and font keys, and re-subsetting those three would
have re-baked all 42 snapshot baselines and every widget golden to save about 1.2 MB. `frozen: true`
in the manifest says so, and `FontManifestTests.FrozenFacesAreNeverSubset` enforces that a frozen
face has no subset spec.

That freeze is what made this milestone additive. **Every one of the 788 tests that existed before
M14 passes unchanged; no baseline moved.**

## 3. The build script, and the two details that make it honest

`assets-src/fonts/tools/build-fonts.py` (with a pinned `requirements.txt`: `fonttools==4.60.1`)
runs fetch → `varLib.instancer` → `pyftsubset` → verify, per face. Two details are load-bearing:

- **The retained layout features are exactly what `HarfBuzzShaper` can turn on**:
  `ccmp,locl,mark,mkmk,rlig,liga,kern,calt`. The shaper explicitly disables `dlig` and `hlig`, so
  retaining those would be dead weight in every file.
- **`head.created`/`head.modified` are pinned to a fixed epoch, with `recalcTimestamp=False`.**
  This one bit less than obvious than it looks: setting the fields is not enough, because
  `table__h_e_a_d.compile()` overwrites `modified` with the wall clock on save unless the font was
  opened with `recalcTimestamp=False`. Without both, no two runs of the script agree byte-for-byte
  and every hash in the manifest is theatre. This was found by running the script twice and
  diffing — the first attempt produced 52 different files on the second run.

`gen-fonts-manifest.py` and `gen-font-catalog.py` sit beside it. They emitted the 59 manifest rows
and the C# catalog once, mechanically, rather than by hand; both outputs are ordinary checked-in
files after that. **Re-running `gen-fonts-manifest.py` blanks the SHA-256 fields**, so it is for
re-shaping the schema only — its docstring says so.

## 4. CI does not run Python, so a managed test carries the guarantee

`tests/Layout.Tests/FontManifestTests` reads `fonts.json` off disk and checks: every entry's file
hashes correctly; every `.ttf` in the tree is listed (an orphan is installer weight nobody accounted
for); every filename is globally unique; every family's licence file exists and is non-empty; frozen
faces are never subset; and both generated documents match what the manifest says they should be.

`tests/Layout.Tests/FontCatalogTests` closes the loop on the other side: set-equality **in both
directions** between the assembly's embedded resources and `BundledFontCatalog`, every family has a
Regular/Normal face, and no face exceeds 65,535 glyphs — `HarfBuzzShaper` casts glyph ids to
`ushort`, so a larger face would wrap silently.

`FontStoreTests.BundledFaces` is now `MemberData` from the catalog rather than a literal
`TheoryData`, which makes it a real 59-face parse gate across both HarfBuzz and Skia at zero
hand-maintenance.

## 5. The licence gap this milestone closed

The OFL requires the licence text to accompany the fonts *in any distribution*. Before M14 the
`OFL.txt` files lived in the repo only — **they were not in the installer, though the fonts were.**

M14 generates `assets-src/fonts/THIRD-PARTY-FONTS.txt` (all 20 licences, each under a heading naming
the family, the designer and the pinned upstream), embeds it in `TrestleBoard.Layout` beside the
faces, and surfaces it from **Help → Fonts and licences**. `docs/FONTS.md` is generated beside it.
`FontManifestTests` recomposes both from the manifest and fails if either has drifted.

One wrinkle worth recording: `ofl/ptserif/OFL.txt` upstream carries a UTF-8 BOM. Python's
`read_text(encoding="utf-8")` keeps it as a `U+FEFF` character while .NET's `File.ReadAllText`
strips it, so the two implementations of the composition rule disagreed by exactly one invisible
character. The script now reads licences as `utf-8-sig`.

## 6. Registration

The seven hand-written `<EmbeddedResource>` items in `TrestleBoard.Layout.csproj` are now one
globbed item that still writes `LogicalName` explicitly, so `FontStore.LoadResource`'s contract is
untouched. Its one hidden coupling — globally unique filenames across family folders — is exactly
what `FontManifestTests` enforces.

`FontStore.Register` gained a lazy overload. `CreateDefaultStore` used to materialise every face
into `_bytes`; at 59 faces that is ~4 MB of managed arrays at startup to serve the three or four a
newsletter actually uses. The eager overload stays for tests.

`BundledFonts` keeps `BodyFamily`/`SansFamily`/`DisplayFamily` and `CreateDefaultStore()`, now
delegating to `BundledFontCatalog` — **every existing call site is untouched**, and there are many.

*Rejected, as the plan directed: a source generator emitting the catalog from `fonts.json`. It would
trade ~60 lines of hand-maintenance for a Roslyn project, a build-order dependency and a debugging
failure mode nobody on this project has today.*

## 7. The sibling trap

`CharacterStyleResolver.TryResolve` falls back to an attribute scan matching same family + same size
+ same colour. Change `body` and leave `body-bold` behind and Ctrl+B silently stops finding its pair
and starts minting duplicates through `EnsureCharacterStyleCommand`.

`SetCharacterStyleFontCommand(baseStyleName, fontFamily?, sizePt?)` resolves the group as **every
style whose `BaseName(name) == baseStyleName`**, snapshots `(Name, Family, SizePt)` for each, and
writes the new values across all of them. One command rather than a `CompositeCommand` of per-style
edits, because atomicity is the whole point. `TryMerge` returns false.

Group membership is **by name, never by attribute scan** — the scan is the thing being protected —
and `BaseName` composes with the `~` override convention for free, so changing `body` correctly
leaves `body~ebgaramond` alone.

The regression test was written before the command: after
`SetCharacterStyleFontCommand("body", "Lora", null)`, resolving `body` at Bold/Normal still returns
`body-bold` **and** that definition's family is `"Lora"`.

## 8. The override creates a derived style

"Runs never carry direct formatting in v1" is locked in `CharacterStyleResolver`'s header and
`docs/M4-spec.md`. The override does not break it. `StyleOverrides.NameFor` mints
`{base}~{family-slug}` (plus `-{size}` when the size differs) and it is applied **by reference** —
the same machinery bold and italic already use, so no new command type was needed, just
`EnsureCharacterStyleCommand` + `ApplyCharacterStyleCommand`.

The `~` separator is the load-bearing detail. `BaseName` strips only `-bold`/`-italic`, so
`body~ebgaramond-bold` bases to `body~ebgaramond`: the sibling machinery keeps working *inside* an
override, which is what you need when the user bolds a word in an overridden span. And because the
resolver's attribute scan matches on font family, the override group cannot cross-match the base
group. Both properties fall out of the existing convention for free, and both have tests.

**Three ways the user can tell text is overridden, all built:**

1. The action panel says "This text uses Lora instead of the Body text font", with a button to put
   it back.
2. **View → Show where fonts were changed** (off by default) draws a light underline — an editor
   overlay beside `TextOverlayRenderer`'s caret and selection, **never through `PageRenderer`**, or
   it would print into the PDF.
3. The styles window's footer says "N pieces of text use a different font", with *Show me* and
   *Put them all back*.

Never a coloured squiggle: §6 bans colour as the only carrier of meaning, so the mark is a line.

## 9. The UI

**Format → "Fonts and text styles…"** is the primary path, keeping §6's every-command-has-a-menu-item
guarantee. The action panel's Text group carries the same commands beside the writing they act on.
**Nothing new in the toolbar** — M11 trimmed it to nine controls and it stays there.

**Deviation from the plan: the shortcut is `Ctrl+Shift+D`, not `Ctrl+Shift+T`.** M11 already gave
`Ctrl+Shift+T` to "add a text frame", and `KeyboardAuditTests` is right to refuse a promise the app
cannot keep. Bigger and Smaller took `Ctrl+Shift+.` and `Ctrl+Shift+,`, following the convention
most word processors use.

`TextStylesWindow` is deliberately **not** a general style editor — that is a scope trap. It is a
font-and-size assignment sheet. Left: the document's style *roles*, one big row each, with a live
sample rendered in its actual font. Right: Font and Size, and nothing else. Bottom: Apply / Cancel
under *"Nothing changes until you press Apply"*, in M12's import-wizard voice.

**Roles, never raw style names.** Only base styles are listed, labelled through
`src/TrestleBoard.Core/Text/StyleLabels.cs` (`body` → "Body text", `table` → "Tables", `display` →
"Cover title") with a `"Style: {name}"` fallback. No model change, therefore no migration — which is
exactly why a lookup beats adding a `DisplayName` field to `CharacterStyleDef`.

The font list is a `ListBox`, not a ComboBox: rows ≥56px, grouped under plain headers, each row
showing the family name **in its own face**, a sample line, and its one-sentence description. A 24pt
search box above it speaks its result count.

**Previews are rendered by our own engine, not Avalonia.** Avalonia's `EmbeddedFontCollection` wants
`avares://` resources and the fonts are `EmbeddedResource`s in Layout, so supporting both would ship
the bytes twice. `FontPreviewRenderer` in `TrestleBoard.Rendering` takes `(FontKey, sizePt, text,
foreground, background, scale)` and returns a PNG, with theme colours as **parameters** rather than
statics. It uses the same deterministic `SKFont` settings as `PageRenderer`, so the preview is the
printed shape and not an approximation of it.

**The reflow warning is not optional.** Line height comes from font metrics and the minimum wrap
segment is `4 × AverageCharWidthPt` of the line's primary font, so a font or size change can move
pagination. Apply announces the scope first, and afterwards the status bar says so if the page count
moved. Ctrl+Z restores it in one step.

**Font size ships here.** A `[− Smaller] 11 pt [+ Bigger]` stepper walks the fixed ladder in
`Core/Text/FontSizeLadder.cs` (6 … 72) so the buttons cannot produce 11.3pt, plus Format → "Make
text bigger" / "Make text smaller". It changes the **style**, not just the highlighted words,
because the sibling trap is identical for size and building it later would have meant writing the
same avoidance twice.

## 10. A document naming an unbundled font

- **Explicit, opt-in substitution on `FontStore`**: exact key → same family with the slant dropped →
  same family with the weight dropped → plain → only if a substitute family is *configured*, that
  family's nearest face → otherwise today's exception with its message unchanged. **Engine default:
  no substitute**, so fixtures, snapshots, widget goldens and PDF export keep M7's intended loud
  failure verbatim. **App default: substitute to the body family** — the app is the one place where
  "the newsletter must still open" outranks "fail loudly".
- **Detected at open, not at paint.** `Core/Text/DocumentFontAudit.cs` is BCL-only and takes the
  known-family set **as a parameter**, mirroring §5's roster projection rule, so Core never learns
  about Layout. `ShowPackage` calls it before the first paint and warns in the status bar — not a
  modal, because the newsletter is perfectly usable and a dialog would be a bigger interruption than
  the problem deserves.
- **The document is never rewritten.** The unknown family stays in `styles.json`, so a round trip
  through this build is lossless.
- **`CurrentMinReaderVersion` stays `"1.0.0"`.** Bumping it would make such documents *refuse to
  open*, which is strictly worse than substituting.
- Risk, stated plainly: a substituted document paginates differently from the machine that made it.
  That is what the warning's last sentence is for. No attempt is made at metric-compatible
  substitution.

## 11. The paragraph-style combo

The combo was never broken — **the documents were empty of styles.** `AddStandardStyles` defined
four character styles and *one* paragraph style.

`Core/Templates/StandardStyles.cs` is now the single table, shared by the shipped templates and both
sample documents, and it carries `body`, `heading`, `subheading`, `caption` and `quote` as real
paragraph styles with matching character styles. Body's `-bold`, `-italic` and `-bold-italic`
siblings are present **by name**, so the resolver finds them by name lookup rather than falling
through to its attribute scan.

The three call sites had drifted to three slightly different sets of numbers. Those differences are
preserved through `StandardStyleMetrics` rather than flattened — flattening them would have moved
pixels. Adding styles nothing uses changes no pixels either, and no baseline moved.

*Known wart, inherited rather than introduced:* `quote` is serif italic at body size, which is
attribute-identical to `body-italic`. Resolving `quote` to bold-italic therefore lands on
`body-bold-italic` through the attribute scan. Two attribute-identical roles were **already**
conflated by that scan before M14; a `StyleSheetDiagnostics` reporting them was the plan's first
thing to cut, and it stays cut.

## 12. Baselines and test churn

`SnapshotInfra.AlignmentSampler` is untouched. A **new** `font-catalog-sampler` fixture renders one
line per family at 12pt.

**Deviation from the plan, since closed — see §15.** The plan asked for three new baselines. At the
time only the Windows one could be produced here — this machine is Windows, and
Skia rasterises glyphs through the platform scaler backend, so a Linux or macOS baseline has to be
generated on Linux or macOS. Rather than commit a fixture that would fail CI on two of three
runners, `FontCatalogSnapshotTests` puts the real guarantee in assertions that need no baseline and
therefore run everywhere:

- `EveryFaceCoversItsSampleLine` — all 59 faces, no `.notdef` for a plain ASCII line. This is the
  single most likely way a bad instancer or `pyftsubset` run would reach users.
- `EveryFacePutsInkOnThePage` — all 59 faces rasterise to actual pixels.
- `TheSamplerRendersEveryFamilyOnItsOwnLine` — 20 distinct families, not overset.
- `FontCatalogSamplerMatchesBaseline` — compares where a baseline for this OS exists, and **skips
  with an explicit reason** where one does not.

All three baselines exist as of 2026-07-27, so that last test now compares on every runner. The
four assertions stay as they are: the three that need no baseline are the ones that keep working on
a machine nobody has baked for yet, which is the situation any new OS starts in.

`PdfParityTests` no longer hard-codes `Assert.Contains("SourceSerif4", …)`. It derives the expected
families and the expected row count from the fixture's own faces, which keeps the original intent,
survives a fixture change, and — now that substitution exists — gains a genuinely new guarantee: a
silent substitution anywhere in the export path fails the test. The `emb=yes sub=yes` assertion is
unchanged.

## 13. Migration

**None.** `MigrationRunner.Chain` stays empty, `CurrentFormatVersion` stays `"1.0.0"`, and
`CurrentMinReaderVersion` stays `"1.0.0"`. Everything here is either a value change to existing
required fields or extra entries in `CharacterStyles`.

`Theme.FontTokens` (declared, unused) is left alone. Wiring the picker through it was explicitly
rejected: a token indirection buys nothing for a four-page newsletter and makes the sibling problem
strictly worse by adding a second place a family name can live.

---

## 14. Open items

1. ~~**`WidgetStyleDefaults.Small` is still serif italic.**~~ **Closed 2026-07-27.** It is sans
   italic now, in its own commit whose entire diff beyond two lines is the re-bake of
   `widgets-gallery-page1`, `issue-page3` and `issue-page4` — Windows locally, Linux in the
   container, macOS from a bake run (§15). Each operating system moved exactly those three files
   and nothing else.
2. ~~**`font-catalog-sampler` has no Linux or macOS baseline.**~~ **Closed 2026-07-27** by §15: the
   Linux baseline was baked in the container and the macOS one promoted from a bake run. All three
   exist, so `FontCatalogSamplerMatchesBaseline` compares everywhere and skips nowhere. The per-face
   assertions that carried the guarantee meanwhile are unchanged.
3. **A keyboard-only run of the font picker by a person** is section 14 of
   `docs/accessibility-test-script.md`. The grouped `ListBox` with image-bearing rows is a shape
   nobody in this project has listened to with a screen reader yet — in particular whether the group
   headings are announced at all.
4. **The `quote` / `body-italic` attribute collision** described in §11. Pre-existing, exposed
   rather than caused, and cheap to fix later by giving `quote` a distinguishing size.

---

## 15. Baking a baseline for an operating system you do not own (2026-07-27)

§12 called the missing Linux and macOS baselines a hardware problem. Two thirds of it was not.

**Linux, in a container.** `mcr.microsoft.com/dotnet/sdk:10.0` is Ubuntu 24.04, which is what
`ubuntu-latest` is. Cloning the repo inside it and running the snapshot suite with
`TRESTLEBOARD_UPDATE_BASELINES=1` reproduced **all 14 committed Linux baselines byte-for-byte** and
wrote the one that was missing. That reproduction is the whole argument: it says the container's
FreeType rasterisation is the same rasterisation the committed baselines came from, so the new file
is a CI-truthful baseline rather than a plausible-looking one. Bind-mount the repo read-only and
clone it inside, so a Windows `obj/` never reaches the Linux build:

```
docker run --rm -v C:\code\TrestleBoard:/src:ro -v <out>:/out \
  -e TRESTLEBOARD_UPDATE_BASELINES=1 mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -c "git clone -q /src /work && cd /work && \
           dotnet test tests/Rendering.SnapshotTests/Rendering.SnapshotTests.csproj -c Release && \
           git status --porcelain && cp -r tests/Rendering.SnapshotTests/Baselines/linux /out/"
```

The `git status --porcelain` line is not decoration — it is the check. Anything other than the one
expected file means the container does not match CI and nothing from it should be promoted.

**macOS, on a runner.** There is no container trick for CoreText. `.github/workflows/bake-baselines.yml`
is `workflow_dispatch`-only, takes the runner as an input, regenerates, prints what moved and uploads
`tests/Rendering.SnapshotTests/Baselines` as an artifact. It **commits nothing** — a maintainer
promotes the file. Dispatch it on the branch that carries the change being re-baked, or it bakes the
old pixels.

```
gh workflow run bake-baselines.yml --ref <branch> -f os=macos-latest
gh run download <id> -n baselines-macos-latest -D <out>
```

The macOS bake reported the same single untracked file the container did, so the same reproduction
argument holds there: 14 of 15 came back byte-identical to what was already committed.

This is the same "promote the CI artifact" idiom the snapshot failure messages have always
suggested; it just has a job behind it now.
