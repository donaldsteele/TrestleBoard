# TrestleBoard M1 — Layout Engine Spike: Implementation Spec

Status: derived from the LOCKED PLAN.md (§1, §3, §8, §9, §11-M1). This document translates §3 into a precise, implementable API contract. It does not re-open any locked decision; where §9's prose and the dependency rules collide (font store placement), it resolves the tension explicitly and documents the one deviation.

> Authored by the M1 Plan-agent pass (2026-07-25); §4 version table updated with the
> nuget.org-verified pins made at implementation time.

Repo facts this spec builds on (verified in-tree):
- `Directory.Build.props`: `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `Deterministic=true`, `InvariantGlobalization=false`.
- `.editorconfig`: file-scoped namespaces are an **error**; private fields `_camelCase`; `var` only when apparent; braces required.
- Central package management in `Directory.Packages.props`; xunit.v3 `3.2.2`.
- `TrestleBoard.Layout` references Core only. `Rendering → Layout, Core`. `Export.Pdf → Rendering`. `Rendering.SnapshotTests → Rendering` (Export.Pdf reference added in M1).

Global constraints honored throughout: coordinates in typographic points (float); no hyphenation, no justification; greedy first-fit only; framework-independent (SkiaSharp/HarfBuzzSharp allowed in Layout/Rendering, **no Avalonia anywhere in these projects**).

---

## 0. Namespace & project map (what lands where)

| Namespace | Project | Contents |
|---|---|---|
| `TrestleBoard.Layout.Input` | Layout | Minimal input model (`LayoutStory`, styles, `FrameRect`, `ExclusionRect`, `LayoutRequest`) |
| `TrestleBoard.Layout.Fonts` | Layout | `FontStore`, `ResolvedFont`, `FontKey`, `BundledFonts`, embedded OFL bytes |
| `TrestleBoard.Layout.Shaping` | Layout | `HarfBuzzShaper`, `ShapedRun` |
| `TrestleBoard.Layout.Breaking` | Layout | `LineBreakAnalyzer`, `BreakOpportunity` |
| `TrestleBoard.Layout` | Layout | `TextLayoutEngine`, output model (`LayoutResult`, `LineBox`, `PositionedGlyphRun`, …) |
| `TrestleBoard.Rendering` | Rendering | `PageRenderer`, `PngRenderer` |
| `TrestleBoard.Export.Pdf` | Export.Pdf | `PdfExporter`, `PdfMetadata`, `PdfPage` |

### Decision: input model lives in **Layout**, not Core

Core references BCL only and (M2) will own the *authoring* model — `Story`/`StyleSheet`/`Theme` with `styleRef`s, override bags, and theme tokens. The Layout input types are a different thing: the **fully-resolved typographic contract** (concrete family/weight/size in points, no refs, no tokens). Putting them in Core would force Core to encode typography-resolution concepts it should not know about, and Core cannot reference SkiaSharp/HarfBuzz anyway.

Therefore the resolved input model lives in `TrestleBoard.Layout.Input`. In M2, a thin flattener (`Core.Story + StyleSheet + Theme → Layout.Input.LayoutStory`, resolving refs + tokens) is added — it is a pure mapping with no rework, because M1 already designs the input types as the *resolved* shape M2 must produce. Geometry (`FrameRect`, `ExclusionRect`) uses plain `readonly record struct`s of `float` points (not `SKRect`), so the input contract stays framework-neutral for M2/Widgets and gives clean value-equality for golden tests.

### Decision: font store lives in **Layout** (reconciling §9)

§9 says "Rendering owns bundled font store," but shaping (in Layout) needs the HarfBuzz `Font`, and Rendering needs the `SKTypeface` — and Rendering references Layout, not the reverse. The font store must therefore be defined in the **lowest** project that consumes fonts, which is Layout. Also, `Layout.Tests` (golden line-box tests) must shape text without referencing Rendering, so the font **bytes must be embedded in Layout**.

Resolution (the one documented deviation from §9 prose): **Layout owns the `FontStore` type and the embedded OFL bytes; Rendering reuses them transitively.** One `ResolvedFont` object owns *both* the `SKTypeface` and the HarfBuzz `Font/Face` built from the *same* byte buffer — this is what guarantees the shaper and the renderer agree glyph-for-glyph with zero re-shaping. §9's intent ("the app bundles its own fonts, never system fonts") is fully preserved.

---

## 1. `TrestleBoard.Layout` public API

### 1.1 Input model (`TrestleBoard.Layout.Input`)

```csharp
namespace TrestleBoard.Layout.Input;

