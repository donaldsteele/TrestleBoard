using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>
/// Auto-levels per PLAN.md §9: per-channel 0.5% percentile clip + linear stretch, with a
/// luminance-only mode that computes one stretch and applies it to every channel so a warm room
/// (candlelight, wood panelling — most lodge photos) does not get neutralised into grey.
/// </summary>
public static class AutoLevels
{
    /// <summary>Fraction of pixels ignored at each end of the histogram (PLAN §9: 0.5%).</summary>
    public const float ClipFraction = 0.005f;

    /// <summary>The per-channel [low, high] taken as black and white points.</summary>
    public readonly record struct Levels(byte RedLow, byte RedHigh, byte GreenLow, byte GreenHigh, byte BlueLow, byte BlueHigh);

    /// <summary>Measures the clip points without touching the pixels (used by tests and the UI).</summary>
    public static Levels Measure(SKBitmap bitmap, AutoLevelsMode mode = AutoLevelsMode.Luminance)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        (int[] red, int[] green, int[] blue, int[] luma, int counted) = Histograms(bitmap);
        if (counted == 0)
        {
            return new Levels(0, 255, 0, 255, 0, 255);
        }

        if (mode is AutoLevelsMode.Luminance)
        {
            (byte low, byte high) = ClipPoints(luma, counted);
            return new Levels(low, high, low, high, low, high);
        }

        (byte rl, byte rh) = ClipPoints(red, counted);
        (byte gl, byte gh) = ClipPoints(green, counted);
        (byte bl, byte bh) = ClipPoints(blue, counted);
        return new Levels(rl, rh, gl, gh, bl, bh);
    }

    /// <summary>
    /// Returns a stretched copy, or the SAME instance when there is nothing safe to stretch — a
    /// flat or single-tone image must be passed through, never amplified into noise.
    /// </summary>
    public static SKBitmap Apply(SKBitmap bitmap, AutoLevelsMode mode = AutoLevelsMode.Luminance)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        Levels levels = Measure(bitmap, mode);
        byte[] red = BuildLut(levels.RedLow, levels.RedHigh);
        byte[] green = BuildLut(levels.GreenLow, levels.GreenHigh);
        byte[] blue = BuildLut(levels.BlueLow, levels.BlueHigh);
        if (IsIdentityLut(red) && IsIdentityLut(green) && IsIdentityLut(blue))
        {
            return bitmap;
        }

        var stretched = new SKBitmap(new SKImageInfo(
            bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(stretched))
        // Alpha passes through untouched; SkiaSharp rejects a null table here.
        using (SKColorFilter filter = SKColorFilter.CreateTable(IdentityLut(), red, green, blue))
        using (var paint = new SKPaint { ColorFilter = filter })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(bitmap, 0, 0, paint);
        }

        return stretched;
    }

    private static (int[] Red, int[] Green, int[] Blue, int[] Luma, int Counted) Histograms(SKBitmap bitmap)
    {
        var red = new int[256];
        var green = new int[256];
        var blue = new int[256];
        var luma = new int[256];
        int counted = 0;

        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        int bytesPerPixel = bitmap.BytesPerPixel;
        SKColorType colorType = bitmap.ColorType;
        if (pixels.IsEmpty || bytesPerPixel < 4)
        {
            return (red, green, blue, luma, 0);
        }

        bool bgra = colorType is SKColorType.Bgra8888;
        for (int i = 0; i + 3 < pixels.Length; i += bytesPerPixel)
        {
            byte r = bgra ? pixels[i + 2] : pixels[i];
            byte g = pixels[i + 1];
            byte b = bgra ? pixels[i] : pixels[i + 2];
            byte a = pixels[i + 3];
            if (a == 0)
            {
                continue; // fully transparent pixels are not part of the picture
            }

            red[r]++;
            green[g]++;
            blue[b]++;
            // Rec. 601 luma, integer-only so the histogram is bit-identical on every platform.
            luma[((r * 299) + (g * 587) + (b * 114)) / 1000]++;
            counted++;
        }

        return (red, green, blue, luma, counted);
    }

    private static (byte Low, byte High) ClipPoints(int[] histogram, int counted)
    {
        int clip = (int)(counted * ClipFraction);
        int low = 0;
        int running = 0;
        for (int i = 0; i < histogram.Length; i++)
        {
            running += histogram[i];
            if (running > clip)
            {
                low = i;
                break;
            }
        }

        int high = 255;
        running = 0;
        for (int i = histogram.Length - 1; i >= 0; i--)
        {
            running += histogram[i];
            if (running > clip)
            {
                high = i;
                break;
            }
        }

        return high > low ? ((byte)low, (byte)high) : ((byte)0, (byte)255);
    }

    private static byte[] IdentityLut()
    {
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            lut[i] = (byte)i;
        }

        return lut;
    }

    private static byte[] BuildLut(byte low, byte high)
    {
        if (high <= low)
        {
            return IdentityLut();
        }

        var lut = new byte[256];

        float scale = 255f / (high - low);
        for (int i = 0; i < 256; i++)
        {
            lut[i] = (byte)Math.Clamp(MathF.Round((i - low) * scale), 0f, 255f);
        }

        return lut;
    }

    private static bool IsIdentityLut(byte[] lut)
    {
        for (int i = 0; i < lut.Length; i++)
        {
            if (lut[i] != i)
            {
                return false;
            }
        }

        return true;
    }
}
