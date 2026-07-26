using SkiaSharp;

namespace TrestleBoard.Imaging.Tests;

/// <summary>
/// Synthetic fixtures only — no photograph of a real person ever enters this repo (PLAN.md §0).
/// Everything here is generated from code, so the tests are also deterministic across platforms.
/// </summary>
internal static class TestImages
{
    public static SKBitmap Solid(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        return bitmap;
    }

    /// <summary>Left half one colour, right half another — the orientation probe.</summary>
    public static SKBitmap SplitHalves(int width, int height, SKColor left, SKColor right)
    {
        SKBitmap bitmap = Solid(width, height, left);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = right };
        canvas.DrawRect(new SKRect(width / 2f, 0, width, height), paint);
        return bitmap;
    }

    /// <summary>A washed-out horizontal ramp spanning only [low, high] — the auto-levels fixture.</summary>
    public static SKBitmap LowContrastRamp(int width, int height, byte low, byte high)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int x = 0; x < width; x++)
        {
            byte v = (byte)(low + ((high - low) * x / Math.Max(1, width - 1)));
            for (int y = 0; y < height; y++)
            {
                bitmap.SetPixel(x, y, new SKColor(v, v, v));
            }
        }

        return bitmap;
    }

    /// <summary>A warm (candle-lit) ramp: red leads, blue trails — the tint-shift fixture.</summary>
    public static SKBitmap WarmRamp(int width, int height)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int x = 0; x < width; x++)
        {
            int t = 60 + (60 * x / Math.Max(1, width - 1));
            for (int y = 0; y < height; y++)
            {
                bitmap.SetPixel(x, y, new SKColor((byte)(t + 40), (byte)t, (byte)Math.Max(0, t - 30)));
            }
        }

        return bitmap;
    }

    /// <summary>Flat field with a busy striped patch — high Sobel energy in a known place.</summary>
    public static SKBitmap DetailPatch(int width, int height, SKRectI patch)
    {
        SKBitmap bitmap = Solid(width, height, new SKColor(0x80, 0x80, 0x80));
        using var canvas = new SKCanvas(bitmap);
        using var dark = new SKPaint { Color = new SKColor(0x10, 0x10, 0x10) };
        for (int x = patch.Left; x < patch.Right; x += 4)
        {
            canvas.DrawRect(new SKRect(x, patch.Top, x + 2, patch.Bottom), dark);
        }

        return bitmap;
    }

    /// <summary>Flat field with a skin-tone oval — stands in for "there are people here".</summary>
    public static SKBitmap SkinPatch(int width, int height, SKRect patch)
    {
        SKBitmap bitmap = Solid(width, height, new SKColor(0x70, 0x74, 0x78));
        using var canvas = new SKCanvas(bitmap);
        using var skin = new SKPaint { Color = new SKColor(0xD0, 0xA0, 0x80), IsAntialias = false };
        canvas.DrawOval(patch, skin);
        return bitmap;
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Encodes a JPEG and splices in a minimal EXIF APP1 segment carrying just the orientation
    /// tag. Skia writes no EXIF of its own, so this is how the decoder's orientation path gets
    /// exercised without committing a binary fixture.
    /// </summary>
    public static byte[] EncodeJpegWithOrientation(SKBitmap bitmap, int orientation)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        byte[] jpeg = data.ToArray();

        byte[] app1 =
        [
            0xFF, 0xE1,                                     // APP1
            0x00, 0x22,                                     // segment length (34)
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,             // "Exif\0\0"
            0x49, 0x49, 0x2A, 0x00,                         // little-endian TIFF header
            0x08, 0x00, 0x00, 0x00,                         // IFD0 at offset 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01,                                     // tag 0x0112 (Orientation)
            0x03, 0x00,                                     // type SHORT
            0x01, 0x00, 0x00, 0x00,                         // count 1
            (byte)orientation, 0x00, 0x00, 0x00,            // value (+ padding)
            0x00, 0x00, 0x00, 0x00,                         // no next IFD
        ];

        var result = new byte[jpeg.Length + app1.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);                   // SOI
        app1.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app1.Length));
        return result;
    }

    public static (byte R, byte G, byte B) At(SKBitmap bitmap, int x, int y)
    {
        SKColor c = bitmap.GetPixel(x, y);
        return (c.Red, c.Green, c.Blue);
    }

    public static (byte Min, byte Max) LumaRange(SKBitmap bitmap)
    {
        byte min = 255;
        byte max = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor c = bitmap.GetPixel(x, y);
                byte luma = (byte)(((c.Red * 299) + (c.Green * 587) + (c.Blue * 114)) / 1000);
                min = Math.Min(min, luma);
                max = Math.Max(max, luma);
            }
        }

        return (min, max);
    }
}