public enum TextAlign { Left, Center, Right }

public readonly record struct FrameRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
}

/// <summary>A rectangle text must avoid. Already in page points; caller pre-supplies WrapMargin.</summary>
public readonly record struct ExclusionRect(FrameRect Rect, float WrapMargin, int ZOrder);

public readonly record struct CharacterStyle(
    string FontFamily,
    FontWeight Weight,        // TrestleBoard.Layout.Fonts.FontWeight
    FontStyleSlant Slant,     // TrestleBoard.Layout.Fonts.FontStyleSlant
    float SizePt,
    uint ColorArgb);          // 0xAARRGGBB; keeps SkiaSharp out of the input contract

public readonly record struct ParagraphStyle(
    float LineSpacing,        // multiple, e.g. 1.15f
    float SpaceBeforePt,
    float SpaceAfterPt,
    float FirstLineIndentPt,
    TextAlign Align,
    CharacterStyle DefaultRun);

public sealed record LayoutRun(string Text, CharacterStyle Style);
public sealed record LayoutParagraph(ParagraphStyle Style, IReadOnlyList<LayoutRun> Runs);
public sealed record LayoutStory(string StoryId, IReadOnlyList<LayoutParagraph> Paragraphs);

/// <summary>One frame in a story's frame chain. ColumnCount is 1 in M1 (multi-column deferred).</summary>
public sealed record LayoutFrame(FrameRect Rect, IReadOnlyList<ExclusionRect> Exclusions, int ColumnCount = 1);

public sealed record LayoutRequest(LayoutStory Story, IReadOnlyList<LayoutFrame> Frames);
```

Notes:
- `CharacterStyle.FontFamily/Weight/Slant` map 1:1 onto `FontKey` (below). `ColorArgb` is a plain `uint` so the input tree is Skia-free.
- `ExclusionRect.ZOrder` and `WrapMargin` mirror the M2 `Block.wrapMode=Rectangle`/`wrapMargin`/`zOrder` semantics so M2 maps directly. The engine applies the inflation (subtracts `Rect` grown by `WrapMargin` on all sides) — see §1.6.

### 1.2 Font store (`TrestleBoard.Layout.Fonts`)

```csharp
namespace TrestleBoard.Layout.Fonts;

public enum FontWeight { Regular = 400, Bold = 700 }   // extend later; M1 needs these two
public enum FontStyleSlant { Normal, Italic }

public readonly record struct FontKey(string Family, FontWeight Weight, FontStyleSlant Slant);

public readonly record struct FontMetrics(
    float AscentPt,          // positive magnitude above baseline
    float DescentPt,         // positive magnitude below baseline
    float LeadingPt,         // >= 0
    float AverageCharWidthPt);

/// <summary>Owns the SKTypeface and the HarfBuzz Font/Face built from the SAME bytes.</summary>
public sealed class ResolvedFont : IDisposable
{
    public FontKey Key { get; }
    public int UnitsPerEm { get; }
    public SkiaSharp.SKTypeface Typeface { get; }        // for drawing
    public HarfBuzzSharp.Font HbFont { get; }            // scaled at HarfBuzzShaper.HbScale
    public HarfBuzzSharp.Face HbFace { get; }

    /// <summary>Deterministic metrics at a point size (from SKFontMetrics + a reference-glyph advance).</summary>
    public FontMetrics GetMetrics(float sizePt);
    public void Dispose();
}

public sealed class FontStore : IDisposable
{
    public void Register(FontKey key, ReadOnlyMemory<byte> fontBytes);  // copies/holds bytes; builds lazily
    public ResolvedFont Resolve(FontKey key);                           // throws KeyNotFoundException if absent (no system fallback in M1)
    public bool TryResolve(FontKey key, out ResolvedFont? font);
    public void Dispose();
}

public static class BundledFonts
{
    public const string BodyFamily    = "Source Serif 4";
    public const string SansFamily    = "Source Sans 3";
    public const string DisplayFamily = "Cinzel";

