using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M97: the paper the newsletter is printed on, and the margins round it.
///
/// <para><see cref="PageMaster.Size"/> and the four margin fields are read by the layout engine,
/// the snap engine, the renderer and three placement paths — eight readers — and until this
/// milestone <b>nothing in the application wrote any of them</b>. US Letter was hardwired by a
/// property default, and the string "A4" did not appear anywhere in the source.</para>
/// </summary>
public sealed class PageSetupTests
{
    private static PageFlowController Flow(EditorTestHarness harness) =>
        new(harness.Session, harness.Source);

    private static PageMaster Master(EditorTestHarness harness) =>
        harness.Session.Document.PageMasters[0];

    /// <summary>A4 is reachable at last, and reaches the field the layout engine reads.</summary>
    [Fact]
    public void ThePaperCanBeChanged()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        SizePt a4 = PageFlowController.Papers[1].Size;

        Assert.True(Flow(harness).SetPageSetup(a4, 54f, 54f, 54f, 54f));

        Assert.Equal(a4.Width, Master(harness).Size.Width, 2);
        Assert.Equal(a4.Height, Master(harness).Size.Height, 2);
    }

    /// <summary>Margins are writable, all four, independently.</summary>
    [Fact]
    public void TheFourMarginsCanBeChanged()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        SizePt letter = PageFlowController.Papers[0].Size;

        Assert.True(Flow(harness).SetPageSetup(letter, 36f, 72f, 40f, 80f));

        PageMaster master = Master(harness);
        Assert.Equal(36f, master.MarginLeftPt, 2);
        Assert.Equal(72f, master.MarginTopPt, 2);
        Assert.Equal(40f, master.MarginRightPt, 2);
        Assert.Equal(80f, master.MarginBottomPt, 2);
    }

    /// <summary>One Ctrl+Z puts all five fields back on every master.</summary>
    [Fact]
    public void TakingItBackRestoresThePaperAndAllFourMargins()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        PageMaster before = Master(harness);
        (SizePt size, float l, float t, float r, float b) =
            (before.Size, before.MarginLeftPt, before.MarginTopPt,
             before.MarginRightPt, before.MarginBottomPt);

        Assert.True(Flow(harness).SetPageSetup(PageFlowController.Papers[2].Size, 20f, 20f, 20f, 20f));
        harness.Session.Undo();

        PageMaster after = Master(harness);
        Assert.Equal(size.Width, after.Size.Width, 2);
        Assert.Equal(size.Height, after.Size.Height, 2);
        Assert.Equal(l, after.MarginLeftPt, 2);
        Assert.Equal(t, after.MarginTopPt, 2);
        Assert.Equal(r, after.MarginRightPt, 2);
        Assert.Equal(b, after.MarginBottomPt, 2);
    }

    /// <summary>
    /// Every master moves together, and each comes back to what IT was. A document that arrived
    /// with a mixture keeps its mixture on undo — the rule `ShowPageFooterCommand` follows, and the
    /// reason both capture per-master rather than taking one snapshot.
    /// </summary>
    [Fact]
    public void EveryMasterMovesTogetherAndComesBackToItsOwn()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        Document document = harness.Session.Document;
        document.PageMasters.Add(new PageMaster
        {
            Id = "master-2",
            Size = new SizePt(400f, 500f),
            MarginLeftPt = 10f,
        });

        Assert.True(Flow(harness).SetPageSetup(PageFlowController.Papers[1].Size, 30f, 30f, 30f, 30f));
        Assert.All(document.PageMasters, m => Assert.Equal(30f, m.MarginLeftPt, 2));

        harness.Session.Undo();

        Assert.Equal(54f, document.PageMasters[0].MarginLeftPt, 2);
        Assert.Equal(10f, document.PageMasters[1].MarginLeftPt, 2);
        Assert.Equal(400f, document.PageMasters[1].Size.Width, 2);
    }

    /// <summary>Asking for the paper it is already on changes nothing.</summary>
    [Fact]
    public void AskingForThePaperItIsAlreadyOnChangesNothing()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (SizePt size, float l, float t, float r, float b) = Flow(harness).PageSetup;

        Assert.False(Flow(harness).SetPageSetup(size, l, t, r, b));
    }

    /// <summary>
    /// Nothing is moved to fit. Making the paper smaller can leave a frame hanging off the edge,
    /// and the committee's layout is not shuffled for them — but the app can say how many, which is
    /// what makes undo an informed choice rather than a guess.
    /// </summary>
    [Fact]
    public void SmallerPaperLeavesTheLayoutAloneAndTheAppCanSayHowMuchHangsOff()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        PageFlowController flow = Flow(harness);

        Assert.Equal(0, flow.BlocksOffThePaper());

        RectPt before = harness.Session.Document.Pages[0].Blocks[0].FrameRect;
        Assert.True(flow.SetPageSetup(new SizePt(200f, 300f), 10f, 10f, 10f, 10f));

        Assert.True(flow.BlocksOffThePaper() > 0, "the frame is wider than the new paper");

        RectPt after = harness.Session.Document.Pages[0].Blocks[0].FrameRect;
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Width, after.Width, 3);
    }

    /// <summary>The three papers offered are the real ones, upright, in inches and millimetres.</summary>
    [Fact]
    public void ThePapersOfferedAreTheRealOnes()
    {
        Assert.Equal(3, PageFlowController.Papers.Count);

        Assert.Equal(612f, PageFlowController.Papers[0].Size.Width, 1);
        Assert.Equal(792f, PageFlowController.Papers[0].Size.Height, 1);

        // A4 is 210 × 297 mm, which is 595.28 × 841.89 pt.
        Assert.Equal(595.28f, PageFlowController.Papers[1].Size.Width, 1);
        Assert.Equal(841.89f, PageFlowController.Papers[1].Size.Height, 1);

        Assert.Equal(1008f, PageFlowController.Papers[2].Size.Height, 1);

        // Every one is offered upright; landscape is a tick box, not a fourth paper.
        Assert.All(PageFlowController.Papers, p => Assert.True(p.Size.Height > p.Size.Width));
    }
}
