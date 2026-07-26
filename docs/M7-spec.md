# M7 — Widget system & wizards

Status: derived from the LOCKED `PLAN.md` (§2, §5, §6, §9, §11-M7) and `docs/M1/M4/M5/M6-spec.md`.
It translates M7's deliverables into implementable contracts. It re-opens no locked decision.
Two seam types are added to `TrestleBoard.Layout` and one type to `TrestleBoard.Core`, each
justified in one line.

Acceptance (PLAN §11-M7): *a headless test drives the Officers wizard end-to-end → styled table
on the page with body text wrapped around it; re-edit pre-fills; widget data survives save/reload;
all wizard defaults and test fixtures use fictional data only (§0 — wizards start EMPTY or with
obvious placeholders, never pre-populated with real names/numbers).*

Repo facts this spec builds on (verified in-tree):

- `WidgetBlock` already exists: `{ WidgetType, DataVersion = 1, Data: JsonElement?, TableStyleRef }`
  on top of `Block`'s `FrameRect / ZOrder / WrapMode / WrapMarginPt / FrameStyleRef / ExtraProperties`.
  PLAN §2's `styleOverrides` is realised as those two refs; M7 adds no new style bag.
- `SetWidgetDataCommand(blockId, JsonElement?, int newDataVersion)` already exists,
  `Scope = BlockContent`, `TryMerge` returns false. `Core.Tests.CommandTests` already registers it.
- `TableStyleDef` (`HeaderCharacterStyleRef`, `BodyCharacterStyleRef`, `RuleArgb`, `RuleWidthPt`),
  `FrameStyleDef` (`FillArgb`, `StrokeArgb`, `StrokeWidthPt`, `PaddingPt`) and `Theme`
  (`ColorTokens`, `FontTokens`, `SpacingScale`) already exist.
- `Core.Tests.Fixtures.BuildDocument()` already contains a `WidgetBlock` with
  `WidgetType = "officersTable"` and data `{"officers":[{"position","name","phone"}]}` —
  **M7's OfficersTable data shape must remain compatible with that fixture.**
- `DocumentRenderSource.RenderBlock` has `case WidgetBlock: RenderPlaceholder(...)`.
  `SampleDocument` contains **no** widget, so the M3/M4/M5/M6 baselines cannot move as long as
  the no-provider path keeps drawing that placeholder.
- `DocumentLayoutAdapter.BuildExclusions` already turns *any* block with `WrapMode.Rectangle` and
  higher `ZOrder` into an `ExclusionRect` — widgets included, with no change.
- `TboardJsonContext`: camelCase, `UseStringEnumConverter`, `WriteIndented`,
  `AllowOutOfOrderMetadataProperties`, `DefaultIgnoreCondition = WhenWritingNull`; unknown
  properties survive through `[JsonExtensionData]` bags on every model type.
- `HarfBuzzShaper.Shape` sets Direction/Script/Language explicitly; `PositionedGlyphRun`'s
  constructor is `internal` to `TrestleBoard.Layout`; `PageRenderer.DrawRun` is `private static`.
- `.editorconfig`: file-scoped namespaces are **required** (`file_scoped:error`); braces required;
  private fields `_camelCase`; `TreatWarningsAsErrors=true`.
- Project references today: `Layout→Core`; `Widgets→Core+Layout`; `Rendering→Core+Layout+Imaging`;
  `Editing→Core+Layout+Rendering`; `Export.Pdf→Rendering`; `App→everything`.

Global constraints honoured throughout: coordinates in typographic points (`float`); LTR/English
only; culture-invariant formatting everywhere; the model mutates only through `IDocumentCommand`;
**no real personal data anywhere** (PLAN §0) — every example, default and fixture in this document
uses "A. Placeholder", "555-0100", "Sample Lodge".

---

## 0. Placement, dependencies, and the leaf rule

### 0.1 The architectural constraint

`TrestleBoard.Widgets` **stays a leaf**: nothing references it except `TrestleBoard.App` and the
test projects. Therefore `Rendering` must not reference `Widgets`, and neither must `Editing`.

The mechanism, in both directions:

1. **Widget → page.** Widget layout produces a device-independent **draw list**
   (`TrestleBoard.Layout.Widgets.WidgetDrawList`). `DocumentRenderSource` is handed an
   **`IWidgetLayoutProvider`** (also declared in Layout) at construction. `TrestleBoard.Widgets`
   implements it; `App` and the tests inject it.
2. **Editor → widget metadata.** `WidgetController` (Editing) is handed an **`IWidgetCatalog`**
   (declared alongside the provider) for display names, default sizes and empty-data creation.

When no provider is injected, `RenderBlock` keeps drawing the current neutral placeholder.
**Normative: the M3 snapshot baselines must not move for documents without widgets, and must not
move for documents *with* widgets when no provider is injected.**

### 0.2 Namespace & project map

| Namespace | Project | Contents |
|---|---|---|
| `TrestleBoard.Layout.Widgets` | Layout (existing) | `WidgetDrawList` + draw items + `WidgetDrawListBuilder`, `WidgetTextShaper`, `WidgetStyleContext`, `WidgetLayoutRequest`, `IWidgetLayoutProvider`, `IWidgetCatalog`, `WidgetInfo`, `WidgetSeed`, `WidgetDrawListDump` |
| `TrestleBoard.Core.Text` | Core (existing) | `MeetingRule` (parse/format "1st Tuesday") |
| `TrestleBoard.Widgets` | Widgets (existing, empty) | `IWidgetDefinition`, `WidgetDefinition<TData>`, `WidgetRegistry`, `BuiltInWidgets`, `WidgetData` codec, `WidgetLayoutProvider`, `WidgetCatalog` |
| `TrestleBoard.Widgets.Wizards` | Widgets | `WizardDefinition`, `IWizardStep` + four step kinds, `WizardField`, `WizardValidators`, `WizardSession` |
| `TrestleBoard.Widgets.Layout` | Widgets | `TableLayouter`, `WidgetLayoutHelpers` (shared, **frozen** — §12) |
| `TrestleBoard.Widgets.Builtins.*` | Widgets | one folder per widget, one owner agent each |
| `TrestleBoard.Rendering` | Rendering (existing) | `WidgetDrawListRenderer`, `WidgetStyleResolver`; `DocumentRenderSource` gains the provider, the widget cache and the widget query API |
| `TrestleBoard.Editing` | Editing (existing) | `WidgetController` |
| `TrestleBoard.App.Dialogs` | App (existing) | `WizardWindow`, `WidgetGridWindow` |

**No project reference changes.** `Widgets.Tests` already references `Widgets` (and gets
Layout/Core transitively).

### 0.3 Decision: `MeetingRule` lives in Core, not Widgets

CoverBanner and DistrictCalendar both need "1st Tuesday", and PLAN §7/§11-M9 has
start-from-last-month recompute dates from `DocumentMetadata.MeetingRule`. M9's date bumping lives
in Core/App and must not reference Widgets. So the parser goes in Core (BCL-only, invariant) and
Widgets consumes it.

```csharp
namespace TrestleBoard.Core.Text;

public enum WeekOrdinal { First = 1, Second = 2, Third = 3, Fourth = 4, Last = 5 }

/// <summary>"1st Tuesday" — the recurrence rule M9's date bumping consumes.</summary>
public readonly record struct MeetingRule(WeekOrdinal Ordinal, DayOfWeek Day)
{
    public static bool TryParse(string? text, out MeetingRule rule);   // invariant, case-insensitive
    public override string ToString();                                 // canonical "1st Tuesday"
    public DateOnly ResolveDate(int year, int month);                  // used by M9; available now
}
```

`TryParse` accepts `1st|first|2nd|second|3rd|third|4th|fourth|last` + an English weekday name, one
or more spaces between. It never throws and never consults `CultureInfo.CurrentCulture`.

---

## 1. The widget contract

### 1.1 `IWidgetDefinition`

```csharp
namespace TrestleBoard.Widgets;

public interface IWidgetDefinition
{
    /// <summary>Stable camelCase id written into WidgetBlock.WidgetType. Never renamed.</summary>
    string TypeId { get; }

    /// <summary>Plain-language menu/gallery label, e.g. "Lodge officers".</summary>
    string DisplayName { get; }

    /// <summary>One sentence for the gallery and the wizard's first screen.</summary>
    string Description { get; }

    /// <summary>Key into the App's icon set; icons are NEVER the only label (PLAN §6).</summary>
    string IconKey { get; }

    Type DataType { get; }

    /// <summary>Highest dataVersion this build writes. Blocks carrying a higher value are read-only (§3).</summary>
    int CurrentDataVersion { get; }

    /// <summary>Frame size used at insert, before fit-to-content (§8).</summary>
    Core.Model.SizePt DefaultSizePt { get; }

    WidgetStyleDefaults StyleDefaults { get; }

    Wizards.WizardDefinition Wizard { get; }

    IWidgetLayouter Layouter { get; }

    /// <summary>Fresh, EMPTY data (PLAN §0: never pre-populated with people). May copy
    /// non-personal document facts from the seed (§8.3).</summary>
    object CreateEmptyData(Layout.Widgets.WidgetSeed seed);

    /// <summary>Raw JSON → typed POCO, after migration (§3). Returns false on malformed data
    /// rather than throwing — a damaged widget must not take the newsletter down with it.</summary>
    bool TryReadData(JsonElement? data, out object typedData);

    JsonElement WriteData(object typedData);

    /// <summary>One migration step. False when fromVersion is not a version this build knows (§3).</summary>
    bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion);
}
```

Concrete widgets derive from the typed base, which supplies `DataType`, the codec plumbing and the
`object`↔`TData` casts in exactly one place:

```csharp
public abstract class WidgetDefinition<TData> : IWidgetDefinition where TData : class, new()
{
    protected abstract JsonTypeInfo<TData> TypeInfo { get; }
    public abstract TData CreateEmpty(WidgetSeed seed);
    public abstract WizardDefinition BuildWizard();       // called once, cached
    public abstract IWidgetLayouter Layouter { get; }
    public virtual int CurrentDataVersion => 1;
    public virtual bool TryMigrateStep(JsonNode data, int from, out JsonNode up, out int to)
        => Fail(out up, out to);                           // no history yet
}
```

### 1.2 `WidgetBlock.Data` ↔ POCO, and the JSON conventions

`WidgetBlock.Data` is an untyped `JsonElement?` that the container writes inline into
`document.json`. The widget codec is the only thing that types it.

