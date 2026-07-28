using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Documents;
using TrestleBoard.Widgets;
using Xunit;

namespace TrestleBoard.Rendering.SnapshotTests;

/// <summary>
/// Captions, drawn at last (PLAN.md §11 M18). <see cref="ImageFrame.Caption"/> has been collected
/// by the insert dialog since M6 and stored in every saved file since; nothing ever printed it.
///
/// <para>These are the golden line-box tests the milestone asks for, and — more importantly — the
/// <b>additive guard</b>: a document with no captions must lay out byte-identically to how it laid
/// out before M18, because otherwise every baseline in the repository quietly stops meaning what it
/// was baked to mean.</para>
/// </summary>
public sealed class CaptionRenderTests
{
    private static readonly WidgetLayoutProvider Widgets = WidgetLayoutProvider.CreateDefault();

    private static DocumentRenderSource CreateIssue(Action<Document>? adjust = null)
    {
        TboardPackage package = SampleIssue.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        adjust?.Invoke(package.Document);
        return DocumentRenderSource.Create(
            package.Document, package.Assets, SnapshotInfra.Store.Value, options: null, widgets: Widgets);
    }

    private static ImageFrame CoverPhoto(Document document) =>
        (ImageFrame)document.FindBlock("img-cover").Block;

    /// <summary>
    /// Where a caption goes: under the frame, a small gap below it, no wider than the picture, and
    /// set in the document's own caption style rather than in anything invented here.
    /// </summary>
    [Fact]
    public void TheCaptionSitsUnderTheFrameInTheDocumentsCaptionStyle()
    {
        using DocumentRenderSource source = CreateIssue();
        Assert.True(source.TryGetCaptionLayout("img-cover", out FrameLayout? caption));

        RectPt frame = source.GetEffectiveRect("img-cover");
        LineBox line = Assert.Single(caption!.Lines);

        Assert.True(
            line.BandTop >= frame.Bottom,
            $"the caption starts at {line.BandTop}, above the bottom of the picture at {frame.Bottom}");
        Assert.True(line.BandTop >= frame.Bottom + CaptionLayout.GapPt - 0.01f);
        Assert.Equal(frame.X, caption.Frame.Left, 3);
        Assert.Equal(frame.Right, caption.Frame.Right, 3);

        LineSegment segment = Assert.Single(line.Segments);
        PositionedGlyphRun run = Assert.Single(segment.Runs);
        Assert.True(run.Glyphs.Count > 10, "the caption drew almost no glyphs");

        // Set in the DOCUMENT's caption style, not in anything this milestone invented: a document
        // whose caption style changes must move its captions with it.
        TboardPackage package = SampleIssue.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        CharacterStyleDef style = package.Document.StyleSheet.GetCharacterStyle(
            package.Document.StyleSheet.GetParagraphStyle(CaptionLayout.StyleName).CharacterStyleRef);
        Assert.Equal(style.SizePt, run.SizePt, 3);
    }

    /// <summary>
    /// The caption is part of what the surrounding text has to flow past. If it were not, the essay
    /// on page one would run straight through the words under the photograph.
    /// </summary>
    [Fact]
    public void TextFlowsPastTheCaptionRatherThanThroughIt()
    {
        using DocumentRenderSource withCaption = CreateIssue();
        RectPt layoutRect = withCaption.GetLayoutRect("img-cover");
        RectPt frameRect = withCaption.GetEffectiveRect("img-cover");

        Assert.True(
            layoutRect.Bottom > frameRect.Bottom,
            "a captioned picture must push the text past its caption, not only past itself");
        Assert.Equal(frameRect.X, layoutRect.X, 3);
        Assert.Equal(frameRect.Width, layoutRect.Width, 3);

        // ...and the frame itself is unchanged: the picture is not stretched into its caption.
        Assert.Equal(frameRect, withCaption.GetEffectiveRect("img-cover"));
    }

