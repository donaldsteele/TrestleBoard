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
    /// One whole page, on the same grey field as the three-up strip (M83, M84).
    ///
    /// <para>A single page rather than three when what the picture is about is one page's LAYOUT —
    /// two columns, or a month on a grid. Three of them at README width would make the very thing
    /// being shown too small to see.</para>
    /// </summary>
    public static byte[] OnePage(DocumentRenderSource source, int pageIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pageIndex, source.PageCount);

        float scale = RenderDpi / PointsPerInch;
        using SKBitmap page = SKBitmap.Decode(source.RenderPageToPng(pageIndex, scale))
            ?? throw new InvalidOperationException($"Page {pageIndex + 1} did not render.");

        using var sheet = new SKBitmap(
            (MarginPx * 2) + page.Width,
            (MarginPx * 2) + page.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(0x6B, 0x6B, 0x6B));
            using var edge = new SKPaint
            {
                Color = new SKColor(0x3A, 0x3A, 0x3A),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                IsAntialias = true,
            };
            canvas.DrawBitmap(page, MarginPx, MarginPx);
            canvas.DrawRect(SKRect.Create(MarginPx, MarginPx, page.Width, page.Height), edge);
        }

        // Narrower than the three-up strip: one page blown to 1360px would be bigger on the README
        // than the editor screenshots, which would make the smallest feature look like the largest.
        return Png.Sanitise(Resample(sheet, 760));
    }

    /// <summary>
    /// The bottom quarter of a page (M78), which is where the footer lives.
    ///
    /// <para>A whole page shrunk to README width prints the footer at about four pixels tall, which
    /// documents nothing. What the reader needs to see is the line itself.</para>
    /// </summary>
    public static byte[] BottomOfPage(DocumentRenderSource source, int pageIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        float scale = RenderDpi / PointsPerInch;
        using SKBitmap page = SKBitmap.Decode(source.RenderPageToPng(pageIndex, scale))
            ?? throw new InvalidOperationException($"Page {pageIndex + 1} did not render.");

        // An EIGHTH, not a quarter. The footer sits about four points above the bottom margin, and
        // the space above it is usually empty — a quarter-page crop is a picture of white paper
        // with one grey line near the bottom, which documents nothing. This is the band the reader
        // is being shown.
        int keep = page.Height / 8;
        using var strip = new SKBitmap(page.Width, keep, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(strip))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(page, 0, -(page.Height - keep));
        }

        return Png.Sanitise(Resample(strip, 900));
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
