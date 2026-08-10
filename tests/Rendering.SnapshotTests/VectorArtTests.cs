using System.IO.Compression;
using System.Text;
using SkiaSharp;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using TrestleBoard.Emblems;
using TrestleBoard.Export.Pdf;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// A drawing on the page and in the PDF (PLAN.md §11 M72).
///
/// <para>This project references <c>TrestleBoard.Emblems</c> so the fixture is the real artwork
/// rather than an invented approximation of it — the same precedent as the <c>Widgets</c> reference
/// above it in the csproj: <c>Rendering</c> itself must never reference either, and injecting the
/// real thing at the test seam is what proves the seam works. The emblem is turned into document
/// geometry here by three lines that mirror <c>App/Emblems/EmblemGeometry</c>; the app-side test
/// <c>EmblemTests.AnEmblemLandsOnThePageAsPathDataAndNotAsAPicture</c> pins that mapping against the
/// library, so the two cannot quietly come to mean different things.</para>
/// </summary>
public sealed class VectorArtTests
{
    /// <summary>The emblems the fixture puts on its page, chosen to exercise curves, strokes and fills.</summary>
    private static readonly string[] FixtureEmblems = ["square-and-compasses-g", "all-seeing-eye", "rule-diamond"];

    // ---- the drawing primitive --------------------------------------------------------------------

    /// <summary>
    /// Every emblem on the shelf draws something. This is the check M65 made by rasterising each
    /// emblem to a PNG and looking for a non-transparent pixel; it belongs here now, against the one
    /// routine that actually paints them.
    /// </summary>
    [Fact]
    public void EveryEmblemOnTheShelfPutsInkOnTheCanvas()
    {
        foreach (Emblem emblem in EmblemLibrary.All)
        {
            using SKBitmap drawn = DrawToBitmap(Specs(emblem), emblem.Width, emblem.Height, 256, 256);
            Assert.True(HasInk(drawn), $"{emblem.Id} drew nothing at all");
        }
    }

    /// <summary>
    /// A path this build cannot read draws nothing and does not throw — and the parts either side of
    /// it still draw.
    ///
    /// <para>This is not defensive programming for its own sake. Path data now arrives from a saved
    /// document, so it can have been written by a later TrestleBoard, hand-edited, or damaged in
    /// transit. A newsletter that refuses to open because one ornament has a typo in it would cost
    /// somebody a month's work to save an ornament.</para>
    /// </summary>
    [Fact]
    public void APathThisBuildCannotReadDrawsNothingRatherThanThrowing()
    {
        VectorPathSpec[] parts =
        [
            new("M10,10 L90,10 L90,90 L10,90 Z", 0),
            new("this is not path data at all", 0),
        ];

        using SKBitmap drawn = DrawToBitmap(parts, 100, 100, 100, 100);

        Assert.True(HasInk(drawn), "the readable part should still have been drawn");
    }

    /// <summary>
    /// A drawing in a frame far wider than itself is scaled to fit and centred: it keeps its own
    /// shape, and the extra width is left empty. This is the claim M65 made with
    /// <c>ImageFit.Contain</c> and M72 has to make about geometry instead — an emblem is a whole
    /// thing, and a square and compasses squashed sideways is a mutilated symbol.
    /// </summary>
    [Fact]
    public void ADrawingKeepsItsShapeInAFrameThatIsTheWrongShape()
    {
        Emblem emblem = EmblemLibrary.Find("square-and-compasses")!;
        using SKBitmap drawn = DrawToBitmap(Specs(emblem), emblem.Width, emblem.Height, 400, 200);

        (int left, int right) = InkColumns(drawn);
        int painted = right - left;

        // Scaled to fit the SHORT side: the artwork's square viewbox maps onto the 200pt height, so
        // nothing painted can be wider than 200 in a 400-wide frame. Stretching to fill would have
        // run it to both edges. (It comes out narrower still — the drawing does not fill its own
        // viewbox to the edges — which is why this is a ceiling rather than an equality.)
        Assert.True(painted <= 205, $"the drawing was painted {painted} wide in a 400x200 frame");

        // And centred in the space left over, rather than pushed to one side.
        int centre = (left + right) / 2;
        Assert.InRange(centre, 190, 210);
    }

    // ---- the page ---------------------------------------------------------------------------------

