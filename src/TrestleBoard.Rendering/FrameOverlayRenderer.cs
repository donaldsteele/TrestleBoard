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

    /// <summary>
    /// M43: what the overset badge says, in the words the status bar and the action panel already
    /// use for it. Three words rather than a sentence, because it is drawn beside a frame edge and
    /// has to fit; the sentence that says what to DO about it stays in the status bar.
    /// </summary>
    public const string OversetLabel = "does not fit";
    public const uint LinkTargetArgb = 0x662A6FCF;

    /// <param name="labelTypeface">
    /// M43: the face the "does not fit" label is drawn in. Optional, and null means the badge alone
    /// — the PDF export and the snapshot suite draw no overlay at all, and a renderer that DEMANDED
    /// a font to draw editor chrome would be asking every caller for something only one of them has.
    /// </param>
    public static void Draw(
        SKCanvas canvas,
        FrameOverlay overlay,
        float overlayScale,
        FrameOverlayColours? colours = null,
        SKTypeface? labelTypeface = null)
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
            DrawOversetBadge(canvas, overset, overlayScale, palette, labelTypeface);
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

    /// <summary>
    /// Red square with a white "+" hung off the bottom-right corner (InDesign convention), and — M43
    /// — the words "does not fit" beside it.
    ///
    /// <para>Colour was never the only signal: the shell has shown a plain-language status line
    /// since M17. But the review (§14.4) is right that a 12-pixel glyph at the edge of a frame, on a
    /// page that already has ink all over it, is not something this audience will notice, and the
    /// status bar only says it while nothing else is talking. The badge is unchanged and the WORDS
    /// are the fix — six more pixels of red square would not have been. They sit on a plate so they
    /// stay readable over whatever the page is showing.</para>
    ///
    /// <para>Both are sized in SCREEN points via <paramref name="overlayScale"/>, so zooming out to
    /// see the whole page does not shrink the one thing that says the page is wrong.</para>
    /// </summary>
    private static void DrawOversetBadge(
        SKCanvas canvas,
        RectPt frame,
        float overlayScale,
        FrameOverlayColours palette,
        SKTypeface? labelTypeface)
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

        if (labelTypeface is null)
        {
            return;
        }

        using var font = new SKFont(labelTypeface, 13f * overlayScale);
        float width = font.MeasureText(OversetLabel);
        float padding = 4f * overlayScale;
        // The plate is taller than the badge, because the words are the part that has to be read.
        float half = ((font.Metrics.Descent - font.Metrics.Ascent) / 2f) + padding;
        var plate = new SKRect(
            badge.Left - width - (padding * 3f),
            badge.MidY - half,
            badge.Left - padding,
            badge.MidY + half);

        using var plateFill = new SKPaint { Color = new SKColor(palette.HandleFill), Style = SKPaintStyle.Fill };
        using var plateEdge = new SKPaint
        {
            Color = new SKColor(palette.Overset),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f * overlayScale,
        };
        canvas.DrawRect(plate, plateFill);
        canvas.DrawRect(plate, plateEdge);

        using var text = new SKPaint { Color = new SKColor(palette.Overset), IsAntialias = true };
        SKFontMetrics metrics = font.Metrics;
        canvas.DrawText(
            OversetLabel,
            plate.Left + padding,
            plate.MidY - ((metrics.Ascent + metrics.Descent) / 2f),
            font,
            text);
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
