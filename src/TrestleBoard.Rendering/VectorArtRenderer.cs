using SkiaSharp;

namespace TrestleBoard.Rendering;

/// <summary>One stroked or filled path of a drawing, in the drawing's own viewbox coordinates.</summary>
/// <param name="PathData">
/// SVG path data — the one piece of SVG this app understands, parsed by Skia rather than by a
/// document reader.
/// </param>
/// <param name="StrokeWidth">
/// Zero to fill the path; a positive number is the pen width, with round caps and joins. A negative
/// number is neither, so the part is not drawn at all (M74 (f)) — the same file tolerance an
/// unreadable path gets, and for the same reason.
/// </param>
public readonly record struct VectorPathSpec(string PathData, double StrokeWidth);

/// <summary>
/// The one routine that turns path data into marks (PLAN.md §11 M72 (c)).
///
/// <para><b>There is exactly one of these, and that is the deliverable.</b> M65 rasterised emblems
/// at insert time specifically to avoid a "second renderer", and it was right to worry: if
/// <c>EmblemRenderer.Draw</c> had survived here for the picker's thumbnails alongside
/// <c>DocumentRenderSource.RenderVector</c>, this milestone would have built the very thing M65
/// feared and merely moved it. So the primitive lives here, in the project that already owns
/// SkiaSharp, and everybody draws through it: the page, the PDF, and the emblem picker's tiles.</para>
///
/// <para>It takes path strings and numbers rather than any document or emblem type, which is what
/// lets both callers reach it without <c>Rendering</c> learning that emblems exist (§9 forbids
/// <c>Rendering → Emblems</c>) and without the picker reaching into the document model.</para>
/// </summary>
public static class VectorArtRenderer
{
    /// <summary>
    /// Draws the paths scaled to fit <paramref name="dest"/> and centred in it, keeping the
    /// viewbox's shape. Pen widths scale with the drawing, so it is the same picture at any size
    /// rather than a spindly one when small.
    ///
    /// <para><b>An unreadable path draws nothing and does not throw.</b> Path data now arrives from
    /// a saved document, which means it can have been written by a later build of TrestleBoard, or
    /// hand-edited, or damaged. A newsletter that cannot be opened because one ornament has a typo
    /// in it would be a far worse failure than an ornament that does not appear — and the rest of
    /// the page is somebody's month of work. <c>Emblems.Tests</c> still refuses to ship a shelf
    /// emblem Skia cannot read, so this tolerance never covers for our own artwork.</para>
    /// </summary>
    public static void Draw(
        SKCanvas canvas,
        IReadOnlyList<VectorPathSpec> parts,
        double viewBoxWidth,
        double viewBoxHeight,
        SKRect dest,
        SKColor ink)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(parts);

        if (viewBoxWidth <= 0 || viewBoxHeight <= 0 || dest.Width <= 0 || dest.Height <= 0)
        {
            return;
        }

        float scale = (float)Math.Min(dest.Width / viewBoxWidth, dest.Height / viewBoxHeight);

        int saved = canvas.Save();
        canvas.Translate(
            dest.Left + (float)((dest.Width - (viewBoxWidth * scale)) / 2),
            dest.Top + (float)((dest.Height - (viewBoxHeight * scale)) / 2));
        canvas.Scale(scale);

        foreach (VectorPathSpec part in parts)
        {
            using SKPath? path = SKPath.ParseSvgPathData(part.PathData);
            if (path is null)
            {
                continue;
            }

            // M74 (f): a negative pen width draws nothing, and that is the whole of this branch.
            // The rule is "zero fills, a positive number is the pen"; a negative number is neither,
            // and it used to fall through to Fill — so a damaged or hand-edited document turned a
            // fine line into a solid blob covering everything the outline enclosed. On the same
            // reasoning as the unreadable-path tolerance above: a part that does not appear is a
            // far smaller failure than a part that appears as a black lump over somebody's page.
            if (part.StrokeWidth < 0)
            {
                continue;
            }

            using var paint = new SKPaint
            {
                Color = ink,
                IsAntialias = true,
                Style = part.StrokeWidth > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
                StrokeWidth = (float)part.StrokeWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
            };

            canvas.DrawPath(path, paint);
        }

        canvas.RestoreToCount(saved);
    }

    /// <summary>
    /// The pixel size a drawing fills at a given longest side, so a caller sizing a bitmap and this
    /// renderer agree about the shape without either doing the arithmetic twice.
    /// </summary>
    public static (int Width, int Height) PixelSize(double viewBoxWidth, double viewBoxHeight, int longestSide)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(longestSide, 1);
        if (viewBoxWidth <= 0 || viewBoxHeight <= 0)
        {
            return (longestSide, longestSide);
        }

        double scale = longestSide / Math.Max(viewBoxWidth, viewBoxHeight);
        return (
            Math.Max(1, (int)Math.Round(viewBoxWidth * scale)),
            Math.Max(1, (int)Math.Round(viewBoxHeight * scale)));
    }

    /// <summary>
    /// PNG bytes for a drawing: ink on transparent. The <b>only</b> caller is a thumbnail in a
    /// window — a picture of the drawing, drawn to be looked at and thrown away with the window.
    /// Nothing rasterised here ever reaches a <c>.tboard</c> or a PDF, which is the whole of M72.
    /// </summary>
    public static byte[] ToPng(
        IReadOnlyList<VectorPathSpec> parts,
        double viewBoxWidth,
        double viewBoxHeight,
        int longestSide,
        SKColor ink)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestSide, 16);

        (int width, int height) = PixelSize(viewBoxWidth, viewBoxHeight, longestSide);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create a surface to draw on.");
        surface.Canvas.Clear(SKColors.Transparent);

        Draw(surface.Canvas, parts, viewBoxWidth, viewBoxHeight, SKRect.Create(width, height), ink);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
