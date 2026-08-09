using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;
using TrestleBoard.Emblems;
using Xunit;

namespace TrestleBoard.Emblems.Tests;

/// <summary>
/// The emblem shelf and its provenance gate (PLAN.md §11 M65, verification gate 22).
/// </summary>
public sealed class EmblemLibraryTests
{
    // ---- the shelf ---------------------------------------------------------------------------------

    /// <summary>
    /// "Deliberately wide rather than minimal" is the owner's direction, so the count is part of the
    /// deliverable: a shelf with a handful of things on it sends people back to the search engine,
    /// which is the whole thing this milestone exists to stop.
    /// </summary>
    [Fact]
    public void TheShelfIsWideRatherThanMinimal() =>
        Assert.True(EmblemLibrary.All.Count >= 18, $"only {EmblemLibrary.All.Count} emblems");

    [Fact]
    public void EveryEmblemIsNamedDescribedAndDrawn()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(emblem.Id), "an emblem has no id");
            Assert.False(string.IsNullOrWhiteSpace(emblem.Name), emblem.Id);
            Assert.NotEmpty(emblem.Parts);
            Assert.True(emblem.Width > 0 && emblem.Height > 0, emblem.Id);

            // The acceptance: "Emblems carry default descriptions for the screen reader". A picture
            // from the shelf is described before anybody thinks to describe it.
            Assert.False(string.IsNullOrWhiteSpace(emblem.Description), emblem.Id);
            Assert.EndsWith(".", emblem.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoTwoEmblemsShareAnId() =>
        Assert.Equal(
            EmblemLibrary.All.Count,
            EmblemLibrary.All.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());

    [Fact]
    public void EveryEmblemBelongsToADeclaredCategory()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            Assert.Contains(emblem.Category, EmblemLibrary.Categories);
        }

        // And no category is declared with nothing in it — an empty heading in the picker is a
        // promise of something that is not there.
        foreach (string category in EmblemLibrary.Categories)
        {
            Assert.Contains(EmblemLibrary.All, e => e.Category == category);
        }
    }

    /// <summary>Every path is one Skia can read. A typo in path data would otherwise throw at insert.</summary>
    [Fact]
    public void EveryPathIsGeometrySkiaUnderstands()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            foreach (EmblemPart part in emblem.Parts)
            {
                using SKPath? path = SKPath.ParseSvgPathData(part.PathData);
                Assert.True(path is not null, $"{emblem.Id}: Skia could not read {part.PathData}");
                Assert.False(path!.Bounds.IsEmpty, $"{emblem.Id}: a part draws nothing");
            }
        }
    }

    /// <summary>
    /// Nothing is drawn outside its own viewbox. An emblem that overflowed would be silently
    /// cropped when rendered, and the crop would only show up on the page.
    /// </summary>
    [Fact]
    public void NothingIsDrawnOutsideItsOwnViewbox()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            foreach (EmblemPart part in emblem.Parts)
            {
                using SKPath path = SKPath.ParseSvgPathData(part.PathData)!;
                float pen = (float)part.StrokeWidth / 2;
                SKRect b = path.Bounds;

                Assert.True(
                    b.Left - pen >= -1 && b.Top - pen >= -1
                    && b.Right + pen <= emblem.Width + 1 && b.Bottom + pen <= emblem.Height + 1,
                    $"{emblem.Id}: {part.PathData} reaches {b} outside {emblem.Width}x{emblem.Height}");
            }
        }
    }

    // ---- finding one -------------------------------------------------------------------------------

    [Theory]
    [InlineData("square", "square-and-compasses-g")]
    [InlineData("compass", "square-and-compasses-g")]
    // The word nobody in a lodge says, that everybody outside one does.
    [InlineData("logo", "square-and-compasses-g")]
    [InlineData("plumb", "plumb")]
    [InlineData("gavel", "gavel")]
    [InlineData("trowel", "trowel")]
    [InlineData("pillars", "pillars")]
    [InlineData("checker", "mosaic-pavement")]
    [InlineData("star", "blazing-star")]
    [InlineData("moon", "moon-and-stars")]
    // "Dividers" is what a draughtsman calls the compasses, so this one belongs to them and not to
    // the decorative rule — a finding rather than a convenience.
    [InlineData("dividers", "compasses")]
    [InlineData("separator", "rule-diamond")]
    [InlineData("corner", "corner-ornament")]
    public void TheWordSomebodyWouldTypeFindsTheThingTheyMeant(string typed, string expected)
    {
        IReadOnlyList<Emblem> found = EmblemLibrary.Search(typed);

        Assert.True(found.Count > 0, $"\"{typed}\" found nothing");
        Assert.Equal(expected, found[0].Id);
    }

    [Fact]
    public void AnEmptyBoxShowsTheWholeShelf()
    {
        Assert.Equal(EmblemLibrary.All, EmblemLibrary.Search(null));
        Assert.Equal(EmblemLibrary.All, EmblemLibrary.Search("   "));
    }

    [Fact]
    public void TypingASecondWordNarrowsRatherThanWidens()
    {
        int one = EmblemLibrary.Search("corner").Count;
        int two = EmblemLibrary.Search("corner ornament").Count;

        Assert.True(two <= one);
        Assert.True(two > 0);
    }

    [Fact]
    public void AWordNothingOnTheShelfUsesFindsNothing() =>
        Assert.Empty(EmblemLibrary.Search("kubernetes"));

    [Fact]
    public void FindingByIdWorksAndAnUnknownIdIsNull()
    {
        Assert.NotNull(EmblemLibrary.Find("gavel"));
        Assert.Null(EmblemLibrary.Find("nothing-like-this"));
    }

    // ---- drawing -----------------------------------------------------------------------------------

    [Fact]
    public void EveryEmblemRendersToAPngWithSomethingInIt()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            byte[] png = EmblemRenderer.ToPng(emblem, 256);

            Assert.True(png.Length > 200, $"{emblem.Id} rendered to {png.Length} bytes");
            Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);

            using SKBitmap bitmap = SKBitmap.Decode(png);
            Assert.NotNull(bitmap);

            // Transparent everywhere would be an emblem that drew nothing at all — the failure a
            // bad path or an off-viewbox drawing produces, and the one a PNG length check misses.
            Assert.True(HasInk(bitmap), $"{emblem.Id} rendered to an empty picture");
        }
    }

    /// <summary>An emblem's rendered shape follows its viewbox, so a rule does not come out square.</summary>
    [Fact]
    public void ARuleComesOutWideAndAnEmblemComesOutSquare()
    {
        (int wide, int short_) = EmblemRenderer.PixelSize(EmblemLibrary.Find("rule-plain")!, 1000);
        Assert.Equal(1000, wide);
        Assert.True(short_ < 200, $"the rule rendered {wide}x{short_}");

        (int w, int h) = EmblemRenderer.PixelSize(EmblemLibrary.Find("gavel")!, 1000);
        Assert.Equal(w, h);
    }

    /// <summary>
    /// **The determinism claim.** The same emblem renders to the same bytes every time — and, since
    /// this test runs on Windows, Linux and macOS in CI against a hash committed here, the same
    /// bytes on every operating system. That is what lets an emblem be an ordinary picture asset in
    /// a <c>.tboard</c>: two committee members on two platforms produce identical documents.
    ///
    /// <para>If this fails after a deliberate change to an emblem, the fix is to re-record the hash
    /// <b>and</b> the manifest entry, in the same commit, having looked at the picture.</para>
    /// </summary>
    [Fact]
    public void TheSameEmblemRendersToTheSameBytesEveryTimeAndEverywhere()
    {
        byte[] once = EmblemRenderer.ToPng(EmblemLibrary.Find("square-and-compasses")!, 256);
        byte[] twice = EmblemRenderer.ToPng(EmblemLibrary.Find("square-and-compasses")!, 256);

        Assert.Equal(once, twice);
        Assert.Equal(
            "2cb65e68af0e47216346ffd09393f222b47704197cad55d47ee5454a2507382f",
            Convert.ToHexStringLower(SHA256.HashData(once)));
    }

    // ---- the provenance gate (PLAN.md gate 22) ------------------------------------------------------

    /// <summary>
    /// The fonts' promise, kept for artwork that lives in code: no emblem ships without a manifest
    /// entry, and no entry describes a drawing that has since changed.
    /// </summary>
    [Fact]
    public void EveryEmblemHasAManifestEntryWhoseHashStillMatches()
    {
        Dictionary<string, string> manifest = ManifestHashes();

        foreach (Emblem emblem in EmblemLibrary.All)
        {
            Assert.True(
                manifest.ContainsKey(emblem.Id),
                $"'{emblem.Id}' is not in assets-src/emblems/emblems.json — gate 22 refuses an "
                + "unmanifested asset, exactly as it refuses an unmanifested TTF");

            Assert.True(
                manifest[emblem.Id] == EmblemFingerprint.Of(emblem),
                $"'{emblem.Id}' has been redrawn since its manifest entry was written. Look at the "
                + "picture, then record the new hash beside its provenance.");
        }
    }

    [Fact]
    public void EveryManifestEntryNamesAnEmblemThatStillExists()
    {
        foreach (string id in ManifestHashes().Keys)
        {
            Assert.True(EmblemLibrary.Find(id) is not null, $"the manifest still lists '{id}'");
        }
    }

    /// <summary>
    /// The provenance file is the point of the gate, not the hashes: the hashes say the artwork has
    /// not changed, and this says somebody wrote down where it came from and under what licence.
    /// </summary>
    [Fact]
    public void TheProvenanceFileIsThereAndSaysWhereTheArtworkCameFrom()
    {
        string text = File.ReadAllText(Path.Combine(AssetsDirectory, "EMBLEMS-PROVENANCE.txt"));

        Assert.True(text.Length > 500, "the provenance file is a stub");
        Assert.Contains("CC0", text, StringComparison.Ordinal);
        Assert.Contains("drawn for TrestleBoard", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>It travels with the build, not only with the repository — the M14 precedent.</summary>
    [Fact]
    public void TheProvenanceIsEmbeddedInTheAssemblyToo()
    {
        using Stream? stream = typeof(EmblemLibrary).Assembly
            .GetManifestResourceStream("TrestleBoard.Emblems.EMBLEMS-PROVENANCE.txt");

        Assert.NotNull(stream);
        Assert.True(stream!.Length > 500);
    }

    // ---- plumbing -----------------------------------------------------------------------------------

    private static string AssetsDirectory
    {
        get
        {
            var at = new DirectoryInfo(AppContext.BaseDirectory);
            while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "assets-src")))
            {
                at = at.Parent;
            }

            Assert.True(at is not null, "could not find assets-src above the test binary");
            return Path.Combine(at!.FullName, "assets-src", "emblems");
        }
    }

    private static Dictionary<string, string> ManifestHashes()
    {
        using FileStream file = File.OpenRead(Path.Combine(AssetsDirectory, "emblems.json"));
        using JsonDocument json = JsonDocument.Parse(file);

        return json.RootElement.GetProperty("emblems").EnumerateArray().ToDictionary(
            e => e.GetProperty("id").GetString()!,
            e => e.GetProperty("sha256").GetString()!,
            StringComparer.Ordinal);
    }

    private static bool HasInk(SKBitmap bitmap)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
