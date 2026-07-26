using SkiaSharp;
using Xunit;

namespace TrestleBoard.Imaging.Tests;

/// <summary>M6 pipeline, fit and cache behaviour (docs/M6-spec.md §2, §5).</summary>
public sealed class PipelineAndCacheTests
{
    private static DecodedImage Decode(SKBitmap bitmap) =>
        ImageDecoder.Decode(TestImages.EncodePng(bitmap))
        ?? throw new InvalidOperationException("fixture failed to decode");

    [Fact]
    public void CropThenRotateAppliesInThatOrder()
    {
        using SKBitmap source = TestImages.SplitHalves(200, 100, SKColors.Red, SKColors.Blue);
        using DecodedImage decoded = Decode(source);

        // Crop the left (red) half, then rotate a quarter turn: 100×100 becomes 100×100, all red.
        var recipe = new ImageRecipeSpec(
            Crop: new NormalizedRect(0f, 0f, 0.5f, 1f),
            RotationSteps: 1);
        using SKImage result = ImagePipeline.Render(decoded, recipe);

        Assert.Equal(100, result.Width);
        Assert.Equal(100, result.Height);
        using SKBitmap pixels = SKBitmap.FromImage(result);
        (byte r, _, byte b) = TestImages.At(pixels, 50, 50);
        Assert.True(r > 200 && b < 60, $"expected the cropped red half, got r={r} b={b}");
    }

    [Fact]
    public void RotationIsNormalisedAndFourStepsIsANoOp()
    {
        using SKBitmap source = TestImages.SplitHalves(80, 40, SKColors.Red, SKColors.Blue);
        using DecodedImage decoded = Decode(source);

        using SKImage full = ImagePipeline.Render(decoded, new ImageRecipeSpec(RotationSteps: 4));
        Assert.Equal(80, full.Width);
        Assert.Equal(40, full.Height);

        using SKImage negative = ImagePipeline.Render(decoded, new ImageRecipeSpec(RotationSteps: -1));
        Assert.Equal(40, negative.Width);
        Assert.Equal(80, negative.Height);
    }

    [Fact]
    public void RenderDownscalesToTheBudgetButNeverUpscales()
    {
        using SKBitmap big = TestImages.LowContrastRamp(1200, 600, 30, 220);
        using DecodedImage decoded = Decode(big);

        using SKImage limited = ImagePipeline.Render(decoded, ImageRecipeSpec.Identity, maxPixelSize: 300);
        Assert.Equal(300, limited.Width);
        Assert.Equal(150, limited.Height);

        using SKBitmap small = TestImages.LowContrastRamp(64, 32, 30, 220);
        using DecodedImage smallDecoded = Decode(small);
        using SKImage untouched = ImagePipeline.Render(smallDecoded, ImageRecipeSpec.Identity, maxPixelSize: 4096);
        Assert.Equal(64, untouched.Width);
    }

    [Fact]
    public void SlidersMoveThePixelsByTheExpectedAmountAndNeverBlackOutThePhoto()
    {
        // Regression: Skia's colour-matrix offsets are normalised [0,1]. Feeding them 0–255
        // clamped every channel and painted photos solid black, which "did it change?" style
        // assertions happily accepted.
        using SKBitmap flat = TestImages.Solid(32, 32, new SKColor(0x80, 0x80, 0x80));
        using DecodedImage decoded = Decode(flat);

        using SKImage untouched = ImagePipeline.Render(decoded, ImageRecipeSpec.Identity);
        using SKBitmap untouchedPixels = SKBitmap.FromImage(untouched);
        (byte ur, _, _) = TestImages.At(untouchedPixels, 16, 16);
        Assert.InRange(ur, 0x7C, 0x84);

        // +0.25 brightness is a quarter of full scale: 0x80 + 64 ≈ 0xC0.
        using SKImage brighter = ImagePipeline.Render(decoded, new ImageRecipeSpec(Brightness: 0.25f));
        using SKBitmap brighterPixels = SKBitmap.FromImage(brighter);
        (byte br, _, _) = TestImages.At(brighterPixels, 16, 16);
        Assert.InRange(br, 0xB8, 0xC8);

        // Contrast pivots on mid-grey, so mid-grey itself must barely move.
        using SKImage contrasted = ImagePipeline.Render(decoded, new ImageRecipeSpec(Contrast: 0.5f));
        using SKBitmap contrastedPixels = SKBitmap.FromImage(contrasted);
        (byte cr, _, _) = TestImages.At(contrastedPixels, 16, 16);
        Assert.InRange(cr, 0x78, 0x88);
    }

