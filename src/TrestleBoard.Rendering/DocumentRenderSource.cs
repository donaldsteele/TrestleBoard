using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Documents;
using TrestleBoard.Layout.Fonts;

namespace TrestleBoard.Rendering;

/// <summary>
/// A laid-out, drawable document: layout runs ONCE at construction, then
/// <see cref="RenderPage"/> replays it onto ANY SKCanvas — editor surface, PNG, or PDF page —
/// which is what makes the viewer and the export pixel-compatible (PLAN.md §1).
/// Read-only over an immutable snapshot of the document (M3 viewer); incremental relayout
/// arrives with editing in M4.
/// </summary>
public sealed class DocumentRenderSource : IDisposable
{
    private readonly Document _document;
    private readonly Dictionary<string, FrameLayout> _frameLayoutsByBlockId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _assets;
    private readonly Dictionary<string, SKImage?> _decodedImages = new(StringComparer.Ordinal);
    private bool _disposed;

    private DocumentRenderSource(Document document, Dictionary<string, byte[]> assets)
    {
        _document = document;
        _assets = assets;
    }

    public int PageCount => _document.Pages.Count;

    /// <summary>True when any story ran out of room in its frame chain (overset indicator, M5 UI).</summary>
    public bool IsOverset { get; private set; }

    public static DocumentRenderSource Create(
        Document document,
        IReadOnlyDictionary<string, byte[]> assets,
        FontStore fonts,
        LayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(fonts);

        var source = new DocumentRenderSource(document, assets.ToDictionary(StringComparer.Ordinal));
        var engine = new TextLayoutEngine(fonts, options);
        foreach (StoryLayoutPlan plan in DocumentLayoutAdapter.BuildPlans(document))
        {
            LayoutResult result = engine.Layout(plan.Request);
            source.IsOverset |= result.IsOverset;
            for (int i = 0; i < result.Frames.Count; i++)
            {
                source._frameLayoutsByBlockId[plan.Placements[i].BlockId] = result.Frames[i];
            }
        }

        return source;
    }

    public Core.Model.SizePt GetPageSize(int pageIndex)
    {
        Page page = _document.Pages[pageIndex];
        return _document.GetMaster(page.MasterRef).Size;
    }

    public void RenderPage(SKCanvas canvas, int pageIndex, uint backgroundArgb = 0xFFFFFFFF)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, PageCount);

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
