using TrestleBoard.Layout.Fonts;
using Xunit;

namespace TrestleBoard.Layout.Tests;

public sealed class FontStoreTests
{
    /// <summary>
    /// Driven off the catalog rather than a hand-written list: adding a family should cost
    /// nobody a test edit, and this becomes a real parse gate for every bundled face across
    /// both HarfBuzz and Skia.
    /// </summary>
    public static TheoryData<string, FontWeight, FontStyleSlant> BundledFaces
    {
        get
        {
            var data = new TheoryData<string, FontWeight, FontStyleSlant>();
            foreach (BundledFace face in BundledFontCatalog.Faces)
            {
                data.Add(face.Key.Family, face.Key.Weight, face.Key.Slant);
            }

            return data;
        }
    }

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

    [Fact]
    public void SubstitutionIsOffByDefault()
    {
        // M7's intended loud failure. Fixtures, snapshots, widget goldens and PDF export all
        // depend on a missing face being an exception rather than a quiet near-miss.
        using var store = new FontStore();
        store.Register(new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Normal), Face());

        Assert.Null(store.SubstituteFamily);
        Assert.Null(store.ResolveKey(new FontKey("Other", FontWeight.Regular, FontStyleSlant.Normal)));
        Assert.Throws<KeyNotFoundException>(() =>
            store.Resolve(new FontKey("Other", FontWeight.Regular, FontStyleSlant.Normal)));
    }

    [Fact]
    public void AMissingFaceDegradesWithinItsOwnFamilyFirst()
    {
        using var store = new FontStore();
        store.Register(new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Normal), Face());
        store.Register(new FontKey("Fictional", FontWeight.Bold, FontStyleSlant.Normal), Face(FontWeight.Bold));
        store.SubstituteFamily = BundledFonts.BodyFamily;

        // Bold italic is not bundled: drop the slant before the weight, and never leave the family.
        Assert.Equal(
            new FontKey("Fictional", FontWeight.Bold, FontStyleSlant.Normal),
            store.ResolveKey(new FontKey("Fictional", FontWeight.Bold, FontStyleSlant.Italic)));
        Assert.Equal(
            new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Normal),
            store.ResolveKey(new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Italic)));
    }

    [Fact]
    public void AnUnknownFamilyFallsBackOnlyWhenASubstituteIsConfigured()
    {
        using FontStore store = BundledFonts.CreateDefaultStore();
        var wanted = new FontKey("Woodcut Antiqua", FontWeight.Bold, FontStyleSlant.Italic);

        Assert.Null(store.ResolveKey(wanted));

        store.SubstituteFamily = BundledFonts.BodyFamily;
        Assert.Equal(
            new FontKey(BundledFonts.BodyFamily, FontWeight.Bold, FontStyleSlant.Italic),
            store.ResolveKey(wanted));
        Assert.True(store.TryResolve(wanted, out ResolvedFont? font));
        Assert.Equal(BundledFonts.BodyFamily, font!.Key.Family);
    }

    [Fact]
    public void SubstitutionNeverInventsAFaceTheSubstituteLacks()
    {
        using var store = new FontStore();
        store.Register(new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Normal), Face());
        store.SubstituteFamily = "Also Fictional";

        Assert.Null(store.ResolveKey(new FontKey("Absent", FontWeight.Regular, FontStyleSlant.Normal)));
    }

    [Fact]
    public void LazyRegistrationDefersTheBytesUntilFirstResolve()
    {
        int reads = 0;
        var key = new FontKey("Fictional", FontWeight.Regular, FontStyleSlant.Normal);
        using var store = new FontStore();
        store.Register(key, () =>
        {
            reads++;
            return Face();
        });

        Assert.Equal(0, reads);
        Assert.Contains(key, store.RegisteredKeys);

        store.Resolve(key);
        store.Resolve(key);
        Assert.Equal(1, reads);
    }

    private static byte[] Face(FontWeight weight = FontWeight.Regular)
    {
        FontKey key = new(BundledFonts.BodyFamily, weight, FontStyleSlant.Normal);
        BundledFace face = BundledFontCatalog.Faces.First(f => f.Key == key);
        using Stream stream = typeof(BundledFonts).Assembly
            .GetManifestResourceStream("TrestleBoard.Layout.Fonts." + face.Resource)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
