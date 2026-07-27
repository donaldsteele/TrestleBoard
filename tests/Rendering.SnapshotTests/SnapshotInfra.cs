using SkiaSharp;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// Fixture documents and baseline plumbing. All fixture text is fictional (PLAN.md §0).
/// Baselines live in the repo at tests/Rendering.SnapshotTests/Baselines/ and are located by
/// walking up from the test output directory to the solution root. Set the environment
/// variable TRESTLEBOARD_UPDATE_BASELINES=1 to regenerate baselines instead of comparing.
/// </summary>
internal static class SnapshotInfra
{
    public static readonly Lazy<FontStore> Store = new(BundledFonts.CreateDefaultStore);

    public const int PageWidthPt = 480;
    public const int PageHeightPt = 680;

    public const string FictionalProse =
        "The Placeholder Lodge convenes on the appointed evening of every month. "
        + "Brothers gather early for fellowship, share a simple meal together, and then "
        + "proceed to the reading of the minutes. Visitors are always welcome to attend "
        + "the open portions of the program and to enjoy the refreshments afterward. "
        + "A. Placeholder, Worshipful Master, may be reached at 555-0100 for questions.";

    public static CharacterStyle Body(float sizePt = 12f) =>
        new(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal, sizePt, 0xFF000000);

    /// <summary>The M1 acceptance fixture: one text column wrapping around two exclusion rects.</summary>
    public static LayoutResult AcceptanceFixture()
    {
        var style = new ParagraphStyle(1.2f, 0f, 6f, 0f, TextAlign.Left, Body());
        var story = new LayoutStory("acceptance",
        [
            new LayoutParagraph(style, [new LayoutRun(FictionalProse, style.DefaultRun)]),
            new LayoutParagraph(style, [new LayoutRun(FictionalProse, style.DefaultRun)]),
        ]);
        var frame = new LayoutFrame(
            new FrameRect(40, 40, 440, 640),
            [
                new ExclusionRect(new FrameRect(200, 120, 340, 230), WrapMargin: 8f, ZOrder: 1),
                new ExclusionRect(new FrameRect(40, 330, 160, 440), WrapMargin: 8f, ZOrder: 2),
            ]);
        return new TextLayoutEngine(Store.Value).Layout(new LayoutRequest(story, [frame]));
    }

    /// <summary>Left/center/right alignment and mixed weight/slant/family sampler.</summary>
    public static LayoutResult AlignmentSampler()
    {
        CharacterStyle body = Body(14f);
        var bold = body with { Weight = FontWeight.Bold };
        var italic = body with { Slant = FontStyleSlant.Italic };
        var sans = new CharacterStyle(BundledFonts.SansFamily, FontWeight.Regular, FontStyleSlant.Normal, 14f, 0xFF000000);
        var display = new CharacterStyle(BundledFonts.DisplayFamily, FontWeight.Regular, FontStyleSlant.Normal, 18f, 0xFF000000);

        static ParagraphStyle P(TextAlign align, CharacterStyle run) => new(1.3f, 4f, 4f, 0f, align, run);

        var story = new LayoutStory("sampler",
        [
            new LayoutParagraph(P(TextAlign.Left, body), [new LayoutRun("Left aligned placeholder text.", body)]),
            new LayoutParagraph(P(TextAlign.Center, body), [new LayoutRun("Centered placeholder text.", body)]),
            new LayoutParagraph(P(TextAlign.Right, body), [new LayoutRun("Right aligned placeholder text.", body)]),
            new LayoutParagraph(P(TextAlign.Left, body),
            [
                new LayoutRun("Mixed run: ", body),
                new LayoutRun("bold, ", bold),
                new LayoutRun("italic, ", italic),
                new LayoutRun("sans, ", sans),
                new LayoutRun("Display.", display),
            ]),
        ]);
        var frame = new LayoutFrame(new FrameRect(40, 40, 440, 640), []);
        return new TextLayoutEngine(Store.Value).Layout(new LayoutRequest(story, [frame]));
    }

    /// <summary>
    /// One line per bundled family, set in that family at 12pt (PLAN.md M14).
    /// <para>
    /// A NEW fixture, deliberately — <see cref="AlignmentSampler"/> is left alone so M14 re-bakes
    /// nothing. This is the only artifact that shows all 20 families rasterising side by side,
    /// and it is what catches a bad instancer run by eye.
    /// </para>
    /// </summary>
    public static LayoutResult FontCatalogSampler()
    {
        var paragraphs = new List<LayoutParagraph>();
        foreach (FontFamilyInfo family in BundledFontCatalog.Families)
        {
            var run = new CharacterStyle(family.Family, FontWeight.Regular, FontStyleSlant.Normal, 12f, 0xFF000000);
            var style = new ParagraphStyle(1.25f, 0f, 3f, 0f, TextAlign.Left, run);
            paragraphs.Add(new LayoutParagraph(style, [new LayoutRun(FamilySampleLine(family), run)]));
        }

        var story = new LayoutStory("font-catalog", paragraphs);
        var frame = new LayoutFrame(new FrameRect(20, 20, 460, 660), []);
        return new TextLayoutEngine(Store.Value).Layout(new LayoutRequest(story, [frame]));
    }