```csharp
namespace TrestleBoard.Widgets;

/// <summary>
/// The one place widget payloads cross the untyped boundary. Options are identical to the
/// container's (TboardJsonContext) so a widget payload looks the same as the rest of document.json.
/// </summary>
public static class WidgetData
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T? Read<T>(JsonElement? data, JsonTypeInfo<T> typeInfo) where T : class;
    public static JsonElement Write<T>(T value, JsonTypeInfo<T> typeInfo) where T : class;
}
```

Normative rules:

1. **Source-generated only.** Every widget declares a `[JsonSourceGenerationOptions]` partial
   context in its own folder (`OfficersTableJson.cs`), with **exactly** the option set above.
   Reflection-based `JsonSerializer` overloads are forbidden in `TrestleBoard.Widgets` (trim/AOT
   safety, and it keeps naming identical to the container).
2. **Indentation is the container's, not the widget's.** When `document.json` is written,
   `JsonElement.WriteTo` re-emits through the container's writer, so the stored payload is indented
   exactly like its surroundings. `WriteIndented` above only matters for a payload inspected in
   isolation (tests); it is set to match anyway so no one has to reason about it.
3. **Property order is declaration order** of the POCO (source-gen guarantee) — stable across runs
   and OSes.
3b. **`PropertyNamingPolicy` does NOT reach enum MEMBER names.** `UseStringEnumConverter` writes the
   raw C# member (`"Both"`, not `"both"`). Any enum stored in widget data therefore carries an
   explicit `[JsonStringEnumMemberName("...")]` on every member, or the payload's casing drifts from
   the rest of the file. DistrictCalendar's `Mode` is the only v1 case.
4. **Lists, never dictionaries.** No widget POCO may contain `Dictionary<,>`; enumeration order of
   a dictionary is not part of the contract and would break determinism.
5. **Unknown properties survive exactly like the rest of the model:** every widget POCO *and every
   nested record type* carries
   ```csharp
   [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
   ```
   A property written by a future build is read into that bag and written back on the next commit.
   No new mechanism, no new rules.
6. **When the type is unknown or the version is too new, nothing is decoded at all** —
   `WidgetBlock.Data` stays the raw `JsonElement` the file contained and is re-serialised verbatim
   (§2, §3). This is the strongest form of round-trip preservation and it is the default path.
7. `Write` returns a self-contained `JsonElement` (`JsonSerializer.SerializeToElement`); it is safe
   to store on the block. Elements obtained from a pooled `JsonDocument` must be `.Clone()`d before
   being handed to `SetWidgetDataCommand`.
8. All string formatting inside widget data and widget layout uses `CultureInfo.InvariantCulture`.
   `int.ToString()`/`string.Format` without an explicit culture are forbidden in
   `TrestleBoard.Widgets`.

---

## 2. Registry

In-process only; **no plugin loader in v1** (PLAN §5).

```csharp
namespace TrestleBoard.Widgets;

public sealed class WidgetRegistry
{
    /// <summary>The six built-ins, in BuiltInWidgets.All order — which is also gallery/menu order.</summary>
    public static WidgetRegistry CreateDefault();

    /// <summary>Throws InvalidOperationException on a duplicate TypeId — a programming error,
    /// caught at startup, never at the user's expense.</summary>
    public void Register(IWidgetDefinition definition);

    /// <summary>NEVER throws for an unrecognised id. This is the forward-compatibility path.</summary>
    public bool TryGet(string typeId, out IWidgetDefinition definition);

    public IReadOnlyList<IWidgetDefinition> All { get; }
}
```

### 2.1 Unknown `TypeId` — the graceful path (normative)

A document written by a future version may contain `widgetType: "memorialPanel"`. The app must keep
the user's newsletter intact. Exactly this happens, and nothing else:

| Layer | Behaviour |
|---|---|
| Container / serializer | Untouched. `WidgetBlock` deserialises fine; `Data` stays a raw `JsonElement`; unknown block properties land in `ExtraProperties`. Save re-emits both verbatim. |
| `WidgetLayoutProvider.TryLayout` | Returns `false`. |
| `DocumentRenderSource` | Draws the existing neutral placeholder (`RenderPlaceholder`) — identical pixels to the no-provider path. |
| `FrameEditorController` | The block selects, moves, resizes, restacks and deletes normally. It is a rectangle like any other. |
| `WidgetController.CanEdit` | `false`. |
| Object ▸ Edit this item… | Disabled, with the status line: *"This part of the newsletter was made by a newer version of TrestleBoard. You can move it, resize it or delete it, and it will be saved exactly as it is — but this version cannot change what is inside it."* |
| Export | The placeholder rectangle prints. Nothing is dropped from the file. |

The load path **never** throws for an unknown widget. The only place `UnsupportedFormatException` is
still raised is `MigrationRunner`, on `minReaderVersion` — unchanged from M2.

---

## 3. `dataVersion` handling

`WidgetBlock.DataVersion` is per-block and independent of the container's `formatVersion`.

### 3.1 Hook

```csharp
/// <summary>Upgrades raw JSON one version step. Operates on JsonNode, not the POCO, so an old
/// shape never needs a live CLR type (same rule as Core's IDocumentMigration).</summary>
bool TryMigrateStep(JsonNode data, int fromVersion, out JsonNode upgraded, out int toVersion);
```

### 3.2 When it runs

**Lazily, at first typed read** — inside `IWidgetDefinition.TryReadData`, which is called by the
layout provider and by `WizardSession.Create`. It does **not** run at file load: Core must not know
widgets exist, and opening a newsletter must not mark it dirty.

Algorithm in `TryReadData`:

1. `version = block.DataVersion`. If `version == CurrentDataVersion`, deserialize and return.
2. If `version > CurrentDataVersion` → **return false** (§3.3).
3. While `version < CurrentDataVersion`: call `TryMigrateStep`. If it returns `false`, return
   `false` (a gap in the chain is a bug, but it degrades to "read-only widget", never to data loss).
4. Deserialize the upgraded node.

The upgraded payload is **not written back to the document** by the read path. It is persisted only
when the user next commits an edit, at which point `SetWidgetDataCommand` carries
`newDataVersion = CurrentDataVersion` — so an untouched old widget stays byte-identical in the
file, and an edited one is silently modernised.

### 3.3 A version newer than the app knows

Treated exactly like an unknown `TypeId` (§2.1): placeholder render, editing refused with the same
plain-language message, payload preserved verbatim. **Normative: the app never attempts a
best-effort read of a future `dataVersion`.** A partially-understood officers table that silently
drops a column is worse than a grey box.

---

## 4. Widget layout

### 4.1 The seam types (declared in `TrestleBoard.Layout.Widgets`)

```csharp
public sealed record WidgetSeed(string LodgeName, int IssueMonth, int IssueYear, string MeetingRule);

public readonly record struct WidgetInfo(
    string TypeId, string DisplayName, string IconKey,
    int CurrentDataVersion, Core.Model.SizePt DefaultSizePt);

/// <summary>What the editor needs to know about widgets without referencing TrestleBoard.Widgets.</summary>
public interface IWidgetCatalog
{
    bool TryGetInfo(string typeId, out WidgetInfo info);
    JsonElement CreateEmptyData(string typeId, WidgetSeed seed);
}

public sealed record WidgetLayoutRequest(
    string WidgetTypeId,
    int DataVersion,
    JsonElement? Data,
    float WidthPt,
    WidgetStyleContext Style,
    WidgetTextShaper Shaper);

/// <summary>What DocumentRenderSource is handed at construction. Implemented in TrestleBoard.Widgets.</summary>
public interface IWidgetLayoutProvider
{
    /// <summary>False = "I do not know this widget type, or its dataVersion is newer than I am."
    /// The caller draws the neutral placeholder. Must NEVER throw.</summary>
    bool TryLayout(WidgetLayoutRequest request, out WidgetDrawList drawList);

    /// <summary>
    /// The widget's own fallback appearance, which the caller layers document styles over.
    /// Rendering cannot see `WidgetStyleDefaults` — that type lives in the leaf project — so the
    /// defaults cross the seam already shaped as a context.
    /// </summary>
    bool TryGetStyleDefaults(string widgetTypeId, out WidgetStyleContext defaults);
}
```

### 4.2 `IWidgetLayouter` (declared in `TrestleBoard.Widgets`)

```csharp
namespace TrestleBoard.Widgets;

public sealed record WidgetLayoutContext(
    object Data,                      // the typed POCO, already decoded and migrated
    float WidthPt,
    WidgetStyleContext Style,
    WidgetTextShaper Shaper);

public interface IWidgetLayouter
{
    /// <summary>Lays the widget out at the given width and reports its natural height.
    /// Pure: no I/O, no clock, no culture, no statics.</summary>
    WidgetDrawList Layout(WidgetLayoutContext context);
}
```

### 4.3 The draw list

```csharp
namespace TrestleBoard.Layout.Widgets;

public enum WidgetRuleOrientation { Horizontal, Vertical }

public abstract record WidgetDrawItem;

/// <summary>
/// Positioned glyphs, produced ONLY by WidgetTextShaper. Zero re-shaping at paint time. `Text` is
/// the string those glyphs came from: the draw list is otherwise write-only, and both the tests and
/// M9's screen-reader peers need to read a laid-out widget back.
/// </summary>
public sealed record WidgetTextItem(IReadOnlyList<PositionedGlyphRun> Runs, string Text = "") : WidgetDrawItem;

/// <summary>Axis-aligned hairline. Position = y (Horizontal) or x (Vertical); Start/End span
/// the other axis.</summary>
public sealed record WidgetRuleItem(
    WidgetRuleOrientation Orientation, float PositionPt, float StartPt, float EndPt,
    float WidthPt, uint ColorArgb) : WidgetDrawItem;

public sealed record WidgetFillItem(
    float LeftPt, float TopPt, float RightPt, float BottomPt, uint ColorArgb) : WidgetDrawItem;

public sealed class WidgetDrawList
{
    public IReadOnlyList<WidgetDrawItem> Items { get; }
    /// <summary>Natural content height at the requested width. Drives fit-to-content (§4.6).</summary>
    public float HeightPt { get; }
    public float WidthPt { get; }
    /// <summary>True when the widget holds no user content yet — Rendering draws the prompt (§8.4).</summary>
    public bool IsEmpty { get; }
    /// <summary>The prompt sentence the widget wants shown when empty, e.g.
    /// "Lodge officers — not filled in yet."</summary>
    public string EmptyPromptText { get; }
}
```

