using SkiaSharp;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Editing;

namespace TrestleBoard.Rendering;

/// <summary>Editor-only overlay painting (caret + selection). NEVER called from the PDF/PNG
/// export path — exports must stay overlay-free (docs/M4-spec.md §7.5).</summary>
public static class TextOverlayRenderer
{
    public static void DrawSelection(SKCanvas canvas, IReadOnlyList<SelectionRect> rects, uint fillArgb = 0x552A6FCF)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(rects);
        using var paint = new SKPaint { Color = new SKColor(fillArgb), Style = SKPaintStyle.Fill };
        foreach (SelectionRect rect in rects)
        {
            canvas.DrawRect(new SKRect(rect.LeftPt, rect.TopPt, rect.RightPt, rect.BottomPt), paint);
        }
    }

    /// <summary>
    /// Underlines the text spans whose font was changed by hand, for View → "Show where fonts
    /// were changed" (PLAN.md M14, off by default).
    /// <para>
    /// An EDITOR overlay, beside the caret and the selection — never a PageRenderer concern, or
    /// the marks would print into the PDF. And a line rather than a coloured squiggle: §6 bans
    /// colour as the only carrier of meaning.
    /// </para>
    /// </summary>
    public static void DrawFontOverrides(
        SKCanvas canvas,
        LayoutResult result,
        IReadOnlyCollection<SourceSpan> spans,
        uint argb = 0x88606060,
        float thicknessPt = 0.9f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(spans);
        if (spans.Count == 0)
        {
            return;
        }

        using var paint = new SKPaint { Color = new SKColor(argb), Style = SKPaintStyle.Fill };
        foreach (FrameLayout frame in result.Frames)
        {
            foreach (LineBox line in frame.Lines)
            {
                float top = line.BaselineY + (line.MaxDescentPt * 0.45f);
                foreach (LineSegment segment in line.Segments)
                {
                    foreach (PositionedGlyphRun run in segment.Runs)
                    {
                        if (run.Glyphs.Count == 0 || !Overlaps(run.Source, spans))
                        {
                            continue;
                        }

                        canvas.DrawRect(
                            new SKRect(run.OriginX, top, run.OriginX + run.AdvanceWidthPt, top + thicknessPt),
                            paint);
                    }
                }
            }
        }
    }

    private static bool Overlaps(SourceSpan run, IReadOnlyCollection<SourceSpan> spans)
    {
        foreach (SourceSpan span in spans)
        {
            if (span.StoryId == run.StoryId
                && span.ParagraphIndex == run.ParagraphIndex
                && span.StartChar < run.EndChar
                && run.StartChar < span.EndChar)
            {
                return true;
            }
        }

        return false;
    }

    public static void DrawCaret(SKCanvas canvas, CaretGeometry caret, uint argb = 0xFF000000, float widthPt = 1.2f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        using var paint = new SKPaint { Color = new SKColor(argb), Style = SKPaintStyle.Fill };
        canvas.DrawRect(
            new SKRect(caret.XPt - widthPt / 2f, caret.TopPt, caret.XPt + widthPt / 2f, caret.BottomPt),
            paint);
    }
}
