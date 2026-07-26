using SkiaSharp;

namespace TrestleBoard.App.Canvas;

/// <summary>Generates the fictional placeholder "photo" PNG for the sample document —
/// no real photographs ship in the app (PLAN.md §0).</summary>
public static class SamplePhoto
{
    public static byte[] CreatePng(int width = 440, int height = 300)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create raster surface for sample photo.");
        SKCanvas canvas = surface.Canvas;

        // Simple sky/ground scene reads as "a photo" at thumbnail size without depicting anyone.
        using (var sky = new SKPaint { Color = new SKColor(0xFF9FB8CE) })
        {
            canvas.DrawRect(SKRect.Create(0, 0, width, height * 0.62f), sky);
        }

        using (var ground = new SKPaint { Color = new SKColor(0xFF7E8B6F) })
        {
            canvas.DrawRect(SKRect.Create(0, height * 0.62f, width, height * 0.38f), ground);
        }

        using (var building = new SKPaint { Color = new SKColor(0xFFB8AB98) })
        {
            canvas.DrawRect(SKRect.Create(width * 0.3f, height * 0.28f, width * 0.4f, height * 0.4f), building);
        }

        using (var roof = new SKPaint { Color = new SKColor(0xFF6E5F4E), IsAntialias = true })
        using (var path = new SKPath())
        {
            path.MoveTo(width * 0.27f, height * 0.28f);
            path.LineTo(width * 0.5f, height * 0.12f);
            path.LineTo(width * 0.73f, height * 0.28f);
            path.Close();
            canvas.DrawPath(path, roof);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("PNG encode failed for sample photo.");
        return data.ToArray();
    }
}
