# TrestleBoard M4 — Text Editing: Implementation Spec

Status: derived from the LOCKED `PLAN.md` (§3, §4, §6, §8, §9, §11-M4) and `docs/M1-spec.md`. It translates M4's deliverables into implementable API contracts. It does not re-open any locked decision. Two structural additions (one new source project, one new test project) and one small additive change to the M1 layout output model are proposed with explicit rationale, in the style of M1's font-store deviation.

> Authored by the M4 Plan-agent pass (2026-07-26).

Repo facts this spec builds on (verified in-tree):

- `.editorconfig`: file-scoped namespaces are an **error**; braces required (warning); `var` only when the type is apparent; private fields `_camelCase`. `TreatWarningsAsErrors=true`.
- `TrestleBoard.Core` references BCL only. `Layout → Core` (+SkiaSharp/HarfBuzzSharp). `Rendering → Layout, Core`. `Export.Pdf → Rendering`. `App` is the only Avalonia project. Solution file is `TrestleBoard.slnx`.
- Layout output (`src/TrestleBoard.Layout/OutputModel.cs`) already carries the glyph→source map M4 needs: `PositionedGlyphRun.Clusters` (paragraph-relative char index per glyph) + `SourceSpan` on runs and `LineBox`es.
- `TextLayoutEngine` fills segments **left→right within one band**, so a band split by a photo reads left-segment-then-right-segment as one visual line.
- `StoryText` already guarantees the canonical run form (no two adjacent runs share `CharacterStyleRef`); `InsertTextCommand`/`DeleteTextCommand` use clone-based revert re-captured on every `Apply`; `DocumentSession.CoalesceWindow` = 1s and takes an injectable `TimeProvider`.
- `DocumentRenderSource.Create` lays out **once** at construction (M3 read-only viewer).
- `Core.Tests.CommandTests.EveryCommandTypeHasIdentityCoverage` reflects over every `IDocumentCommand` implementation — it **fails the build** until every new command added in M4 is registered in `CommandsUnderTest`.

Global constraints honored throughout: **LTR-only** (see §0.3); coordinates in typographic points (`float`); storage offsets are UTF-16 code units, **caret motion is grapheme-cluster based**; no wall-clock in layout (caret blink is UI-only); model mutated only through `IDocumentCommand`.

---

## 0. Decisions, placement, and scope

### 0.1 Namespace & project map (what lands where)

| Namespace | Project | Contents |
|---|---|---|
| `TrestleBoard.Core.Text` | Core (existing) | `TextPosition`, `TextAffinity`, `CaretPosition`, `TextRange`, `TextSelection`, `StoryNavigator` (grapheme/word motion) |
| `TrestleBoard.Core.Commands` | Core (existing) | `SplitParagraphCommand`, `MergeParagraphCommand`, `ApplyCharacterStyleCommand`, `ApplyParagraphStyleCommand`, `CompositeCommand`, `EnsureCharacterStyleCommand`; `StoryText` extensions; `TextEditBuilder` |
| `TrestleBoard.Layout.Editing` | Layout (existing) | `StoryTextGeometry` (hit-test, caret geometry, selection rects, vertical motion), `TextHit`, `CaretGeometry`, `SelectionRect`, `CaretXGoal` |
| `TrestleBoard.Rendering` | Rendering (existing) | `DocumentRenderSource` gains relayout + a text index; new `TextOverlayRenderer` (caret + selection painting) |
| `TrestleBoard.Editing` | **NEW project** | `TextEditorController`, `CaretMotion`, `ITextClipboard`, `EditorEventArgs` |
| `TrestleBoard.App.Canvas` | App (existing) | `PageCanvasControl` input/focus/overlay/blink, `AvaloniaTextClipboard`, Edit-menu wiring |

### 0.2 Decision: `TextEditorController` lives in a new `TrestleBoard.Editing` project

The controller needs three things at once: `DocumentSession` (Core), caret/selection geometry (Layout), and relayout invalidation (`DocumentRenderSource`, Rendering). Candidate homes:

- **Core** — impossible: it would need Layout for geometry.
- **Rendering** — compiles, but "Rendering" would then own input handling, clipboard, and undo policy; and Rendering is referenced by `Export.Pdf`, which must never see editor state.
- **App** — compiles, but the segment-aware caret math and coalescing behaviour are the second-riskiest code in the project (PLAN §11 M4) and would only be testable through an Avalonia headless session.

Resolution: **new project `src/TrestleBoard.Editing` (deps: Core, Layout, Rendering; no Avalonia, no Skia in its public surface)** plus **`tests/Editing.Tests`**. PLAN §9's *dependency rules* — Core references nothing, Avalonia only in App, layout/render stack runs headless — are strengthened, not violated: the whole editing brain becomes headless-testable. Both projects are added to `TrestleBoard.slnx`.

The **pure position types live in Core** (`TrestleBoard.Core.Text`) so commands, the navigator, Layout's geometry service, and the controller all speak one vocabulary without new dependencies.

