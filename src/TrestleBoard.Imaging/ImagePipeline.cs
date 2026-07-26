using SkiaSharp;

namespace TrestleBoard.Imaging;

/// <summary>How a processed photo fills its frame (mirrors Core's ImageFit without referencing it).</summary>
public enum ImageFitMode
{
    Cover,
    Contain,
    Stretch,
}

/// <summary>
/// Applies a recipe to a decoded photo in the fixed order crop → rotate → auto-levels → colour
/// sliders → resample (docs/M6-spec.md §2; the steps are not commutative, so the order is
/// normative). Never writes back to the asset: originals stay byte-identical in the container.
/// </summary>
public static class ImagePipeline
{
    /// <summary>Slider range is [-1, 1] with 0 meaning untouched.</summary>
    public const float SliderRange = 1f;

    /// <summary>
    /// Renders the recipe. The result is downscaled so its long edge is at most
    /// <paramref name="maxPixelSize"/> — it is never upscaled, because enlarging a small photo to
    /// fill a frame only makes the blur bigger.
    /// </summary>
    public static SKImage Render(DecodedImage source, ImageRecipeSpec recipe, int maxPixelSize = 2048)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPixelSize, 1);

        SKBitmap working = source.Bitmap;
        var owned = new List<SKBitmap>();

        try
        {
            if (recipe.Crop is { } crop)
            {
                SKRectI region = crop.ToPixels(working.Width, working.Height);
                if (region.Width != working.Width || region.Height != working.Height)
                {
                    working = Track(owned, Extract(working, region));
                }
            }

            if (recipe.NormalizedRotation != 0)
            {
                working = Track(owned, Rotate(working, recipe.NormalizedRotation));
            }

            if (recipe.AutoLevels)
            {
                SKBitmap leveled = AutoLevels.Apply(working, recipe.LevelsMode);
                if (!ReferenceEquals(leveled, working))
                {
                    working = Track(owned, leveled);
                }
            }

            return Resample(working, recipe, maxPixelSize);
        }
        finally
        {
            foreach (SKBitmap bitmap in owned)
            {
                bitmap.Dispose();
            }
        }
    }

    /// <summary>
    /// Colour-slider matrix: brightness as an offset, contrast around mid-grey, saturation as a
    /// luma-weighted blend. One filter, so the three sliders cost one draw.
    /// </summary>
    public static SKColorFilter? CreateColorFilter(float brightness, float contrast, float saturation)
    {
        if (brightness == 0f && contrast == 0f && saturation == 0f)
        {
            return null;
        }

        // Saturation: blend each channel toward Rec. 601 luma. s = 1 leaves colour untouched.
        float s = Math.Clamp(1f + saturation, 0f, 2f);
        const float lr = 0.299f;
        const float lg = 0.587f;
        const float lb = 0.114f;
        float[] matrix =
        [
            lr + (s * (1 - lr)), lg * (1 - s), lb * (1 - s), 0, 0,
            lr * (1 - s), lg + (s * (1 - lg)), lb * (1 - s), 0, 0,
            lr * (1 - s), lg * (1 - s), lb + (s * (1 - lb)), 0, 0,
            0, 0, 0, 1, 0,
        ];
        SKColorFilter filter = SKColorFilter.CreateColorMatrix(matrix);

        // Contrast pivots on mid-grey so a bump does not also brighten; brightness is a plain
        // offset applied afterwards. Skia's colour-matrix translation column is in NORMALISED
        // [0,1] units — scaling it by 255 clamps every channel and paints the photo black.
        float c = Math.Clamp(1f + contrast, 0f, 2f);
        float offset = (0.5f - (0.5f * c)) + brightness;
        if (c != 1f || offset != 0f)
        {
            float[] levels =
            [
                c, 0, 0, 0, offset,
                0, c, 0, 0, offset,
                0, 0, c, 0, offset,
                0, 0, 0, 1, 0,
            ];
            using SKColorFilter tone = SKColorFilter.CreateColorMatrix(levels);
            // Skia filters are ref-counted: CreateCompose takes its own references, so disposing
            // both inputs here leaves the composed filter valid.
            using SKColorFilter saturationOnly = filter;
            filter = SKColorFilter.CreateCompose(tone, saturationOnly);
        }

        return filter;
    }

    /// <summary>
    /// Source/destination rects for drawing a processed image into a frame. Cover fills and clips,
    /// Contain letterboxes, Stretch distorts — the placement half of the pipeline, kept here so it
    /// is testable without the document model.
    /// </summary>
    public static (SKRect Source, SKRect Dest) Fit(SKSize imagePx, SKRect frame, ImageFitMode mode)
    {
        var full = new SKRect(0, 0, imagePx.Width, imagePx.Height);
        if (imagePx.Width <= 0 || imagePx.Height <= 0 || frame.Width <= 0 || frame.Height <= 0)
        {
            return (full, frame);
        }

        switch (mode)
        {
            case ImageFitMode.Stretch:
                return (full, frame);

            case ImageFitMode.Contain:
            {
                float scale = Math.Min(frame.Width / imagePx.Width, frame.Height / imagePx.Height);
                float w = imagePx.Width * scale;
                float h = imagePx.Height * scale;
                var dest = new SKRect(
                    frame.MidX - (w / 2f), frame.MidY - (h / 2f),
                    frame.MidX + (w / 2f), frame.MidY + (h / 2f));
                return (full, dest);
            }

            default:
            {
                // Cover: crop the source to the frame's aspect, so nothing is distorted and the
                // frame is completely filled.
                float frameAspect = frame.Width / frame.Height;
                float imageAspect = imagePx.Width / imagePx.Height;
                SKRect src = full;
                if (imageAspect > frameAspect)
                {
                    float w = imagePx.Height * frameAspect;
                    src = new SKRect((imagePx.Width - w) / 2f, 0, (imagePx.Width + w) / 2f, imagePx.Height);
                }
                else if (imageAspect < frameAspect)
                {
                    float h = imagePx.Width / frameAspect;
                    src = new SKRect(0, (imagePx.Height - h) / 2f, imagePx.Width, (imagePx.Height + h) / 2f);
                }

                return (src, frame);
            }
        }
    }

    private static SKImage Resample(SKBitmap working, ImageRecipeSpec recipe, int maxPixelSize)
    {
        float scale = Math.Min(1f, (float)maxPixelSize / Math.Max(working.Width, working.Height));
        int width = Math.Max(1, (int)MathF.Round(working.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(working.Height * scale));

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create an image surface.");
        using SKColorFilter? filter = CreateColorFilter(recipe.Brightness, recipe.Contrast, recipe.Saturation);
        using var paint = new SKPaint { ColorFilter = filter, IsAntialias = true };

        surface.Canvas.Clear(SKColors.Transparent);
        using SKImage snapshot = SKImage.FromBitmap(working);
        surface.Canvas.DrawImage(
            snapshot,
            new SKRect(0, 0, working.Width, working.Height),
            new SKRect(0, 0, width, height),
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            paint);
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    private static SKBitmap Extract(SKBitmap source, SKRectI region)
    {
        var cropped = new SKBitmap(new SKImageInfo(
            region.Width, region.Height, source.ColorType, source.AlphaType));
        if (!source.ExtractSubset(cropped, region))
        {
            cropped.Dispose();
            throw new InvalidOperationException("Could not crop the photo.");
        }

        // ExtractSubset aliases the source pixels; copy so later stages own their memory.
        SKBitmap detached = cropped.Copy();
        cropped.Dispose();
        return detached;
    }

    private static SKBitmap Rotate(SKBitmap source, int steps)
    {
        bool swapsAxes = steps is 1 or 3;
        int width = swapsAxes ? source.Height : source.Width;
        int height = swapsAxes ? source.Width : source.Height;
        var rotated = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Translate(width / 2f, height / 2f);
            canvas.RotateDegrees(90 * steps);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
        }

        return rotated;
    }

    private static SKBitmap Track(List<SKBitmap> owned, SKBitmap bitmap)
    {
        owned.Add(bitmap);
        return bitmap;
    }
}
