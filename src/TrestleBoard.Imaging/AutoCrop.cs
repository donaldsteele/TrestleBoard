using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>
/// Auto-crop per PLAN.md §9: downscale to 256px, Sobel energy map with a skin-tone bonus (lodge
/// photos are mostly people), slide target-aspect windows and score energy inside minus a
/// border-cut penalty, then propose the winner for the user to adjust.
///
/// Fully deterministic — integer analysis grid, fixed step, centre-biased tie-break — so the same
/// photo proposes the same crop on Windows, Linux and macOS.
/// </summary>
public static class AutoCrop
{
    /// <summary>Long edge of the analysis image. Small on purpose: this is a saliency estimate.</summary>
    public const int AnalysisLongEdgePx = 256;

    /// <summary>Search step as a fraction of the analysis long edge.</summary>
    public const float SearchStepFraction = 0.02f;

    /// <summary>How much a skin-tone pixel adds to the energy map (PLAN §9 "skin-tone bonus").</summary>
    public const float SkinToneBonus = 48f;

    /// <summary>Weight on energy sliced off by the window edge — what stops it cutting faces.</summary>
    public const float BorderCutPenalty = 1.5f;

    /// <summary>Proposes a crop of the given aspect (width/height). Analysis only ever runs on the
    /// downscaled copy; the returned rect is normalized against the full-size image.</summary>
    public static NormalizedRect Propose(DecodedImage image, float targetAspect)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Propose(image.Bitmap, targetAspect);
    }

    public static NormalizedRect Propose(SKBitmap bitmap, float targetAspect)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!float.IsFinite(targetAspect) || targetAspect <= 0f || bitmap.Width < 2 || bitmap.Height < 2)
        {
            return NormalizedRect.Full;
        }

        using SKBitmap analysis = Downscale(bitmap, AnalysisLongEdgePx);
        float[] energy = EnergyMap(analysis);
        int w = analysis.Width;
        int h = analysis.Height;

        // Largest window of the requested aspect that still fits.
        float sourceAspect = (float)w / h;
        int windowW = targetAspect >= sourceAspect ? w : (int)MathF.Round(h * targetAspect);
        int windowH = targetAspect >= sourceAspect ? (int)MathF.Round(w / targetAspect) : h;
        windowW = Math.Clamp(windowW, 1, w);
        windowH = Math.Clamp(windowH, 1, h);
        if (windowW == w && windowH == h)
        {
            return NormalizedRect.Full; // already the right shape — nothing to choose
        }

        float[] integral = Integral(energy, w, h);
        float total = RegionSum(integral, w, h, 0, 0, w, h);
        int step = Math.Max(1, (int)MathF.Round(AnalysisLongEdgePx * SearchStepFraction));

        float bestScore = float.NegativeInfinity;
        int bestX = (w - windowW) / 2;
        int bestY = (h - windowH) / 2;
        float centerX = (w - windowW) / 2f;
        float centerY = (h - windowH) / 2f;
        float bestCenterDistance = float.MaxValue;

        for (int y = 0; y <= h - windowH; y += step)
        {
            for (int x = 0; x <= w - windowW; x += step)
            {
                float inside = RegionSum(integral, w, h, x, y, x + windowW, y + windowH);
                float score = inside - (BorderCutPenalty * (total - inside));
                float distance = ((x - centerX) * (x - centerX)) + ((y - centerY) * (y - centerY));

                // Strictly-better wins; an exact tie goes to the more centred window, which keeps
                // the result stable regardless of scan order.
                if (score > bestScore + 1e-4f || (score > bestScore - 1e-4f && distance < bestCenterDistance))
                {
                    bestScore = score;
                    bestCenterDistance = distance;
                    bestX = x;
                    bestY = y;
                }
            }
        }

        return new NormalizedRect(
            (float)bestX / w,
            (float)bestY / h,
            (float)windowW / w,
            (float)windowH / h).Clamped();
    }

    /// <summary>Sobel gradient magnitude on luma, plus the skin-tone bonus.</summary>
    public static float[] EnergyMap(SKBitmap analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        int w = analysis.Width;
        int h = analysis.Height;
        var luma = new float[w * h];
        var bonus = new float[w * h];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                SKColor c = analysis.GetPixel(x, y);
                int i = (y * w) + x;
                luma[i] = ((c.Red * 299) + (c.Green * 587) + (c.Blue * 114)) / 1000f;
                bonus[i] = IsSkinTone(c.Red, c.Green, c.Blue) ? SkinToneBonus : 0f;
            }
        }

        var energy = new float[w * h];
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                float gx =
                    -luma[((y - 1) * w) + x - 1] - (2 * luma[(y * w) + x - 1]) - luma[((y + 1) * w) + x - 1]
                    + luma[((y - 1) * w) + x + 1] + (2 * luma[(y * w) + x + 1]) + luma[((y + 1) * w) + x + 1];
                float gy =
                    -luma[((y - 1) * w) + x - 1] - (2 * luma[((y - 1) * w) + x]) - luma[((y - 1) * w) + x + 1]
                    + luma[((y + 1) * w) + x - 1] + (2 * luma[((y + 1) * w) + x]) + luma[((y + 1) * w) + x + 1];
                energy[(y * w) + x] = MathF.Sqrt((gx * gx) + (gy * gy)) + bonus[(y * w) + x];
            }
        }

        // Edge rows/columns carry the bonus but no gradient (no neighbourhood to sample).
        // The column loop skips the first and last row: those four corners belong to the row loop,
        // and adding the bonus twice would quietly over-weight them.
        for (int x = 0; x < w; x++)
        {
            energy[x] += bonus[x];
            energy[((h - 1) * w) + x] += bonus[((h - 1) * w) + x];
        }

        for (int y = 1; y < h - 1; y++)
        {
            energy[y * w] += bonus[y * w];
            energy[(y * w) + w - 1] += bonus[(y * w) + w - 1];
        }

        return energy;
    }

    /// <summary>
    /// Normalized-RGB skin envelope: hue-ish ratios rather than absolute brightness, so it survives
    /// the dim indoor light lodge photos are taken in.
    /// </summary>
    public static bool IsSkinTone(byte r, byte g, byte b)
    {
        int sum = r + g + b;
        if (sum < 90 || r <= g || r <= b)
        {
            return false;
        }

        float nr = (float)r / sum;
        float ng = (float)g / sum;
        return nr is > 0.33f and < 0.50f
            && ng is > 0.26f and < 0.36f
            && r - g >= 12
            && Math.Abs(r - g) < 130;
    }

    private static SKBitmap Downscale(SKBitmap source, int longEdge)
    {
        int w = source.Width;
        int h = source.Height;
        float scale = (float)longEdge / Math.Max(w, h);
        if (scale >= 1f)
        {
            return source.Copy();
        }

        var info = new SKImageInfo(
            Math.Max(2, (int)MathF.Round(w * scale)),
            Math.Max(2, (int)MathF.Round(h * scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Unpremul);
        var scaled = new SKBitmap(info);
        source.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return scaled;
    }

    private static float[] Integral(float[] energy, int w, int h)
    {
        // (w+1)×(h+1) summed-area table: every window score is then four lookups.
        var integral = new float[(w + 1) * (h + 1)];
        for (int y = 1; y <= h; y++)
        {
            float rowSum = 0f;
            for (int x = 1; x <= w; x++)
            {
                rowSum += energy[((y - 1) * w) + x - 1];
                integral[(y * (w + 1)) + x] = integral[((y - 1) * (w + 1)) + x] + rowSum;
            }
        }

        return integral;
    }

    private static float RegionSum(float[] integral, int w, int h, int left, int top, int right, int bottom)
    {
        int stride = w + 1;
        right = Math.Min(right, w);
        bottom = Math.Min(bottom, h);
        return integral[(bottom * stride) + right]
            - integral[(top * stride) + right]
            - integral[(bottom * stride) + left]
            + integral[(top * stride) + left];
    }
}
