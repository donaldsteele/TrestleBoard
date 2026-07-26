using SkiaSharp;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Input;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M4 overlay snapshot (docs/M4-spec.md §8.5): the wrap fixture with a known multi-line
/// selection and a caret, drawn via TextOverlayRenderer onto the PNG path. Per-OS baselines,
/// strict 0-pixel gate. The PDF/PNG export paths never call the overlay renderer — verified
/// by construction (PdfExporter/DocumentPdfExporter have no overlay parameter) and by the
/// text-equality parity gate.
/// </summary>
public sealed class OverlaySnapshotTests
{
    private static (LayoutRequest Request, LayoutResult Layout) BuildFixture()
    {
        var style = new ParagraphStyle(1.2f, 0f, 6f, 0f, TextAlign.Left, SnapshotInfra.Body());
        var story = new LayoutStory("overlay",
        [
            new LayoutParagraph(style, [new LayoutRun(SnapshotInfra.FictionalProse, style.DefaultRun)]),
        ]);
        var frame = new LayoutFrame(
            new FrameRect(40, 40, 440, 640),
            [new ExclusionRect(new FrameRect(200, 120, 340, 230), WrapMargin: 8f, ZOrder: 1)]);
        var request = new LayoutRequest(story, [frame]);
        return (request, new TextLayoutEngine(SnapshotInfra.Store.Value).Layout(request));
    }

    private static byte[] RenderWithOverlay()
    {
        (LayoutRequest request, LayoutResult layout) = BuildFixture();
        var geometry = new StoryTextGeometry(request, layout);

        var info = new SKImageInfo(
            SnapshotInfra.PageWidthPt, SnapshotInfra.PageHeightPt,
            SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        SKCanvas canvas = surface.Canvas;
        canvas.DrawColor(SKColors.White);
        new PageRenderer().Render(canvas, layout);

        // A selection spanning the exclusion-split lines plus a caret at its end.
        var range = new Core.Text.TextRange(
            new Core.Text.TextPosition("overlay", 0, 20),
            new Core.Text.TextPosition("overlay", 0, 160));
        TextOverlayRenderer.DrawSelection(canvas, geometry.GetSelectionRects(range));
        if (geometry.TryGetCaretGeometry(
                Core.Text.CaretPosition.Trailing(new Core.Text.TextPosition("overlay", 0, 160)),
                out CaretGeometry caret))
        {
            TextOverlayRenderer.DrawCaret(canvas, caret);
        }

        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("png encode");
        return data.ToArray();
    }

    [Fact]
    public void OverlayMatchesBaseline()
    {
        byte[] actual = RenderWithOverlay();
        const string name = "overlay-selection-caret";
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
    public void OverlayDrawsSelectionAndCaretInk()
    {
        (LayoutRequest request, LayoutResult layout) = BuildFixture();
        _ = request;
        byte[] plain = SnapshotInfra.RenderFixturePng(layout);
        byte[] withOverlay = RenderWithOverlay();
        SnapshotInfra.ComparisonResult diff = SnapshotInfra.ComparePixels(withOverlay, plain);
        Assert.True(diff.DiffPixelCount > 500, "overlay should visibly change the render");
    }
}
