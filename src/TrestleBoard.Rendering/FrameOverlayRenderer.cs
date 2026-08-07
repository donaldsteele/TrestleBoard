using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;

namespace TrestleBoard.Rendering;

/// <summary>
/// What the canvas draws on top of the page while a frame is selected or being dragged
/// (docs/M5-spec.md §10). Pure data so the App can snapshot it on the UI thread and hand it to
/// the render thread.
/// </summary>
/// <param name="SelectedRect">Frame outline + handles, or null when nothing is selected.</param>
/// <param name="ShowHandles">False during a drag — the grips would just chase the pointer.</param>
/// <param name="SnapGuides">Guides for the snaps that fired on this drag frame.</param>
/// <param name="OversetRects">Frames whose chain ran out of room (badge anchor).</param>
/// <param name="LinkedRects">Frames that continue into another frame (chain badge anchor).</param>
/// <param name="LinkTargetRects">Candidate targets highlighted while link mode is armed.</param>
/// <param name="LinkTargetRect">The candidate the keyboard has landed on, drawn stronger.</param>
public sealed record FrameOverlay(
    RectPt? SelectedRect,
    bool ShowHandles,
    IReadOnlyList<SnapGuide> SnapGuides,
    IReadOnlyList<RectPt> OversetRects,
    IReadOnlyList<RectPt> LinkedRects,
    IReadOnlyList<RectPt> LinkTargetRects,
    RectPt? LinkTargetRect = null)
{
    public static readonly FrameOverlay Empty = new(null, false, [], [], [], []);
}

/// <summary>
/// Editor-only frame chrome: selection outline, oversized handles, snap guides, overset and link
/// badges. Like <see cref="TextOverlayRenderer"/> it is NEVER reachable from the export path —
/// <c>DocumentPdfExporter</c> calls <c>RenderPage</c> only (docs/M5-spec.md §10).
///
/// Every size is multiplied by <c>overlayScale = 1 / zoom</c> so the chrome is constant on screen
/// while the page under it scales (PLAN.md §6: 12pt visual / 24pt hit handles).
/// </summary>
public static class FrameOverlayRenderer
{
    public const uint SelectionArgb = 0xFF2A6FCF;
    public const uint HandleFillArgb = 0xFFFFFFFF;
    public const uint SnapGuideArgb = 0xFFE5308C;
    public const uint OversetArgb = 0xFFC62828;
    public const uint LinkTargetArgb = 0x662A6FCF;

    public static void Draw(
        SKCanvas canvas,
        FrameOverlay overlay,
        float overlayScale,
        FrameOverlayColours? colours = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(overlay);

        FrameOverlayColours palette = colours ?? FrameOverlayColours.Default;

        foreach (SnapGuide guide in overlay.SnapGuides)
        {
            DrawSnapGuide(canvas, guide, overlayScale, palette);
        }

        foreach (RectPt target in overlay.LinkTargetRects)
        {
            using var fill = new SKPaint { Color = new SKColor(palette.LinkTarget), Style = SKPaintStyle.Fill };
            canvas.DrawRect(ToRect(target), fill);
        }

        if (overlay.LinkTargetRect is { } keyboardTarget)
        {
            // Tab landed here: a heavier outline so the keyboard path is as clear as the pointer's.
            using var stroke = new SKPaint
            {
                Color = new SKColor(palette.Selection),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f * overlayScale,
                IsAntialias = true,
            };
            canvas.DrawRect(ToRect(keyboardTarget), stroke);
        }

        foreach (RectPt linked in overlay.LinkedRects)
        {
            DrawLinkBadge(canvas, linked, overlayScale, palette);
        }

        foreach (RectPt overset in overlay.OversetRects)
        {
            DrawOversetBadge(canvas, overset, overlayScale, palette);
        }

        if (overlay.SelectedRect is not { } rect)
        {
            return;
        }

        using var outline = new SKPaint
        {
            Color = new SKColor(palette.Selection),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = overlayScale,
            IsAntialias = true,
        };
        canvas.DrawRect(ToRect(rect), outline);

        if (!overlay.ShowHandles)
        {
            return;
        }

        using var handleFill = new SKPaint { Color = new SKColor(palette.HandleFill), Style = SKPaintStyle.Fill };
        foreach (FrameHandle handle in FrameGeometry.HandleOrder)
        {
            SKRect square = ToRect(FrameGeometry.HandleVisualRect(rect, handle, overlayScale));
            canvas.DrawRect(square, handleFill);
            canvas.DrawRect(square, outline);
        }
    }

    private static void DrawSnapGuide(SKCanvas canvas, SnapGuide guide, float overlayScale, FrameOverlayColours palette)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(palette.SnapGuide),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = overlayScale,
        };
        if (guide.Axis == SnapAxis.X)
        {
            canvas.DrawLine(guide.PositionPt, guide.FromPt, guide.PositionPt, guide.ToPt, paint);
        }
        else
        {
            canvas.DrawLine(guide.FromPt, guide.PositionPt, guide.ToPt, guide.PositionPt, paint);
        }
    }

    /// <summary>Red square with a white "+" hung off the bottom-right corner (InDesign convention).
    /// Colour is never the only signal — the shell also shows a plain-language status line.</summary>
    private static void DrawOversetBadge(SKCanvas canvas, RectPt frame, float overlayScale, FrameOverlayColours palette)
    {
        float side = 12f * overlayScale;
        var badge = new SKRect(frame.Right - side, frame.Bottom, frame.Right, frame.Bottom + side);
        using var fill = new SKPaint { Color = new SKColor(palette.Overset), Style = SKPaintStyle.Fill };
        canvas.DrawRect(badge, fill);

        using var glyph = new SKPaint
        {
            // The glyph has to contrast with the badge it sits on, and HandleFill is exactly the
            // colour that does: white against the red badge in Light and Dark, black against the
            // white one in High Contrast. Reusing it keeps the pair inverting together.
            Color = new SKColor(palette.HandleFill),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * overlayScale,
        };
        float inset = side * 0.25f;
        canvas.DrawLine(badge.Left + inset, badge.MidY, badge.Right - inset, badge.MidY, glyph);
        canvas.DrawLine(badge.MidX, badge.Top + inset, badge.MidX, badge.Bottom - inset, glyph);
    }

    /// <summary>Small arrow marking "this frame continues in another frame".</summary>
    private static void DrawLinkBadge(SKCanvas canvas, RectPt frame, float overlayScale, FrameOverlayColours palette)
    {
        float side = 10f * overlayScale;
        var badge = new SKRect(frame.Right - side, frame.Bottom - side, frame.Right, frame.Bottom);
        using var fill = new SKPaint { Color = new SKColor(palette.Selection), Style = SKPaintStyle.Fill };
        using var path = new SKPath();
        path.MoveTo(badge.Left, badge.Top);
        path.LineTo(badge.Right, badge.MidY);
        path.LineTo(badge.Left, badge.Bottom);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    private static SKRect ToRect(RectPt r) => new(r.X, r.Y, r.Right, r.Bottom);
}
