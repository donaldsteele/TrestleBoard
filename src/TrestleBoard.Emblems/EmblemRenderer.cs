using SkiaSharp;

namespace TrestleBoard.Emblems;

/// <summary>
/// Turns an emblem into pixels (PLAN.md §11 M65).
///
/// <para><b>Why raster at all, when the emblems are vectors.</b> The acceptance asks for
/// determinism, and the app already has exactly one thing that is proven byte-identical on Windows,
/// Linux and macOS: its own SkiaSharp pipeline, held by the snapshot suite. Rendering the emblem
/// through that pipeline once, at insert time, and storing the result as an ordinary image asset
/// means the layout engine, the PDF export and the snapshot tests never learn that emblems exist —
/// there is no second drawing path to keep in agreement with the first. A vector emblem carried all
/// the way to the page would have been a second renderer, and WYSIWYG is a promise about there
/// being one.</para>
///
/// <para><b>Big enough to print.</b> <see cref="DefaultLongestSide"/> is 2048 pixels, so an emblem
/// placed three inches wide has roughly 680 pixels to the inch — beyond what any printer the
/// committee will use can resolve, and beyond what M22's picture-resolution warning asks for. The
/// cost is a few tens of kilobytes in the container, once.</para>
/// </summary>
public static class EmblemRenderer
{
    /// <summary>The longest side of a rendered emblem, in pixels.</summary>
    public const int DefaultLongestSide = 2048;

    /// <summary>
    /// PNG bytes for an emblem: black on transparent, so it sits on any page colour and the
    /// committee can put one over a tinted panel without a white box appearing around it.
    /// </summary>
    public static byte[] ToPng(Emblem emblem, int longestSide = DefaultLongestSide)
    {
        ArgumentNullException.ThrowIfNull(emblem);
        ArgumentOutOfRangeException.ThrowIfLessThan(longestSide, 16);

        (int width, int height) = PixelSize(emblem, longestSide);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        Draw(canvas, emblem, width, height);

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// The pixel size an emblem renders at, kept public so the picker's thumbnails and the inserted
    /// picture agree about the shape without either of them doing the arithmetic twice.
    /// </summary>
    public static (int Width, int Height) PixelSize(Emblem emblem, int longestSide)
    {
        ArgumentNullException.ThrowIfNull(emblem);

        double scale = longestSide / Math.Max(emblem.Width, emblem.Height);
        return (
            Math.Max(1, (int)Math.Round(emblem.Width * scale)),
            Math.Max(1, (int)Math.Round(emblem.Height * scale)));
    }

    /// <summary>
    /// Draws the emblem into the given canvas, scaled to fit exactly.
    ///
    /// <para>Stroke widths scale with the drawing, so an emblem is the same picture at any size
    /// rather than a thin one when it is small — the whole reason the shelf is geometry and not a
    /// folder of fixed-size images.</para>
    /// </summary>
    public static void Draw(SKCanvas canvas, Emblem emblem, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(emblem);

        float scale = (float)Math.Min(width / emblem.Width, height / emblem.Height);

        int saved = canvas.Save();
        canvas.Translate(
            (float)((width - (emblem.Width * scale)) / 2),
            (float)((height - (emblem.Height * scale)) / 2));
        canvas.Scale(scale);

        foreach (EmblemPart part in emblem.Parts)
        {
            using SKPath path = SKPath.ParseSvgPathData(part.PathData)
                ?? throw new InvalidOperationException(
                    $"Emblem '{emblem.Id}' has a part Skia cannot read: {part.PathData}");

            using var paint = new SKPaint
            {
                Color = SKColors.Black,
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
}