### 0.3 Scope statements (explicit)

- **LTR-only in v1 is accepted and intentional.** Trestle boards are English-language; `HarfBuzzShaper` already sets `Direction = LeftToRight`, `Script = Latin`, `Language = en` explicitly (M1 §1.4). BiDi/RTL caret splitting, visual-vs-logical caret movement, and mixed-direction selection are **non-goals** — not deferred features with reserved hooks, just out of scope. The one concession: `TextAffinity` (§2) is exactly the mechanism BiDi would also need, so adding it later is not a rewrite.
- **Storage offsets stay UTF-16 code units** (`StoryRun.Text.Length`, `SourceSpan`, `Clusters`) — unchanged from M1/M2. Grapheme clusters are a *motion and deletion* concept only, computed on demand from paragraph text via `System.Globalization.StringInfo`.
- **Determinism unchanged**: nothing in this spec introduces time, culture, or platform dependence into layout. The caret blink timer lives in `PageCanvasControl`.
- **Deferred to M5+** (flagged in §10): IME composition (`TextInputMethodClient`), soft line breaks (Shift+Enter), text drag-and-drop, find/replace, spell check, multi-column frames, `VerticalAlignment` other than `Top`, justified text, editing text inside widgets, selection spanning stories, per-run direct formatting overrides.

---

## 1. Hit-testing: page point → `(storyId, paragraphIndex, charOffset)`

### 1.1 Prerequisite: a layout change for empty paragraphs (small, additive)

`TextLayoutEngine.Layout` currently consumes a line slot for an empty paragraph and emits **no `LineBox`**. An editor cannot survive that: pressing Enter creates an empty paragraph whose caret would have no geometry and no hit-test target.

**Required change** (in `TextLayoutEngine`, empty-paragraph branch): emit a `LineBox` with `Segments = [ new LineSegment(firstUsableInterval, style.Align, []) ]` and `Source = new SourceSpan(storyId, paraIdx, 0, 0)`, using the same band/segment computation as a normal line (so an empty paragraph beside a photo still reports the narrowed interval). `IsParagraphStart = true`. `PageRenderer.RenderFrame` iterates `segment.Runs` and therefore draws nothing — no rendering change, no snapshot churn.

### 1.2 Prerequisite: pen positions on `PositionedGlyphRun` (small, additive)

Hit-testing and caret geometry need each glyph's **pen x**, which is currently only recoverable from `GlyphOffsets[i].X` under the assumption `XOffsetPt == 0`. Add one field, populated in `TextLayoutEngine` where `x` is already known:

```csharp
/// <summary>Pen x of each glyph relative to OriginX, strictly ascending; excludes XOffsetPt.
/// The caret/hit-test x-grid (M4). Last glyph's advance = AdvanceWidthPt - GlyphPenXPt[^1].</summary>
public IReadOnlyList<float> GlyphPenXPt { get; }
```

Additive: value-equality snapshots and PNG baselines are unaffected. All M4 x-math uses `GlyphPenXPt` exclusively.

### 1.3 Types

```csharp
namespace TrestleBoard.Layout.Editing;

/// <summary>Result of a page-point hit test. IsInsideContent is false when the point was
/// clamped from a gap, a margin, or an exclusion band.</summary>
public readonly record struct TextHit(
    string StoryId,
    int FrameIndex,
    int LineIndex,
    int SegmentIndex,
    Core.Text.CaretPosition Caret,
    bool IsInsideContent);
```

### 1.4 Algorithm — page → block → frame → band → segment → run → cluster → char

Entry point on the render source (Rendering owns the page→block index; Layout owns the in-frame math):

```csharp
// TrestleBoard.Rendering.DocumentRenderSource
public bool TryHitTestText(int pageIndex, float xPt, float yPt, out TextHit hit, out string blockId);
// TrestleBoard.Layout.Editing.StoryTextGeometry
public bool TryHitTest(int frameIndex, float xPt, float yPt, out TextHit hit);
```

1. **Page → frame.** Take the page's `TextBlock`s that have a `FrameLayout`, sorted by `ZOrder` **descending** (topmost wins), document order breaking ties. Pick the first whose `FrameRect` contains `(x, y)`. If none contains the point and the caller passed `slopPt > 0` (drag-select / shift-click), pick the frame with the smallest squared distance to its rect within `slopPt` (default `18f`). Otherwise return `false` — the click belongs to block selection (M5).
2. **Frame → band (line).** `FrameLayout.Lines` are ordered top→bottom with non-overlapping `[BandTop, BandBottom)`.
   - `y < Lines[0].BandTop` → line 0.
   - `y >= Lines[^1].BandBottom` → line `^1`.
   - Else the line whose band contains `y`. If `y` falls in a vertical gap, choose the line minimizing distance to its band interval; **ties break to the earlier (upper) line**. Set `IsInsideContent = false` for gap hits.
   - `Lines.Count == 0` → §1.5 table.