    /// <summary>Loads the embedded OFL faces into a new store.</summary>
    public static FontStore CreateDefaultStore();
}
```

Implementation constraints:
- `Register` holds the byte buffer; `ResolvedFont` builds the `SKTypeface` via `SKTypeface.FromStream` over the embedded bytes and the HarfBuzz side via `Blob → Face → Font`, then `HbFont.SetScale(HbScale, HbScale)` once (see §1.4). **Never** touch system fonts.
- `GetMetrics`: ascent/descent/leading from `SKFont(typeface, sizePt).Metrics` (Skia returns ascent negative; store positive magnitudes). `AverageCharWidthPt` = advance (points) of the `'n'` glyph at `sizePt`, derived from HarfBuzz (deterministic), used only by the min-segment-width rule.
- Native handles are created once and cached per `ResolvedFont`; `FontStore` owns their lifetime and disposes them.

### 1.3 Bundled OFL fonts for M1

M1 required set (embed as `EmbeddedResource` in `Layout/Assets/Fonts/`, sources committed under `assets-src/fonts/<family>/` with each family's license):

- **Required:** Source Serif 4 — `SourceSerif4-Regular.ttf`, `SourceSerif4-Bold.ttf`, `SourceSerif4-It.ttf` (+ `BoldIt` staged). The M1 body font.
- **Recommended (multi-family resolution):** `SourceSans3-Regular.ttf`, `SourceSans3-Bold.ttf`, `Cinzel-Regular.ttf`.

Use **static** instance files, not variable fonts (Cinzel is instanced to static wght=400 via `fonttools varLib.instancer`). Fonts are OFL-licensed and freely redistributable; committing them does **not** violate §0 (no personal data). Family choice is not PLAN-locked; final typography is an M9 concern.

### 1.4 Shaping layer (`TrestleBoard.Layout.Shaping`)

```csharp
namespace TrestleBoard.Layout.Shaping;

public readonly record struct ShapedGlyph(
    ushort GlyphId,
    int Cluster,       // source char index into the shaped substring's origin text
    float XAdvancePt,
    float XOffsetPt,
    float YOffsetPt);

public sealed class ShapedRun
{
    public ResolvedFont Font { get; }
    public float SizePt { get; }
    public uint ColorArgb { get; }
    public string Text { get; }            // the run's text
    public int ParagraphCharStart { get; } // offset of this run within the paragraph's concatenated text
    public IReadOnlyList<ShapedGlyph> Glyphs { get; } // in visual order (LTR)
}

public readonly record struct ShapeOptions(bool Ligatures, bool Kerning);

public sealed class HarfBuzzShaper
{
    public const int HbScale = 512;

    public ShapedRun Shape(ResolvedFont font, float sizePt, uint colorArgb,
                           string paragraphText, int runStart, int runLength, ShapeOptions options);
}
```

HarfBuzz usage (exact, for determinism):
1. `ResolvedFont.HbFont` has `SetScale(HbScale, HbScale)` applied once at construction; HarfBuzz returns integer advances/offsets in `HbScale` units.
2. Per shape: `Buffer.AddUtf16(...)`, then set segment properties **explicitly** (never `GuessSegmentProperties`): `Direction = LeftToRight`, `Script = Latin`, `Language = new Language("en")`.
3. Features: `liga`/`kern` on when requested; `dlig=0`, `hlig=0` always, so defaults can't drift.
4. Convert with `f = sizePt / HbScale`: `XAdvancePt = pos.XAdvance * f`, etc. `Cluster = info.Cluster + runStart` (paragraph-relative).
5. HarfBuzz core is integer/table math → bit-identical output cross-OS given pinned native version + same bytes; the single float multiply is IEEE-deterministic.

Itemization (M1): style runs → shaping runs is 1:1. Architected so a future itemizer can sub-split on script/bidi boundaries without changing `ShapedRun`.

### 1.5 Break opportunities — UAX#14-lite (`TrestleBoard.Layout.Breaking`)

```csharp
namespace TrestleBoard.Layout.Breaking;

public enum BreakKind { Allowed, Mandatory }

public readonly record struct BreakOpportunity(int TextIndex, BreakKind Kind, bool KeepHyphenBefore);

