using SkiaSharp;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// Proof that every bundled face actually rasterises on THIS operating system (PLAN.md M14).
/// <para>
/// The per-face assertions below carry the guarantee, because they need no baseline and so run
/// everywhere the CI matrix runs. The whole-page sampler is compared against a baseline where
/// one exists for this OS, and skips with a reason where one does not — a maintainer on that OS
/// creates it with TRESTLEBOARD_UPDATE_BASELINES=1.
/// </para>
/// </summary>
public sealed class FontCatalogSnapshotTests
{
    private const string FixtureName = "font-catalog-sampler";

    public static TheoryData<string, FontWeight, FontStyleSlant> AllFaces
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
    [MemberData(nameof(AllFaces))]
    public void EveryFaceCoversItsSampleLine(string family, FontWeight weight, FontStyleSlant slant)
    {
        // .notdef for a plain ASCII sample means the subset dropped something it should not have
        // — the single most likely way a bad instancer or pyftsubset run reaches users.
        FontFamilyInfo info = BundledFontCatalog.Find(family)!;
        string text = SnapshotInfra.FamilySampleLine(info);
        var style = new CharacterStyle(family, weight, slant, 12f, 0xFF000000);
        var paragraph = new ParagraphStyle(1.25f, 0f, 0f, 0f, TextAlign.Left, style);
        LayoutResult result = new TextLayoutEngine(SnapshotInfra.Store.Value).Layout(
            new LayoutRequest(
                new LayoutStory("one", [new LayoutParagraph(paragraph, [new LayoutRun(text, style)])]),
                [new LayoutFrame(new FrameRect(0, 0, 2000, 100), [])]));

        ushort[] glyphs = result.Frames
            .SelectMany(f => f.Lines)
            .SelectMany(l => l.Segments)
            .SelectMany(s => s.Runs)
            .SelectMany(r => r.Glyphs)
            .ToArray();

        Assert.NotEmpty(glyphs);
        Assert.DoesNotContain((ushort)0, glyphs);
    }

    [Theory]
    [MemberData(nameof(AllFaces))]
    public void EveryFacePutsInkOnThePage(string family, FontWeight weight, FontStyleSlant slant)
    {
        FontFamilyInfo info = BundledFontCatalog.Find(family)!;
        var style = new CharacterStyle(family, weight, slant, 24f, 0xFF000000);
        var paragraph = new ParagraphStyle(1.25f, 0f, 0f, 0f, TextAlign.Left, style);
        LayoutResult result = new TextLayoutEngine(SnapshotInfra.Store.Value).Layout(
            new LayoutRequest(
                new LayoutStory("one", [new LayoutParagraph(paragraph, [new LayoutRun(info.SampleText, style)])]),
                [new LayoutFrame(new FrameRect(4, 4, 900, 60), [])]));

        byte[] png = PngRenderer.RenderToPng(result, 920, 70, scale: 1f);
        Assert.True(DarkPixelCount(png) > 200, $"{family} {weight} {slant} rendered almost nothing.");
    }

    [Fact]
    public void TheSamplerRendersEveryFamilyOnItsOwnLine()
    {
        LayoutResult result = SnapshotInfra.FontCatalogSampler();
        string[] families = result.Frames
            .SelectMany(f => f.Lines)
            .SelectMany(l => l.Segments)
            .SelectMany(s => s.Runs)
            .Select(r => r.Font.Key.Family)
            .Distinct()
            .ToArray();

        Assert.False(result.IsOverset, "the font catalog sampler no longer fits on one page.");
        Assert.Equal(BundledFontCatalog.FamilyNames.Count, families.Length);
    }

    [Fact]
    public void FontCatalogSamplerMatchesBaseline()
    {
        byte[] actual = SnapshotInfra.RenderFixturePng(SnapshotInfra.FontCatalogSampler());
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, FixtureName + ".png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        Assert.SkipUnless(
            File.Exists(baselinePath),
            $"No {FixtureName} baseline for this OS yet — create it with TRESTLEBOARD_UPDATE_BASELINES=1. "
            + "The per-face assertions in this class carry the guarantee meanwhile.");

        SnapshotInfra.ComparisonResult diff =
            SnapshotInfra.ComparePixels(actual, File.ReadAllBytes(baselinePath));
        if (diff.DiffPixelCount > 0)
        {
            string artifact = SnapshotInfra.WriteActualArtifact(FixtureName, actual);
            Assert.Fail(
                $"Snapshot '{FixtureName}' differs from baseline: {diff.DiffPixelCount}/{diff.TotalPixels} "
                + $"pixels, max channel diff {diff.MaxChannelDiff}. Actual written to {artifact}");
        }
    }

    private static int DarkPixelCount(byte[] png)
    {
        using SKBitmap bitmap = SKBitmap.Decode(png);
        int dark = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 128 && pixel.Green < 128 && pixel.Blue < 128)
                {
                    dark++;
                }
            }
        }

        return dark;
    }
}
