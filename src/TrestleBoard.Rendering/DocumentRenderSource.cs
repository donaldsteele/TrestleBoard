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
using TrestleBoard.Layout.Input;
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

    /// <summary>
    /// One laid-out caption per captioned picture frame (M18), keyed by block id. Rebuilt at the
    /// top of every relayout, because a caption's height depends on the frame's width and both can
    /// change under a drag.
    /// </summary>
    private readonly Dictionary<string, FrameLayout> _captionLayouts = new(StringComparer.Ordinal);

    /// <summary>
    /// The rects layout sees: the preview rects, plus captioned picture frames grown downwards to
    /// cover their caption. Painting and hit-testing deliberately do NOT use this — a caption is
    /// something the text has to flow past, not part of the picture.
    /// </summary>
    private readonly Dictionary<string, RectPt> _layoutRects = new(StringComparer.Ordinal);
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

        // A COPY. This used to hand out the live internal list, which the next relayout clears in
        // place — so a caller that held onto the result watched it silently empty itself, with no
        // hint that the thing it was reading had been recycled underneath it (review §14.2).
        return [.. _oversetTailBlockIds];
    }

    /// <summary>True when the block is a text frame (the canvas needs it for mode arbitration).</summary>
    public bool IsTextBlock(string blockId) => _document.FindBlock(blockId).Block is TextBlock;

    /// <summary>
    /// True when the block is one of the lists TrestleBoard fills in — what the canvas needs to
    /// decide whether a double-click should open an editor (M17).
    /// </summary>
    public bool IsWidgetBlock(string blockId) => _document.FindBlock(blockId).Block is WidgetBlock;

    /// <summary>A picture frame, whether or not there is a picture in it yet (M18).</summary>
    public bool IsImageBlock(string blockId) => _document.FindBlock(blockId).Block is ImageFrame;

    /// <summary>
    /// The picture frames on this page with nothing in them yet (PLAN.md §11 M18) — where the shell
    /// hangs "Double-click to choose a picture", exactly as it hangs the empty-frame hint on a text
    /// frame nobody has typed in.
    ///
    /// <para>"Empty" is decided by whether the bytes DECODE, not by whether the container holds an
    /// entry: a template ships frames pointing at assets that were never in the package, and a file
    /// whose picture failed to decode is just as empty from where the user is standing.</para>
    /// </summary>
    public IReadOnlyList<(string BlockId, RectPt Rect)> GetPlaceholderPictureRects(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= _document.Pages.Count)
        {
            return [];
        }

        var empty = new List<(string, RectPt)>();
        foreach (ImageFrame frame in _document.Pages[pageIndex].Blocks.OfType<ImageFrame>())
        {
            if (ResolveImage(frame.AssetRef) is null)
            {
                empty.Add((frame.Id, DocumentLayoutAdapter.EffectiveRect(frame, _previewRects)));
            }
        }

        return empty;
    }

    /// <summary>
    /// The text frames on one page that hold nothing yet, so the editor can paint "click here and
    /// start typing" over them (PLAN.md §11 M17).
    ///
    /// <para>A query, not a drawing: the hint itself is an EDITOR ADORNMENT painted by
    /// <c>PageCanvasControl</c> with Avalonia's own primitives, and it must stay that way. Anything
    /// this class draws goes onto whatever canvas it is handed, and one of those canvases is the
    /// PDF's — a hint that reached <c>RenderPage</c> would print.</para>
    ///
    /// <para>Continuation frames are left out. A frame that is the second half of a linked story is
    /// empty only because the first half has not overflowed yet, and telling the user to type into
    /// it would put their words in the wrong place.</para>
    /// </summary>
    public IReadOnlyList<(string BlockId, RectPt Rect)> GetEmptyTextFrameRects(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pageIndex < 0 || pageIndex >= _document.Pages.Count)
        {
            return [];
        }

        var empty = new List<(string, RectPt)>();
        foreach (TextBlock block in _document.Pages[pageIndex].Blocks.OfType<TextBlock>())
        {
            bool isContinuation = _document.Pages
                .SelectMany(p => p.Blocks)
                .OfType<TextBlock>()
                .Any(b => string.Equals(b.LinkNext, block.Id, StringComparison.Ordinal));
            if (isContinuation || !StoryIsEmpty(block.StoryRef))
            {
                continue;
            }

            empty.Add((block.Id, DocumentLayoutAdapter.EffectiveRect(block, _previewRects)));
        }

        return empty;
    }

    private bool StoryIsEmpty(string storyId) =>
        _document.Stories.FirstOrDefault(s => s.Id == storyId) is not { } story
        || story.Paragraphs.All(p => p.Runs.All(r => r.Text.Trim().Length == 0));

    /// <summary>
    /// Would this story still overflow if the given frames existed? Auto-flow asks this before it
    /// commits anything, so a run that cannot help leaves the document untouched (docs/M8-spec.md §2).
    /// The speculative layout is never cached and never affects the painted state.
    /// </summary>
    public bool WouldStillBeOverset(
        string blockId,
        IReadOnlyList<(string PageId, Block Frame, string PreviousFrameId)> plannedFrames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plannedFrames);
        if (_document.FindBlock(blockId).Block is not TextBlock head)
        {
            return false;
        }

        var planned = new List<(string PageId, TextBlock Frame)>(plannedFrames.Count);
        foreach ((string pageId, Block frame, string _) in plannedFrames)
        {
            if (frame is TextBlock text)
            {
                planned.Add((pageId, text));
            }
        }

        LayoutRequest request = DocumentLayoutAdapter.BuildSpeculativeRequest(_document, head, planned);
        return _engine.Layout(request).IsOverset;
    }

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
            WidgetStyleResolver.Resolve(_document, widget, defaults, _fonts),
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

        // A copy, for the reason given on GetOversetTailBlockIds.
        return [.. _widgetOverflowBlockIds];
    }

    /// <summary>
    /// A PNG of one page, at a fraction of full size. The recovery dialog uses it to answer "is this
    /// the work I lost?" by showing rather than telling (docs/M9-spec.md §1.3).
    /// </summary>
    public byte[] RenderPageToPng(int pageIndex, float scale = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        SizePt size = GetPageSize(pageIndex);
        var info = new SKImageInfo(
            Math.Max(1, (int)MathF.Ceiling(size.Width * scale)),
            Math.Max(1, (int)MathF.Ceiling(size.Height * scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());

        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create a thumbnail surface.");
        surface.Canvas.Scale(scale);
        RenderPage(surface.Canvas, pageIndex);
        surface.Canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90)
            ?? throw new InvalidOperationException("Could not encode the thumbnail.");
        return data.ToArray();
    }

    /// <summary>
    /// What a screen reader should say about each block on a page, in stacking order
    /// (docs/M9-spec.md §5). Names come from the SAME data the page prints — a photo's alt text, a
    /// widget's display name, the story's own first words — so they cannot drift from what is there.
    /// </summary>
    public IReadOnlyList<(string BlockId, string Name)> DescribeBlocks(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _document.Pages.Count);
        EnsureLayout();

        var described = new List<(string BlockId, string Name)>();
        foreach (Block block in _document.Pages[pageIndex].Blocks.OrderBy(b => b.ZOrder))
        {
            described.Add((block.Id, Describe(block)));
        }

        return described;
    }

    private string Describe(Block block) => block switch
    {
        TextBlock text => "Text frame: " + Opening(text),
        ImageFrame image => image.AltText.Length > 0
            ? "Photo: " + image.AltText

            // M6 has demanded a description at insert since it shipped, so a blank one means the
            // file predates that or was written by another tool. Say so rather than saying nothing.
            : "Photo with no description",
        WidgetBlock widget => WidgetName(widget),
        ShapeBlock => "Decoration",
        _ => "Item",
    };

    /// <summary>
    /// A widget's own display name — "Lodge officers", "Birthdays". The catalogue is the interface
    /// that knows them; the same object implements both seams in practice, and a widget this build
    /// does not recognise still gets something sayable.
    /// </summary>
    private string WidgetName(WidgetBlock widget) =>
        _widgets is IWidgetCatalog catalog && catalog.TryGetInfo(widget.WidgetType, out WidgetInfo info)
            ? info.DisplayName
            : "Newsletter item";

    /// <summary>First few words of a story, for the frame's spoken name.</summary>
    private string Opening(TextBlock text)
    {
        Story story = _document.GetStory(text.StoryRef);
        foreach (StoryParagraph paragraph in story.Paragraphs)
        {
            string line = string.Concat(paragraph.Runs.Select(r => r.Text)).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            return line.Length <= 40 ? line : line[..40].TrimEnd() + "…";
        }

        return "empty";
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
        // Validated, like every other page-indexed method here. This one, RenderPageToPng and
        // DescribeBlocks all indexed the list raw and so threw a bare IndexOutOfRangeException
        // instead of the guarded ArgumentOutOfRangeException their siblings produce — which matters
        // because the compositor-thread draw path calls this with a page index that may have gone
        // stale (review §14.2, and M25's guard in PageDrawOperation.Render).
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, _document.Pages.Count);

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

    /// <summary>
    /// The laid-out caption under a picture frame, if it has one (M18). The lines are in page
    /// coordinates, like every other frame layout, which is what lets a test say where a caption
    /// sits rather than only that the page looks different.
    /// </summary>
    public bool TryGetCaptionLayout(string blockId, [NotNullWhen(true)] out FrameLayout? layout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        return _captionLayouts.TryGetValue(blockId, out layout);
    }

    /// <summary>
    /// The rectangle the surrounding text flows around: the frame, plus its caption when it has one
    /// (M18). Equal to <see cref="GetEffectiveRect"/> for everything else — which is the additive
    /// guarantee, stated as a method rather than left implicit.
    /// </summary>
    public RectPt GetLayoutRect(string blockId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureLayout();
        return _layoutRects.TryGetValue(blockId, out RectPt rect) ? rect : GetEffectiveRect(blockId);
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

        // M18: captions first. They depend only on the picture frame they hang under, so they can
        // be laid out before the stories — and they have to be, because how far a caption reaches
        // down the page is part of what the text beside the picture must flow past.
        LayOutCaptions();

        // Plan rebuild is cheap (style resolution + text concat); the engine is the cost, so
        // clean stories reuse their cached LayoutResult.
        _plans = [.. DocumentLayoutAdapter.BuildPlans(_document, _layoutRects)];
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

    /// <summary>
    /// Lays out every caption in the document and records how far each one reaches (PLAN.md §11 M18).
    ///
    /// <para>The extended rect goes into <see cref="_layoutRects"/> and nowhere else. A captioned
    /// picture still PAINTS in its own rectangle and still hit-tests as its own rectangle — it is
    /// only what the surrounding text flows around that grows, which is what makes a caption
    /// additive: a document with no captions produces exactly the rects it produced before M18, and
    /// every existing baseline keeps meaning what it meant.</para>
    /// </summary>
    private void LayOutCaptions()
    {
        _captionLayouts.Clear();
        _layoutRects.Clear();
        foreach ((string blockId, RectPt rect) in _previewRects)
        {
            _layoutRects[blockId] = rect;
        }

        foreach (Block block in _document.Pages.SelectMany(p => p.Blocks))
        {
            RectPt rect = DocumentLayoutAdapter.EffectiveRect(block, _previewRects);
            if (CaptionLayout.Request(_document, block, rect) is not { } request)
            {
                continue;
            }

            LayoutResult result = _engine.Layout(request);
            if (result.Frames.Count == 0 || result.Frames[0].Lines.Count == 0)
            {
                continue;
            }

            FrameLayout laid = result.Frames[0];
            _captionLayouts[block.Id] = laid;

            float bottom = laid.Lines[^1].BandBottom;
            _layoutRects[block.Id] = new RectPt(
                rect.X, rect.Y, rect.Width, Math.Max(rect.Height, bottom - rect.Y));
        }
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

                // M18: and the words under it, drawn by the same frame renderer every story uses,
                // which is what makes them print in the PDF exactly as they appear on screen.
                if (_captionLayouts.TryGetValue(image.Id, out FrameLayout? caption))
                {
                    PageRenderer.RenderFrame(canvas, caption);
                }

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
            WidgetStyleResolver.Resolve(_document, widget, defaults, _fonts),
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
