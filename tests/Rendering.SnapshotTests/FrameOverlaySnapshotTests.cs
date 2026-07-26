using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Input;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M5 frame chrome snapshot (docs/M5-spec.md §10): selection outline with all eight oversized
/// handles, a snap guide, and the overset and link badges over the wrap fixture. Per-OS
/// baselines, strict 0-pixel gate — same rules as every other raster snapshot.
/// The export path never draws this: <c>DocumentPdfExporter</c> calls <c>RenderPage</c> only.
/// </summary>
public sealed class FrameOverlaySnapshotTests
{
    // Both rects sit well inside the page so the badges that hang off their corners are visible
    // in the baseline rather than clipped at the sheet edge.
    private static readonly RectPt SelectedFrame = new(40f, 40f, 400f, 560f);

    private static readonly RectPt PhotoFrame = new(180f, 120f, 220f, 180f);

    private static FrameOverlay BuildOverlay() => new(
        SelectedFrame,
        ShowHandles: true,
        SnapGuides: [new SnapGuide(SnapAxis.X, SelectedFrame.X, 0f, SnapshotInfra.PageHeightPt)],
        OversetRects: [SelectedFrame],
        LinkedRects: [PhotoFrame],
        LinkTargetRects: []);

    private static LayoutResult BuildFixture()
    {
        var style = new ParagraphStyle(1.2f, 0f, 6f, 0f, TextAlign.Left, SnapshotInfra.Body());
        var story = new LayoutStory("frame-overlay",
        [
            new LayoutParagraph(style, [new LayoutRun(SnapshotInfra.FictionalProse, style.DefaultRun)]),
        ]);
        var frame = new LayoutFrame(
            new FrameRect(SelectedFrame.X, SelectedFrame.Y, SelectedFrame.Right, SelectedFrame.Bottom),
            [
                new ExclusionRect(
                    new FrameRect(PhotoFrame.X, PhotoFrame.Y, PhotoFrame.Right, PhotoFrame.Bottom),
                    WrapMargin: 8f,
                    ZOrder: 1),
            ]);
        return new TextLayoutEngine(SnapshotInfra.Store.Value).Layout(new LayoutRequest(story, [frame]));
    }

    private static byte[] RenderWithFrameOverlay()
    {
        LayoutResult layout = BuildFixture();
        var info = new SKImageInfo(
            SnapshotInfra.PageWidthPt, SnapshotInfra.PageHeightPt,
            SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;
        canvas.DrawColor(SKColors.White);
        new PageRenderer().Render(canvas, layout);

        FrameOverlayRenderer.Draw(canvas, BuildOverlay(), overlayScale: 1f);

        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    [Fact]
    public void FrameOverlayMatchesBaseline()
    {
        byte[] actual = RenderWithFrameOverlay();
        const string name = "frame-selection-handles";
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
    public void EmptyOverlayDrawsNothing()
    {
        LayoutResult layout = BuildFixture();
        byte[] plain = SnapshotInfra.RenderFixturePng(layout);

        var info = new SKImageInfo(
            SnapshotInfra.PageWidthPt, SnapshotInfra.PageHeightPt,
            SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;
        canvas.DrawColor(SKColors.White);
        new PageRenderer().Render(canvas, layout);
        FrameOverlayRenderer.Draw(canvas, FrameOverlay.Empty, overlayScale: 1f);
        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");

        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(data.ToArray(), plain);
        Assert.Equal(0, diff.DiffPixelCount);
    }
}