3. **Line → segment.** `LineBox.Segments` are in ascending x. For each segment compute the **content extent** `[contentLeft, contentRight]` = `(firstRun.OriginX, lastRun.OriginX + lastRun.AdvanceWidthPt)`; for an empty segment (§1.1) both equal the alignment-resolved caret x.
   - `x` inside a segment's `XRange` → that segment.
   - `x` left of the first segment → segment 0, offset = segment start, affinity `Leading`.
   - `x` right of the last segment → last segment, offset = segment end, affinity `Trailing`.
   - `x` in a gap **between** two segments (i.e. inside an exclusion) → the segment with smaller x-distance; ties → the **left** segment at its end (`Trailing`). `IsInsideContent = false`.
4. **Segment → run.** Runs are ascending in `OriginX` and contiguous within a segment. Pick the run with `OriginX <= x < OriginX + AdvanceWidthPt`; before the first → first run at its start; after the last → last run at its end.
5. **Run → cluster.** Build the run's cluster table once: distinct values of `Clusters[]` in order, each cluster `c_k` spanning source `[Clusters[i_k], nextClusterStart)` (last cluster's end = `run.Source.EndChar`) and pen span `[GlyphPenXPt[i_k], GlyphPenXPt[i_{k+1}])` (last = `AdvanceWidthPt`). A ligature is one glyph covering several chars — the caret **never lands inside a cluster**.
6. **Cluster → char offset.** `xLocal = x - run.OriginX`. Find the containing cluster; `offset = xLocal < (clusterLeft + clusterRight) / 2 ? clusterStart : clusterEnd`. Affinity: `Trailing` if the resolved offset equals the segment's end offset, else `Leading`.
7. **Grapheme snap.** `offset = StoryNavigator.SnapToGrapheme(paragraphText, offset)` — nearest grapheme boundary, forward on ties.

### 1.5 Gap and degenerate behaviour

| Point location | Result |
|---|---|
| Above the first line of a frame | First line, x-resolved within it; `IsInsideContent=false` |
| Below the last line of a frame | Last line, x-resolved within it; `IsInsideContent=false` |
| Left of a frame's text, inside frame rect | Start of the first segment on that band, `Leading` |
| Right of a frame's text, inside frame rect | End of the last segment on that band, `Trailing` |
| Inside an exclusion (a segment gap on that band) | Nearest segment edge — end of left segment (`Trailing`) or start of right (`Leading`); tie → left |
| In a band fully blocked by exclusions (no line there) | Nearest line by band distance, tie → upper |
| In a paragraph-spacing gap | Nearest line by band distance, tie → upper |
| Outside every text frame | `TryHitTestText` returns `false`; the shell ends the editing session |
| Frame with `Lines.Count == 0` | v1: `(0,0)` for the chain head, else the previous frame's last position, `Leading`; `IsInsideContent=false` |
| Story with a single empty paragraph | `(0, 0)`, `Leading`; caret x from the empty `LineSegment` (§1.1) |
| Point in the overset region | Unreachable by hit-test — overset text has no geometry; see §2.5 |

---

## 2. Caret model

### 2.1 Types (Core)

```csharp
namespace TrestleBoard.Core.Text;

/// <summary>Which side of a line/segment boundary a caret belongs to. Leading = belongs to the
/// text that FOLLOWS (draws at the start of the next line/segment). Trailing = belongs to the
/// text that PRECEDES (draws at the end of the previous one).</summary>
public enum TextAffinity { Leading, Trailing }

public readonly record struct TextPosition(string StoryId, int ParagraphIndex, int Offset)
    : IComparable<TextPosition>;   // StoryId (Ordinal), then ParagraphIndex, then Offset; < <= > >= operators

public readonly record struct CaretPosition(TextPosition Position, TextAffinity Affinity)
{
    public static CaretPosition Leading(TextPosition p) => new(p, TextAffinity.Leading);
    public static CaretPosition Trailing(TextPosition p) => new(p, TextAffinity.Trailing);
}
```

Comparing carets ignores affinity: affinity is a **display** property, never a document coordinate.

### 2.2 Affinity rules (normative)

| Situation | Affinity |
|---|---|
| Fresh caret from a hit test at a non-boundary offset | `Leading` |
| Hit test clamped to the end of a segment/line | `Trailing` |
| Hit test clamped to the start of a segment/line | `Leading` |
| After rightward motion (Right, Ctrl+Right, typing, paste) | `Leading` |
| After leftward motion (Left, Ctrl+Left, Backspace) | `Trailing` |
| `End` (line end) | `Trailing` |
| `Home` (line start) | `Leading` |
| After Up/Down/PageUp/PageDown | `Trailing` if resolved offset is that line's end offset, else `Leading` |
| After a paragraph split (Enter) | `Leading` at `(p+1, 0)` |
| After a paragraph merge (Backspace at offset 0) | `Trailing` at `(p-1, oldLength)` |
| Selection endpoints for geometry | Start uses `Leading`, End uses `Trailing` |