public static class LineBreakAnalyzer
{
    public static IReadOnlyList<BreakOpportunity> Analyze(string paragraphText);
}
```

Exact rule list (v1, no hyphenation, no dictionary):

1. **Mandatory break after** any of: `U+000A` LF, `U+000D` CR (CRLF treated as one), `U+000C` FF, `U+2028` LS, `U+2029` PS.
2. **Allowed break after a whitespace run**: after a maximal run of breakable spaces (`U+0020`, TAB, `U+1680`, other `Zs`) — **except** `U+00A0` NBSP and `U+202F` NNBSP, which never break. Trailing whitespace up to the break belongs to the upper line and is **excluded from fit-width measurement** (it "hangs").
3. **Allowed break after a hyphen/dash**: `U+002D`, `U+2010`, `U+2012`, `U+2013`, `U+2014`. `KeepHyphenBefore=true`. Excluded: `U+2011` non-breaking hyphen, `U+00AD` soft hyphen (ignored).
4. **Allowed break after closing punctuation** `)` `]` `}` and after `/` — only when the next char is neither whitespace nor another closing mark. Deliberately narrow. Sentence punctuation breaks only via the following space.
5. **Never** break before opening punctuation, before a combining mark, or **inside a HarfBuzz cluster** — the breaker snaps every opportunity forward to the next cluster boundary (ligature safety).

Break segments ("word clusters") = glyphs between consecutive opportunities; each records total advance (placement) and fit advance (total minus trailing whitespace, for fit tests).

### 1.6 LineBreaker + exclusion segments (`TrestleBoard.Layout`)

```csharp
namespace TrestleBoard.Layout;

public sealed record LayoutOptions
{
    public float MinSegmentAvgCharMultiple { get; init; } = 4f; // §3 "~4 average char widths"
    public bool EnableStandardLigatures { get; init; } = true;
    public bool EnableKerning { get; init; } = true;
}

public sealed class TextLayoutEngine
{
    public TextLayoutEngine(Fonts.FontStore fonts, LayoutOptions? options = null);
    public LayoutResult Layout(Input.LayoutRequest request);
}
```

**Per-band segment computation** (the §3 algorithm). Given a candidate line band `[yTop, yBottom]` inside frame `F` with exclusions `X`:

1. Start with interval list `[{ F.Left, F.Right }]`.
2. For each exclusion `e ∈ X`, inflate `e.Rect` by `e.WrapMargin` on all sides → `r`. Include `e` only if `r.Top < yBottom && r.Bottom > yTop`.
3. Subtract each overlapping `[r.Left, r.Right]` from every interval (0, 1, or 2 sub-intervals each).
4. **Discard** intervals narrower than `MinSegmentAvgCharMultiple × avgCharWidth` (primary font of the line, at size). Kills one-word slivers.
5. If **no** interval survives → band blocked; advance candidate top by one `lineHeight` and retry. Stop when band bottom would exceed `F.Bottom` → frame full (spill, §1.8).

**Line height & band derivation:** provisional metrics = paragraph-max over runs: `lineHeight = LineSpacing × (maxAscent + maxDescent + maxLeading)`; `baselineY = bandTop + maxAscent`. If a placed run exceeds the provisional max (mixed sizes), recompute band/segments once and re-fill (bounded single recompute; no-op for M1's uniform fixtures).

**Greedy first-fit within segments:** fill segments in x-order; pack word clusters while `placedFitWidth + next.FitAdvance ≤ segment.Width` (first-line indent insets segment left on paragraph line 1). Words never split; a single word wider than the whole segment is placed anyway (overflows right edge) and the line ends. `Mandatory` ends the line immediately.

**Alignment & positioning** (per segment): `contentWidth` = placed advances minus trailing whitespace. Left/Right/Center per standard math; multi-segment lines align each segment's content independently. Shared baseline per line; per-glyph `YOffsetPt` added. `SpaceBeforePt` above paragraph's first band, `SpaceAfterPt` after its last.

Complexity `O(lines × exclusions)` per §3.

### 1.7 Output model (`TrestleBoard.Layout`)

```csharp
namespace TrestleBoard.Layout;

public readonly record struct FloatInterval(float Left, float Right) { public float Width => Right - Left; }

