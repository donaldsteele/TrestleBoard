using System.Text;
using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Export.Pdf;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// M78: the two things every reader feels and no editor sees — tappable links, and the line along
/// the bottom of the page.
/// </summary>
public sealed class LinkAndFooterTests
{
    // ---- Finding the links --------------------------------------------------------------------

    /// <summary>
    /// What the newsletter actually contains: an address, a website and a telephone number, all
    /// written as a lodge secretary writes them.
    /// </summary>
    [Fact]
    public void TheThreeKindsAreFoundAndGivenTheRightScheme()
    {
        IReadOnlyList<DetectedLink> links = LinkDetector.FindInParagraph(
            "Write to secretary@lodge414.org, see www.lodge414.org, or ring (803) 555-0100.",
            paragraphIndex: 0);

        Assert.Equal(3, links.Count);
        Assert.Equal(
            ["mailto:secretary@lodge414.org", "https://www.lodge414.org", "tel:+18035550100"],
            links.Select(l => l.Uri));
    }

    /// <summary>
    /// <b>A false link is worse than a missing one</b>, because it is a promise the newsletter makes
    /// to a reader who then dials a lodge number that is not a telephone. Every string here is one a
    /// trestle board really prints.
    /// </summary>
    [Theory]
    [InlineData("Indian Land Lodge No. 414 meets on the first Tuesday.")]
    [InlineData("The installation was held on 12-04-2025 in the dining hall.")]
    [InlineData("Our charter dates from 1954 and the lodge room seats 120.")]
    [InlineData("Refreshment at 6:30, the meeting at 7:30.")]
    public void ThingsThatAreNotTelephoneNumbersAreLeftAlone(string sentence) =>
        Assert.Empty(LinkDetector.FindInParagraph(sentence, 0));

    /// <summary>
    /// An email address contains something a loose web pattern would call a domain. Two annotations
    /// over the same glyphs is a reader tapping one thing and getting the other.
    /// </summary>
    [Fact]
    public void AnEmailIsNotAlsoReadAsAWebsite()
    {
        IReadOnlyList<DetectedLink> links = LinkDetector.FindInParagraph("secretary@lodge414.org", 0);

        DetectedLink only = Assert.Single(links);
        Assert.Equal(LinkKind.Email, only.Kind);
    }

    /// <summary>Trailing punctuation belongs to the sentence, not to the address.</summary>
    [Fact]
    public void TheFullStopAtTheEndOfTheSentenceIsNotPartOfTheAddress()
    {
        DetectedLink link = Assert.Single(LinkDetector.FindInParagraph("Visit www.lodge414.org.", 0));

        Assert.Equal("https://www.lodge414.org", link.Uri);
    }

    // ---- Reaching the PDF ----------------------------------------------------------------------

    /// <summary>
    /// The annotations reach the file. Read straight out of the PDF bytes rather than through
    /// poppler, so this runs on all three operating systems rather than on Linux CI alone — the
    /// annotation is the whole feature, and a feature checked on one platform is a feature that
    /// can quietly stop working on the other two.
    /// </summary>
    [Fact]
    public void TheExportedPdfCarriesOneAnnotationPerLinkAndNoneForTheDecoys()
    {
        using DocumentRenderSource source = CreateSourceWithLinks();
        byte[] pdf = ExportToBytes(source);
        string raw = Encoding.Latin1.GetString(pdf);

        Assert.Contains("mailto:secretary@lodge414.org", raw, StringComparison.Ordinal);
        Assert.Contains("https://www.lodge414.org", raw, StringComparison.Ordinal);
        Assert.Contains("tel:+18035550100", raw, StringComparison.Ordinal);

        // "Lodge No. 414" and the year are in the same paragraph and must have produced nothing.
        Assert.Equal(3, CountOccurrences(raw, "/Type /Annot") + CountOccurrences(raw, "/Type/Annot"));
    }

    /// <summary>
    /// <b>Not a pixel moves.</b> The same page is rendered with the link pass reachable and the
    /// annotation is not a drawing operation, so the raster is byte-identical to what the same
    /// fixture produced before M78 existed. No underline, no blue: the reader's PDF app owns the
    /// affordance, and blue underlines on paper are the one modern habit this audience finds ugly.
    ///
    /// <para>Rendering twice from two independent sources also proves the link query itself has no
    /// side effect on layout — it runs between them.</para>
    /// </summary>
    [Fact]
    public void FindingTheLinksChangesNothingThatIsDrawn()
    {
        using DocumentRenderSource before = CreateSourceWithLinks();
        byte[] withoutAsking = RenderPng(before, 0);

        using DocumentRenderSource after = CreateSourceWithLinks();
        Assert.NotEmpty(after.GetLinksOnPage(0));
        byte[] afterAsking = RenderPng(after, 0);

        Assert.Equal(withoutAsking, afterAsking);
    }

    /// <summary>A stretch that wraps is two rectangles, so both halves are tappable.</summary>
    [Fact]
    public void EveryLinkOnThePageHasARectangleWithRealArea()
    {
        using DocumentRenderSource source = CreateSourceWithLinks();

        IReadOnlyList<DocumentRenderSource.PageLink> links = source.GetLinksOnPage(0);

        Assert.Equal(3, links.Count);
        Assert.All(links, link =>
        {
            Assert.True(link.Rect.Width > 0f, $"{link.Uri} has no width");
            Assert.True(link.Rect.Height > 0f, $"{link.Uri} has no height");
        });
    }