### 2.3 Caret geometry query

```csharp
namespace TrestleBoard.Layout.Editing;

public readonly record struct CaretGeometry(
    int FrameIndex, string? PageId, string? BlockId,
    float XPt, float TopPt, float BaselineYPt, float HeightPt,
    int LineIndex, int SegmentIndex);

public sealed class StoryTextGeometry
{
    public StoryTextGeometry(Documents.StoryLayoutPlan plan, LayoutResult layout);
    public bool TryGetCaretGeometry(Core.Text.CaretPosition caret, out CaretGeometry geometry);
    public bool TryHitTest(int frameIndex, float xPt, float yPt, out TextHit hit);
}
```

Algorithm:

1. **Flatten** the chain into a visual line list once at construction: frames in chain order, lines in frame order. Each `SegmentInfo` caches `XRange`, content extents, `StartChar`/`EndChar`, per-run cluster/pen tables.
2. **Locate the line.** Candidates: `ParagraphIndex == caret.ParagraphIndex` and `Source.StartChar <= Offset <= Source.EndChar`. At most two candidates (shared endpoint). `Trailing` → earlier line; `Leading` → later.
3. **Locate the segment** by the same rule.
4. **x.** Walk to the first cluster with `clusterStart >= Offset`; `x = run.OriginX + GlyphPenXPt[thatGlyph]`. Offset at/past segment end → `x = ContentRight`.
5. **Caret clamp rule (load-bearing).** `XPt = Math.Clamp(x, segment.XRange.Left, segment.XRange.Right)`.
   *Why:* the engine tests fit with `FitAdvancePt` (trailing whitespace excluded) but advances by `TotalAdvancePt`, so trailing space **can** extend past `XRange.Right` into the inflated exclusion. Without this clamp the "caret never lands inside an exclusion" acceptance fails on the wrap fixtures. Same clamp applies to selection rects. Also covers the force-placed over-wide-token path.
6. **Vertical.** `TopPt = BaselineY - MaxAscentPt` (== `BandTop`), `HeightPt = MaxAscentPt + MaxDescentPt`. Ascent+descent, not `LineHeight`: the caret matches the glyphs, not the leading.
7. **Empty line**: x from alignment over the empty interval (`Left`: `XRange.Left + FirstLineIndent`; `Center`: midpoint; `Right`: `XRange.Right`), then clamp.
8. **Page mapping** from `FramePlacement[FrameIndex]`.

### 2.4 Invariant: the caret never lands inside an exclusion

`ComputeSegments` subtracts every inflated exclusion overlapping the band from the band's x-interval → `segment.XRange × [BandTop, BandBottom]` is disjoint from every inflated exclusion. Caret rect ⊆ that region by clamp + band-height. Enforced by a §8 grid property test.

### 2.5 Overset carets

Offset past the last laid-out position → `TryGetCaretGeometry` returns `false`. Controller keeps the logical caret (editing still works), skips painting and reveal, raises overset state. Do **not** clamp the logical caret.

---

## 3. Selection model

```csharp
namespace TrestleBoard.Core.Text;

public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public bool IsEmpty => Start == End;
}

/// <summary>Anchor stays put; Extent follows the caret. Normalization happens in Range.</summary>
public readonly record struct TextSelection(CaretPosition Anchor, CaretPosition Extent)
{
    public CaretPosition Caret => Extent;
    public TextRange Range { get; }                    // normalized
    public static TextSelection At(CaretPosition caret) => new(caret, caret);
}
```

- Anchor and Extent **must** share `StoryId`; a hit in a different story ends the session and starts a new one.
- Selection is *not* document state — controller-owned, never serialized, clamped after undo/redo (§7.4).

### 3.1 Selection geometry

One rect per (visual line × segment) intersection, in reading order: clip range offsets to segment; `left/right = XForOffset` through the **caret clamp rule**; `top/bottom = BandTop/BandBottom` (bands, not ascent boxes — multi-line selections are continuous). Empty-line/paragraph-break-selected rows get a minimum visible width (3pt), clamped. Fully-covered segments run to content right (trailing whitespace included, clamped).

---

## 4. Keyboard navigation

### 4.1 Grapheme and word primitives (Core, BCL-only)

```csharp
public static class StoryNavigator
{
    public static string GetParagraphText(Model.StoryParagraph paragraph);
    public static int[] GetGraphemeBoundaries(string text);   // StringInfo-based, includes 0 and Length
    public static int NextGrapheme(string text, int offset);
    public static int PreviousGrapheme(string text, int offset);
    public static int SnapToGrapheme(string text, int offset, bool preferForward = true);
    public static int NextWordStart(string text, int offset);
    public static int PreviousWordStart(string text, int offset);
    public static (int Start, int End) WordAt(string text, int offset);
    public static string GetRangeText(Model.Story story, TextRange range);  // '\n' between paragraphs
}
```

