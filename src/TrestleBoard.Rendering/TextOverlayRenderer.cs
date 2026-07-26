using SkiaSharp;
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

    public static void DrawCaret(SKCanvas canvas, CaretGeometry caret, uint argb = 0xFF000000, float widthPt = 1.2f)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        using var paint = new SKPaint { Color = new SKColor(argb), Style = SKPaintStyle.Fill };
        canvas.DrawRect(
            new SKRect(caret.XPt - widthPt / 2f, caret.TopPt, caret.XPt + widthPt / 2f, caret.BottomPt),
            paint);
    }
}