/// <summary>Maps back to source text. Char indices are paragraph-relative.</summary>
public readonly record struct SourceSpan(string StoryId, int ParagraphIndex, int StartChar, int EndChar);

public sealed class PositionedGlyphRun
{
    public Fonts.ResolvedFont Font { get; }
    public float SizePt { get; }
    public uint ColorArgb { get; }
    public float OriginX { get; }                          // page pt
    public float BaselineY { get; }                        // page pt
    public IReadOnlyList<ushort> Glyphs { get; }
    public IReadOnlyList<SkiaSharp.SKPoint> GlyphOffsets { get; } // relative to (OriginX, BaselineY), y-down
    public IReadOnlyList<int> Clusters { get; }            // paragraph-relative source char index per glyph
    public SourceSpan Source { get; }
    public float AdvanceWidthPt { get; }
}

public sealed class LineSegment
{
    public FloatInterval XRange { get; }
    public Input.TextAlign Align { get; }
    public IReadOnlyList<PositionedGlyphRun> Runs { get; }
}

public sealed class LineBox
{
    public int ParagraphIndex { get; }
    public bool IsParagraphStart { get; }
    public float BaselineY { get; }
    public float BandTop { get; }
    public float BandBottom { get; }
    public float LineHeight { get; }
    public float MaxAscentPt { get; }
    public float MaxDescentPt { get; }
    public IReadOnlyList<LineSegment> Segments { get; }
    public SourceSpan Source { get; }
}

public sealed class FrameLayout
{
    public int FrameIndex { get; }
    public Input.FrameRect Frame { get; }
    public IReadOnlyList<LineBox> Lines { get; }
}

public readonly record struct OversetInfo(int LastFrameIndex, int ParagraphIndex, int CharIndex);

public sealed class LayoutResult
{
    public IReadOnlyList<FrameLayout> Frames { get; }
    public bool IsOverset { get; }
    public OversetInfo? Overflow { get; }
}
```

`GlyphOffsets` are run-relative so the renderer builds one `SKTextBlob` per run and draws at `(OriginX, BaselineY)` — zero re-shaping. `Clusters` + `Source` give M4 a monotonic glyph→source map.

### 1.8 Frame spill / overflow

Frames fill in chain order; a band that would pass `Frame.Bottom` with story remaining continues at the same story position in the next frame. Story text remaining after the last frame → `IsOverset = true`, `Overflow = OversetInfo(lastFrameIndex, paragraphIndex, charIndexInParagraph)`.

---

## 2. `TrestleBoard.Rendering`

```csharp
namespace TrestleBoard.Rendering;

public sealed class PageRenderer
{
    public PageRenderer(PageRenderOptions? options = null);
    public void Render(SkiaSharp.SKCanvas canvas, TrestleBoard.Layout.LayoutResult result);
    public void RenderFrame(SkiaSharp.SKCanvas canvas, TrestleBoard.Layout.FrameLayout frame);
}

public sealed record PageRenderOptions
{
    public uint BackgroundArgb { get; init; } = 0xFFFFFFFF;
    public bool DrawExclusionDebug { get; init; } = false; // dev overlay; off for snapshots
}

public static class PngRenderer
{
    public static byte[] RenderToPng(
        TrestleBoard.Layout.LayoutResult result,
        int pageWidthPt, int pageHeightPt,
        float scale = 1f,
        uint backgroundArgb = 0xFFFFFFFF,
        PageRenderOptions? options = null);
}
```

Drawing: per `PositionedGlyphRun`, `SKTextBlob.CreatePositioned(glyphs, offsets, skFont)` → `canvas.DrawText(blob, OriginX, BaselineY, paint)`. No `MeasureText`, no shaping. Deterministic `SKFont`/`SKPaint` config in §5. `PngRenderer` is the only rendering path the tests use.

`Rendering.SnapshotTests.csproj` gains a `ProjectReference` to `Export.Pdf` for the parity test.

---

## 3. `TrestleBoard.Export.Pdf`

```csharp
namespace TrestleBoard.Export.Pdf;

public sealed record PdfMetadata(
    string Title, string Author, string Subject,
    string Creator = "TrestleBoard", string Producer = "TrestleBoard");