    /// <summary>The one line the sampler sets for a family. Fictional text only (PLAN.md §0).</summary>
    public static string FamilySampleLine(FontFamilyInfo family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return $"{family.Family}: {family.SampleText}";
    }

    public static byte[] RenderFixturePng(LayoutResult result) =>
        PngRenderer.RenderToPng(result, PageWidthPt, PageHeightPt, scale: 1f);

    /// <summary>
    /// Baselines are PER-OS (windows/linux/macos): Skia rasterizes glyphs through the platform
    /// scaler backend (DirectWrite / FreeType / CoreText), so antialiased glyph coverage differs
    /// slightly per OS even with identical font bytes and layout. Cross-OS LAYOUT determinism is
    /// proven by the golden LineBox tests; within each OS the pixel gate stays strict (0 diff).
    /// </summary>
    public static string BaselineDir
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
            {
                dir = dir.Parent;
            }

            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
            }

            string os = OperatingSystem.IsWindows() ? "windows"
                : OperatingSystem.IsMacOS() ? "macos"
                : OperatingSystem.IsLinux() ? "linux"
                : throw new PlatformNotSupportedException("No snapshot baselines for this OS.");
            return Path.Combine(dir.FullName, "tests", "Rendering.SnapshotTests", "Baselines", os);
        }
    }

    public static bool UpdateBaselines =>
        Environment.GetEnvironmentVariable("TRESTLEBOARD_UPDATE_BASELINES") == "1";

    public sealed record ComparisonResult(int DiffPixelCount, int MaxChannelDiff, int TotalPixels);

    /// <summary>Compares decoded pixels (never PNG bytes — encoders may differ in chunk layout).</summary>
    public static ComparisonResult ComparePixels(byte[] actualPng, byte[] expectedPng)
    {
        using SKBitmap actual = SKBitmap.Decode(actualPng) ?? throw new InvalidOperationException("actual PNG failed to decode");
        using SKBitmap expected = SKBitmap.Decode(expectedPng) ?? throw new InvalidOperationException("expected PNG failed to decode");
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            throw new InvalidOperationException(
                $"Size mismatch: actual {actual.Width}x{actual.Height} vs expected {expected.Width}x{expected.Height}");
        }

        int diffPixels = 0;
        int maxChannel = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                SKColor a = actual.GetPixel(x, y);
                SKColor e = expected.GetPixel(x, y);
                int d = Math.Max(
                    Math.Max(Math.Abs(a.Red - e.Red), Math.Abs(a.Green - e.Green)),
                    Math.Max(Math.Abs(a.Blue - e.Blue), Math.Abs(a.Alpha - e.Alpha)));
                if (d > 0)
                {
                    diffPixels++;
                    maxChannel = Math.Max(maxChannel, d);
                }
            }
        }

        return new ComparisonResult(diffPixels, maxChannel, actual.Width * actual.Height);
    }

    public sealed record BlockComparisonResult(int BlocksOverThreshold, int TotalBlocks, double MaxBlockDiff);

    /// <summary>
    /// Compares after box-downsampling both images by <paramref name="factor"/>. Averaging
    /// washes out antialiasing differences between rasterizers (Skia vs poppler) while a real
    /// layout shift — a glyph or line in the wrong place — survives and trips many blocks.
    /// </summary>
    public static BlockComparisonResult CompareDownsampled(byte[] actualPng, byte[] expectedPng, int factor, int channelThreshold)
    {
        using SKBitmap actual = SKBitmap.Decode(actualPng) ?? throw new InvalidOperationException("actual PNG failed to decode");
        using SKBitmap expected = SKBitmap.Decode(expectedPng) ?? throw new InvalidOperationException("expected PNG failed to decode");
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            throw new InvalidOperationException(
                $"Size mismatch: actual {actual.Width}x{actual.Height} vs expected {expected.Width}x{expected.Height}");
        }

        int blocksX = actual.Width / factor;
        int blocksY = actual.Height / factor;
        int over = 0;
        double maxDiff = 0;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                double sumAr = 0, sumAg = 0, sumAb = 0, sumEr = 0, sumEg = 0, sumEb = 0;
                for (int y = 0; y < factor; y++)
                {
                    for (int x = 0; x < factor; x++)
                    {
                        SKColor a = actual.GetPixel(bx * factor + x, by * factor + y);
                        SKColor e = expected.GetPixel(bx * factor + x, by * factor + y);
                        sumAr += a.Red;
                        sumAg += a.Green;
                        sumAb += a.Blue;
                        sumEr += e.Red;
                        sumEg += e.Green;
                        sumEb += e.Blue;
                    }
                }

                double n = factor * factor;
                double d = Math.Max(
                    Math.Abs(sumAr - sumEr) / n,
                    Math.Max(Math.Abs(sumAg - sumEg) / n, Math.Abs(sumAb - sumEb) / n));
                maxDiff = Math.Max(maxDiff, d);
                if (d > channelThreshold)
                {
                    over++;
                }
            }
        }

        return new BlockComparisonResult(over, blocksX * blocksY, maxDiff);
    }

    /// <summary>Writes the failing actual image where CI's snapshot-diff artifact glob finds it.</summary>
    public static string WriteActualArtifact(string name, byte[] actualPng)
    {
        string path = Path.Combine(AppContext.BaseDirectory, name + ".actual.png");
        File.WriteAllBytes(path, actualPng);
        return path;
    }
}