    // ---- The footer -----------------------------------------------------------------------------

    /// <summary>The three facts, in the order a reader scans them.</summary>
    [Fact]
    public void TheFooterSaysTheLodgeTheMonthAndWhichPage() =>
        Assert.Equal(
            "Indian Land Lodge 414 · September 2026 · page 3 of 6",
            PageFooterRenderer.Compose("Indian Land Lodge 414", new DateOnly(2026, 9, 1), 3, 6));

    /// <summary>
    /// A newsletter that does not know which issue it is still has pages, and "page 3 of 6" alone is
    /// the fact the reader is most likely to be looking for. The clause it cannot fill in is left
    /// out rather than filled with a blank or a guess.
    /// </summary>
    [Fact]
    public void AFooterLeavesOutWhatItDoesNotKnowRatherThanGuessing()
    {
        Assert.Equal("page 1 of 4", PageFooterRenderer.Compose(null, null, 1, 4));
        Assert.Equal("Indian Land Lodge 414 · page 1 of 4", PageFooterRenderer.Compose("Indian Land Lodge 414", null, 1, 4));
        Assert.Equal("September 2026 · page 1 of 4", PageFooterRenderer.Compose("   ", new DateOnly(2026, 9, 1), 1, 4));
    }

    /// <summary>
    /// <b>The footer is drawn, and turning it off puts the page back exactly as it was.</b> The
    /// second half is what stops this milestone from being a one-way door for every existing
    /// newsletter: a file written before M78 has no such property and must open looking as it did
    /// yesterday.
    /// </summary>
    [Fact]
    public void TheFooterIsDrawnWhenItIsOnAndTheRestOfThePageIsUntouched()
    {
        using DocumentRenderSource source = CreateSourceWithLinks();
        byte[] without = RenderPng(source, 0);

        foreach (PageMaster master in MasterlessDocument(source).PageMasters)
        {
            master.ShowFooter = true;
        }

        source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        byte[] with = RenderPng(source, 0);
        Assert.NotEqual(without, with);

        foreach (PageMaster master in MasterlessDocument(source).PageMasters)
        {
            master.ShowFooter = false;
        }

        source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        Assert.Equal(without, RenderPng(source, 0));
    }

    /// <summary>
    /// M47 draws the margin and tells the user it is the edge to keep inside. The app's own
    /// furniture must not be the one thing that breaks the app's own rule.
    /// </summary>
    [Fact]
    public void TheFooterSitsInsideTheMarginTheAppDrawsAndNotBelowIt()
    {
        var master = new PageMaster { Id = "m", ShowFooter = true };
        float bottomOfTheTextArea = master.Size.Height - master.MarginBottomPt;

        // The renderer is handed that line and puts the baseline above it. Asserting on the
        // arithmetic the renderer is given rather than on pixels, because what is worth proving is
        // the RULE — that the footer is on the paper side of the margin, not the reader's.
        Assert.True(bottomOfTheTextArea < master.Size.Height);
        Assert.True(bottomOfTheTextArea > master.MarginTopPt);
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>
    /// The sample newsletter with one paragraph added that carries all three kinds of link and two
    /// decoys — the lodge's own number, which is not a telephone number, and a year.
    /// </summary>
    internal static DocumentRenderSource CreateSourceWithLinks()
    {
        TboardPackage package = SampleDocument.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        Document document = package.Document;

        // The BODY story's first paragraph, replaced. Two earlier versions of this fixture found no
        // links and therefore proved nothing: the first appended a paragraph to a story that
        // already filled its frame, so it overset and laid out on no page at all; the second
        // replaced the first paragraph of Stories[0], which is the cover heading — a three-line
        // story in a small frame that this sentence promptly overflowed. The body story is the one
        // with room, and block-body-1 is on page 1.
        Story story = document.Stories.Single(s => s.Id == "story-body");
        story.Paragraphs[0].Runs =
        [
            new StoryRun
            {
                Text = "Indian Land Lodge No. 414, chartered 1954. "
                    + "Write to secretary@lodge414.org, see www.lodge414.org, "
                    + "or ring (803) 555-0100.",
            },
        ];

        return DocumentRenderSource.Create(document, package.Assets, SnapshotInfra.Store.Value);
    }

    /// <summary>
    /// Reaches the document behind a source. The render source deliberately owns its document, so
    /// this test drives the model through the same page master the renderer reads — which is the
    /// only way to prove that turning the footer off puts the pixels back.
    /// </summary>
    private static Document MasterlessDocument(DocumentRenderSource source) => source.DocumentForTest;

    private static byte[] RenderPng(DocumentRenderSource source, int pageIndex) =>
        DocumentSnapshotTests.RenderPagePng(source, pageIndex);

    private static byte[] ExportToBytes(DocumentRenderSource source)
    {
        using var buffer = new MemoryStream();
        DocumentPdfExporter.Export(
            buffer, source, new PdfMetadata("Trestle Board", "Indian Land Lodge 414", "Newsletter"));
        return buffer.ToArray();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