**Coordinates are widget-local**: origin `(0,0)` at the frame's top-left, y-down, typographic
points. The renderer translates. This is what makes a dragged widget re-use its cached draw list
instead of re-laying-out 60 times a second.

#### Why exactly these three primitives, and nothing more

| Primitive | Needed by | Why nothing else serves |
|---|---|---|
| Positioned text run | all six | Everything a trestle board widget prints is text. Carrying glyph ids + offsets (not strings) is what guarantees widget text and body text rasterise identically. |
| Axis-aligned rule | OfficersTable row/column rules, BirthdayList header underline, CommitteeList separators, DistrictCalendar table grid, CoverBanner divider, EventCard border (four rules) | Row/column separators cannot be faked with fills without sub-pixel rounding differences at hairline widths. |
| Filled rect | EventCard background, table header shading, CoverBanner band | A shaded header cannot be built from rules. |

**Explicitly excluded, with reasons:** diagonal or curved strokes (the largest cross-OS antialiasing
risk, and no widget needs one); stroked rects (four rules via `WidgetDrawListBuilder.Border` — one
fewer primitive to render identically); images (photos belong to `ImageFrame`, which already has a
non-destructive pipeline); gradients (theme tokens are flat colours); clip regions
(`WidgetDrawListRenderer` clips once, to the frame); dot leaders (an alignment convention, not a
primitive — see BirthdayList).

`WidgetDrawListBuilder` is a small mutable helper in Layout with
`Text/HRule/VRule/Fill/Border(rect,width,argb)` and a running `HeightPt` cursor, so six agents do
not each hand-roll list construction. It also carries `PrependFill`/`PrependBorder`: a card's
background is sized from the MEASURED content height, which is not known until the content is laid
out, and the renderer paints in list order — so the content goes down first and the background
slides underneath, rather than the widget shaping all of its text twice.

Rules are drawn **antialiased**. A 0.5pt hairline that lands between device pixels otherwise
vanishes on some rows and not others, and a table of half-drawn rules looks broken. Fills and rules
rasterise identically on every OS — only glyph scalers differ (docs/M1-spec.md §5) — so this costs
no determinism.

### 4.4 Shaping and measurement — how widgets share the existing utilities

**Normative: a widget never touches `HarfBuzzShaper`, `FontStore`, `SKFont` or `SKPaint` directly.
All text goes through `WidgetTextShaper`.** That is the enforcement mechanism for "widgets share the
existing shaping utilities" (PLAN §5) and for determinism.

```csharp
namespace TrestleBoard.Layout.Widgets;

public readonly record struct WidgetLineMetrics(
    float AscentPt, float DescentPt, float LeadingPt, float AverageCharWidthPt, float LineHeightPt);

public sealed class WidgetTextShaper
{
    public WidgetTextShaper(FontStore fonts, LayoutOptions? options = null);

    /// <summary>Advance width of a string, trailing whitespace included. The measurement primitive.</summary>
    public float MeasureWidthPt(string text, Input.CharacterStyle style);

    /// <summary>Ascent/descent/leading + lineHeight = lineSpacing × (ascent+descent+leading) —
    /// the SAME formula TextLayoutEngine uses, so widget rows and body lines sit on the same rhythm.</summary>
    public WidgetLineMetrics GetLineMetrics(Input.CharacterStyle style, float lineSpacing = 1f);

    /// <summary>One shaped, positioned run at a given origin/baseline. The only way to make a
    /// WidgetTextItem.</summary>
    public WidgetTextItem ShapeRun(string text, Input.CharacterStyle style,
        float originXPt, float baselineYPt);

    /// <summary>Shapes with alignment inside [leftPt, rightPt].</summary>
    public WidgetTextItem ShapeAligned(string text, Input.CharacterStyle style,
        float leftPt, float rightPt, float baselineYPt, Input.TextAlign align);

    /// <summary>Greedy first-fit wrap through LineBreakAnalyzer — the same UAX#14-lite rules the
    /// body text uses. firstLineWidthPt differs from restWidthPt for hanging indents.</summary>
    public IReadOnlyList<string> WrapToWidth(string text, Input.CharacterStyle style,
        float firstLineWidthPt, float restWidthPt);
}
```

**How a widget measures a string:** `Shaper.MeasureWidthPt(text, style)`. That is the whole answer —
column widths, hanging indents, "does the phone column fit" are all built from it. It shapes through
`HarfBuzzShaper` with the engine's `LayoutOptions` (ligatures/kerning) and sums `XAdvancePt`, so a
string measured by a widget and the same string laid out by `TextLayoutEngine` agree to the float.

`ShapeRun` constructs `PositionedGlyphRun`s (whose constructor is `internal` to Layout — hence the
shaper is the only possible source) with `Source = new SourceSpan(ownerId, -1, startChar, endChar)`.
**`ParagraphIndex == -1` marks "not story text"**; nothing in `StoryTextGeometry` or the caret
machinery ever sees these runs, and widget text is not editable in v1 (§11).

### 4.5 Determinism (normative — the same rule the layout engine already follows)

Same inputs → byte-identical draw list on Windows, Linux and macOS. Concretely, inside
`TrestleBoard.Widgets`:

- Text is measured and shaped only through `WidgetTextShaper` (bundled fonts, fixed HarfBuzz scale,
  explicit script/language).
- No `DateTime.Now`, `Random`, `Guid.NewGuid`, `Environment.*`, `Path.*`, `GetHashCode`,
  `Dictionary`/`HashSet` enumeration, `Parallel`, or `CultureInfo.CurrentCulture`.
- All sorting is by an explicit total order with an ordinal string tiebreak
  (`StringComparer.Ordinal`) — never `string.Compare` with a culture, never an unstable sort without
  a tiebreak.
- All arithmetic is `float`, computed non-accumulatively where the engine does (baselines are
  `top + n * lineHeight`, not `y += lineHeight` in a loop that also adds padding).
- Colours are `uint` ARGB, never `SKColor`.

Enforcement: `WidgetDrawListDump.Write(WidgetDrawList)` produces a canonical text rendering (item
kind, `F4`-formatted invariant coordinates, glyph ids, colours as `X8`). M7 uses it as an
**equality gate** — every widget laid out twice from the same inputs must dump identically — rather
than committing golden files. The reason: glyph ids and advances come from the bundled fonts through
the shared shaper, so a committed dump would be a second copy of what the raster baselines already
pin, and it would have to be regenerated on every font or metric change. The cross-OS proof is the
3-OS raster baselines plus the per-widget structural assertions in §10.3.

### 4.6 Reported height vs `WidgetBlock.FrameRect` (decision + justification)

**The frame does not grow by itself during layout. Content is clipped to the frame at paint time.
The frame is resized to the measured height only when the user acts, as part of the same command.**

Reasoning:

- The frame rect **is** the wrap exclusion rectangle. If layout silently grew it, the painted rect,
  the model rect and the exclusion rect would disagree for one frame, and reopening a document could
  reflow the whole issue without any user action.
- Layout must not mutate the document; only `IDocumentCommand` may (PLAN §4). Auto-growth in
  `EnsureLayout` would violate that outright.
- Widget height is a pure function of width, so there is no convergence problem — auto-fit is
  *safe*, just not *silent*.

Therefore:

1. `WidgetDrawListRenderer` clips to the frame rect. Overflowing content is cut, never spilled over
   neighbouring blocks.
2. `WidgetController` issues a `ResizeBlockCommand` to `HeightPt` **inside the same
   `CompositeCommand`** as (a) insert and (b) every wizard/grid commit. One undo step covers "the
   officers changed and the table got taller".
3. When the measured height still exceeds the frame (the user shrank it by hand), the block is
   reported through `DocumentRenderSource.GetWidgetOverflowBlockIds()`, `FrameOverlayRenderer` hangs
   the existing overset badge on it, and the status line reads: *"This does not all fit in its box.
   Choose Object ▸ Fit to contents, or drag the bottom edge down."*
4. `Object ▸ Fit to contents` (`Ctrl+Shift+Y`) is the manual escape hatch, one `ResizeBlockCommand`
   described "Fit to contents".
5. Width is authoritative and never changed by the widget.

---

## 5. Wrap integration

To the layout engine a widget is an opaque rectangle with `wrapMode=Rectangle` (PLAN §5).

**Confirmed, already true in-tree:** `DocumentLayoutAdapter.BuildExclusions` iterates `page.Blocks`,
tests `block.WrapMode == WrapMode.Rectangle && block.ZOrder > textBlock.ZOrder`, and inflates by
`WrapMarginPt`. It is type-blind, so a `WidgetBlock` already produces a correct exclusion.
`DocumentRenderSource.MarkBlockGeometryDirty` already narrows a widget move/resize to that page's
stories (M5 §4.2). `FrameEditorController` already selects, drags, resizes, restacks and deletes
widget blocks with no type test.

M7 must add exactly this and no more:

1. **Insert defaults**: every widget is inserted with `WrapMode = WrapMode.Rectangle` and
   `WrapMarginPt = 6f` (`FrameEditorController.DefaultWrapMarginPt`). Without this the acceptance
   criterion ("body text wrapped around it") is not met by default, and PLAN §5's promise is not
   delivered.
2. **Widget draw-list cache invalidation.** `DocumentRenderSource` gains `_widgetDrawLists` keyed by
   `(blockId, widthPt)` with a stored data fingerprint; `Invalidate` drops the entry for
   `scope.BlockId` on `BlockContent`/`BlockGeometry`, and clears the whole cache on any broad scope.
   Note: a `BlockContent` change on a widget currently falls into `Invalidate`'s `else` branch and
   sets `_allDirty` — **this is kept**, because a data change can change the measured height and
   therefore the exclusion.
3. **Widgets on a page master do not exclude.** `BuildExclusions` looks only at `page.Blocks`;
   master decoration never pushes text aside. Unchanged, restated so it is not "fixed" later by
   accident.
4. **Preview during drag**: `DocumentLayoutAdapter.EffectiveRect` already honours `_previewRects`
   for widgets, so live reflow around a dragged widget works with no new code. The draw list is
   position-independent (§4.3), so a drag re-uses the cached list; only a resize that changes
   **width** invalidates it.

---

## 6. The `WizardDefinition` schema

