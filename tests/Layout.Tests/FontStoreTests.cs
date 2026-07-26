using TrestleBoard.Layout.Fonts;
using Xunit;

namespace TrestleBoard.Layout.Tests;

public sealed class FontStoreTests
{
    public static TheoryData<string, FontWeight, FontStyleSlant> BundledFaces => new()
    {
        { BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal },
        { BundledFonts.BodyFamily, FontWeight.Bold, FontStyleSlant.Normal },
        { BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Italic },
        { BundledFonts.BodyFamily, FontWeight.Bold, FontStyleSlant.Italic },
        { BundledFonts.SansFamily, FontWeight.Regular, FontStyleSlant.Normal },
        { BundledFonts.SansFamily, FontWeight.Bold, FontStyleSlant.Normal },
        { BundledFonts.DisplayFamily, FontWeight.Regular, FontStyleSlant.Normal },
    };

    [Theory]
    [MemberData(nameof(BundledFaces))]
    public void EveryBundledFaceResolves(string family, FontWeight weight, FontStyleSlant slant)
    {
        ResolvedFont font = TestData.Store.Value.Resolve(new FontKey(family, weight, slant));

        Assert.NotNull(font.Typeface);
        Assert.NotNull(font.HbFont);
        Assert.True(font.UnitsPerEm > 0);

        FontMetrics metrics = font.GetMetrics(12f);
        Assert.True(metrics.AscentPt > 0f);
        Assert.True(metrics.DescentPt > 0f);
        Assert.True(metrics.AverageCharWidthPt > 0f);
    }

    [Fact]
    public void UnknownKeyThrows()
    {
        var key = new FontKey("No Such Family", FontWeight.Regular, FontStyleSlant.Normal);
        Assert.Throws<KeyNotFoundException>(() => TestData.Store.Value.Resolve(key));
        Assert.False(TestData.Store.Value.TryResolve(key, out _));
    }
}
