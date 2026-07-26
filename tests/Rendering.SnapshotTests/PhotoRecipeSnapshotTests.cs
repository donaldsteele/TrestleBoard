using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M6 acceptance on the page (docs/M6-spec.md §2): the sample document's photo rendered with a
/// real recipe — cropped, turned, auto-levelled and warmed — proving the pipeline is what paints
/// and that its output is deterministic. Per-OS baselines, strict gate.
/// </summary>
public sealed class PhotoRecipeSnapshotTests
{
    private const string PhotoBlockId = "block-photo";

    /// <summary>
    /// A slightly flat variant of the scene fixture — enough headroom for auto-levels to do
    /// visible work, but a realistic tonal range. A pathologically flat fixture (30 luma levels)
    /// makes a luminance stretch multiply chroma differences ~8×, which is correct arithmetic and
    /// a useless-looking baseline.
    /// </summary>
    private static byte[] FlatPhotoPng()
    {
        var info = new SKImageInfo(440, 300, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;

        // Gradients, not flat fills: a three-tone scene has nothing between its extremes, so any
        // stretch drives them to pure black and white and the baseline stops resembling a photo.
        using (var sky = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, 186),
                [new SKColor(0xFFC8D6E2), new SKColor(0xFF9FB0BE)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 440, 186), sky);
        }

        using (var ground = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 186),
                new SKPoint(0, 300),
                [new SKColor(0xFF8A9280), new SKColor(0xFF5C6650)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(0, 186, 440, 114), ground);
        }

        using (var building = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(132, 0),
                new SKPoint(308, 0),
                [new SKColor(0xFFA89E90), new SKColor(0xFF7E7568)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(132, 84, 176, 120), building);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    private static DocumentRenderSource CreateSource()
    {
        TboardPackage package = SampleDocument.CreatePackage(FlatPhotoPng());
        var photo = (ImageFrame)package.Document.FindBlock(PhotoBlockId).Block;
        photo.Recipe = new ImageRecipe
        {
            CropNormalized = new RectPt(0.15f, 0.10f, 0.70f, 0.75f),
            RotationSteps = 0,
            AutoLevels = true,
            Brightness = 0.04f,
            Contrast = 0.10f,
            Saturation = 0.10f,
        };
        return DocumentRenderSource.Create(package.Document, package.Assets, SnapshotInfra.Store.Value);
    }

    [Fact]
    public void PhotoRecipeMatchesBaseline()
    {
        using DocumentRenderSource source = CreateSource();
        byte[] actual = DocumentSnapshotTests.RenderPagePng(source, 0);
        const string name = "photo-recipe-page1";
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, name + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            string candidate = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail($"Baseline missing: {baselinePath}. Actual written to {candidate} " +
                        "(run with TRESTLEBOARD_UPDATE_BASELINES=1 or promote the CI artifact).");
        }

        byte[] expected = File.ReadAllBytes(baselinePath);
        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(actual, expected);
        if (diff.DiffPixelCount > 0)
        {
            string artifact = SnapshotInfra.WriteActualArtifact(name, actual);
            Assert.Fail(
                $"Snapshot '{name}' differs from baseline: {diff.DiffPixelCount}/{diff.TotalPixels} pixels, "
                + $"max channel diff {diff.MaxChannelDiff}. Actual written to {artifact}");
        }
    }

    [Fact]
    public void TheRecipeVisiblyChangesWhatIsPainted()
    {
        TboardPackage plainPackage = SampleDocument.CreatePackage(FlatPhotoPng());
        using DocumentRenderSource plain = DocumentRenderSource.Create(
            plainPackage.Document, plainPackage.Assets, SnapshotInfra.Store.Value);
        byte[] untouched = DocumentSnapshotTests.RenderPagePng(plain, 0);

        using DocumentRenderSource cooked = CreateSource();
        byte[] processed = DocumentSnapshotTests.RenderPagePng(cooked, 0);

        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(processed, untouched);
        Assert.True(diff.DiffPixelCount > 5000, $"recipe barely changed the page ({diff.DiffPixelCount} px)");
    }

    [Fact]
    public void RenderingIsStableAcrossRepeatedPaints()
    {
        using DocumentRenderSource source = CreateSource();
        byte[] first = DocumentSnapshotTests.RenderPagePng(source, 0);
        byte[] second = DocumentSnapshotTests.RenderPagePng(source, 0);
        Assert.Equal(first, second);
    }
}