    /// <summary>
    /// The per-OS pixel baseline for a page of drawings.
    ///
    /// <para><b>Baselines are selected by OS and not by architecture</b> (<c>SnapshotInfra</c>
    /// <c>BaselineDir</c>), and this is the first fixture in the suite whose content is antialiased
    /// curves. That makes the macOS baseline implicitly an <b>arm64</b> artifact, because
    /// macos-latest is arm64 and the other two legs are x64 — the very difference that made M65's
    /// single committed PNG hash impossible to keep. A macOS baseline must be baked on arm64. See
    /// docs/M72-spec.md §8.</para>
    /// </summary>
    [Fact]
    public void APageOfDrawingsMatchesItsBaseline()
    {
        using DocumentRenderSource source = CreateFixtureSource();
        byte[] actual = DocumentSnapshotTests.RenderPagePng(source, 0);
        string baselinePath = Path.Combine(SnapshotInfra.BaselineDir, "vector-emblems-page1.png");

        if (SnapshotInfra.UpdateBaselines)
        {
            Directory.CreateDirectory(SnapshotInfra.BaselineDir);
            File.WriteAllBytes(baselinePath, actual);
            return;
        }

        Assert.SkipUnless(
            File.Exists(baselinePath),
            $"no baseline for this OS yet at {baselinePath} — bake it with TRESTLEBOARD_UPDATE_BASELINES=1");

        SnapshotInfra.ComparisonResult diff =
            SnapshotInfra.ComparePixels(actual, File.ReadAllBytes(baselinePath));
        Assert.True(
            diff.DiffPixelCount == 0,
            $"vector-emblems-page1 differs from its baseline in {diff.DiffPixelCount} pixels "
            + $"(max channel {diff.MaxChannelDiff})");
    }

    /// <summary>The caption under a drawing is laid out and drawn, like the caption under a photo.</summary>
    [Fact]
    public void ADrawingsCaptionIsLaidOut()
    {
        using DocumentRenderSource source = CreateFixtureSource();

        // The caption band extends the block's laid-out rect below its frame — the same mechanism a
        // photo's caption uses, which is what makes the text on the page flow past both alike.
        Assert.True(
            source.GetLayoutRect("drawing-1").Height > FixtureFrame(0).Height,
            "the caption did not extend the drawing's laid-out height");

        // And the second drawing, which has no caption, is unchanged by any of it.
        Assert.Equal(FixtureFrame(1).Height, source.GetLayoutRect("drawing-2").Height, 2);
    }

