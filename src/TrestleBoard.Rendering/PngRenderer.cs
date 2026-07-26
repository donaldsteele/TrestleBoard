using SkiaSharp;
using TrestleBoard.Layout;

namespace TrestleBoard.Rendering;

/// <summary>
/// Offscreen PNG helper for tests and headless export. Uses a deterministic CPU raster
/// surface: Rgba8888, premultiplied, explicit sRGB — never GPU (docs/M1-spec.md §5).
/// </summary>
public static class PngRenderer
{
    public static byte[] RenderToPng(
        LayoutResult result,
        int pageWidthPt,
        int pageHeightPt,
        float scale = 1f,
        uint backgroundArgb = 0xFFFFFFFF,
        PageRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageWidthPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageHeightPt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        int widthPx = (int)MathF.Ceiling(pageWidthPt * scale);
        int heightPx = (int)MathF.Ceiling(pageHeightPt * scale);
        var info = new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create CPU raster surface.");
        SKCanvas canvas = surface.Canvas;
        canvas.Scale(scale);
        PageRenderOptions effective = (options ?? new PageRenderOptions()) with { BackgroundArgb = backgroundArgb };
        new PageRenderer(effective).Render(canvas, result);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return data.ToArray();
    }
}