This is the accessibility centrepiece of the whole app (PLAN §6). It is **declarative**: one generic
`WizardWindow` renders every widget's wizard, and no widget ships a window.

### 6.1 Fields

```csharp
namespace TrestleBoard.Widgets.Wizards;

public enum WizardFieldKind
{
    Text,           // one-line free text
    MultiLineText,  // several lines, Enter inserts a newline
    Phone,          // "555-0100"
    MonthDay,       // "1/1" — month and day, never a year, never an age
    DayOfMonthRule, // "1st Tuesday" — parsed by Core.Text.MeetingRule
    Time,           // "6:30"
    Choice,         // one of a fixed list
}

public sealed record WizardChoice(string Value, string Label);

/// <summary>null = valid; otherwise the plain-language sentence shown to the user.</summary>
public delegate string? WizardFieldValidator(string value);

public sealed record WizardField(
    string Key,
    string Label,                                  // the question, sentence case, plain language
    WizardFieldKind Kind = WizardFieldKind.Text,
    bool IsOptional = false,
    string? HelpText = null,                       // one calm sentence under the box
    string? ExampleText = null,                    // watermark AND part of the error message
    IReadOnlyList<WizardChoice>? Choices = null,   // required iff Kind == Choice
    int MaxLength = 200,
    WizardFieldValidator? Validator = null);
```

`Time` is added beyond the brief's minimum because CoverBanner asks for two of them and "6:30" typed
as "630" is the single most likely elderly-user slip; a dedicated kind buys a real error sentence for
one enum member.

### 6.2 Validation and its error text

Order: (1) trim; (2) if empty → `IsOptional ? valid : Required`; (3) built-in rule for the `Kind`;
(4) the field's own `Validator`. First failure wins; only one message is ever shown per field.

```csharp
public static class WizardValidators
{
    public static string? Required(string label);        // "Please fill in {label}."
    public static string? Phone(string v);               // "Phone numbers look like 555-0100. Please check this one."
    public static string? MonthDay(string v);            // "Please type the month and day like 1/1 or 12/25."
    public static string? DayOfMonthRule(string v);      // "Please type something like 1st Tuesday."
    public static string? Time(string v);                // "Please type a time like 6:30."
    public static string? TooLong(string label, int max);// "{label} is too long. Please keep it under {max} letters."
}
```

Normative on wording: every message is a complete sentence, names the thing in the user's words, and
says what to do. The strings "invalid", "error", "required field" and "malformed" must not appear in
any user-visible wizard text.

`Phone` accepts digits with optional `-`, `.`, spaces and parentheses, 7–15 digits; it **normalises
nothing** — what the user typed is what prints (the examples show local 7-digit and full 10-digit
numbers side by side). `MonthDay` accepts `M/D` or `M-D`, 1–12 and 1–31, and stores
`(int Month, int Day)`.

### 6.3 Steps

Four step kinds cover all six widgets:

```csharp
public enum WizardStepKind { Fields, RecordList, Text, Review }

public enum WizardListPagination { AllRows, OneRowPerScreen }

public interface IWizardStep
{
    WizardStepKind Kind { get; }
    string Title { get; }            // the question or screen heading
    string? HelpText { get; }
    IReadOnlyList<WizardField> Fields { get; }

    // Untyped drive-time surface — the ONE place object is cast back to TData.
    string GetValue(object data, string fieldKey, int rowIndex);
    void   SetValue(object data, string fieldKey, int rowIndex, string value);
    int    GetRowCount(object data);
    string GetRowLabel(object data, int rowIndex);          // "" for non-list steps
    int    AddRow(object data);                             // -1 when not allowed
    bool   RemoveRow(object data, int rowIndex);
    bool   MoveRow(object data, int fromIndex, int toIndex);
    IReadOnlyList<WizardFieldError> Validate(object data, int rowIndex);
    IReadOnlyList<string> GetSummaryLines(object data);      // feeds the review step
}

public readonly record struct WizardFieldError(string FieldKey, int RowIndex, string Message);
```

| Kind | Type | Purpose | Constraints |
|---|---|---|---|
| `Fields` | `FieldsStep<TData>` | A fixed set of single-value questions | **At most 3 fields**, and only when they are one thought (dinner time + lodge time). Otherwise one field per step (PLAN §6: one question per screen). |
| `RecordList` | `RecordListStep<TData, TRow>` | A variable-length list of records — and, with `FixedRows`, a fixed-length one | `FixedRows` (row labels) ⇒ Add/Remove hidden, labels read-only; `AllowReorder` enables Move up/down; `Pagination` selects one screen for all rows or one screen per row |
| `Text` | `TextStep<TData>` | One free-text passage | Exactly one `MultiLineText` field |
| `Review` | `ReviewStep` | Mandatory last screen | Read-only summary + "Save it" |

The 12 officer positions are *"a list whose rows are pre-named"*, which is why `FixedRows` is a
property of `RecordListStep` rather than a fifth step kind — one renderer, one session code path,
one set of tests.

### 6.4 Binding to the data POCO — explicit accessors, no reflection

```csharp
public sealed record WizardFieldBinding<T>(
    string Key, Func<T, string> Get, Action<T, string> Set);

public sealed class FieldsStep<TData> : IWizardStep
{
    public FieldsStep(string title, string? helpText,
        IReadOnlyList<(WizardField Field, WizardFieldBinding<TData> Binding)> fields);
}

public sealed class RecordListStep<TData, TRow> : IWizardStep
{
    public RecordListStep(
        string title, string? helpText,
        Func<TData, IList<TRow>> getRows,
        Func<TRow> createRow,
        IReadOnlyList<(WizardField Field, WizardFieldBinding<TRow> Binding)> fields,
        IReadOnlyList<string>? fixedRows = null,
        WizardListPagination pagination = WizardListPagination.AllRows,
        bool allowReorder = false,
        Func<TRow, string>? rowLabel = null,
        string addButtonText = "Add another",
        string emptyText = "Nothing here yet. Press “Add another” to start.");
}
```

**Decision: explicit `Func`/`Action` pairs, not reflection over property names.** Justification: a
lambda pair is compile-checked (a renamed property fails the build, not the user's evening), is
trim/AOT-safe, and expresses the string↔value conversion where it belongs — `MonthDay` binds one
field to two `int` properties, which no property-name binder can do. The cost is one line per field.

The generic types disappear at drive time behind `IWizardStep`'s untyped surface; each typed step
performs exactly one `(TData)data` cast, inside itself.

```csharp
public sealed class WizardDefinition
{
    /// <summary>Appends a ReviewStep if the caller did not supply one; throws if a Review step
    /// is present anywhere but last, or if more than one exists.</summary>
    public WizardDefinition(string title, string introText, IReadOnlyList<IWizardStep> steps);

    public string Title { get; }        // "Lodge officers"
    public string IntroText { get; }    // one sentence on the first screen
    public IReadOnlyList<IWizardStep> Steps { get; }
}
```

### 6.5 `WizardSession` — the headless driver

The entire wizard is unit-testable with no window. `WizardWindow` holds no wizard state whatsoever;
it is a renderer over this object.

```csharp
namespace TrestleBoard.Widgets.Wizards;

public sealed class WizardSession
{
    /// <summary>existingData == null (or unreadable) → definition.CreateEmptyData(seed).
    /// Otherwise the decoded, migrated POCO — this is the re-edit pre-fill (§7).</summary>
    public static WizardSession Create(IWidgetDefinition definition, JsonElement? existingData,
        int dataVersion, WidgetSeed seed);

    public IWidgetDefinition Definition { get; }
    public string Title { get; }
    public string IntroText { get; }

    // ---- navigation ------------------------------------------------------------------
    public int ScreenCount { get; }            // rows on OneRowPerScreen steps count individually
    public int ScreenIndex { get; }
    public IWizardStep CurrentStep { get; }
    public int CurrentRowIndex { get; }        // -1 for non-paginated screens
    public string ScreenTitle { get; }         // step title, or "Worshipful Master" on a row screen
    public bool IsFirstScreen { get; }
    public bool IsReviewScreen { get; }
    public string ProgressText { get; }        // "Step 4 of 15"

    public bool TryGoNext();                   // validates the CURRENT screen only
    public bool TryGoTo(int screenIndex);      // used by the review screen's "change this"
    public void GoBack();

    // ---- values ----------------------------------------------------------------------
    public string GetValue(string fieldKey, int rowIndex = -1);
    public void SetValue(string fieldKey, string value, int rowIndex = -1);
    public IReadOnlyList<WizardFieldError> Errors { get; }   // current screen, after TryGoNext
    public bool IsDirty { get; }                             // drives the Cancel confirmation

    // ---- rows ------------------------------------------------------------------------
    public int RowCount { get; }
    public string GetRowLabel(int rowIndex);
    public int AddRow();                       // -1 when the step forbids it
    public bool RemoveRow(int rowIndex);
    public bool MoveRow(int from, int to);

    // ---- finish ----------------------------------------------------------------------
    public IReadOnlyList<string> ReviewLines { get; }        // from every step's GetSummaryLines
    public bool TryCommit(out JsonElement data, out int dataVersion,
        out IReadOnlyList<WizardFieldError> errors);          // validates EVERY step
    public string UndoLabel { get; }           // "Edit lodge officers" — the Edit-menu sentence

    /// <summary>Switches a OneRowPerScreen step to AllRows for this session only (§6.7).</summary>
    public void ShowAllRows();
}
```

Normative session behaviour:

- The session mutates only its own private POCO. **It never touches the document.** `TryCommit`
  returns data; the caller executes the command (§7).
- `TryGoNext` validates only the current screen so a user is never blocked by a question they have
  not reached. `TryCommit` validates all of them and, on failure, the caller navigates to
  `errors[0]`'s screen.
- An optional field left blank is valid everywhere, including in `TryCommit`.
- Removing the last row of a required list is allowed; the widget simply renders empty (§8.4).
- `ScreenCount` is stable during a session unless rows are added/removed on a `OneRowPerScreen`
  step, in which case it is recomputed and `ScreenIndex` is clamped.

### 6.6 What `WizardWindow` renders (App)

One window, `TrestleBoard.App.Dialogs.WizardWindow(WizardSession session)`. It reads and writes only
through the session.

**Chrome, identical on every screen:**

- Window `MinWidth=900`, `MinHeight=700`, resizable, `WindowStartupLocation=CenterOwner`,
  `AutomationProperties.Name = $"{Title} — {ProgressText}"`.
- Header: screen title, `FontSize=24`, bold, wrapped; `ProgressText` beneath at `FontSize=16`.
- `HelpText` beneath that at `FontSize=18`, wrapped, `MaxWidth=760`.
- Error panel (only when `Errors` is non-empty): a warning glyph **and** the word "Check this"
  **and** red text — colour is never the only signal (PLAN §6). Each message is one line.
- Footer buttons, all `FontSize=20`, `MinHeight=44`, `MinWidth=160`, icon+text (never icon-only):
  `◀ Back` (left, disabled on the first screen), spacer, `Cancel`, `Next ▶` / `Save it` (right,
  `IsDefault=true`).

**Per step kind:**

| Kind | Rendering |
|---|---|
| `Fields` | One block per field: a label `TextBlock` (18pt) above a `TextBox`/`ComboBox` (`FontSize=20`, `MinHeight=44`, `Width=560`), then the field's `HelpText` (16pt, grey). `ExampleText` becomes the `Watermark` **in addition to** the label — never instead of it. First box focused on entry. |
| `Text` | One `TextBox`, `AcceptsReturn=true`, `TextWrapping=Wrap`, `MinHeight=260`, `FontSize=20`, focused on entry. Enter inserts a newline; the footer's default button is still `Next` but Enter does not trigger it while this box has focus. |
| `RecordList` / `AllRows` | A `ScrollViewer` over one row-panel per row: the row label (when `FixedRows`) as an 18pt `TextBlock` of fixed width, then one 44pt-high input per field, then `Remove` (and `▲`/`▼` when `AllowReorder`). Below: a full-width `+ Add another` button, 44pt. `emptyText` is shown when `RowCount == 0`. A new row's first input receives focus. |
| `RecordList` / `OneRowPerScreen` | Identical to `Fields`, with `ScreenTitle` = the row label. The footer gains `Show all at once`, calling `ShowAllRows()`. |
| `Review` | A read-only `ItemsControl` of `ReviewLines` at 18pt, each with a `Change this` button navigating via `TryGoTo`; the primary button reads `Save it`. |

**Keyboard (complete, no gesture-only path):**

| Gesture | Effect |
|---|---|
| `Tab` / `Shift+Tab` | Reading order: header → fields in order → row buttons → footer |
| `Enter` | `Next` / `Save it`, except inside a `MultiLineText` box |
| `Alt+B` / `Alt+N` / `Alt+S` | Back / Next / Save it |
| `Esc` | Cancel; when `IsDirty`, a plain-language confirm: *"Throw away what you typed? Nothing has been added to the newsletter yet."* |
| `Alt+A` | Add another (list screens) |
| `Ctrl+Enter` | Jump to the review screen when `TryCommit` would succeed |

**Screen readers:** every input gets `AutomationProperties.SetName(input, field.Label)` and
`SetHelpText(input, field.HelpText)`; the label `TextBlock` is wired with `SetLabeledBy`. On every
screen change the header `TextBlock` is made focusable and focused, then unfocusable — Avalonia has
no live region, and this is the only reliable way to make Narrator/VoiceOver announce the new
question. This is normative.

### 6.7 Screen budget

OfficersTable's `OneRowPerScreen` produces 14 screens (one heading, twelve offices, one review). That is the intended accessibility trade (one
question at a time), and it is mitigated three ways, all normative: `Show all at once` on every
paginated screen; `Object ▸ Edit list…` as the fast re-edit path (§7); and carry-forward
(PLAN §5/§7), which means the 14-screen run happens roughly once a year.