    [Fact]
    public void SlidersChangeThePixelsAndZeroLeavesThemAlone()
    {
        using SKBitmap source = TestImages.LowContrastRamp(64, 32, 80, 160);
        using DecodedImage decoded = Decode(source);

        using SKImage neutral = ImagePipeline.Render(decoded, ImageRecipeSpec.Identity);
        using SKBitmap neutralPixels = SKBitmap.FromImage(neutral);
        (byte nr, _, _) = TestImages.At(neutralPixels, 32, 16);

        using SKImage brighter = ImagePipeline.Render(decoded, new ImageRecipeSpec(Brightness: 0.25f));
        using SKBitmap brightPixels = SKBitmap.FromImage(brighter);
        (byte br, _, _) = TestImages.At(brightPixels, 32, 16);
        Assert.True(br > nr + 30, $"brightness slider did nothing: {nr} → {br}");

        using SKImage grey = ImagePipeline.Render(decoded, new ImageRecipeSpec(Saturation: -1f));
        using SKBitmap greyPixels = SKBitmap.FromImage(grey);
        (byte gr, byte gg, byte gb) = TestImages.At(greyPixels, 32, 16);
        Assert.True(Math.Abs(gr - gg) <= 2 && Math.Abs(gg - gb) <= 2, "full desaturation should be grey");
    }

    [Fact]
    public void IdentityRecipeIsRecognised()
    {
        Assert.True(ImageRecipeSpec.Identity.IsIdentity);
        Assert.True(new ImageRecipeSpec(RotationSteps: 4).IsIdentity);
        Assert.False(new ImageRecipeSpec(AutoLevels: true).IsIdentity);
        Assert.False(new ImageRecipeSpec(Crop: new NormalizedRect(0f, 0f, 0.5f, 1f)).IsIdentity);
    }

    [Fact]
    public void StableHashSeparatesRecipesAndSurvivesRestarts()
    {
        var a = new ImageRecipeSpec(Brightness: 0.2f);
        var b = new ImageRecipeSpec(Brightness: 0.2f);
        var c = new ImageRecipeSpec(Brightness: 0.2f, AutoLevels: true);
        var d = new ImageRecipeSpec(Brightness: 0.2f, AutoLevels: true, LevelsMode: AutoLevelsMode.PerChannel);

        Assert.Equal(a.StableHash(), b.StableHash());
        Assert.NotEqual(a.StableHash(), c.StableHash());
        Assert.NotEqual(c.StableHash(), d.StableHash());

        // The whole point of the hash: it is a value, not a per-process random.
        Assert.Equal("107d08cc148580f5", ImageRecipeSpec.Identity.CacheKeyPart());
    }

    // ---- Fit ---------------------------------------------------------------------------------

    [Fact]
    public void CoverFillsTheFrameByCroppingTheSource()
    {
        (SKRect src, SKRect dest) = ImagePipeline.Fit(
            new SKSize(400, 200), new SKRect(0, 0, 100, 100), ImageFitMode.Cover);

        Assert.Equal(new SKRect(0, 0, 100, 100), dest);
        Assert.Equal(200f, src.Width, 3);   // a square window out of a 2:1 photo
        Assert.Equal(200f, src.Height, 3);
        Assert.Equal(100f, src.Left, 3);    // centred
    }