Word classes: `Word` = letters/digits/connector punctuation/marks/apostrophes-between-letters; `Space` = whitespace; `Punct` = rest. `NextWordStart`: skip current class run, then a following Space run. Both stop at the paragraph boundary — a paragraph edge always costs one keypress.

### 4.2 Vertical motion and the x-goal

```csharp
/// <summary>Sticky x for Up/Down, stored relative to the frame's left edge so the goal
/// survives a jump into a linked frame on another page.</summary>
public readonly record struct CaretXGoal(float OffsetFromFrameLeftPt);

public bool TryMoveVertical(CaretPosition from, int deltaLines, CaretXGoal? goal,
    out CaretPosition to, out CaretXGoal newGoal);
```

1. Resolve `from` to `(visualLineIndex, geometry)`; overset → `false`.
2. `goalX = goal ?? (caretX - frame.Left)`; goal preserved verbatim across the move.
3. `target = visualLineIndex + deltaLines`; below 0 → story start; past end → story end.
4. `targetX = targetFrame.Left + goalX` — **cross-frame translation** keeps the visual column.
5. Segment containing `targetX`, else nearest by x-distance (ties → left) — vertical motion **never lands inside an exclusion**.
6. `offset = XToOffset(segment, targetX)` — identical math to hit-testing, so click and Down agree.
7. Affinity: `Trailing` iff offset == line's last segment end.

### 4.3 Motion table

Non-extending motion from a selection: Left/Up collapse to `Range.Start` without moving; Right/Down collapse to `Range.End`; others collapse then move.

| Motion | Gesture | Behaviour |
|---|---|---|
| `Left`/`Right` | ← → | grapheme step; crosses paragraph boundary (one keypress); affinity Trailing/Leading; clears x-goal |
| `Up`/`Down` | ↑ ↓ | `TryMoveVertical(±1)`; **preserves** x-goal |
| `WordLeft`/`WordRight` | Ctrl+←/→ | word starts; paragraph edge costs one press; clears x-goal |
| `LineStart`/`LineEnd` | Home / End | visual line (not segment) start `Leading` / end `Trailing`; clears x-goal |
| `StoryStart`/`StoryEnd` | Ctrl+Home / Ctrl+End | `(0,0)` / `(last, len)`; clears x-goal |
| `PageUp`/`PageDown` | PgUp / PgDn | `TryMoveVertical(∓N)`, `N = max(1, viewportH/lineH - 1)`; preserves x-goal; raises reveal |

**Shell keybinding conflict:** `MainWindow` binds bare `PageUp`/`PageDown` to page navigation. Editing session active → editor consumes them; page navigation moves to **Ctrl+PageUp / Ctrl+PageDown** (bare gestures still work with no session). Undo/redo gestures come from `PlatformSettings.HotkeyConfiguration` (Cmd on macOS).

Extra gestures: double-click word select; triple-click paragraph; Shift+click extend; Ctrl+A story; Esc ends session.

---

## 5. Editing operations

### 5.1 New commands

House pattern: `Apply` re-captures pre-state on every call; `Revert` restores clones; `Scope` = `Text` for run-level changes, `StoryStructure` for paragraph count changes.

