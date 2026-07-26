using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Documents;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Fonts;

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
    private readonly Dictionary<string, SKImage?> _decodedImages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LayoutResult> _layoutsByStory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoryTextGeometry> _geometriesByStory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameLayout> _frameLayoutsByBlockId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string StoryId, int FrameIndex)> _framesByBlockId = new(StringComparer.Ordinal);
    private List<StoryLayoutPlan> _plans = [];
    private readonly HashSet<string> _dirtyStories = new(StringComparer.Ordinal);
    private bool _allDirty = true;
    private bool _disposed;

    private DocumentRenderSource(Document document, Dictionary<string, byte[]> assets, TextLayoutEngine engine)
    {
        _document = document;
        _assets = assets;
        _engine = engine;
    }

    public int PageCount => _document.Pages.Count;

    /// <summary>True when any story ran out of room in its frame chain (overset indicator).</summary>
    public bool IsOverset { get; private set; }

    /// <summary>Times the full relayout path ran; the M4 laziness tests assert on this.</summary>
    public int RelayoutCount { get; private set; }

    /// <summary>Raised by <see cref="Invalidate"/>. Handlers must NOT lay out — geometry is
    /// recomputed lazily on the next paint or query.</summary>
    public event EventHandler? LayoutInvalidated;

    public static DocumentRenderSource Create(
        Document document,
        IReadOnlyDictionary<string, byte[]> assets,
        FontStore fonts,
        LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(fonts);
        return new DocumentRenderSource(
            document,
            assets.ToDictionary(StringComparer.Ordinal),
            new TextLayoutEngine(fonts, options));
    }

    /// <summary>Creates a source wired to a session: every executed/undone command marks the
    /// matching dirty scope (docs/M4-spec.md §7.3).</summary>
    public static DocumentRenderSource CreateEditable(
        Document document,
        IReadOnlyDictionary<string, byte[]> assets,
        FontStore fonts,
        DocumentSession session,
        LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        DocumentRenderSource source = Create(document, assets, fonts, options);
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
        else
        {
            // Block geometry/content moves exclusions; page structure changes frames; play safe.
            _allDirty = true;
        }

        LayoutInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public Core.Model.SizePt GetPageSize(int pageIndex)
    {
        Page page = _document.Pages[pageIndex];
        return _document.GetMaster(page.MasterRef).Size;
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

            RectPt r = text.FrameRect;
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
        _plans = [.. DocumentLayoutAdapter.BuildPlans(_document)];
        var liveStories = new HashSet<string>(_plans.Select(p => p.StoryId), StringComparer.Ordinal);
        _layoutsByStory.Keys.Where(k => !liveStories.Contains(k)).ToList().ForEach(k =>
        {
            _layoutsByStory.Remove(k);
            _geometriesByStory.Remove(k);
        });

        IsOverset = false;
        _frameLayoutsByBlockId.Clear();
        _framesByBlockId.Clear();
        foreach (StoryLayoutPlan plan in _plans)
        {
            bool dirty = _allDirty
                || _dirtyStories.Contains(plan.StoryId)
                || !_layoutsByStory.ContainsKey(plan.StoryId);
            if (dirty)
            {
                LayoutResult result = _engine.Layout(plan.Request);
                _layoutsByStory[plan.StoryId] = result;
                _geometriesByStory[plan.StoryId] = new StoryTextGeometry(plan, result);
            }

            LayoutResult layout = _layoutsByStory[plan.StoryId];
            IsOverset |= layout.IsOverset;
            for (int i = 0; i < layout.Frames.Count; i++)
            {
                _frameLayoutsByBlockId[plan.Placements[i].BlockId] = layout.Frames[i];
                _framesByBlockId[plan.Placements[i].BlockId] = (plan.StoryId, i);
            }
        }

        _allDirty = false;
        _dirtyStories.Clear();
    }

    // ---- Block rendering (unchanged from M3) ------------------------------------------------

    private void RenderBlock(SKCanvas canvas, Block block)
    {
        switch (block)
        {
            case TextBlock text:
                if (_frameLayoutsByBlockId.TryGetValue(text.Id, out FrameLayout? layout))
                {
                    PageRenderer.RenderFrame(canvas, layout);
                }

                break;
            case ImageFrame image:
                RenderImage(canvas, image);
                break;
            case ShapeBlock shape:
                RenderShape(canvas, shape);
                break;
            case WidgetBlock:
                // Widgets render for real in M7; a neutral placeholder keeps layout honest.
                RenderPlaceholder(canvas, block.FrameRect);
                break;
            default:
                throw new NotSupportedException($"Unknown block type: {block.GetType().Name}");
        }
    }

    private void RenderImage(SKCanvas canvas, ImageFrame image)
    {
        SKRect dest = ToRect(image.FrameRect);
        SKImage? decoded = ResolveImage(image.AssetRef);
        if (decoded is null)
        {
            RenderPlaceholder(canvas, image.FrameRect);
            return;
        }

        // M3: aspect-fill (cover) only; recipes (crop/rotate/color) arrive with Imaging in M6.
        float scale = Math.Max(dest.Width / decoded.Width, dest.Height / decoded.Height);
        float w = decoded.Width * scale;
        float h = decoded.Height * scale;
        var src = new SKRect(0, 0, decoded.Width, decoded.Height);
        var fitted = new SKRect(
            dest.MidX - w / 2f,
            dest.MidY - h / 2f,
            dest.MidX + w / 2f,
            dest.MidY + h / 2f);

        int save = canvas.Save();
        canvas.ClipRect(dest);
        using var sampling = new SKPaint { IsAntialias = true };
        canvas.DrawImage(decoded, src, fitted, new SKSamplingOptions(SKCubicResampler.Mitchell), sampling);
        canvas.RestoreToCount(save);
    }

    private static void RenderShape(SKCanvas canvas, ShapeBlock shape)
    {
        SKRect rect = ToRect(shape.FrameRect);
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

    private SKImage? ResolveImage(string assetRef)
    {
        if (_decodedImages.TryGetValue(assetRef, out SKImage? cached))
        {
            return cached;
        }

        SKImage? decoded = _assets.TryGetValue(assetRef, out byte[]? bytes)
            ? SKImage.FromEncodedData(bytes)
            : null;
        _decodedImages[assetRef] = decoded;
        return decoded;
    }

    private static SKRect ToRect(RectPt r) => new(r.X, r.Y, r.Right, r.Bottom);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (SKImage? image in _decodedImages.Values)
        {
            image?.Dispose();
        }

        _decodedImages.Clear();
    }
}