---

## 7. Re-editing

Two entry points, one data path, one command.

### 7.1 Wizard, pre-filled

`WizardSession.Create(definition, block.Data, block.DataVersion, seed)` decodes and migrates the
existing payload into the POCO. Every field's `Get` binding therefore returns the stored value on
first render. When the payload is null, unreadable, or a newer `dataVersion`, the wizard is not
offered at all (§2.1/§3.3) — it never silently starts empty over real data.

### 7.2 Grid re-editor

**Decision: the grid editor is a second *view* over the same `WizardSession`, not a second session
type.** `WidgetGridWindow(WizardSession session)` renders every `RecordList` step in `AllRows` mode,
back to back, in one scrolling page with Add / Remove / Move up / Move down and a single `Save`
button that calls `TryCommit`. `Fields` and `Text` steps render above it as a compact form.

This makes "both emit the SAME data POCO through ONE command" true by construction rather than by
discipline, and it halves the code and the tests.

### 7.3 The command shape

Reuse `SetWidgetDataCommand`. Wrap it in a `CompositeCommand` — following the M6 precedent exactly
(`PhotoController.Execute` wraps `SetImageRecipeCommand` so the Edit menu can say what the user did):

```csharp
_session.Execute(new CompositeCommand(
    undoLabel,                                                     // "Edit lodge officers"
    new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
    [
        new SetWidgetDataCommand(blockId, data, dataVersion),
        new ResizeBlockCommand(blockId, fittedRect),                // only when the height changed
    ]));
```

The composite earns its keep twice: `SetWidgetDataCommand.Description` is the generic "Edit widget",
and fit-to-content must land in the same undo step as the data change (§4.6). `undoLabel` comes from
`WizardSession.UndoLabel`, which is `$"Edit {definition.DisplayName.ToLowerInvariant()}"` unless the
widget overrides it.

### 7.4 Undo granularity (normative)

- **One wizard run = one undo step.** Nothing is executed until `Save it`; `Cancel` executes nothing
  at all.
- **One grid `Save` = one undo step**, regardless of how many rows changed.
- Two consecutive wizard runs are two undo steps: `SetWidgetDataCommand.TryMerge` stays `false`, and
  `CompositeCommand` never merges.
- Inserting a widget and then filling it in are **two** undo steps (§8.2), so `Ctrl+Z` after a
  cancelled wizard removes the empty widget.

---

## 8. Insert flow

### 8.1 Menus and shortcuts

A new top-level `_Insert` menu is added between `F_ormat` and `Ob_ject`. The existing photo item
moves into it unchanged (same `x:Name`, handler and gesture) so "Insert" means insert.

| Menu item | Gesture | Enabled when |
|---|---|---|
| Insert ▸ `Insert a _picture…` | `Ctrl+Shift+P` | a document is open (moved, unchanged) |
| Insert ▸ `_Lodge officers` | — | a document is open |
| Insert ▸ `_Birthdays` | — | " |
| Insert ▸ `_Committees` | — | " |
| Insert ▸ `_District calendar` | — | " |
| Insert ▸ `_Announcement box` | — | " |
| Insert ▸ `_Cover heading` | — | " |
| Object ▸ `_Edit this item…` (wizard) | `Ctrl+Shift+E` | an editable widget is selected |
| Object ▸ `Edit _list…` (grid) | `Ctrl+Shift+G` | a selected widget has ≥1 `RecordList` step |
| Object ▸ `Fit to _contents` | `Ctrl+Shift+Y` | a widget is selected |

The six direct items exist so a keyboard user never has to go through a picker. A gallery dialog is
NOT built in M7: with exactly six widgets, six named menu items are more discoverable than a picker,
and one fewer dialog is one fewer thing to make accessible. It returns if the set grows (§11).
Bindings verified free of collisions with the existing ones (`Ctrl+Shift+ T/W/P/F/A/L/K`,
`Ctrl+ Z/Y/B/I/X/C/V/O/E/+/-/0/1/[/]`).

### 8.2 Placement and sequence

```csharp
namespace TrestleBoard.Editing;

public sealed class WidgetController
{
    public WidgetController(DocumentSession session, DocumentRenderSource layout, IWidgetCatalog catalog);

    public bool IsWidget(string? blockId);
    public bool CanEdit(string? blockId);            // false for unknown type / newer dataVersion
    public string? GetWidgetType(string? blockId);
    public string? StatusMessage { get; }

    /// <summary>Adds an empty widget inside the page margins, selects it, returns the block id.</summary>
    public string InsertWidget(int pageIndex, string typeId);

    /// <summary>The single commit path used by BOTH the wizard and the grid (§7.3).</summary>
    public bool ApplyWidgetData(string blockId, JsonElement data, int dataVersion, string undoLabel);

    public bool FitToContents(string blockId);
    public event EventHandler? Changed;
}
```

`InsertWidget` mirrors `FrameEditorController.AddTextFrame`:

- rect = `info.DefaultSizePt` at the page's top-left margin corner plus an 18pt-per-existing-block
  cascade, clamped inside the margins;
- `ZOrder` = top of the page; `WrapMode = Rectangle`; `WrapMarginPt = 6`;
- `Data = catalog.CreateEmptyData(typeId, seed)`, `DataVersion = info.CurrentDataVersion`;
- deterministic id `widget-1`, `widget-2`, … (no clock, no Guid — same rule as M5/M6);
- executed as one
  `CompositeCommand("Add lodge officers", PageStructure, [AddBlockCommand, ResizeBlockCommand(fitted)])`.

The shell then **immediately opens the wizard**. Justification: inserting means "I want to fill this
in", and the widget is already visible on the page while the user answers. Cancelling leaves an
empty, clearly-labelled widget; the status line reads *"Nothing was filled in yet. Press Ctrl+Z to
take it back off the page."*

### 8.3 Data at insert: EMPTY, with structural scaffolding only (decision)

**Normative: no widget is ever inserted carrying a person's name, phone number or date.** PLAN §0 is
absolute, and a fake "A. Placeholder, 555-0100" that survives to print is worse than a visible blank.

What each widget starts with:

| Widget | At insert |
|---|---|
| OfficersTable | Heading `"Lodge Officers"`, the **12 position labels** in order, every name and phone blank. Positions are structure, not people. |
| BirthdayList | Heading `"Birthdays"`, **zero rows**. |
| CommitteeList | Heading `"Committees"`, default notice text, **zero rows**. |
| DistrictCalendar | Heading `"22nd District"`, `Mode = MeetingDays`, **zero lodges, zero events**. |
| EventCard | All text blank. |
| CoverBanner | `LodgeName` and `MeetingRule` copied from the **open document's own metadata** via `WidgetSeed` (the user's own data, not a shipped default); heading `"STATED COMMUNICATION"`; date and times blank. |

Shipped templates (M9) follow the same rule: placeholder *prompts*, never placeholder *people*.

### 8.4 The empty state

