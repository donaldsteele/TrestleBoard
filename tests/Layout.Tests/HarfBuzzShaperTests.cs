using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Shaping;
using Xunit;

namespace TrestleBoard.Layout.Tests;

public sealed class HarfBuzzShaperTests
{
    private static ResolvedFont BodyFont() =>
        TestData.Store.Value.Resolve(new FontKey(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal));

    [Fact]
    public void ShapesSimpleTextWithMonotonicClusters()
    {
        ShapedRun run = HarfBuzzShaper.Shape(
            BodyFont(), 12f, 0xFF000000, "Hi there", 0, 8, new ShapeOptions(true, true));

        Assert.True(run.Glyphs.Count > 0);
        Assert.All(run.Glyphs, g => Assert.True(g.XAdvancePt > 0f));
        for (int i = 1; i < run.Glyphs.Count; i++)
        {
            Assert.True(run.Glyphs[i].Cluster >= run.Glyphs[i - 1].Cluster);
        }
    }

    [Fact]
    public void ClustersAreParagraphRelative()
    {
        ShapedRun run = HarfBuzzShaper.Shape(
            BodyFont(), 12f, 0xFF000000, "abc def", 4, 3, new ShapeOptions(true, true));

        Assert.All(run.Glyphs, g => Assert.InRange(g.Cluster, 4, 6));
    }

    [Fact]
    public void ShapingIsDeterministic()
    {
        var options = new ShapeOptions(true, true);
        ShapedRun first = HarfBuzzShaper.Shape(
            BodyFont(), 12f, 0xFF000000, TestData.FictionalProse, 0, TestData.FictionalProse.Length, options);
        ShapedRun second = HarfBuzzShaper.Shape(
            BodyFont(), 12f, 0xFF000000, TestData.FictionalProse, 0, TestData.FictionalProse.Length, options);

        Assert.Equal(first.Glyphs.Count, second.Glyphs.Count);
        for (int i = 0; i < first.Glyphs.Count; i++)
        {
            Assert.Equal(first.Glyphs[i], second.Glyphs[i]);
        }
    }
}
