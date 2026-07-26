using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M3 acceptance (PLAN.md §11): the code-generated trestle-board fixture — photo exclusion,
/// wrapped text, officers table box, two pages — rendered through DocumentRenderSource, the
/// SAME object the editor canvas and the PDF exporter draw from. Per-OS baselines, strict gate.
/// </summary>
public sealed class DocumentSnapshotTests
{
    public static TheoryData<int> PageIndexes => new() { 0, 1 };

    /// <summary>Deterministic stand-in "photo" (fictional scene, PLAN.md §0). Kept local to the
    /// test project — App's SamplePhoto is equivalent but Apps are not test dependencies.</summary>
    internal static byte[] TestPhotoPng()
    {
        var info = new SKImageInfo(440, 300, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;
        using (var sky = new SKPaint { Color = new SKColor(0xFF9FB8CE) })
        {
            canvas.DrawRect(SKRect.Create(0, 0, 440, 186), sky);
        }

        using (var ground = new SKPaint { Color = new SKColor(0xFF7E8B6F) })
        {
            canvas.DrawRect(SKRect.Create(0, 186, 440, 114), ground);
        }

        using (var building = new SKPaint { Color = new SKColor(0xFFB8AB98) })
        {
            canvas.DrawRect(SKRect.Create(132, 84, 176, 120), building);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    internal static DocumentRenderSource CreateSource()
    {
        TboardPackage package = SampleDocument.CreatePackage(TestPhotoPng());
        return DocumentRenderSource.Create(package.Document, package.Assets, SnapshotInfra.Store.Value);
    }

    internal static byte[] RenderPagePng(DocumentRenderSource source, int pageIndex)
    {
        TrestleBoard.Core.Model.SizePt size = source.GetPageSize(pageIndex);
        var info = new SKImageInfo(
            (int)MathF.Ceiling(size.Width),
            (int)MathF.Ceiling(size.Height),
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        source.RenderPage(surface.Canvas, pageIndex);
        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    [Theory]
    [MemberData(nameof(PageIndexes))]
    public void SampleDocumentPageMatchesBaseline(int pageIndex)
    {
        using DocumentRenderSource source = CreateSource();
        byte[] actual = RenderPagePng(source, pageIndex);
        string name = $"sample-document-page{pageIndex + 1}";
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, name + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            // Write the actual image so the CI artifact upload captures a baseline candidate.
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
    public void DocumentRenderingIsDeterministicWithinProcess()
    {
        using DocumentRenderSource first = CreateSource();
        using DocumentRenderSource second = CreateSource();
        Assert.Equal(RenderPagePng(first, 0), RenderPagePng(second, 0));
        Assert.Equal(RenderPagePng(first, 1), RenderPagePng(second, 1));
    }

    [Fact]
    public void SampleDocumentIsNotOverset()
    {
        using DocumentRenderSource source = CreateSource();
        Assert.False(source.IsOverset, "sample document frames must hold the whole story");
        Assert.Equal(2, source.PageCount);
    }

    [Fact]
    public void MissingAssetRendersPlaceholderNotThrow()
    {
        TboardPackage package = SampleDocument.CreatePackage(photoPng: null);
        using var source = DocumentRenderSource.Create(package.Document, package.Assets, SnapshotInfra.Store.Value);
        byte[] png = RenderPagePng(source, 0);
        Assert.NotEmpty(png);
    }
}