`WidgetDrawList.IsEmpty` is `true` when the widget holds no user content, and `EmptyPromptText`
carries a sentence such as `"Lodge officers — not filled in yet."`.

`WidgetDrawListRenderer` draws that sentence, centred, in a grey 10pt sans face, **only when
`DocumentRenderSource.ShowEmptyPrompts` is true**. That property defaults to `true` (screen) and
`DocumentPdfExporter.Export` sets it to `false` for the duration of an export, so a prompt can never
be printed. The flag is a **render-time** decision, not part of the draw list: the widget cache is
therefore unaffected by it, and the prompt is implemented once instead of six times.

---

## 9. The six widgets

Common to all: `[JsonExtensionData]` bag on every POCO and nested record; all strings default to
`""`; all lists default to `[]`; layout reads `WidgetStyleContext` for faces and colours and never
hardcodes a font family.

`WidgetStyleContext` (Layout), produced by `Rendering.WidgetStyleResolver` from `Theme` +
`TableStyleDef` (`block.TableStyleRef`) + `FrameStyleDef` (`block.FrameStyleRef`) + the definition's
`StyleDefaults`, with document styles winning over defaults:

```csharp
public sealed record WidgetStyleContext(
    Input.CharacterStyle Display,   // Cinzel — banners
    Input.CharacterStyle Heading,   // table headings
    Input.CharacterStyle Body,      // rows
    Input.CharacterStyle Emphasis,  // bold rows / dates
    Input.CharacterStyle Small,     // notes, closing lines
    float LineSpacing,
    uint RuleArgb, float RuleWidthPt,
    uint? FillArgb, uint? StrokeArgb, float StrokeWidthPt, float PaddingPt,
    IReadOnlyDictionary<string, uint> ColorTokens)
{
    public uint Color(string token, uint fallback);
}
```

### 9.1 OfficersTable — `typeId "officersTable"`