public sealed record PdfPage(int WidthPt, int HeightPt, TrestleBoard.Layout.LayoutResult Layout);

public sealed class PdfExporter
{
    public PdfExporter(TrestleBoard.Rendering.PageRenderer renderer);
    public void Export(System.IO.Stream output, IReadOnlyList<PdfPage> pages, PdfMetadata metadata);
}
```

`SKDocument.CreatePdf(SKManagedWStream, metadata)`; per page `BeginPage → renderer.Render → EndPage`; `Close()`. Fixed `RasterDpi = 300`. **Same `PageRenderer` path as screen/PNG → parity by construction.**

Text-embedding verification (M1, scripted where possible):
1. **Selectable text:** `pdftotext page.pdf -` returns the fixture text.
2. **Subset embedding:** `pdffonts page.pdf` shows `emb=yes`, `sub=yes` (tag like `ABCDEF+SourceSerif4`). Asserted on the Linux CI job (poppler-utils).
3. **Size sanity:** non-empty; the ~1.3–1.8 MB target is an M8 concern.

---

## 4. Package pinning (VERIFIED against nuget.org, 2026-07-25)

| Package | Version (verified) | Where referenced |
|---|---|---|
| `SkiaSharp` | `3.119.4` | Layout |
| `HarfBuzzSharp` | `8.3.1.5` | Layout |
| `SkiaSharp.NativeAssets.Linux.NoDependencies` | `3.119.4` | Layout |
| `HarfBuzzSharp.NativeAssets.Linux` | `8.3.1.5` | Layout |

Notes from verification:
- HarfBuzzSharp does **not** version in lockstep with SkiaSharp. The authoritative pairing comes from `SkiaSharp.HarfBuzz 3.119.4`, whose nuspec depends on exactly `HarfBuzzSharp 8.3.1.5` — that pair is pinned.
- SkiaSharp 4.x (4.150.x) exists on nuget.org but is untested against the plan-locked Avalonia 11.3.x line; we stay on 3.x (latest patch 3.119.4). Avalonia 11.3 supports SkiaSharp 3.x (compat added in 11.1.0-beta2, AvaloniaUI/Avalonia#15503) — the App-project version unification at M3 is expected to be safe, verified empirically then.
- `NoDependencies` Linux variant avoids the fontconfig dependency chain — we never use system fonts.
- `SkiaSharp.HarfBuzz` (managed `SKShaper`) is **not** referenced: we roll our own shaper for feature/scale/determinism control.

---

## 5. Determinism plan

Goal: **byte-identical rendered pixels across the 3-OS CI matrix**, reconciled with §8's tolerance policy.

1. **Fonts from stream, never system.** Same embedded bytes on every OS → same tables → same shaping and rasterization.
2. **Shaping is integer + one multiply.** Explicit `Direction/Script/Language`, fixed features, fixed `HbScale`; the only float is `advance × (sizePt/HbScale)`.
3. **Invariant culture.** No culture-sensitive APIs in the layout/render hot path; `CultureInfo.InvariantCulture` for any diagnostics formatting.
4. **Float end-to-end.** No double/float straddling; only `+ - * /` (IEEE-exact cross-OS); no transcendental `Math` calls.
5. **Baseline pattern.** Each line's `lineHeight` derived fresh from that line's font-table metrics; `candidateTop` advances by exact additions; `baselineY = bandTop + maxAscent` (fresh sum per line, no drifting accumulator). IEEE float addition is bit-identical cross-OS — the §3 "non-accumulative" guidance is about stability, satisfied by fresh-per-line computation.
6. **Deterministic raster surface & font settings.**
   - `SKImageInfo(wPx, hPx, Rgba8888, Premul, SKColorSpace.CreateSrgb())`, CPU raster only.
   - `SKFont`: `Edging = Antialias`, `Hinting = None`, `Subpixel = true` — identical on every draw.
   - `SKPaint`: `IsAntialias = true`; no LCD text.
   - Fixed integer render scale, frozen.

**Tolerance policy:** compare **decoded pixel buffers**, not PNG file bytes (encoders may differ in chunks/metadata). Default gate = strict `0/0` (true pixel identity). The comparer reports `maxAbsChannelDiff`/`diffPixelCount` and accepts optional tolerances as a documented-fallback mechanism only; on failure it writes actual/expected/diff PNGs for CI artifact upload.

**Empirical amendment (M1 CI, 2026-07-26):** cross-OS byte-identity of rendered pixels is
**not achievable** with SkiaSharp text blobs: Skia rasterizes glyphs through the platform
scaler backend (DirectWrite on Windows, CoreText on macOS, FreeType on Linux), so antialiased
glyph coverage differs on ~1–6% of pixels (max channel diff ≤139) even with identical font
bytes, layout, and surface settings. Layout itself IS cross-OS deterministic — all golden
LineBox tests pass identically on the 3-OS matrix. Resolution: **per-OS baselines**
(`Baselines/windows|linux|macos/`), each gated strictly at 0 differing pixels; the
determinism proof is per-OS pixel identity + cross-OS golden-test identity. The PDF parity
test compares 4×-box-downsampled images (AA noise averages out; measured correct-render max
block diff ≈35, gate at 64 with ≤0.2% blocks over) because poppler's rasterizer is not Skia's.

---

## 6. Test plan

All fixture text is **fictional** (§0) — e.g., "The Placeholder Lodge convenes on the appointed evening…". No real names, phones, emails.

### 6.1 Golden LineBox tests (`Layout.Tests`)

Assert on structure (line counts, break membership, segment x-ranges, glyph counts, baseline Ys) with epsilon `1e-3` pt:

1. Single word, single line.
2. Wrapping paragraph, no exclusions — expected line count + last word per line.
3. Mandatory break (`\n`) forces two lines.
4. Hyphen break — "well-known" wraps after hyphen, hyphen stays on upper line.
5. Unbreakable over-wide token — placed alone, overflows right edge.
6. First-line indent + paragraph spacing reflected in baselines.
7. Center & right alignment; trailing space excluded from measurement.
8. Single exclusion (photo top-right) — narrowed bands, full-width resume below, no glyph inside exclusion.
9. Min-segment-width discard — `< 4×avgChar` sliver dropped.
10. **Acceptance fixture — text column with two exclusion rects** — correct segment count per band, wraps around both, no glyph inside either, full-width resume below.
11. Frame spill / overset — (a) one short frame → `IsOverset`, correct `Overflow`; (b) two-frame chain flows remainder, `IsOverset==false`.

### 6.2 Snapshot tests (`Rendering.SnapshotTests`)

Fixture #10 + alignment sampler + exclusion sampler → PNG via `PngRenderer`, fixed page size/scale; decoded-pixel compare vs `Baselines/*.png` (strict 0/0). Baselines committed after human review. Full 3-OS matrix = determinism proof. Failure uploads actual/expected/diff artifacts.

### 6.3 PDF-vs-PNG parity (Linux CI only)

Export fixture #10 → `pdftoppm -png` rasterize → tolerant compare vs screen PNG (`maxPerChannelDiff ≤ 24`, diff fraction ≤ ~1%), plus `pdftotext` returns fixture text and `pdffonts` shows `emb=yes sub=yes`. Gate: `Assert.Skip` unless Linux + `pdftoppm` on PATH; CI installs poppler-utils on ubuntu only.

---

## 7. Implementation order

1. **Package pinning + project wiring.** *Check:* solution builds warnings-as-errors green; Linux resolves native `.so`.
2. **Bundled fonts + FontStore.** *Check:* each `FontKey` resolves (Typeface + HbFont non-null, `UnitsPerEm>0`, positive metrics); missing key throws.
3. **HarfBuzzShaper.** *Check:* "Hi" → expected glyph count, monotonic clusters, positive advances; double-shape bit-identical.
4. **LineBreakAnalyzer.** *Check:* per-rule unit tests.
5. **Exclusion→segment resolver.** *Check:* expected intervals incl. min-width discard + blocked-band advance.
6. **TextLayoutEngine.** *Check:* golden tests #1–#11 pass.
7. **PageRenderer.** *Check:* fixture #10 renders; non-background pixels > 0; known-empty region check.
8. **PngRenderer + snapshot tests.** *Check:* baselines committed; byte-identical across 3-OS matrix.
9. **PdfExporter + parity/embedding.** *Check:* PDF opens; `pdftotext`/`pdffonts` assertions; Linux parity within tolerance; matrix green.