- **`SplitParagraphCommand(storyId, paragraphIndex, offset)`** — "Split paragraph". Tail becomes a new paragraph at index+1 inheriting `ParagraphStyleRef`; runs split at offset, styles preserved, canonical form maintained. Revert restores single clone + removes tail. No merge.
- **`MergeParagraphCommand(storyId, paragraphIndex)`** — "Join paragraphs". Folds `p+1` into `p`; survivor keeps `p`'s style; seam canonicalized. Revert restores both clones. No merge.
- **`ApplyCharacterStyleCommand(storyId, paragraphIndex, offset, length, characterStyleRef?)`** — "Change text style". Split runs at boundaries, assign, canonicalize. Clone-based revert. No merge.
- **`ApplyParagraphStyleCommand(storyId, paragraphIndex, paragraphStyleRef)`** — "Change paragraph style". Old/new ref swap. No merge.
- **`CompositeCommand(description, scope, children)`** — one undo step for replace-selection, multi-paragraph delete, paste, multi-span style. Apply in order, Revert in reverse; each child re-captures its own pre-state → redo correct. Never merges.
- **`EnsureCharacterStyleCommand(styleDef)`** — adds a `CharacterStyleDef` if absent (bold toggle reaching a variant a template didn't define); Revert removes only what it added.

### 5.2 `StoryText` extensions

```csharp
public static (StoryParagraph Head, StoryParagraph Tail) SplitAt(StoryParagraph paragraph, int offset);
public static void Canonicalize(StoryParagraph paragraph);          // drop empty runs, merge same-ref neighbours
public static int SplitRunAt(StoryParagraph paragraph, int offset); // index of run starting at offset
public static void SetCharacterStyle(StoryParagraph paragraph, int offset, int length, string? styleRef);
public static string? StyleRefAt(StoryParagraph paragraph, int offset);   // left-neighbour rule
```

Invariants preserved: concatenated text unchanged; no empty runs; no two adjacent runs share a `CharacterStyleRef`.

### 5.3 Typing / insert

1. Sanitize: `\r\n`/`\r` → `\n`; drop other control chars; return early if empty.
2. Non-empty selection → delete children prepended; whole thing one `CompositeCommand("Replace text")`.
3. Text contains `\n` → paste algorithm (§5.6).
4. Otherwise a **bare** `InsertTextCommand` so `DocumentSession` coalescing applies (word-burst undo). A `CompositeCommand` never merges — replace-selection typing starts a new undo step; subsequent keystrokes coalesce onto following bare inserts.
5. `PendingCharacterStyleRef` set (bold toggled, empty selection) → `CompositeCommand[EnsureStyle?, Insert, ApplyCharacterStyle]`; clear pending.
6. Caret → `(p, offset + len)` `Leading`; clear x-goal.

### 5.4 Delete a range

`TextEditBuilder.BuildDeleteRange(story, range)`:

- Same paragraph → `[DeleteTextCommand(p, start, end-start)]`.
- Spanning `p1 < p2`, in order: truncate tail of `p1`; empty each interior paragraph; truncate head of `p2`; then `MergeParagraphCommand(p1)` × `(p2 - p1)`.
- Invariant: a story always retains at least one paragraph.
- No `RemoveParagraphCommand` needed: "empty it, then merge it".

### 5.5 Backspace / Delete / Enter

| Key | Empty selection |
|---|---|
| Backspace | `offset > 0` → delete previous grapheme, `Trailing`; `offset == 0 && p > 0` → `MergeParagraphCommand(p-1)`, caret `(p-1, oldLen)` `Trailing`; story start → no-op |
| Delete | `offset < len` → delete next grapheme; `offset == len && p < last` → `MergeParagraphCommand(p)`; story end → no-op |
| Enter | `SplitParagraphCommand(p, offset)`; caret `(p+1, 0)` `Leading`. With selection: `CompositeCommand("New paragraph", [delete…, split])` |
| Shift+Enter | same as Enter in v1 (soft break deferred) |
| Tab | swallowed inside a session (block cycling is M5) |

Non-empty selection + Backspace/Delete → delete range (bare command if single-paragraph so backspace bursts coalesce; composite otherwise).

### 5.6 Clipboard (plain text v1)

```csharp
public interface ITextClipboard
{
    Task<string?> GetTextAsync();
    Task SetTextAsync(string text);
}
```

- **Copy** — `GetRangeText`; `\n` between paragraphs; no-op on empty selection.
- **Cut** — copy + delete children as `CompositeCommand("Cut text")`.
- **Paste** — sanitize, split on `\n` into chunks; single chunk + empty selection → bare insert; else one `CompositeCommand("Paste text")`: optional delete children; `Split(p, offset)`; insert chunk 0 at end of head; for middle chunks, split at end of previous then insert at 0; insert last chunk at 0 of tail. Example: `XY|Z` + `"A\nB\nC"` → `XYA` / `B` / `CZ`. Caret → end of last chunk, `Leading`.
- Pasted text adopts the insertion point's style (left-neighbour rule); new paragraphs inherit the split paragraph's style. Async (`CopyAsync`/`CutAsync`/`PasteAsync`); mutation on UI thread after await.

---

## 6. Style application

### 6.1 Bold / italic toggling

Runs carry only `CharacterStyleRef` — toggling retargets runs at a **sibling style def** (same family/size/color, different weight/slant). Naming convention: `body`, `body-bold`, `body-italic`, `body-bold-italic`.

```csharp
public static class CharacterStyleResolver
{
    public static string BaseName(string styleName);
    public static string VariantName(string baseName, FontWeightToken w, FontSlantToken s);
    public static bool TryResolve(StyleSheet sheet, string styleName, FontWeightToken w, FontSlantToken s, out CharacterStyleDef def);
    public static CharacterStyleDef Derive(CharacterStyleDef from, string name, FontWeightToken w, FontSlantToken s);
}
```

`ToggleBold()`: effective def per run (null ref inherits paragraph default); `IsBoldActive` = ALL runs bold; target = toggle; per distinct source style resolve variant (Derive + `EnsureCharacterStyleCommand` when absent — derived def must reference a bundled face `FontStore` can resolve; no bold face → disable toggle); one `ApplyCharacterStyleCommand` per contiguous same-style sub-range; wrap in `CompositeCommand("Bold text"/"Remove bold")`. Canonical form preserved by construction (mid-word toggle = 3 runs; untoggle = 1). **Empty selection** → `PendingCharacterStyleRef`, applied by next insert, cleared by any motion.

### 6.2 Named paragraph style

One `ApplyParagraphStyleCommand` per touched paragraph (composite when several). Style must exist; UI offers existing names only.

### 6.3 UI surface (App)

Toolbar + menus: Bold (Ctrl+B), Italic (Ctrl+I), paragraph-style combo, Undo/Redo (labels from `UndoDescription`/`RedoDescription`), Cut/Copy/Paste. Icon+text, ≥44px, 16pt+ (PLAN §6); every action has a gesture.

---

## 7. Editor architecture

### 7.1 Where state lives

| State | Owner |
|---|---|
| Document content | `Document` via `DocumentSession` |
| Undo/redo, coalescing | `DocumentSession` |
| Layout | `DocumentRenderSource` (now invalidating) |
| Caret, selection, x-goal, pending style, active story/block | `TextEditorController` (Editing) |
| Caret blink, focus, pointer capture, zoom transform | `PageCanvasControl` (App) |

### 7.2 `TextEditorController`

```csharp
public enum CaretMotion
{
    Left, Right, Up, Down, WordLeft, WordRight,
    LineStart, LineEnd, StoryStart, StoryEnd, PageUp, PageDown,
}

public sealed class TextEditorController
{
    public TextEditorController(DocumentSession session, DocumentRenderSource layout, ITextClipboard clipboard);

    public bool IsActive { get; }
    public string? StoryId { get; }
    public string? BlockId { get; }
    public bool TryBeginAt(int pageIndex, float xPt, float yPt);
    public void End();

    public TextSelection Selection { get; }
    public void ExtendTo(int pageIndex, float xPt, float yPt);
    public void SelectWordAt(int pageIndex, float xPt, float yPt);
    public void SelectParagraphAt(int pageIndex, float xPt, float yPt);
    public void SelectAll();
    public bool Move(CaretMotion motion, bool extend);

    public void InsertText(string text);
    public void InsertParagraphBreak();
    public void Backspace();
    public void DeleteForward();
    public Task CopyAsync();
    public Task CutAsync();
    public Task PasteAsync();

    public bool IsBoldActive { get; }
    public bool IsItalicActive { get; }
    public void ToggleBold();
    public void ToggleItalic();
    public void ApplyParagraphStyle(string paragraphStyleRef);
    public IReadOnlyList<string> AvailableParagraphStyles { get; }

    public float ViewportHeightPt { get; set; }
    public bool TryGetCaretGeometry(out CaretGeometry geometry);
    public IReadOnlyList<SelectionRect> GetSelectionRects();
    public bool IsCaretOverset { get; }

    public event EventHandler? Changed;
    public event EventHandler<CaretRevealEventArgs>? RevealRequested;
}
```

Never mutates the document directly; every path builds commands → `session.Execute`. Subscribes to `session.Changed` (drop caches, clamp selection) and `layout.LayoutInvalidated`. UI thread only; no locking, no timers.

### 7.3 Relayout on document change (`DocumentRenderSource`)

```csharp
public static DocumentRenderSource CreateEditable(Document document, IReadOnlyDictionary<string, byte[]> assets,
    FontStore fonts, DocumentSession session, LayoutOptions? options = null);
public event EventHandler? LayoutInvalidated;
public bool TryGetStoryGeometry(string storyId, out StoryTextGeometry geometry);
public bool TryHitTestText(int pageIndex, float xPt, float yPt, out TextHit hit, out string blockId);
```

1. `Invalidate(scope)` records the coarsest dirty level and raises `LayoutInvalidated`. **No layout in the handler.**
2. `EnsureLayout()` at the top of `RenderPage` and geometry queries — a keystroke burst costs one relayout per painted frame.
3. Dirty levels: `Text`/`StoryStructure` (StoryId set) → that story's plan+layout only; `BlockGeometry`/`BlockContent` → all plans (exclusions may have moved); `PageStructure`/`Metadata`/null → full rebuild.
4. `Create` (read-only) remains for viewer/PDF; PDF path never draws overlays.
5. Budget: 6-page fixture relayout after a keystroke < 50 ms (test, not gate).

### 7.4 Selection survival across undo/redo

After any `session.Changed`: clamp `ParagraphIndex → [0, count-1]`, `Offset → [0, len]`, snap to grapheme. Commands do not carry caret state (M9 polish).

### 7.5 App layer: input, focus, blink, overlay

- `Focusable = true`; `Focus()` on press; `OnLostFocus` → end session.
- **Coordinate transform**: `TryToPagePoint(controlPt, out xPt, out yPt)` (inverse of padding+zoom); all controller APIs take page points.
- Pointer: press → `TryBeginAt` (ClickCount 2/3 → word/paragraph; Shift → extend); move+capture → `ExtendTo`.
- `OnKeyDown` → `CaretMotion`/edit ops; `e.Handled = true` when consumed.
- **`OnTextInput` is the text-entry path for v1** — no `TextInputMethodClient` in M4 (would need a second caret/composition model). Dead keys/Latin keyboards deliver composed chars through TextInput. CJK/IME documented unsupported until M5+.
- **Caret blink**: `DispatcherTimer` 530ms toggling visibility, reset on caret change, stopped when inactive. UI-only.
- **Overlay**: in `PageDrawOperation.Render` after `source.RenderPage` — selection rects then caret via `TextOverlayRenderer`, current page only. v1 draws translucent fill over text (`0x552A6FCF`).

```csharp
public static class TextOverlayRenderer
{
    public static void DrawSelection(SKCanvas canvas, IReadOnlyList<SelectionRect> rects, uint fillArgb = 0x552A6FCF);
    public static void DrawCaret(SKCanvas canvas, CaretGeometry caret, uint argb = 0xFF000000, float widthPt = 1.2f);
}
```

- `AvaloniaTextClipboard : ITextClipboard` over `TopLevel.Clipboard`.
- `MainWindow`: owns session + controller, Edit/Format menus, page-nav rebound to Ctrl+PageUp/Down, `RevealRequested` → switch page + scroll.

---

## 8. Test matrix

All fixture text fictional (PLAN §0). New test project `tests/Editing.Tests`.

**Core.Tests**: all six new commands registered in `CommandsUnderTest` (reflection gate) with identity+redo cycles; Split∘Merge round-trip property over offsets; merge canonicalizes seam; `SetCharacterStyle` property tests (text invariant, canonical form, no empty runs); `StoryNavigator` grapheme/word tables; `BuildDeleteRange` across 0–3 intervening paragraphs + last-paragraph invariant; `CompositeCommand` reverse-order revert.

**Layout.Tests**: empty-paragraph LineBox (incl. beside exclusion); hit-test round-trip property (geometry→hit→same offset; x monotonic); **caret-never-inside-exclusion grid property test** (4pt grid over M1 acceptance fixture; caret rect intersects no inflated exclusion; x within XRange); §1.5 gap table as Theory; affinity dual geometry at line boundaries; **arrow navigation crosses segment boundaries** (Right through photo-split band; Down lands nearest x-goal, never inside exclusion); x-goal preservation incl. cross-frame; selection rects (multi-line, split band, empty paragraph, clamped); overset → false without throwing.

**Editing.Tests**: **M4 acceptance — types a paragraph around an exclusion, undoes in word-chunks** (FakeTimeProvider, <1s within words, >1s between; undo count == word count; final undo restores exactly); replace-selection typing one step + following keystrokes coalesce; Backspace at 0 merges + caret placement + undo; Enter splits preserving styles; multi-paragraph paste one undo; cut/copy text with `\n`; bold toggle (mixed→all-bold, canonical run counts, EnsureStyle undo); pending style on empty selection; paragraph style over selection one step; cross-frame reveal event; relayout laziness (100 keystrokes between paints → 1 relayout); selection clamp after undo of paste.

**App.HeadlessTests**: open sample → click → type → text changed + CanUndo; Ctrl+Z/Y round-trip with menu labels; bare PageDown moves caret while editing, Ctrl+PageDown changes page, Esc ends session; click outside frames ends session.

**Rendering.SnapshotTests**: overlay fixture (selection + caret) per-OS baselines; PDF export contains no overlay.

Acceptance traceability: types-and-undoes-in-word-chunks → Editing test 17 + shell test; caret-never-in-exclusion → grid property + clamp; arrow-across-segments → Layout tests 13–14; command identity → reflection gate.

---

## 9. Implementation order

1. Core text vocabulary (`TextPosition` et al., `StoryNavigator`, `CharacterStyleResolver`).
2. `StoryText` extensions.
3. New commands + `TextEditBuilder`; register in `CommandTests`.
4. Layout output additions — empty-paragraph LineBox, `GlyphPenXPt`.
5. `StoryTextGeometry` — hit-test + caret geometry with clamp.
6. Vertical motion + line bounds + selection rects.
7. `DocumentRenderSource` invalidation + text index + `TextOverlayRenderer`.
8. `TrestleBoard.Editing` project + `TextEditorController`.
9. App wiring.
10. Snapshot + parity additions.
11. Post-milestone: `/graphify . --update`; wiki-ingest decisions.

---

## 10. Deferred to M5+ (explicit)

| Item | Why |
|---|---|
| IME composition / `TextInputMethodClient` | second caret model; English-only user base |
| Soft line break (Shift+Enter) | needs mandatory-break chars in run text |
| BiDi / RTL | non-goal (§0.3) |
| Drag-and-drop, find/replace, spell check | not in PLAN §11 M4 |
| `VerticalAlignment` ≠ Top, multi-column, justify | engine doesn't implement them |
| Caret restoration in undo | M9 polish; clamping suffices |
| Widget text editing | M7 |
| Rich-text clipboard | v1 plain text |
| Per-page dirty narrowing for BlockGeometry | M5 owns the perf loop |
| Overset caret UI | M5/M8 |