```csharp
public sealed class OfficersTableData
{
    public string Heading { get; set; } = "Lodge Officers";
    public List<OfficerEntry> Officers { get; set; } = [];
    public string VacantText { get; set; } = "(vacant)";
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>The 12 fixed positions, in the printed order. Never reordered, never trimmed.</summary>
    public static readonly IReadOnlyList<string> StandardPositions =
    [
        "Worshipful Master", "Senior Warden", "Junior Warden",
        "Senior Deacon", "Junior Deacon", "Senior Steward", "Junior Steward",
        "Treasurer", "Secretary", "Chaplain", "Tiler", "Marshall",
    ];
}

public sealed class OfficerEntry
{
    public string Position { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

Compatible with `Core.Tests.Fixtures` (`{"officers":[{"position","name","phone"}]}`) — that fixture
must keep deserialising unchanged.

**Wizard:** `FieldsStep` (Heading) → `RecordListStep<OfficersTableData, OfficerEntry>` with
`FixedRows = StandardPositions`, `Pagination = OneRowPerScreen`, fields `Name` (Text, **optional** —
vacancies are real) and `Phone` (Phone, **optional**) → `Review`.

**Layout rules:**

1. Heading in `Heading` style, left-aligned, followed by a full-width rule at `RuleWidthPt`.
2. Three columns: position (left), name (left), phone (right-aligned at the widget's right edge).
3. Column widths: position = `max(MeasureWidthPt(position))` + 8pt gutter, clamped to 45% of the
   width; phone = `max(MeasureWidthPt(phone))` + 8pt gutter; name takes the remainder.
4. **The phone column collapses when no officer has a phone** (computed from data, never a setting),
   and the other two widen.
5. **Vacancies keep their row**: an entry whose `Name` is blank prints `VacantText` in the name
   column. Rows are never collapsed or reordered — the printed table always has 12 rows in
   `StandardPositions` order. An entry whose `Position` is not in `StandardPositions` prints after
   the twelve, in stored order (forward compatibility with a lodge that adds an office).
6. A name too wide for its column **wraps** within the column with a hanging indent equal to the
   position column's width; nothing is ever truncated or ellipsised — a printed newsletter must not
   silently drop a brother's name.
7. A hairline rule between rows; row height = `GetLineMetrics(Body, LineSpacing).LineHeightPt` × the
   row's line count.

**Acceptance detail:** a headless wizard run entering 12 fictional officers (2 with blank names, 1
with no phone) produces a draw list with 1 heading run + 12 rows in `StandardPositions` order,
`"(vacant)"` in exactly the 2 blank rows, 12 hairline `WidgetRuleItem`s, and a phone column present
(because one phone exists); a second fixture with **no** phones at all yields two columns and a wider
name column.

### 9.2 BirthdayList — `typeId "birthdayList"`

```csharp
public sealed class BirthdayListData
{
    public string Heading { get; set; } = "Birthdays";
    public List<BirthdayEntry> Entries { get; set; } = [];
    public string? ClosingNote { get; set; }   // "Miss anyone? Let us know."
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class BirthdayEntry
{
    public string Name { get; set; } = "";
    public int Month { get; set; }
    public int Day { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

Month and day only — **no year, no age**, by design (the source data is a member roster).

**Wizard:** `FieldsStep` (Heading) → `RecordListStep` (`AllRows`, `AllowReorder = false`) with `Name`
(Text, required) and `Date` (MonthDay, required, bound to `Month`+`Day`) → `FieldsStep`
(ClosingNote, optional) → `Review`.

**Layout rules:**

1. Narrow single column — the signature wrap-around case. Default frame `150 × 220` pt.
2. **One canonical format: `Name` left, `M/D` right-aligned at the column's right edge**, on one
   line, no leaders. This ends the per-issue drift observed across the examples (`Name M/D`,
   `M/D Name`, ALL CAPS, …). Names are printed exactly as typed — no case normalisation.
3. **Sorted by day**: total order `(Month, Day, Name ordinal)`. Sorting happens in the layouter, not
   in the data, so the stored order is whatever the user typed and re-editing shows their own order
   back.
4. Dates format as `{Month}/{Day}` with no leading zeros, invariant culture.
5. Designed for 6–20 rows; more is allowed and simply overflows (§4.6). A name too wide wraps to a
   second line with the date staying on the first.
6. `ClosingNote`, when present, prints after a 6pt gap in `Small` style, wrapped.

**Acceptance detail:** entries entered as 12/25, 1/1, 7/4 render in the order 1/1, 7/4, 12/25; every
date matches `^\d{1,2}/\d{1,2}$`; a 20-row fixture reports a `HeightPt` within the default frame.

### 9.3 CommitteeList — `typeId "committeeList"`

```csharp
public sealed class CommitteeListData
{
    public string Heading { get; set; } = "Committees";
    public string? NoticeText { get; set; } =
        "Please notify the Worshipful Master to be added or removed.";
    public List<CommitteeEntry> Committees { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class CommitteeEntry
{
    public string Name { get; set; } = "";
    public List<string> Members { get; set; } = [];
    /// <summary>Free-text tail for cases like "and team".</summary>
    public string? Note { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

**Wizard:** `FieldsStep` (Heading, NoticeText — 2 fields, one thought) → `RecordListStep` (`AllRows`,
`AllowReorder = true`) with `Committee name` (Text, required), `Members` (**MultiLineText, one name
per line**), `Note` (Text, optional) → `Review`.

Members are entered one per line rather than comma-separated: a comma-separated list of seven names
in a single-line box is the most error-prone input in the whole app, and one-per-line reads back
cleanly on re-edit. The layouter joins with `", "`.

**Layout rules:**

1. Heading, then `NoticeText` in `Small` style, then a rule.
2. One logical row per committee: `"{Name}: {member, member, …}{, Note}"`, where `Name:` is in
   `Emphasis` and the rest in `Body`.
3. **Hanging indent**: continuation lines start at
   `min(MeasureWidthPt(Name + ": "), 0.40 × WidthPt)`. Wrapping uses
   `Shaper.WrapToWidth(text, Body, firstLineWidth, restWidth)` so committee rows break on the same
   rules as body prose.
4. A committee with no members prints the name and colon only — never an error, never a placeholder
   name.
5. 6pt between committees; no inter-row rules (the examples are a plain list).

**Acceptance detail:** a committee whose member list is long enough to wrap produces ≥2
`WidgetTextItem`s whose second and later runs share an `OriginX` equal to the computed hanging
indent, and whose first run starts at 0.

### 9.4 DistrictCalendar — `typeId "districtCalendar"`

**Decision: one widget with a mode, not two widgets.** Justification: both flavours come from the
same upstream source ("District 22 Trestle Board"), the static meeting-days table must survive
carry-forward while the dated events are replaced, and an issue frequently shows both. One block
that can print either or both keeps the two halves of one concept together and makes the mode a
single wizard question. The cost — two blocks each in a different mode duplicate the unused half —
is accepted and noted; carry-forward copies both blocks regardless.

```csharp
public enum DistrictCalendarMode { MeetingDays, Events, Both }

public sealed class DistrictCalendarData
{
    public string Heading { get; set; } = "22nd District";
    public DistrictCalendarMode Mode { get; set; } = DistrictCalendarMode.MeetingDays;
    public string LodgesHeading { get; set; } = "Meeting days";
    public List<DistrictLodgeEntry> Lodges { get; set; } = [];
    public string EventsHeading { get; set; } = "Coming up";
    public List<DistrictEventEntry> Events { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class DistrictLodgeEntry
{
    public string LodgeName { get; set; } = "";     // "Sample Lodge 000"
    public string MeetingRule { get; set; } = "";   // "1st Tuesday"
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

public sealed class DistrictEventEntry
{
    public string DateText { get; set; } = "";      // "July 12" — free text, prints as typed
    public string? HostText { get; set; }           // "Sample Lodge 000"
    public string Description { get; set; } = "";   // "Masters and Wardens meeting"
    public string? TimesText { get; set; }          // "Meal 6:30, work 7:30"
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

**Wizard:** `FieldsStep` (Heading, Mode as `Choice`: "Just the meeting-days table" / "Just the dated
events" / "Both") → `RecordListStep` (Lodges: `LodgeName` Text, `MeetingRule` DayOfMonthRule) →
`RecordListStep` (Events, `AllowReorder = true`: `DateText`, `HostText` optional, `Description`,
`TimesText` optional) → `Review`. Steps whose list is irrelevant to the chosen `Mode` are skipped by
the session (`IWizardStep` participation is recomputed after each `SetValue` on the mode field).

**Layout rules:**

1. `MeetingDays`: two-column table (lodge left, meeting day right), built by the shared
   `TableLayouter`, sorted by `(MeetingRule.Ordinal, MeetingRule.Day, LodgeName ordinal)` — which is
   exactly the order the printed district table uses. An unparseable rule sorts last and prints as
   typed.
2. `Events`: one hanging-indent row per event, in **stored order** (dates are free text and cannot
   be sorted reliably; the wizard offers Move up/down instead). Row text =
   `"{DateText} — {HostText} — {Description}"` with missing parts and their separators omitted, then
   `TimesText` on a continuation line in `Small` style when present.
3. `Both`: the table, a 8pt gap, `EventsHeading` in `Heading` style, then the events.
4. `MeetingRule.ToString()` is used for display, so "first tuesday" typed by the user prints
   "1st Tuesday" — the one place a widget normalises input, because it is a machine-readable rule.

**Acceptance detail:** a fixture with lodges entered in scrambled order renders sorted
`1st Monday, 1st Tuesday, 2nd Monday, …`; `Mode = Events` emits no table rules at all; `Mode = Both`
emits both sections with the events in entry order.

### 9.5 EventCard — `typeId "eventCard"`

```csharp
public sealed class EventCardData
{
    public string Title { get; set; } = "";
    public string? WhenText { get; set; }
    public string? WhereText { get; set; }
    public string BodyText { get; set; } = "";
    public bool ShowBorder { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

**Wizard:** `FieldsStep` (Title) → `FieldsStep` (WhenText, WhereText — both optional, one thought) →
`TextStep` (BodyText) → `Review`.

**Layout rules:**

1. Background `WidgetFillItem` in `FillArgb` (default `ColorTokens["widgetAccent"]`, fallback
   `0xFFF4F1E8`), inset 0.
2. When `ShowBorder`, four rules at `StrokeWidthPt` (default 1) in `StrokeArgb` (default
   `0xFF6B5B3E`) via `WidgetDrawListBuilder.Border`.
3. Content inset by `PaddingPt` (default 10) on all sides.
4. Title centred in `Heading`; `WhenText`/`WhereText` centred in `Emphasis`, **each omitted entirely
   — no blank line — when absent**; a rule under them when either is present; body wrapped in
   `Body`, left-aligned.
5. `HeightPt` = content height + 2 × padding.

**Acceptance detail:** the draw list contains exactly one `WidgetFillItem`; with `ShowBorder` it
contains exactly four more `WidgetRuleItem`s than without; a fixture with both `WhenText` and
`WhereText` blank has a `HeightPt` strictly smaller than one with them filled, and contains no empty
text run.

### 9.6 CoverBanner — `typeId "coverBanner"`

```csharp
public sealed class CoverBannerData
{
    public string LodgeName { get; set; } = "";
    public string HeadingText { get; set; } = "STATED COMMUNICATION";
    /// <summary>"1st Tuesday" — the rule M9's date bumping consumes (Core.Text.MeetingRule).</summary>
    public string MeetingRule { get; set; } = "";
    /// <summary>What actually prints, e.g. "July 7th". M9 recomputes it from MeetingRule.</summary>
    public string MeetingDateText { get; set; } = "";
    public string? DinnerTimeText { get; set; }   // "6:30"
    public string? WorkTimeText { get; set; }     // "7:30"
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
```

Storing **both** the rule and the printed date is deliberate: v1 prints exactly what the user typed
(no surprises), while M9's start-from-last-month has the machine-readable rule it needs to recompute
the date without re-parsing English prose. `MeetingRule` is validated with
`Core.Text.MeetingRule.TryParse`.

**Wizard:** `FieldsStep` (LodgeName, HeadingText) → `FieldsStep` (MeetingRule [DayOfMonthRule,
optional], MeetingDateText [Text]) → `FieldsStep` (DinnerTimeText, WorkTimeText — both Time, both
optional) → `Review`.

**Layout rules:**

1. `LodgeName` centred in `Display` (Cinzel), wrapped to at most two lines.
2. `HeadingText` centred in `Heading`, printed exactly as typed (no case transformation — the
   lodge's ALL-CAPS voice is theirs).
3. A full-width rule.
4. `MeetingDateText` centred in `Emphasis`.
5. Times line, centred in `Body`: both present → `"Dinner at {Dinner} · Lodge opens at {Work}"`;
   only dinner → `"Dinner at {Dinner}"`; only work → `"Lodge opens at {Work}"`; **neither → the line
   is omitted entirely** (March/April issues have no times).
6. Default frame `504 × 130` pt (full text width of a US-Letter master).

**Acceptance detail:** `MeetingRule = "1st Tuesday"` survives save→reload and
`Core.Text.MeetingRule.TryParse` yields `(First, Tuesday)`; a fixture with both times blank produces
exactly one fewer text run than the same fixture with both filled.

---

## 10. Testing

All fixture data fictional (PLAN §0): "A. Placeholder", "B. Sample", "C. Example",
"555-0100"…"555-0111", "Sample Lodge 000", "Placeholder Lodge No. 000".

### 10.1 What each project gates

| Project | Gates |
|---|---|
| `Core.Tests` | The command surface and the file format. New: a `CompositeCommand`-wrapped widget edit in `CommandsUnderTest` (the reflection gate `EveryCommandTypeHasIdentityCoverage` already fails the build otherwise); a round-trip test proving a `WidgetBlock` with an **unknown** `widgetType`, unknown nested payload properties and unknown block properties survives save→load→save byte-identically; the `MeetingRule` parse/format/resolve table. |
| `Widgets.Tests` | The whole widget contract, headless: registry, codec, migrations, `WizardSession`, and one golden draw-list dump per widget. This is where the six parallel agents' work is proved. |
| `Layout.Tests` | The seam types: `WidgetTextShaper` measurement agrees with `TextLayoutEngine` for the same string+style; `WrapToWidth` produces the same breaks as the engine for a zero hanging indent; `WidgetDrawListDump` stability. |
| `Editing.Tests` | `WidgetController`: insert geometry, undo granularity, fit-to-content, refusal paths. |
| `App.HeadlessTests` | The PLAN §11-M7 acceptance run, end to end through the shell. |
| `Rendering.SnapshotTests` | Pixels: the six widgets on a page, the wrap fixture, and the no-provider placeholder invariance. |

### 10.2 The acceptance criterion, assertion by assertion

> *A headless test drives the Officers wizard end to end and produces a styled table on the page with
> body text wrapped around it; re-edit pre-fills; widget data survives save/reload; all fixtures
> fictional.*

`App.HeadlessTests/WidgetShellTests.cs`, one test method per clause, sharing a fixture that boots the
window, `OpenSample()`, and invokes `Insert ▸ Lodge officers`:

| Clause | Assertions |
|---|---|
| *drives the Officers wizard end to end* | `session.ScreenCount == 14`; for each of the 12 rows, `SetValue("name", …)`/`SetValue("phone", …)` then `TryGoNext()` returns `true`; `TryGoNext` on a screen with a malformed phone returns `false` and `Errors[0].Message` equals the `WizardValidators.Phone` sentence; the review screen's `ReviewLines.Count == 13`; `TryCommit` succeeds. |
| *produces a styled table on the page* | After commit, `source.TryGetWidgetDrawList(blockId, out var list)` yields 12 rows in `OfficersTableData.StandardPositions` order, the two blank-name rows print `"(vacant)"`, and there are 12 `WidgetRuleItem`s whose `ColorArgb`/`WidthPt` come from the document's `TableStyleDef` (not the widget's defaults) — proving styling is resolved, not hardcoded. |
| *with body text wrapped around it* | The body story's `LayoutResult`: at least one `LineBox` whose band overlaps the widget's inflated rect has `Segments.Count == 2`, and no segment's `XRange` intersects `widgetRect` inflated by `WrapMarginPt`. Additionally `RemoveBlockCommand` on the widget restores `Segments.Count == 1` on that band — the wrap is caused by *this* block. |
| *re-edit pre-fills* | `WizardSession.Create(definition, block.Data, block.DataVersion, seed)` returns `GetValue("name", row)` equal to the entered value for all 12 rows and `GetValue("phone", row)` equal for all rows including the blank one; `session.IsDirty == false` before any `SetValue`. |
| *widget data survives save/reload* | `TboardContainer.Save` → `Load` → the reloaded `WidgetBlock.Data` produces a byte-identical `WidgetData.Write(TryReadData(...))`, and `DataVersion` is unchanged; a second Save produces a byte-identical archive entry for `document.json`. |
| *all fixtures fictional* | A repo-wide test in `Widgets.Tests` scans every string literal in the widget fixtures and defaults for the patterns "555-01xx", "Placeholder", "Sample", "Example", "Fictional" and asserts that no phone-shaped literal outside the `555-01xx` range appears. (A blunt instrument, deliberately — it is the automated half of PLAN §12's privacy gate.) |
| *one wizard run = one undo step* | `session.CanUndo`, `session.UndoDescription == "Edit lodge officers"`, one `Undo()` restores the empty widget (not the pre-insert state), a second `Undo()` removes the block. |

### 10.3 Additional required tests

**Widgets.Tests**
- Registry: `TryGet("noSuchWidget")` returns `false` and throws nothing; duplicate `Register` throws;
  `All` order equals `BuiltInWidgets.All` order.
- Codec: POCO → `JsonElement` → POCO round-trips for all six with fully-populated fictional data; a
  payload carrying an unknown property survives the round trip in `ExtraProperties` and is
  re-emitted.
- Versions: a two-step synthetic migration chain upgrades v1→v3; a missing step degrades to
  `TryReadData == false` without throwing; `DataVersion = CurrentDataVersion + 1` returns `false`
  and leaves the input `JsonElement` untouched.
- `WizardSession`: per widget — required/optional behaviour, every built-in validator's message,
  Add/Remove/Move on list steps, `ShowAllRows` collapsing screen count, `TryCommit` reporting the
  *first* invalid screen, `IsDirty` transitions, and `Cancel` (dropping the session) leaving no
  observable effect.
- Layouters: per-widget structural assertions for (a) empty data, (b) typical data, (c) the stress
  case (12 officers / 20 birthdays / a wrapping committee / both district modes / a border-less card
  / no times), plus the `WidgetDrawListDump` equality gate — the same inputs laid out twice must
  dump byte-identically.
- Determinism guard: a test that reflects over `TrestleBoard.Widgets` and fails if any type
  references `DateTime`, `Random`, `Guid`, `Dictionary<,>` in a public data POCO, or
  `CultureInfo.CurrentCulture`.

**Editing.Tests**
- Insert lands inside the page margins with `WrapMode.Rectangle` and `WrapMarginPt == 6`; a second
  insert cascades by 18pt; block ids are `widget-1`, `widget-2`.
- `ApplyWidgetData` emits exactly one `CompositeCommand`, whose `Description` is the supplied label
  and whose children are `[SetWidgetDataCommand, ResizeBlockCommand]` when the height changed and
  `[SetWidgetDataCommand]` when it did not.
- `CanEdit` is `false` for an unknown type and for a newer `dataVersion`; `ApplyWidgetData` on either
  returns `false` and executes nothing (`session.CanUndo` unchanged).
- `FitToContents` is one `ResizeBlockCommand`; a hand-shrunk widget appears in
  `GetWidgetOverflowBlockIds()`.

**Rendering.SnapshotTests**
- `widgets-gallery-page1.png`: one page carrying all six widgets with fictional data — the styling
  regression net.
- `widget-officers-wrap.png`: the acceptance page (officers table + body text wrapping).
- `widget-empty-prompts.png`: empty widgets with `ShowEmptyPrompts = true`, plus a **non-snapshot**
  assertion that the same page exported to PDF contains none of the prompt strings.
- **Placeholder invariance:** a test rendering a document containing widgets through
  `DocumentRenderSource.Create` with **no** provider, asserting the output is byte-identical to the
  M3 grey-placeholder rendering. This is the guard on "M3 snapshot baselines must not move".

### 10.4 Baselines

Raster baselines in this repo are **per-OS** (`Baselines/windows|linux|macos/`), because Skia
rasterises glyphs through platform code paths. New M7 baselines must be **promoted from CI
artifacts**, not hand-authored: generate your own OS's with `TRESTLEBOARD_UPDATE_BASELINES=1`, push,
take the other two from the failing CI run's uploaded *actual* PNGs, and commit all three in one
commit. There are no golden dump files to promote: `WidgetDrawListDump` is used as a same-process
equality gate (§4.5).

---

## 11. Deferrals — what M7 explicitly does NOT do

| Item | Why |
|---|---|
| Runtime plugin loading / third-party widgets | PLAN §1 non-goal; the registry is in-process (PLAN §5). |
| Editing text *inside* a widget on the canvas (caret, selection) | The wizard and the grid are the editing paths; widget runs deliberately carry `ParagraphIndex == -1` so the caret machinery never sees them. Carried over from M4's deferral list. |
| Widget text flowing across frames, or a widget as a text-flow target | Widgets are opaque rectangles (PLAN §5). |
| Non-rectangular wrap around widgets | M5 deferral, unchanged. |
| Automatic frame growth during layout | §4.6 — fit-to-content is a command, not a side effect. |
| A style editor for `TableStyleDef`/`FrameStyleDef` | M7 resolves and consumes styles; authoring them is M9's template work. |
| Carry-forward ("New issue from last month") and meeting-date recomputation | PLAN §11-M9. M7 ships the `MeetingRule` type and the `MeetingRule`/`MeetingDateText` split that M9 consumes. |
| Import of officers/birthdays from CSV or a paste | Not in PLAN §11-M7; the wizard is the entry path. |
| Per-widget icons beyond an `IconKey` string | The App maps keys to glyphs; the icon set itself is M9 chrome work. Icons are never the only label. |
| Widget-level dot leaders, column rules with cap styles, gradients, images inside widgets | Not needed by the six; see §4.3's exclusion list. |
| A widget gallery/picker dialog | Six widgets, six named menu items (§8.1). A picker returns if the set grows. |
| Localisation of wizard text | English-only, like the rest of v1. |
| A dedicated `SetWidgetDataCommand` description per widget | Solved by the `CompositeCommand` label (§7.3), following the M6 precedent. |
| Automation peers exposing widget internals to a screen reader on the canvas | PLAN §11-M9 owns the canvas automation-peer pass; M7's a11y obligation is the wizard, which is fully covered here. |

---

## 12. Parallel-implementation plan (six agents, zero collisions)

### 12.1 Where six agents would collide, and the rule that prevents it

| Collision | Rule |
|---|---|
| `WidgetRegistry.CreateDefault()` / `BuiltInWidgets.All` — every agent wants to add a line | The **scaffold pass** writes `BuiltInWidgets.cs` with all six entries **and six compiling stub definition classes** before the parallel phase starts. After that the file is **frozen**; each widget agent only fills in its own class. |
| Shared layout helpers (`TableLayouter`, `WidgetLayoutHelpers`, `WidgetDrawListBuilder`) | Written by the scaffold pass, **frozen** during the parallel phase. An agent needing a change requests it from the integrating session rather than editing. |
| Style defaults — six agents adding styles to the document `StyleSheet` | **No widget ever writes into `Document.StyleSheet`.** Each definition exposes its own `WidgetStyleDefaults` record in its own file; `Rendering.WidgetStyleResolver` layers document styles over defaults. Zero shared edits by construction. |
| `Core.Tests.CommandsUnderTest` | One entry (the composite widget edit), added by the integration pass only. |
| Menu XAML | The six insert items are added once by the integration pass. |
| Test files | One test file per widget, named after it. Shared fictional fixtures live in `Widgets.Tests/WidgetTestData.cs`, written by the scaffold pass and **frozen**. |
| Snapshot baselines | Exactly two new raster fixtures, both authored by the integration pass after all six land. No widget agent touches `Baselines/`. |

### 12.2 File layout (each path owned by exactly one agent)

```
src/TrestleBoard.Layout/Widgets/          ← scaffold, then frozen
    WidgetDrawList.cs  WidgetDrawListBuilder.cs  WidgetDrawListDump.cs
    WidgetTextShaper.cs  WidgetStyleContext.cs
    IWidgetLayoutProvider.cs  IWidgetCatalog.cs
src/TrestleBoard.Core/Text/MeetingRule.cs ← scaffold
src/TrestleBoard.Widgets/                 ← scaffold, then frozen
    IWidgetDefinition.cs  WidgetDefinition.cs  WidgetRegistry.cs
    BuiltInWidgets.cs  WidgetData.cs  WidgetStyleDefaults.cs
    WidgetLayoutProvider.cs  WidgetCatalog.cs
    Wizards/  WizardDefinition.cs IWizardStep.cs Steps.cs WizardField.cs
              WizardValidators.cs WizardSession.cs
    Layout/   TableLayouter.cs WidgetLayoutHelpers.cs
    Builtins/OfficersTable/     ← agent 1   (Data, Definition, Layouter, Json)
    Builtins/BirthdayList/      ← agent 2
    Builtins/CommitteeList/     ← agent 3
    Builtins/DistrictCalendar/  ← agent 4
    Builtins/EventCard/         ← agent 5
    Builtins/CoverBanner/       ← agent 6
src/TrestleBoard.Rendering/WidgetDrawListRenderer.cs, WidgetStyleResolver.cs  ← integration
src/TrestleBoard.Editing/WidgetController.cs                                  ← integration
src/TrestleBoard.App/Dialogs/WizardWindow.cs, WidgetGridWindow.cs             ← integration
tests/Widgets.Tests/WidgetTestData.cs                                         ← scaffold, frozen
tests/Widgets.Tests/{OfficersTable,BirthdayList,…}Tests.cs                     ← one per agent
```

### 12.3 Implementation order

1. **Scaffold** — `Core.Text.MeetingRule`; the Layout seam (`WidgetDrawList`, builder, dump,
   `WidgetTextShaper`, `WidgetStyleContext`, the two interfaces); `Widgets` contract, registry,
   codec, `BuiltInWidgets` with six stubs; the wizard schema and `WizardSession`; shared layouters;
   `WidgetTestData`. Everything compiles and `Widgets.Tests` is green with six stubs rendering an
   empty draw list.
2. **Rendering + Editing integration** — `WidgetStyleResolver`, `WidgetDrawListRenderer`,
   `DocumentRenderSource` provider/cache/queries, `PageRenderer.DrawRun` made public,
   `ShowEmptyPrompts`, `WidgetController`. The placeholder-invariance snapshot test goes green here,
   before any widget exists.
3. **The six widgets, in parallel** — one agent per `Builtins/*` folder plus its own test file.
   Nothing outside its folder is edited.
4. **App shell** — `WizardWindow`, `WidgetGridWindow`, the Insert menu, the Object-menu edit items,
   status-line wiring.
5. **Acceptance + snapshots** — `App.HeadlessTests/WidgetShellTests.cs`; the two new raster fixtures;
   baseline promotion from CI.
6. **Post-milestone** — `/graphify . --update`; `/wiki:ingest` the M7 decisions.

---

## 13. Implementation notes (recorded after the build)

Things the build discovered that the contract above could not have known, kept here so the next
milestone does not rediscover them.

- **The wizard's screen budget is 14, not 15.** One heading screen, twelve office screens and the
  review screen. There is no separate intro screen: `IntroText` renders on the first screen instead,
  which saves every wizard a click.
- **No italic Source Sans is bundled** (`BundledFonts` ships Serif Regular/Bold/Italic/BoldItalic,
  Sans Regular/Bold, Cinzel Regular) and the engine has no system fallback by design (PLAN.md §1).
  The `Small` role and the empty-widget prompt therefore use serif italic. A widget that asks for an
  unbundled face throws at `FontStore.Resolve`, which is the intended loud failure.
- **BirthdayList sets its own type size** (9pt, 1.05 spacing) through `WidgetStyleDefaults`, because
  it is the narrow column body text flows beside. A long list overflows its default box and is fixed
  by growing the box — never by shrinking the type further. That is the rule for every widget: the
  readers are elderly.
- **`App.HeadlessTests` runs serially** (`[assembly: CollectionBehavior(DisableTestParallelization)]`)
  and the widget tests deliberately do NOT `Show()` their window. Avalonia initializes once per
  process; a shown window leaves a queued menu measure that the headless session's teardown runs
  against an already-disposed font manager ("fonts:SystemFonts was not present"), which fails the
  test that queued it and every test after it.
- **A widget payload is re-indented by the container.** `JsonElement.WriteTo` re-emits it inside
  `document.json` at the surrounding indent, so a save/reload comparison must be on the DECODED
  value, never on `GetRawText()`.
