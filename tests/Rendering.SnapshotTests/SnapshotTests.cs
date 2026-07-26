using TrestleBoard.Layout;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// PNG snapshot tests vs committed baselines. The gate is STRICT (0 differing pixels):
/// bundled fonts + integer HarfBuzz shaping + CPU raster surface make output byte-identical
/// across the 3-OS CI matrix (docs/M1-spec.md §5). On failure the actual image is written as
/// *.actual.png for CI artifact upload.
/// </summary>
public sealed class SnapshotTests
{
    public static TheoryData<string> FixtureNames => new() { "acceptance-two-exclusions", "alignment-sampler" };

    private static LayoutResult BuildFixture(string name) => name switch
    {
        "acceptance-two-exclusions" => SnapshotInfra.AcceptanceFixture(),
        "alignment-sampler" => SnapshotInfra.AlignmentSampler(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void SnapshotMatchesBaseline(string name)
    {
        byte[] actual = SnapshotInfra.RenderFixturePng(BuildFixture(name));
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, name + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        Assert.True(File.Exists(baselinePath),
            $"Baseline missing: {baselinePath}. Run with TRESTLEBOARD_UPDATE_BASELINES=1 to create it.");

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
    public void RenderingIsDeterministicWithinProcess()
    {
        byte[] first = SnapshotInfra.RenderFixturePng(SnapshotInfra.AcceptanceFixture());
        byte[] second = SnapshotInfra.RenderFixturePng(SnapshotInfra.AcceptanceFixture());
        Assert.Equal(first, second);
    }

    [Fact]
    public void AcceptanceFixtureRendersInk()
    {
        byte[] png = SnapshotInfra.RenderFixturePng(SnapshotInfra.AcceptanceFixture());
        SnapshotInfra.ComparisonResult vsBlank = SnapshotInfra.ComparePixels(
            png,
            SnapshotInfra.RenderFixturePng(new TextLayoutEngine(SnapshotInfra.Store.Value).Layout(
                new TrestleBoard.Layout.Input.LayoutRequest(
                    new TrestleBoard.Layout.Input.LayoutStory("empty", []),
                    [new TrestleBoard.Layout.Input.LayoutFrame(new TrestleBoard.Layout.Input.FrameRect(0, 0, 1, 1), [])]))));
        Assert.True(vsBlank.DiffPixelCount > 1000, "expected substantial glyph coverage in the acceptance fixture");
    }
}