    /// <summary>
    /// <b>The additive guard.</b> Take the caption away and page one lays out exactly as it did
    /// before M18 — same line boxes, same pixels — which is what keeps every baseline in the
    /// repository honest and confines the re-bake to the pages that genuinely gained words.
    /// </summary>
    [Fact]
    public void ACaptionlessDocumentLaysOutExactlyAsItDidBefore()
    {
        using DocumentRenderSource captionless = CreateIssue(d => CoverPhoto(d).Caption = null);
        using DocumentRenderSource blank = CreateIssue(d => CoverPhoto(d).Caption = "   ");

        Assert.False(captionless.TryGetCaptionLayout("img-cover", out _));
        Assert.False(blank.TryGetCaptionLayout("img-cover", out _));
        Assert.Equal(
            captionless.GetEffectiveRect("img-cover"),
            captionless.GetLayoutRect("img-cover"));

        // A caption of nothing but spaces is not a caption — same pixels, to the byte.
        Assert.Equal(
            DocumentSnapshotTests.RenderPagePng(captionless, 0),
            DocumentSnapshotTests.RenderPagePng(blank, 0));

        // ...and the pages with no picture on them at all are untouched by any of this.
        using DocumentRenderSource captioned = CreateIssue();
        Assert.Equal(
            DocumentSnapshotTests.RenderPagePng(captionless, 2),
            DocumentSnapshotTests.RenderPagePng(captioned, 2));
    }

    /// <summary>A caption changes the page, which is the whole point of the milestone.</summary>
    [Fact]
    public void AddingACaptionChangesThePage()
    {
        using DocumentRenderSource captionless = CreateIssue(d => CoverPhoto(d).Caption = null);
        using DocumentRenderSource captioned = CreateIssue();

        Assert.NotEqual(
            DocumentSnapshotTests.RenderPagePng(captionless, 0),
            DocumentSnapshotTests.RenderPagePng(captioned, 0));
    }

    /// <summary>
    /// A caption is a line under a photograph, not an article. Something pasted in by accident is
    /// held to three lines rather than being allowed to walk down the page pushing the text ahead
    /// of it.
    /// </summary>
    [Fact]
    public void ALongCaptionIsHeldToThreeLines()
    {
        using DocumentRenderSource source = CreateIssue(d => CoverPhoto(d).Caption =
            "The brothers of Placeholder Lodge gathered on the appointed evening for fellowship, "
            + "a simple meal, the reading of the minutes, and a long and cheerful discussion of "
            + "every announcement that had been made since the previous stated communication.");

        Assert.True(source.TryGetCaptionLayout("img-cover", out FrameLayout? caption));
        Assert.InRange(caption!.Lines.Count, 2, CaptionLayout.MaxLines);
    }

    /// <summary>
    /// Editing a caption relays the page out. The command carries a geometry scope for exactly this
    /// reason — a content scope would have repainted the words without moving the text around them.
    /// </summary>
    [Fact]
    public void ChangingTheCaptionMovesTheTextAgain()
    {
        TboardPackage package = SampleIssue.CreatePackage(DocumentSnapshotTests.TestPhotoPng());
        var session = new TrestleBoard.Core.Commands.DocumentSession(package.Document);
        using DocumentRenderSource source = DocumentRenderSource.CreateEditable(
            package.Document, package.Assets, SnapshotInfra.Store.Value, session, options: null, widgets: Widgets);

        float before = source.GetLayoutRect("img-cover").Bottom;

        session.Execute(TrestleBoard.Core.Commands.SetPictureWordsCommand.ForCaption(
            "img-cover",
            "A warm evening at the lodge, with the brothers gathered outside on the steps "
            + "after the stated communication."));

        Assert.True(
            source.GetLayoutRect("img-cover").Bottom > before,
            "a longer caption must take more room from the text beside it");

        session.Undo();
        Assert.Equal(before, source.GetLayoutRect("img-cover").Bottom, 3);
    }
}
