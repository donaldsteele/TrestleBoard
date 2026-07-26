using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Canvas;

/// <summary>
/// The page canvas: draws one document page through the app's own SkiaSharp pipeline via
/// Avalonia's <see cref="ISkiaSharpApiLeaseFeature"/> (PLAN.md §11 M3) — the exact renderer
/// the PDF export uses, so what you see IS what prints.
///
/// Fallback note (plan-required): if a platform ever runs a non-Skia Avalonia backend the
/// lease feature returns null and the page is not drawn. The documented fallback is to render
/// into a WriteableBitmap (lock the framebuffer, wrap it in SKSurface.Create(info, address,
/// rowBytes), call RenderPage, then DrawImage the bitmap). All Avalonia 11.3 desktop targets
/// ship the Skia backend, so the fallback stays unimplemented until a real platform needs it.
/// </summary>
public sealed class PageCanvasControl : Control
{
    /// <summary>Empty margin around the page so the sheet reads as a sheet (points × zoom).</summary>
    private const double PagePaddingPx = 24d;

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PageCanvasControl, double>(nameof(Zoom), defaultValue: 1d);

    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<PageCanvasControl, int>(nameof(PageIndex), defaultValue: 0);

    private DocumentRenderSource? _source;

    static PageCanvasControl()
    {
        AffectsRender<PageCanvasControl>(ZoomProperty, PageIndexProperty);
        AffectsMeasure<PageCanvasControl>(ZoomProperty, PageIndexProperty);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int PageIndex
    {
        get => GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    /// <summary>The laid-out document; not an AvaloniaProperty because it is set wholesale on open.</summary>
    public DocumentRenderSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return default;
        }

        Core.Model.SizePt size = _source.GetPageSize(PageIndex);
        return new Size(
            (size.Width * Zoom) + (2 * PagePaddingPx),
            (size.Height * Zoom) + (2 * PagePaddingPx));
    }

    public override void Render(DrawingContext context)
    {
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return;
        }

        context.Custom(new PageDrawOperation(new Rect(Bounds.Size), _source, PageIndex, Zoom, PagePaddingPx));
    }

    private sealed class PageDrawOperation(
        Rect bounds,
        DocumentRenderSource source,
        int pageIndex,
        double zoom,
        double padding) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = (ISkiaSharpApiLeaseFeature?)context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature));
            if (leaseFeature is null)
            {
                return; // Non-Skia backend: see the WriteableBitmap fallback note on the control.
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            int save = canvas.Save();
            try
            {
                Core.Model.SizePt size = source.GetPageSize(pageIndex);
                float pageW = (float)(size.Width * zoom);
                float pageH = (float)(size.Height * zoom);

                // Neutral backdrop; the white sheet with a soft edge sits centered inside it.
                canvas.DrawColor(new SKColor(0xFF6B6B6B));
                var page = SKRect.Create((float)padding, (float)padding, pageW, pageH);
                using (var shadow = new SKPaint { Color = new SKColor(0x55000000), IsAntialias = true })
                {
                    canvas.DrawRect(new SKRect(page.Left + 3, page.Top + 3, page.Right + 3, page.Bottom + 3), shadow);
                }

                canvas.ClipRect(page);
                canvas.Translate(page.Left, page.Top);
                canvas.Scale((float)zoom);
                source.RenderPage(canvas, pageIndex);
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }
}
