using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Imaging;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Documents;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Rendering;

/// <summary>
/// A laid-out, drawable document: layout runs lazily, then <see cref="RenderPage"/> replays it
/// onto ANY SKCanvas — editor surface, PNG, or PDF page — which is what makes the viewer and
/// the export pixel-compatible (PLAN.md §1).
///
/// M4 (docs/M4-spec.md §7.3): the source is INVALIDATING. <see cref="Invalidate"/> only marks
/// dirty and raises <see cref="LayoutInvalidated"/>; <c>EnsureLayout</c> runs at the top of
/// paint and geometry queries, so a keystroke burst costs one relayout per painted frame.
/// Text/story changes relayout only the affected story; geometry changes relayout everything
/// (exclusion rects may have moved).
/// </summary>
public sealed class DocumentRenderSource : IDisposable
{
    private readonly Document _document;
    private readonly TextLayoutEngine _engine;
    private readonly Dictionary<string, byte[]> _assets;
    private readonly Dictionary<string, DecodedImage?> _decodedImages = new(StringComparer.Ordinal);
    private readonly RecipeCache _recipeCache = new();
    private readonly Dictionary<string, LayoutResult> _layoutsByStory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoryTextGeometry> _geometriesByStory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameLayout> _frameLayoutsByBlockId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string StoryId, int FrameIndex)> _framesByBlockId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _storiesByPageId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RectPt> _previewRects = new(StringComparer.Ordinal);
    private readonly List<string> _oversetTailBlockIds = [];
    private readonly Dictionary<string, (float WidthPt, WidgetDrawList List)> _widgetDrawLists =
        new(StringComparer.Ordinal);
    private readonly List<string> _widgetOverflowBlockIds = [];
    private readonly FontStore _fonts;
    private readonly LayoutOptions? _layoutOptions;
    private readonly IWidgetLayoutProvider? _widgets;
    private WidgetTextShaper? _widgetShaper;
    private List<StoryLayoutPlan> _plans = [];
    private readonly HashSet<string> _dirtyStories = new(StringComparer.Ordinal);
    private bool _allDirty = true;
    private bool _isOverset;
    private bool _disposed;

    private DocumentRenderSource(
        Document document,
        Dictionary<string, byte[]> assets,
        TextLayoutEngine engine,
        FontStore fonts,
        LayoutOptions? options,
        IWidgetLayoutProvider? widgets)
    {
        _document = document;
        _assets = assets;
        _engine = engine;
        _fonts = fonts;
        _layoutOptions = options;
        _widgets = widgets;
    }

    public int PageCount => _document.Pages.Count;

    /// <summary>True when any story ran out of room in its frame chain (overset indicator).
    /// Lays out first if needed — callers must not have to paint to get a truthful answer.</summary>
    public bool IsOverset
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLayout();
            return _isOverset;
        }
    }

    /// <summary>Times the full relayout path ran; the M4 laziness tests assert on this.</summary>
    public int RelayoutCount { get; private set; }

    /// <summary>Times a story was actually handed to the layout engine. The M5 drag tests assert
    /// that a page-local drag only re-lays-out that page's stories (docs/M5-spec.md §4.2).</summary>
    public int StoryLayoutCount { get; private set; }

    /// <summary>Raised by <see cref="Invalidate"/>. Handlers must NOT lay out — geometry is
    /// recomputed lazily on the next paint or query.</summary>
    public event EventHandler? LayoutInvalidated;

    public static DocumentRenderSource Create(
        Document document,
        IReadOnlyDictionary<string, byte[]> assets,
        FontStore fonts,
        LayoutOptions? options = null,
        IWidgetLayoutProvider? widgets = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(fonts);
        return new DocumentRenderSource(
            document,
            assets.ToDictionary(StringComparer.Ordinal),
            new TextLayoutEngine(fonts, options),
            fonts,
            options,
            widgets);
    }

    /// <summary>Creates a source wired to a session: every executed/undone command marks the
    /// matching dirty scope (docs/M4-spec.md §7.3).</summary>
    public static DocumentRenderSource CreateEditable(
        Document document,
        IReadOnlyDictionary<string, byte[]> assets,
        FontStore fonts,
        DocumentSession session,
        LayoutOptions? options = null,
        IWidgetLayoutProvider? widgets = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        DocumentRenderSource source = Create(document, assets, fonts, options, widgets);
        session.Changed += (_, e) => source.Invalidate(e.Scope);
        return source;
    }

    /// <summary>Marks dirty and notifies; no layout work happens here.</summary>
    public void Invalidate(ChangeScope scope)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (scope.Kind is ChangeKind.Text or ChangeKind.StoryStructure && scope.StoryId is { } storyId)
        {
            _dirtyStories.Add(storyId);
        }
        else if (scope.Kind is ChangeKind.BlockGeometry && scope.BlockId is { } blockId)
        {
            MarkBlockGeometryDirty(blockId);
        }
        else
        {
            // Page structure changes the frame set; content may change intrinsic size; play safe.
            _allDirty = true;
        }

        // Widget geometry AND widget data both change the drawn box, and a data change can change
        // the measured height and therefore the exclusion (docs/M7-spec.md §5.2).
        if (scope.BlockId is { } widgetBlockId)
        {
            _widgetDrawLists.Remove(widgetBlockId);
        }
        else
        {
            _widgetDrawLists.Clear();
        }

        LayoutInvalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Installs (or clears, with null) a drag preview rect for one block — layout and painting
    /// both honour it without the document being touched (docs/M5-spec.md §4.1). Only the stories
    /// with a frame on that block's page relayout, which is what keeps a drag at 60fps (§4.2).
    /// </summary>
    public void SetGeometryPreview(string blockId, RectPt? rect)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(blockId);
        if (rect is { } value)
        {
            if (_previewRects.TryGetValue(blockId, out RectPt existing) && existing == value)
            {
                return;
            }

            _previewRects[blockId] = value;
        }
        else if (!_previewRects.Remove(blockId))
        {
            return;
        }

        MarkBlockGeometryDirty(blockId);
        LayoutInvalidated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while any drag preview is installed.</summary>
    public bool HasGeometryPreview => _previewRects.Count > 0;

    /// <summary>The rect a block is currently drawn at: its preview while dragging, else its own.</summary>
    public RectPt GetEffectiveRect(string blockId)
    {
        (_, Block block) = _document.FindBlock(blockId);
        return DocumentLayoutAdapter.EffectiveRect(block, _previewRects);
    }

    /// <summary>
    /// Last frame of every chain whose text ran out of room — where the overset badge hangs
    /// (docs/M5-spec.md §8.4).
    /// </summary>
    public IReadOnlyList<string> GetOversetTailBlockIds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        return _oversetTailBlockIds;
    }

    /// <summary>True when the block is a text frame (the canvas needs it for mode arbitration).</summary>
    public bool IsTextBlock(string blockId) => _document.FindBlock(blockId).Block is TextBlock;

    /// <summary>
    /// Empty-widget prompts are drawn on screen and never printed. The PDF exporter turns this off
    /// for the duration of an export (docs/M7-spec.md §8.4).
    /// </summary>
    public bool ShowEmptyPrompts { get; set; } = true;

    /// <summary>The shaper widgets measure and draw through; one per source, metrics cached.</summary>
    private WidgetTextShaper WidgetShaper => _widgetShaper ??= new WidgetTextShaper(_fonts, _layoutOptions);

    /// <summary>The laid-out widget at its current frame width, or false when it cannot be laid out.</summary>
    public bool TryGetWidgetDrawList(string blockId, [NotNullWhen(true)] out WidgetDrawList? drawList)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(blockId);
        drawList = null;
        if (_document.FindBlock(blockId).Block is not WidgetBlock widget)
        {
            return false;
        }

        RectPt rect = DocumentLayoutAdapter.EffectiveRect(widget, _previewRects);
        return TryLayoutWidget(widget, rect.Width, out drawList);
    }

    /// <summary>Natural height of a widget at a width — what "Fit to contents" resizes to (§4.6).</summary>
    public bool TryMeasureWidgetHeight(string blockId, float widthPt, out float heightPt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        heightPt = 0f;
        if (_document.FindBlock(blockId).Block is not WidgetBlock widget
            || !TryLayoutWidget(widget, widthPt, out WidgetDrawList? drawList))
        {
            return false;
        }

        heightPt = drawList.HeightPt;
        return true;
    }

    /// <summary>
    /// Height a widget WOULD have with a payload that is not on the document yet. This is what lets
    /// a wizard commit and its fit-to-contents resize be one command instead of two (§7.3); the
    /// hypothetical layout is never cached.
    /// </summary>
    public bool TryMeasureWidgetHeight(
        string blockId,
        JsonElement? data,
        int dataVersion,
        float widthPt,
        out float heightPt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        heightPt = 0f;
        if (_widgets is null || _document.FindBlock(blockId).Block is not WidgetBlock widget)
        {
            return false;
        }

        if (!_widgets.TryGetStyleDefaults(widget.WidgetType, out WidgetStyleContext defaults))
        {
            return false;
        }

        var request = new WidgetLayoutRequest(
            widget.WidgetType,
            dataVersion,
            data,
            widthPt,
            WidgetStyleResolver.Resolve(_document, widget, defaults),
            WidgetShaper);

        if (!_widgets.TryLayout(request, out WidgetDrawList list))
        {
            return false;
        }

        heightPt = list.HeightPt;
        return true;
    }

    /// <summary>
    /// Widgets whose content does not fit the box the user gave them — the overset badge hangs on
    /// these exactly as it does on an overflowing story (docs/M7-spec.md §4.6).
    /// </summary>
    public IReadOnlyList<string> GetWidgetOverflowBlockIds()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _widgetOverflowBlockIds.Clear();
        if (_widgets is null)
        {
            return _widgetOverflowBlockIds;
        }

        foreach (Page page in _document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block is not WidgetBlock widget)
                {
                    continue;
                }

                RectPt rect = DocumentLayoutAdapter.EffectiveRect(widget, _previewRects);
                if (TryLayoutWidget(widget, rect.Width, out WidgetDrawList? list)
                    && list.HeightPt > rect.Height + 0.5f)
                {
                    _widgetOverflowBlockIds.Add(widget.Id);
                }
            }
        }

        return _widgetOverflowBlockIds;
    }

    /// <summary>Topmost block containing the point, or null (docs/M5-spec.md §1.1).</summary>
    public string? HitTestBlock(int pageIndex, float xPt, float yPt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (Block block in _document.Pages[pageIndex].Blocks.OrderByDescending(b => b.ZOrder))
        {
            RectPt rect = DocumentLayoutAdapter.EffectiveRect(block, _previewRects);
            if (xPt >= rect.X && xPt <= rect.Right && yPt >= rect.Y && yPt <= rect.Bottom)
            {
                return block.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Geometry changes are page-local: <c>BuildExclusions</c> only ever looks at blocks on the
    /// same page, so only stories with a frame there can be affected — plus the moved block's own
    /// chain, which may continue onto other pages.
    /// </summary>
    private void MarkBlockGeometryDirty(string blockId)
    {
        if (_plans.Count == 0 || _storiesByPageId.Count == 0)
        {
            _allDirty = true;
            return;
        }

        Page? page = _document.Pages.FirstOrDefault(p => p.Blocks.Any(b => b.Id == blockId));
        if (page is null || !_storiesByPageId.TryGetValue(page.Id, out HashSet<string>? stories))
        {
            _allDirty = true;
            return;
        }

        foreach (string storyId in stories)
        {
            _dirtyStories.Add(storyId);
        }

        if (_framesByBlockId.TryGetValue(blockId, out (string StoryId, int FrameIndex) owner))
        {
            _dirtyStories.Add(owner.StoryId);
        }
    }

    public Core.Model.SizePt GetPageSize(int pageIndex)
    {
        Page page = _document.Pages[pageIndex];
        return _document.GetMaster(page.MasterRef).Size;
    }

    /// <summary>
    /// The laid-out lines of one text frame. Public so tests can assert that body text actually
    /// split around a block — the acceptance criterion for wrap (docs/M7-spec.md §10.2).
    /// </summary>
    public bool TryGetFrameLayout(string blockId, [NotNullWhen(true)] out FrameLayout? layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        return _frameLayoutsByBlockId.TryGetValue(blockId, out layout);
    }

    public bool TryGetStoryGeometry(string storyId, out StoryTextGeometry geometry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        return _geometriesByStory.TryGetValue(storyId, out geometry!);
    }

    public bool TryGetPageIndexOfBlock(string blockId, out int pageIndex)
    {
        for (int i = 0; i < _document.Pages.Count; i++)
        {
            if (_document.Pages[i].Blocks.Any(b => b.Id == blockId))
            {
                pageIndex = i;
                return true;
            }
        }

        pageIndex = -1;
        return false;
    }

    /// <summary>Page point → text position via the topmost text frame under (or near) the
    /// point (docs/M4-spec.md §1.4 step 1).</summary>
    public bool TryHitTestText(
        int pageIndex,
        float xPt,
        float yPt,
        out TextHit hit,
        out string blockId,
        float slopPt = 0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        hit = default;
        blockId = "";

        TextBlock? best = null;
        float bestDistance = float.MaxValue;
        foreach (Block block in _document.Pages[pageIndex].Blocks
            .Where(b => b is TextBlock)
            .OrderByDescending(b => b.ZOrder))
        {
            var text = (TextBlock)block;
            if (!_framesByBlockId.ContainsKey(text.Id))
            {
                continue;
            }

            RectPt r = DocumentLayoutAdapter.EffectiveRect(text, _previewRects);
            bool contains = xPt >= r.X && xPt <= r.Right && yPt >= r.Y && yPt <= r.Bottom;
            if (contains)
            {
                best = text;
                break;
            }

            if (slopPt > 0f)
            {
                float dx = Math.Max(Math.Max(r.X - xPt, 0f), xPt - r.Right);
                float dy = Math.Max(Math.Max(r.Y - yPt, 0f), yPt - r.Bottom);
                float distanceSq = (dx * dx) + (dy * dy);
                if (distanceSq <= slopPt * slopPt && distanceSq < bestDistance)
                {
                    bestDistance = distanceSq;
                    best = text;
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        (string storyId, int frameIndex) = _framesByBlockId[best.Id];
        if (!_geometriesByStory.TryGetValue(storyId, out StoryTextGeometry? geometry)
            || !geometry.TryHitTest(frameIndex, xPt, yPt, out hit))
        {
            return false;
        }

        blockId = best.Id;
        return true;
    }

    public void RenderPage(SKCanvas canvas, int pageIndex, uint backgroundArgb = 0xFFFFFFFF)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);
        EnsureLayout();

        Page page = _document.Pages[pageIndex];
        PageMaster master = _document.GetMaster(page.MasterRef);
        canvas.DrawColor(new SKColor(backgroundArgb));

        // Master blocks are background decoration and ALWAYS sit beneath page content
        // (PLAN.md §2); z-order sorts within each collection, not across them.
        // OrderBy is stable, so document order breaks z ties.
        foreach (Block block in master.Blocks.OrderBy(b => b.ZOrder))
        {
            RenderBlock(canvas, block);
        }

        foreach (Block block in page.Blocks.OrderBy(b => b.ZOrder))
        {
            RenderBlock(canvas, block);
        }
    }

    // ---- Lazy relayout (docs/M4-spec.md §7.3) -----------------------------------------------

    private void EnsureLayout()
    {
        if (!_allDirty && _dirtyStories.Count == 0 && _plans.Count > 0)
        {
            return;
        }

        RelayoutCount++;
        // Plan rebuild is cheap (style resolution + text concat); the engine is the cost, so
        // clean stories reuse their cached LayoutResult.
        _plans = [.. DocumentLayoutAdapter.BuildPlans(_document, _previewRects)];
        var liveStories = new HashSet<string>(_plans.Select(p => p.StoryId), StringComparer.Ordinal);
        _layoutsByStory.Keys.Where(k => !liveStories.Contains(k)).ToList().ForEach(k =>
        {
            _layoutsByStory.Remove(k);
            _geometriesByStory.Remove(k);
        });

        _isOverset = false;
        _frameLayoutsByBlockId.Clear();
        _framesByBlockId.Clear();
        _storiesByPageId.Clear();
        _oversetTailBlockIds.Clear();
        foreach (StoryLayoutPlan plan in _plans)
        {
            bool dirty = _allDirty
                || _dirtyStories.Contains(plan.StoryId)
                || !_layoutsByStory.ContainsKey(plan.StoryId);
            if (dirty)
            {
                StoryLayoutCount++;
                LayoutResult result = _engine.Layout(plan.Request);
                _layoutsByStory[plan.StoryId] = result;
                _geometriesByStory[plan.StoryId] = new StoryTextGeometry(plan, result);
            }

            LayoutResult layout = _layoutsByStory[plan.StoryId];
            _isOverset |= layout.IsOverset;
            if (layout.IsOverset && plan.Placements.Count > 0)
            {
                _oversetTailBlockIds.Add(plan.Placements[^1].BlockId);
            }

            for (int i = 0; i < layout.Frames.Count; i++)
            {
                _frameLayoutsByBlockId[plan.Placements[i].BlockId] = layout.Frames[i];
                _framesByBlockId[plan.Placements[i].BlockId] = (plan.StoryId, i);
            }

            foreach (FramePlacement placement in plan.Placements)
            {
                if (!_storiesByPageId.TryGetValue(placement.PageId, out HashSet<string>? stories))
                {
                    stories = new HashSet<string>(StringComparer.Ordinal);
                    _storiesByPageId[placement.PageId] = stories;
                }

                stories.Add(plan.StoryId);
            }
        }

        _allDirty = false;
        _dirtyStories.Clear();
    }

    // ---- Block rendering (unchanged from M3) ------------------------------------------------

    private void RenderBlock(SKCanvas canvas, Block block)
    {
        // Drag previews move the painted block too, not just the text flowing around it.
        RectPt rect = DocumentLayoutAdapter.EffectiveRect(block, _previewRects);
        switch (block)
        {
            case TextBlock text:
                if (_frameLayoutsByBlockId.TryGetValue(text.Id, out FrameLayout? layout))
                {
                    PageRenderer.RenderFrame(canvas, layout);
                }

                break;
            case ImageFrame image:
                RenderImage(canvas, image, rect);
                break;
            case ShapeBlock shape:
                RenderShape(canvas, shape, rect);
                break;
            case WidgetBlock widget:
                RenderWidget(canvas, widget, rect);
                break;
            default:
                throw new NotSupportedException($"Unknown block type: {block.GetType().Name}");
        }
    }

    /// <summary>
    /// Paints a photo through the M6 pipeline: decode once (EXIF baked in), render the recipe
    /// through the cache, then place it per the block's fit mode (docs/M6-spec.md §2).
    /// </summary>
    private void RenderImage(SKCanvas canvas, ImageFrame image, RectPt frameRect)
    {
        SKRect dest = ToRect(frameRect);
        DecodedImage? decoded = ResolveImage(image.AssetRef);
        if (decoded is null)
        {
            RenderPlaceholder(canvas, frameRect);
            return;
        }

        ImageRecipeSpec recipe = ToSpec(image.Recipe);
        int budget = PixelBudget(frameRect);
        SKImage rendered = _recipeCache.GetOrAdd(
            image.AssetRef,
            recipe,
            budget,
            () => ImagePipeline.Render(decoded, recipe, budget));

        (SKRect src, SKRect placed) = ImagePipeline.Fit(
            new SKSize(rendered.Width, rendered.Height), dest, ToFitMode(image.Fit));

        int save = canvas.Save();
        canvas.ClipRect(dest);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(rendered, src, placed, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        canvas.RestoreToCount(save);
    }

    /// <summary>
    /// Render resolution is derived from the FRAME, never the zoom: a snapshot must not change
    /// because the window happens to be a different size.
    /// </summary>
    private static int PixelBudget(RectPt frameRect) =>
        Math.Clamp((int)MathF.Ceiling(Math.Max(frameRect.Width, frameRect.Height) * 2f), 64, 2048);

    /// <summary>Core's recipe → the Imaging-side record (docs/M6-spec.md §0).</summary>
    public static ImageRecipeSpec ToSpec(ImageRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return new ImageRecipeSpec(
            recipe.CropNormalized is { } crop
                ? new NormalizedRect(crop.X, crop.Y, crop.Width, crop.Height)
                : null,
            recipe.RotationSteps,
            recipe.Brightness,
            recipe.Contrast,
            recipe.Saturation,
            recipe.AutoLevels,
            recipe.AutoLevelsPerChannel ? AutoLevelsMode.PerChannel : AutoLevelsMode.Luminance);
    }

    private static ImageFitMode ToFitMode(ImageFit fit) => fit switch
    {
        ImageFit.Contain => ImageFitMode.Contain,
        ImageFit.Stretch => ImageFitMode.Stretch,
        _ => ImageFitMode.Cover,
    };

    private static void RenderShape(SKCanvas canvas, ShapeBlock shape, RectPt frameRect)
    {
        SKRect rect = ToRect(frameRect);
        if (shape.FillArgb is { } fill)
        {
            using var fillPaint = new SKPaint { Color = new SKColor(fill), IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawRect(rect, fillPaint);
        }

        if (shape.StrokeArgb is { } stroke && shape.StrokeWidthPt > 0f)
        {
            using var strokePaint = new SKPaint
            {
                Color = new SKColor(stroke),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = shape.StrokeWidthPt,
            };
            canvas.DrawRect(rect, strokePaint);
        }
    }

    /// <summary>
    /// Paints a widget through the injected provider (docs/M7-spec.md §0.1). With no provider — or
    /// an unknown widget type, or a dataVersion newer than this build — the neutral M3 placeholder
    /// is drawn instead, which is what keeps the pre-M7 baselines from moving.
    /// </summary>
    private void RenderWidget(SKCanvas canvas, WidgetBlock widget, RectPt frameRect)
    {
        if (!TryLayoutWidget(widget, frameRect.Width, out WidgetDrawList? drawList))
        {
            RenderPlaceholder(canvas, frameRect);
            return;
        }

        WidgetDrawListRenderer.Render(canvas, drawList, frameRect, WidgetShaper, ShowEmptyPrompts);
    }

    private bool TryLayoutWidget(WidgetBlock widget, float widthPt, [NotNullWhen(true)] out WidgetDrawList? drawList)
    {
        drawList = null;
        if (_widgets is null)
        {
            return false;
        }

        if (_widgetDrawLists.TryGetValue(widget.Id, out (float WidthPt, WidgetDrawList List) cached)
            && cached.WidthPt == widthPt)
        {
            drawList = cached.List;
            return true;
        }

        if (!_widgets.TryGetStyleDefaults(widget.WidgetType, out WidgetStyleContext defaults))
        {
            return false;
        }

        var request = new WidgetLayoutRequest(
            widget.WidgetType,
            widget.DataVersion,
            widget.Data,
            widthPt,
            WidgetStyleResolver.Resolve(_document, widget, defaults),
            WidgetShaper);

        if (!_widgets.TryLayout(request, out WidgetDrawList list))
        {
            return false;
        }

        _widgetDrawLists[widget.Id] = (widthPt, list);
        drawList = list;
        return true;
    }

    private static void RenderPlaceholder(SKCanvas canvas, RectPt frameRect)
    {
        SKRect rect = ToRect(frameRect);
        using var fill = new SKPaint { Color = new SKColor(0xFFE8E8E8), Style = SKPaintStyle.Fill };
        canvas.DrawRect(rect, fill);
        using var border = new SKPaint
        {
            Color = new SKColor(0xFF9A9A9A),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };
        canvas.DrawRect(rect, border);
    }

    private DecodedImage? ResolveImage(string assetRef)
    {
        if (_decodedImages.TryGetValue(assetRef, out DecodedImage? cached))
        {
            return cached;
        }

        DecodedImage? decoded = _assets.TryGetValue(assetRef, out byte[]? bytes)
            ? ImageDecoder.Decode(bytes)
            : null;
        _decodedImages[assetRef] = decoded;
        return decoded;
    }

    /// <summary>
    /// Registers photo bytes for an asset the document is about to reference (the insert path).
    /// The originals are stored verbatim — nothing here ever re-encodes them.
    /// </summary>
    public void AddAsset(string assetRef, byte[] bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(assetRef);
        ArgumentNullException.ThrowIfNull(bytes);

        _assets[assetRef] = bytes;
        if (_decodedImages.Remove(assetRef, out DecodedImage? stale))
        {
            stale?.Dispose();
        }

        _recipeCache.InvalidateAsset(assetRef);
    }

    public bool HasAsset(string assetRef) => _assets.ContainsKey(assetRef);

    /// <summary>Pixel size of a decoded asset, for aspect-aware operations like auto-crop.</summary>
    public bool TryGetImageSize(string assetRef, out int width, out int height)
    {
        DecodedImage? decoded = ResolveImage(assetRef);
        width = decoded?.Width ?? 0;
        height = decoded?.Height ?? 0;
        return decoded is not null;
    }

    /// <summary>The decoded (upright) photo behind an asset, for auto-crop proposals.</summary>
    public DecodedImage? GetDecodedImage(string assetRef) => ResolveImage(assetRef);

    private static SKRect ToRect(RectPt r) => new(r.X, r.Y, r.Right, r.Bottom);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (DecodedImage? image in _decodedImages.Values)
        {
            image?.Dispose();
        }

        _decodedImages.Clear();
        _recipeCache.Dispose();
    }
}