    [Fact]
    public void ContainLetterboxesInsideTheFrame()
    {
        (SKRect src, SKRect dest) = ImagePipeline.Fit(
            new SKSize(400, 200), new SKRect(0, 0, 100, 100), ImageFitMode.Contain);

        Assert.Equal(new SKRect(0, 0, 400, 200), src);
        Assert.Equal(100f, dest.Width, 3);
        Assert.Equal(50f, dest.Height, 3);
        Assert.Equal(25f, dest.Top, 3);
    }

    [Fact]
    public void StretchUsesTheWholeFrameAndTheWholeSource()
    {
        (SKRect src, SKRect dest) = ImagePipeline.Fit(
            new SKSize(400, 200), new SKRect(10, 20, 110, 220), ImageFitMode.Stretch);
        Assert.Equal(new SKRect(0, 0, 400, 200), src);
        Assert.Equal(new SKRect(10, 20, 110, 220), dest);
    }

    // ---- Cache -------------------------------------------------------------------------------

    [Fact]
    public void CacheServesRepeatRendersAndCountsHits()
    {
        using var cache = new RecipeCache(capacity: 4);
        using SKBitmap source = TestImages.LowContrastRamp(64, 32, 40, 200);
        using DecodedImage decoded = Decode(source);

        int renders = 0;
        SKImage Render() { renders++; return ImagePipeline.Render(decoded, ImageRecipeSpec.Identity, 256); }

        SKImage first = cache.GetOrAdd("photo.png", ImageRecipeSpec.Identity, 256, Render);
        SKImage second = cache.GetOrAdd("photo.png", ImageRecipeSpec.Identity, 256, Render);

        Assert.Same(first, second);
        Assert.Equal(1, renders);
        Assert.Equal(1, cache.HitCount);
        Assert.Equal(1, cache.MissCount);
    }

    [Fact]
    public void CacheEvictsTheLeastRecentlyUsedEntry()
    {
        using var cache = new RecipeCache(capacity: 2);
        using SKBitmap source = TestImages.LowContrastRamp(32, 16, 40, 200);
        using DecodedImage decoded = Decode(source);

        SKImage Render(ImageRecipeSpec r) => ImagePipeline.Render(decoded, r, 64);
        var a = new ImageRecipeSpec(Brightness: 0.1f);
        var b = new ImageRecipeSpec(Brightness: 0.2f);
        var c = new ImageRecipeSpec(Brightness: 0.3f);

        cache.GetOrAdd("p", a, 64, () => Render(a));
        cache.GetOrAdd("p", b, 64, () => Render(b));
        cache.GetOrAdd("p", a, 64, () => Render(a));   // 'a' is now the most recent
        cache.GetOrAdd("p", c, 64, () => Render(c));   // evicts 'b'

        Assert.Equal(2, cache.Count);
        int missesBefore = cache.MissCount;
        cache.GetOrAdd("p", b, 64, () => Render(b));
        Assert.Equal(missesBefore + 1, cache.MissCount);
    }

    [Fact]
    public void InvalidatingAnAssetDropsOnlyItsRenders()
    {
        using var cache = new RecipeCache(capacity: 8);
        using SKBitmap source = TestImages.LowContrastRamp(32, 16, 40, 200);
        using DecodedImage decoded = Decode(source);
        SKImage Render() => ImagePipeline.Render(decoded, ImageRecipeSpec.Identity, 64);

        cache.GetOrAdd("one.png", ImageRecipeSpec.Identity, 64, Render);
        cache.GetOrAdd("two.png", ImageRecipeSpec.Identity, 64, Render);
        cache.InvalidateAsset("one.png");

        Assert.Equal(1, cache.Count);
        int hitsBefore = cache.HitCount;
        cache.GetOrAdd("two.png", ImageRecipeSpec.Identity, 64, Render);
        Assert.Equal(hitsBefore + 1, cache.HitCount);
    }
}
