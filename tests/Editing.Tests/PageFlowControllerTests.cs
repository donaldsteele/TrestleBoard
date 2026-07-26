using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Page structure and auto-flow (docs/M8-spec.md §2/§3): pouring an overset story onward, and what
/// happens to a chain when the page carrying its continuation goes away.
/// </summary>
public sealed class PageFlowControllerTests
{
    private static string Prose(int sentences) => string.Concat(
        Enumerable.Repeat(
            "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ",
            sentences));

    private static PageFlowController Flow(EditorTestHarness harness) =>
        new(harness.Session, harness.Source);

    // ---- auto-flow -----------------------------------------------------------------------------

    [Fact]
    public void AutoFlowIsOfferedOnlyWhenTextHasActuallyRunOut()
    {
        using var shortText = new EditorTestHarness("A single line.", withExclusion: false);
        Assert.False(Flow(shortText).CanAutoFlow(EditorTestHarness.BlockId));

        using var overflowing = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        Assert.True(Flow(overflowing).CanAutoFlow(EditorTestHarness.BlockId));
    }

    [Fact]
    public void AutoFlowPoursTheRestOntoNewPagesUntilItFits()
    {
        using var harness = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        PageFlowController flow = Flow(harness);

        Assert.True(flow.AutoFlow(EditorTestHarness.BlockId));

        Assert.False(harness.Source.IsOverset);
        Assert.True(harness.Session.Document.Pages.Count > 1);
        Assert.Null(flow.StatusMessage);
    }

    [Fact]
    public void AutoFlowAddsTheFewestFramesThatWorks()
    {
        using var harness = new EditorTestHarness(Prose(20), frameHeightPt: 120, withExclusion: false);
        Flow(harness).AutoFlow(EditorTestHarness.BlockId);

        int frames = harness.Session.Document.Pages.SelectMany(p => p.Blocks).Count(b => b is TextBlock);

        // The prose is short enough to need only a couple of continuations; if the loop kept going
        // after the story fit, this would be at the cap instead.
        Assert.False(harness.Source.IsOverset);
        Assert.InRange(frames, 2, 4);
    }

    [Fact]
    public void TheWholeRunIsOneUndoStep()
    {
        using var harness = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        int pagesBefore = harness.Session.Document.Pages.Count;
        int blocksBefore = harness.Session.Document.Pages.SelectMany(p => p.Blocks).Count();

        Flow(harness).AutoFlow(EditorTestHarness.BlockId);
        Assert.Equal("Make the rest fit", harness.Session.UndoDescription);

        harness.Session.Undo();

        Assert.Equal(pagesBefore, harness.Session.Document.Pages.Count);
        Assert.Equal(blocksBefore, harness.Session.Document.Pages.SelectMany(p => p.Blocks).Count());
        Assert.Null(((TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block).LinkNext);
    }

    [Fact]
    public void ContinuationFramesDoNotPushTheirOwnTextAside()
    {
        using var harness = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        Flow(harness).AutoFlow(EditorTestHarness.BlockId);

        IEnumerable<TextBlock> added = harness.Session.Document.Pages
            .SelectMany(p => p.Blocks)
            .OfType<TextBlock>()
            .Where(b => b.Id != EditorTestHarness.BlockId);

        Assert.All(added, b => Assert.Equal(WrapMode.None, b.WrapMode));
    }

    /// <summary>
    /// A story longer than the cap allows must stop there and say so, rather than adding pages until
    /// the machine gives out.
    /// </summary>
    [Fact]
    public void AStoryTooLongForTheCapStopsThereAndExplainsItself()
    {
        using var harness = new EditorTestHarness(Prose(600), frameHeightPt: 60, withExclusion: false);
        PageFlowController flow = Flow(harness);

        Assert.True(flow.AutoFlow(EditorTestHarness.BlockId));

        int added = harness.Session.Document.Pages.SelectMany(p => p.Blocks).Count(b => b is TextBlock) - 1;
        Assert.Equal(PageFlowController.MaxAutoFlowFrames, added);
        Assert.Contains("does not all fit", flow.StatusMessage ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFlowOnTextThatAlreadyFitsChangesNothing()
    {
        using var harness = new EditorTestHarness("A single line.", withExclusion: false);
        int before = harness.Session.Document.Pages.Count;

        Assert.False(Flow(harness).AutoFlow(EditorTestHarness.BlockId));
        Assert.Equal(before, harness.Session.Document.Pages.Count);
        Assert.False(harness.Session.CanUndo);
    }

    // ---- page structure ------------------------------------------------------------------------

    [Fact]
    public void PagesCanBeAddedRemovedAndReordered()
    {
        using var harness = new EditorTestHarness("Some text.", withExclusion: false);
        PageFlowController flow = Flow(harness);

        string second = flow.AddPage(0);
        Assert.Equal(2, harness.Session.Document.Pages.Count);
        Assert.Equal(second, harness.Session.Document.Pages[1].Id);

        Assert.True(flow.MovePage(1, 0));
        Assert.Equal(second, harness.Session.Document.Pages[0].Id);

        harness.Session.Undo();
        Assert.Equal(second, harness.Session.Document.Pages[1].Id);

        Assert.True(flow.RemovePage(1));
        Assert.Single(harness.Session.Document.Pages);
    }

    [Fact]
    public void TheLastPageCannotBeRemoved()
    {
        using var harness = new EditorTestHarness("Some text.", withExclusion: false);
        PageFlowController flow = Flow(harness);

        Assert.False(flow.RemovePage(0));
        Assert.Single(harness.Session.Document.Pages);
        Assert.Contains("only page", flow.StatusMessage ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Deleting the page that carried a story's continuation must cost the continuation, not the
    /// document (docs/M8-spec.md §3.1) — the layout has to survive the dangling link.
    /// </summary>
    [Fact]
    public void RemovingAPageThatCarriedAContinuationLeavesTheStoryOversetNotBroken()
    {
        using var harness = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        PageFlowController flow = Flow(harness);
        flow.AutoFlow(EditorTestHarness.BlockId);
        Assert.False(harness.Source.IsOverset);

        Assert.True(flow.RemovePage(1));

        // The chain now points at a block that is gone. Layout must terminate there, not throw.
        Assert.True(harness.Source.IsOverset);
        Assert.Contains("carried the rest of a story", flow.StatusMessage ?? "", StringComparison.Ordinal);

        // And the head still holds its now-dangling link, so Ctrl+Z restores the chain intact.
        var head = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        Assert.NotNull(head.LinkNext);
        harness.Session.Undo();
        Assert.False(harness.Source.IsOverset);
    }

    [Fact]
    public void MovingAPageDoesNotDisturbTheChain()
    {
        using var harness = new EditorTestHarness(Prose(60), frameHeightPt: 120, withExclusion: false);
        PageFlowController flow = Flow(harness);
        flow.AutoFlow(EditorTestHarness.BlockId);

        var head = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        string? linkBefore = head.LinkNext;

        Assert.True(flow.MovePage(0, harness.Session.Document.Pages.Count - 1));

        Assert.Equal(linkBefore, head.LinkNext);

        // The chain is still the ordering authority, so the text still flows head-first.
        Assert.False(harness.Source.IsOverset);
    }
}
