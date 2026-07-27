using System.Reflection;
using TrestleBoard.Layout.Fonts;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// The csproj globs the font tree into embedded resources; BundledFontCatalog is a hand-kept
/// table of what it expects to find. These tests are the seam between the two.
/// </summary>
public sealed class FontCatalogTests
{
    private const string Prefix = "TrestleBoard.Layout.Fonts.";

    private static HashSet<string> EmbeddedFontResources() =>
        typeof(BundledFonts).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)
                        && n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
            .Select(n => n[Prefix.Length..])
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void EveryCatalogFaceIsEmbedded()
    {
        HashSet<string> embedded = EmbeddedFontResources();
        string[] missing = BundledFontCatalog.Faces
            .Select(f => f.Resource)
            .Where(r => !embedded.Contains(r))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEmbeddedFaceIsInTheCatalog()
    {
        // Set-equality in both directions on purpose: an orphan embedded font is installer
        // weight the picker will never offer, and should fail as loudly as a missing one.
        HashSet<string> catalogued = BundledFontCatalog.Faces
            .Select(f => f.Resource)
            .ToHashSet(StringComparer.Ordinal);
        string[] orphans = EmbeddedFontResources().Where(r => !catalogued.Contains(r)).ToArray();

        Assert.Empty(orphans);
    }

    [Fact]
    public void EveryFamilyHasARegularUprightFace()
    {
        // The picker previews a family in its own face and the substitution ladder ends here,
        // so a family without Regular/Normal has no floor to fall back to.
        string[] without = BundledFontCatalog.FamilyNames
            .Where(family => !BundledFontCatalog.Faces.Any(f =>
                f.Key.Family == family
                && f.Key.Weight == FontWeight.Regular
                && f.Key.Slant == FontStyleSlant.Normal))
            .ToArray();

        Assert.Empty(without);
    }

    [Fact]
    public void EveryFaceNamesACataloguedFamily()
    {
        foreach (BundledFace face in BundledFontCatalog.Faces)
        {
            Assert.True(BundledFontCatalog.Contains(face.Key.Family), face.Key.Family);
        }
    }

    [Fact]
    public void FamilyNamesAreUniqueAndInSortOrder()
    {
        Assert.Equal(
            BundledFontCatalog.FamilyNames.Count,
            BundledFontCatalog.FamilyNames.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            BundledFontCatalog.Families.OrderBy(f => f.SortOrder).Select(f => f.Family),
            BundledFontCatalog.FamilyNames);
    }

    [Fact]
    public void FaceKeysAreUnique()
    {
        Assert.Equal(
            BundledFontCatalog.Faces.Count,
            BundledFontCatalog.Faces.Select(f => f.Key).Distinct().Count());
    }

    [Fact]
    public void EveryFamilyHasADescriptionAndASample()
    {
        foreach (FontFamilyInfo family in BundledFontCatalog.Families)
        {
            Assert.False(string.IsNullOrWhiteSpace(family.Description), family.Family);
            Assert.False(string.IsNullOrWhiteSpace(family.SampleText), family.Family);
            Assert.False(string.IsNullOrWhiteSpace(BundledFontCatalog.CategoryLabel(family.Category)));
        }
    }

    [Fact]
    public void EveryCategoryHasAtLeastOneFamily()
    {
        foreach (FontCategory category in Enum.GetValues<FontCategory>())
        {
            Assert.NotEmpty(BundledFontCatalog.InCategory(category));
        }
    }

    [Fact]
    public void TheThreeFoundingFamiliesKeepTheirNames()
    {
        // Every existing call site names these three through BundledFonts; renaming one in the
        // catalog would silently orphan the constant.
        Assert.True(BundledFontCatalog.Contains(BundledFonts.BodyFamily));
        Assert.True(BundledFontCatalog.Contains(BundledFonts.SansFamily));
        Assert.True(BundledFontCatalog.Contains(BundledFonts.DisplayFamily));
    }

    [Fact]
    public void NoFaceExceedsTheGlyphIdCeiling()
    {
        // HarfBuzzShaper casts HarfBuzz glyph ids to ushort, so a face with more than 65,535
        // glyphs would wrap silently and draw the wrong outlines.
        FontStore store = TestData.Store.Value;
        foreach (BundledFace face in BundledFontCatalog.Faces)
        {
            ResolvedFont font = store.Resolve(face.Key);
            Assert.True(font.Typeface.GlyphCount <= ushort.MaxValue,
                $"{face.Key} has {font.Typeface.GlyphCount} glyphs.");
        }
    }

    [Fact]
    public void TheLicenceBundleIsEmbeddedBesideTheFonts()
    {
        Assert.Contains(
            Prefix + BundledFonts.LicenceResource,
            typeof(BundledFonts).Assembly.GetManifestResourceNames());
    }
}
