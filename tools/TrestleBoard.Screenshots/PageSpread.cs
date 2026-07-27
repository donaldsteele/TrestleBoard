using SkiaSharp;
using TrestleBoard.Rendering;

namespace TrestleBoard.Screenshots;

/// <summary>
/// The one image in the set that involves no Avalonia at all: three pages of the finished
/// newsletter, drawn by <see cref="DocumentRenderSource"/> — the same call the thumbnails and the
/// PDF export go through — and laid side by side.
///
/// It stands in for an export screenshot deliberately. There is no export dialog worth showing:
/// it is the operating system's file picker, and file pickers put the maintainer's own folder path
/// on screen (PLAN.md §0 rule 6).
/// </summary>
internal static class PageSpread
{
    /// <summary>Pages are rasterised at 150 dpi, then the finished strip is resampled down.</summary>
    private const float RenderDpi = 150f;

    private const float PointsPerInch = 72f;

    /// <summary>
    /// Wide enough to stay sharp on a high-density display, small enough not to cost four times the
    /// bytes for detail nobody sees in GitHub's ~880px README column.
    /// </summary>
    private const int FinalWidthPx = 1360;

    private const int GapPx = 24;

    private const int MarginPx = 24;

    public static byte[] ThreeUp(DocumentRenderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        int pageCount = Math.Min(3, source.PageCount);
        if (pageCount == 0)
        {
            throw new InvalidOperationException("The document has no pages to render.");
        }

        float scale = RenderDpi / PointsPerInch;
        SKBitmap[] pages = new SKBitmap[pageCount];
        try
        {
            for (int i = 0; i < pageCount; i++)
            {
                pages[i] = SKBitmap.Decode(source.RenderPageToPng(i, scale))
                    ?? throw new InvalidOperationException($"Page {i + 1} did not render.");
            }

            int stripWidth = (MarginPx * 2) + pages.Sum(p => p.Width) + (GapPx * (pageCount - 1));
            int stripHeight = (MarginPx * 2) + pages.Max(p => p.Height);

            using var strip = new SKBitmap(stripWidth, stripHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(strip))
            {
                canvas.Clear(new SKColor(0x6B, 0x6B, 0x6B));
                float x = MarginPx;
                foreach (SKBitmap page in pages)
                {
                    // A hairline edge, so a white page on a grey field still reads as a sheet of
                    // paper rather than as a hole in the image.
                    using var edge = new SKPaint
                    {
                        Color = new SKColor(0x3A, 0x3A, 0x3A),
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = 2,
                        IsAntialias = true,
                    };
                    canvas.DrawBitmap(page, x, MarginPx);
                    canvas.DrawRect(SKRect.Create(x, MarginPx, page.Width, page.Height), edge);
                    x += page.Width + GapPx;
                }
            }

            return Png.Sanitise(Resample(strip, FinalWidthPx));
        }
        finally
        {
            foreach (SKBitmap page in pages)
            {
                page?.Dispose();
            }
        }
    }

    /// <summary>
    /// Rasterise high, then resample once. Drawing the pages small in the first place would ask
    /// Skia to hint serif text at eight points, which bands; this way the glyphs are drawn properly
    /// and then averaged down.
    /// </summary>
    private static byte[] Resample(SKBitmap source, int width)
    {
        int height = (int)Math.Round(source.Height * (double)width / source.Width);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

        using var resized = source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Resampling the page spread failed.");
        using SKImage image = SKImage.FromBitmap(resized);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