    // ---- the PDF ------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The prize, verified by reading the file rather than by looking at it.</b> The exported
    /// page's content stream contains path-painting operators and the document contains no image
    /// XObject at all — so the emblem is a curve in the PDF instead of a ~680dpi picture of one, and
    /// it prints at the press's resolution rather than at the one the app guessed.
    ///
    /// <para><c>Export.Pdf</c> needed no change whatever to make this true: <c>DrawPath</c> on an
    /// <c>SKDocument</c> canvas emits vector operators, and that was the entire bet of the milestone.
    /// This test is what checks the bet paid.</para>
    /// </summary>
    [Fact]
    public void TheEmblemReachesThePdfAsPathsAndNotAsAPicture()
    {
        using DocumentRenderSource source = CreateFixtureSource();
        using var pdf = new MemoryStream();
        DocumentPdfExporter.Export(pdf, source, Metadata("Vector emblem fixture"));
        byte[] bytes = pdf.ToArray();

        string streams = string.Concat(InflatedStreams(bytes));
        Assert.Contains(" c\n", streams, StringComparison.Ordinal);
        Assert.True(
            CountOperator(streams, " c\n") > 50,
            "a page of emblems should be hundreds of curve operators");

        // No raster anywhere in the file: not in a stream dictionary, and not as an XObject subtype.
        string raw = Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain("/Subtype /Image", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype/Image", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("/DCTDecode", raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the drawings cost in the exported file, measured rather than assumed (PLAN.md §11 M72:
    /// "a 19-part ornament repeated on six pages is not free as content-stream operators").
    ///
    /// <para>The real issues run 1.28–1.80 MB and M8's ceiling is 2.5 MB. The number this records is
    /// the whole five-page sample issue with the corner ornament added to every page, so it is
    /// measured against the same budget the issue itself is. See docs/M72-spec.md §7 for the figure
    /// as measured.</para>
    /// </summary>
    [Fact]
    public void OrnamentsOnEveryPageStayWellInsideTheSizeBudget()
    {
        const long budgetBytes = 2_500_000;

        long without = ExportedIssueSize(withOrnaments: false);
        long with = ExportedIssueSize(withOrnaments: true);

        Assert.True(
            with <= budgetBytes,
            $"the issue with an ornament on every page is {with / 1024} KB, over the "
            + $"{budgetBytes / 1024} KB budget");

        // Path operators are cheap next to a page of text. A raster emblem at 2048px cost tens of
        // kilobytes per distinct picture; this asserts the vector one costs far less than that.
        long cost = with - without;
        Assert.True(cost > 0, "the ornaments should cost something");
        Assert.True(
            cost < 40_000,
            $"five ornaments cost {cost} bytes in the exported file — they were under 6 KB when this "
            + "was written, so something has changed about how they are emitted");
    }

    // ---- the fixture ---------------------------------------------------------------------------------

    private static DocumentRenderSource CreateFixtureSource()
    {
        var document = new Document
        {
            Metadata = new DocumentMetadata
            {
                LodgeName = "Placeholder Lodge No. 000",
                IssueMonth = 7,
                IssueYear = 2026,
                Title = "Emblem fixture",
            },
        };

        StandardStyles.Add(document, new StandardStyleMetrics
        {
            BodySizePt = 12f,
            TableSizePt = 12f,
            TableHeaderSizePt = 13f,
            BodyLineSpacing = 1.25f,
            BodySpaceAfterPt = 6f,
        });
        document.PageMasters.Add(new PageMaster { Id = "master-letter" });

        var page = new Page { Id = "page-1", MasterRef = "master-letter" };
        for (int i = 0; i < FixtureEmblems.Length; i++)
        {
            Emblem emblem = EmblemLibrary.Find(FixtureEmblems[i])!;
            page.Blocks.Add(new VectorBlock
            {
                Id = $"drawing-{i + 1}",
                FrameRect = FixtureFrame(i),
                ZOrder = i,
                ViewBoxWidth = emblem.Width,
                ViewBoxHeight = emblem.Height,
                Parts = Parts(emblem),
                AltText = emblem.Description,

                // One caption only: enough to prove a drawing is captioned like a picture without
                // making every emblem in the baseline a text-rendering test as well.
                Caption = i == 0 ? "The square and compasses, with the letter G" : null,
            });
        }

        document.Pages.Add(page);
        return DocumentRenderSource.Create(document, new Dictionary<string, byte[]>(), SnapshotInfra.Store.Value);
    }

    /// <summary>Three frames down the page, all wider than the artwork so the centring shows.</summary>
    private static RectPt FixtureFrame(int index) => new(126f, 90f + (index * 220f), 360f, 180f);

    private static List<VectorPart> Parts(Emblem emblem) =>
        [.. emblem.Parts.Select(p => new VectorPart { PathData = p.PathData, StrokeWidth = p.StrokeWidth })];

    private static PdfMetadata Metadata(string title) =>
        new(title, "A. Placeholder", "Emblem rendering fixture");

    private static VectorPathSpec[] Specs(Emblem emblem) =>
        [.. emblem.Parts.Select(p => new VectorPathSpec(p.PathData, p.StrokeWidth))];

    private static long ExportedIssueSize(bool withOrnaments)
    {
        TboardPackage package = Core.Samples.SampleIssue.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        if (withOrnaments)
        {
            Emblem ornament = EmblemLibrary.Find("corner-ornament")!;
            foreach (Page page in package.Document.Pages)
            {
                page.Blocks.Add(new VectorBlock
                {
                    Id = $"ornament-{page.Id}",
                    FrameRect = new RectPt(36f, 36f, 54f, 54f),
                    ZOrder = 99,
                    ViewBoxWidth = ornament.Width,
                    ViewBoxHeight = ornament.Height,
                    Parts = Parts(ornament),
                    AltText = ornament.Description,
                });
            }
        }

        using DocumentRenderSource source = DocumentRenderSource.Create(
            package.Document,
            package.Assets,
            SnapshotInfra.Store.Value,
            options: null,
            widgets: TrestleBoard.Widgets.WidgetLayoutProvider.CreateDefault());
        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(buffer, source, Metadata("Size measurement"));
        return buffer.Length;
    }

    // ---- reading the PDF back --------------------------------------------------------------------------

    /// <summary>
    /// Every FlateDecode stream in the file, inflated. A hand-rolled walk rather than a PDF library:
    /// the only question being asked is whether the bytes contain path operators, and poppler — which
    /// the parity tests use — is Linux-only, so this would have skipped on two of the three CI legs.
    /// </summary>
    private static IEnumerable<string> InflatedStreams(byte[] pdf)
    {
        string raw = Encoding.Latin1.GetString(pdf);
        int at = 0;
        while ((at = raw.IndexOf("stream", at, StringComparison.Ordinal)) >= 0)
        {
            int start = at + "stream".Length;
            while (start < raw.Length && (raw[start] == '\r' || raw[start] == '\n'))
            {
                start++;
            }

            int end = raw.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0)
            {
                yield break;
            }

            at = end + 1;
            byte[] slice = pdf[start..end];
            string? inflated = TryInflate(slice);
            if (inflated is not null)
            {
                yield return inflated;
            }
        }
    }

    private static string? TryInflate(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var text = new StreamReader(zlib, Encoding.Latin1);
            return text.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            // Not a deflate stream — a font file, or the cross-reference table.
            return null;
        }
    }

    private static int CountOperator(string content, string op)
    {
        int count = 0;
        int at = 0;
        while ((at = content.IndexOf(op, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += op.Length;
        }

        return count;
    }

    // ---- drawing into a bitmap ---------------------------------------------------------------------------

    private static SKBitmap DrawToBitmap(
        IReadOnlyList<VectorPathSpec> parts, double viewBoxWidth, double viewBoxHeight, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info)!;
        surface.Canvas.Clear(SKColors.Transparent);
        VectorArtRenderer.Draw(
            surface.Canvas, parts, viewBoxWidth, viewBoxHeight, SKRect.Create(width, height), SKColors.Black);
        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
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

    private static (int Left, int Right) InkColumns(SKBitmap bitmap)
    {
        int left = int.MaxValue;
        int right = int.MinValue;
        for (int x = 0; x < bitmap.Width; x++)
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 8)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    break;
                }
            }
        }

        Assert.True(left <= right, "nothing was drawn at all");
        return (left, right);
    }
}
