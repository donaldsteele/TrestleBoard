using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M5 direct manipulation (docs/M5-spec.md): selection, drag with live reflow and a single
/// commit, snapping, keyboard equivalents, structure, z-order and frame linking.
/// </summary>
public sealed class FrameEditorControllerTests
{
    private const string ImageId = "img-1";

    private static string Prose(int sentences) => string.Concat(
        Enumerable.Repeat(
            "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ",
            sentences));

    private static int VisualLineCount(EditorTestHarness harness)
    {
        Assert.True(harness.Source.TryGetStoryGeometry(EditorTestHarness.StoryId, out StoryTextGeometry geometry));
        return geometry.VisualLineCount;
    }

    private static RectPt DocumentRect(EditorTestHarness harness, string blockId) =>
        harness.Session.Document.FindBlock(blockId).Block.FrameRect;

    // ---- Selection ---------------------------------------------------------------------------

    [Fact]
    public void SelectAtPicksTheTopmostBlock()
    {
        using var harness = new EditorTestHarness(Prose(6));
        Assert.True(harness.Frames.SelectAt(0, 300f, 150f));
        Assert.Equal(ImageId, harness.Frames.SelectedBlockId);

        // Below the photo only the text frame is there.
        Assert.True(harness.Frames.SelectAt(0, 100f, 400f));
        Assert.Equal(EditorTestHarness.BlockId, harness.Frames.SelectedBlockId);

        Assert.False(harness.Frames.SelectAt(0, 580f, 700f));
        Assert.Null(harness.Frames.SelectedBlockId);
    }

    [Fact]
    public void TabCyclesBlocksInStackingOrder()
    {
        using var harness = new EditorTestHarness(Prose(4));
        Assert.True(harness.Frames.CycleSelection(0, forward: true));
        Assert.Equal(EditorTestHarness.BlockId, harness.Frames.SelectedBlockId); // z = 1

        harness.Frames.CycleSelection(0, forward: true);
        Assert.Equal(ImageId, harness.Frames.SelectedBlockId); // z = 10

        harness.Frames.CycleSelection(0, forward: true);
        Assert.Equal(EditorTestHarness.BlockId, harness.Frames.SelectedBlockId); // wraps

        harness.Frames.CycleSelection(0, forward: false);
        Assert.Equal(ImageId, harness.Frames.SelectedBlockId);
    }

    // ---- Drag, preview, commit ---------------------------------------------------------------

    [Fact]
    public void DragPreviewsWithoutTouchingTheDocumentAndCommitsOnce()
    {
        using var harness = new EditorTestHarness(Prose(8));
        RectPt before = DocumentRect(harness, ImageId);
        harness.Frames.Select(ImageId);

        Assert.True(harness.Frames.BeginDrag(FrameHandle.Body, 300f, 150f));
        harness.Frames.DragTo(300f, 330f, snap: false);

        // Mid-drag: the render source shows the new rect, the document still shows the old one.
        Assert.Equal(before, DocumentRect(harness, ImageId));
        Assert.True(harness.Source.HasGeometryPreview);
        Assert.Equal(before.Y + 180f, harness.Source.GetEffectiveRect(ImageId).Y, 3);
        Assert.False(harness.Session.CanUndo);

        harness.Frames.EndDrag(commit: true);
        Assert.False(harness.Source.HasGeometryPreview);
        Assert.Equal(before.Y + 180f, DocumentRect(harness, ImageId).Y, 3);

        harness.Session.Undo();
        Assert.Equal(before, DocumentRect(harness, ImageId));
        Assert.False(harness.Session.CanUndo);
    }

    [Fact]
    public void CancelledDragRestoresEverythingAndCommitsNothing()
    {
        using var harness = new EditorTestHarness(Prose(8));
        RectPt before = DocumentRect(harness, ImageId);
        int linesBefore = VisualLineCount(harness);

        harness.Frames.Select(ImageId);
        harness.Frames.BeginDrag(FrameHandle.Body, 300f, 150f);
        harness.Frames.DragTo(300f, 330f, snap: false);
        harness.Frames.EndDrag(commit: false);

        Assert.Equal(before, DocumentRect(harness, ImageId));
        Assert.False(harness.Source.HasGeometryPreview);
        Assert.False(harness.Session.CanUndo);
        Assert.Equal(linesBefore, VisualLineCount(harness));
    }

    [Fact]
    public void DraggingAPhotoThroughTheColumnReflowsTextLive()
    {
        using var harness = new EditorTestHarness(Prose(10));
        int linesBefore = VisualLineCount(harness);

        harness.Frames.Select(ImageId);
        harness.Frames.BeginDrag(FrameHandle.Body, 300f, 150f);

        // Push the photo fully outside the text column: the wrap disappears and the text
        // re-flows during the drag, before any command exists.
        harness.Frames.DragTo(900f, 150f, snap: false);
        Assert.NotEqual(linesBefore, VisualLineCount(harness));
        Assert.False(harness.Session.CanUndo);

        harness.Frames.DragTo(300f, 150f, snap: false);
        Assert.Equal(linesBefore, VisualLineCount(harness));
    }

    [Fact]
    public void ResizeHandleDragMovesOnlyThatEdgeAndCommitsAResizeCommand()
    {
        using var harness = new EditorTestHarness(Prose(6));
        RectPt before = DocumentRect(harness, EditorTestHarness.BlockId);
        harness.Frames.Select(EditorTestHarness.BlockId);

        Assert.True(harness.Frames.BeginDrag(FrameHandle.Right, before.Right, before.Y + 100f));
        harness.Frames.DragTo(before.Right - 100f, before.Y + 100f, snap: false);
        harness.Frames.EndDrag(commit: true);

        RectPt after = DocumentRect(harness, EditorTestHarness.BlockId);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Right - 100f, after.Right, 3);
        Assert.Equal("Resize frame", harness.Session.UndoDescription);
    }

    [Fact]
    public void DragSnapsToThePageMarginAndReportsAGuide()
    {
        using var harness = new EditorTestHarness(Prose(4));
        harness.Frames.Select(ImageId);
        harness.Frames.BeginDrag(FrameHandle.Body, 300f, 150f);

        // Land the frame's left edge 3pt shy of the 54pt margin.
        harness.Frames.DragTo(300f - (260f - 57f), 150f);

        Assert.Equal(54f, harness.Frames.SelectedRect!.Value.X, 3);
        SnapGuide guide = Assert.Single(harness.Frames.SnapGuides);
        Assert.Equal(SnapAxis.X, guide.Axis);
        Assert.Equal(54f, guide.PositionPt, 3);
    }

    // ---- Keyboard equivalents ----------------------------------------------------------------

    [Fact]
    public void NudgeBurstCoalescesIntoOneUndoStep()
    {
        using var harness = new EditorTestHarness(Prose(4));
        RectPt before = DocumentRect(harness, ImageId);
        harness.Frames.Select(ImageId);

        for (int i = 0; i < 5; i++)
        {
            harness.Clock.Advance(TimeSpan.FromMilliseconds(60));
            Assert.True(harness.Frames.Nudge(1f, 0f));
        }

        Assert.Equal(before.X + 5f, DocumentRect(harness, ImageId).X, 3);
        harness.Session.Undo();
        Assert.Equal(before, DocumentRect(harness, ImageId));
        Assert.False(harness.Session.CanUndo);
    }

    [Fact]
    public void NudgeMovesExactlyAndNeverSnaps()
    {
        using var harness = new EditorTestHarness(Prose(4));
        harness.Frames.Select(ImageId);

        // Park the frame 1pt off the left margin, then nudge left once: a snapping nudge would
        // jump 1pt further onto the margin. It must land exactly on 54 + 1 - 1 = 54... so start
        // at 56 and nudge once: the result must be 55, not 54.
        harness.Session.Execute(new Core.Commands.MoveBlockCommand(
            ImageId, new RectPt(56f, 120f, 140f, 110f)));
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        harness.Frames.Nudge(-1f, 0f);
        Assert.Equal(55f, DocumentRect(harness, ImageId).X, 3);

        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        harness.Frames.Nudge(-1f, 0f, large: true);
        Assert.Equal(45f, DocumentRect(harness, ImageId).X, 3);
    }

    [Fact]
    public void KeyboardResizeMatchesTheBottomRightHandle()
    {
        using var harness = new EditorTestHarness(Prose(4));
        RectPt before = DocumentRect(harness, ImageId);
        harness.Frames.Select(ImageId);

        Assert.True(harness.Frames.NudgeResize(1f, 1f, large: true));
        RectPt after = DocumentRect(harness, ImageId);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(before.Width + 10f, after.Width, 3);
        Assert.Equal(before.Height + 10f, after.Height, 3);
    }

    // ---- Structure ---------------------------------------------------------------------------

    [Fact]
    public void AddTextFrameCreatesStoryAndBlockAsOneUndoStep()
    {
        using var harness = new EditorTestHarness(Prose(3));
        int storiesBefore = harness.Session.Document.Stories.Count;
        int blocksBefore = harness.Session.Document.Pages[0].Blocks.Count;

        string id = harness.Frames.AddTextFrame(0);

        Assert.Equal(id, harness.Frames.SelectedBlockId);
        Assert.Equal(storiesBefore + 1, harness.Session.Document.Stories.Count);
        Assert.Equal(blocksBefore + 1, harness.Session.Document.Pages[0].Blocks.Count);
        Assert.Equal("Add text frame", harness.Session.UndoDescription);

        // Sits inside the margins and on top of the stack.
        var block = (TextBlock)harness.Session.Document.FindBlock(id).Block;
        Assert.True(block.FrameRect.X >= 54f);
        Assert.True(block.FrameRect.Y >= 54f);
        Assert.Equal(harness.Session.Document.Pages[0].Blocks.Max(b => b.ZOrder), block.ZOrder);

        harness.Session.Undo();
        Assert.Equal(storiesBefore, harness.Session.Document.Stories.Count);
        Assert.Equal(blocksBefore, harness.Session.Document.Pages[0].Blocks.Count);
        Assert.Null(harness.Frames.SelectedBlockId);
    }

    [Fact]
    public void DeleteFrameRemovesItsStoryAndUndoBringsBothBack()
    {
        using var harness = new EditorTestHarness(Prose(3));
        string id = harness.Frames.AddTextFrame(0);
        var added = (TextBlock)harness.Session.Document.FindBlock(id).Block;
        string storyId = added.StoryRef;

        Assert.True(harness.Frames.DeleteSelected());
        Assert.DoesNotContain(harness.Session.Document.Stories, s => s.Id == storyId);
        Assert.DoesNotContain(harness.Session.Document.Pages[0].Blocks, b => b.Id == id);
        Assert.Equal("Delete frame", harness.Session.UndoDescription);

        harness.Session.Undo();
        Assert.Contains(harness.Session.Document.Stories, s => s.Id == storyId);
        Assert.Contains(harness.Session.Document.Pages[0].Blocks, b => b.Id == id);
    }

    [Fact]
    public void ToggleWrapChangesHowTextFlows()
    {
        using var harness = new EditorTestHarness(Prose(10));
        int wrapped = VisualLineCount(harness);

        harness.Frames.Select(ImageId);
        Assert.True(harness.Frames.ToggleWrap());
        Assert.Equal(WrapMode.None, harness.Session.Document.FindBlock(ImageId).Block.WrapMode);
        int unwrapped = VisualLineCount(harness);
        Assert.NotEqual(wrapped, unwrapped);

        harness.Frames.ToggleWrap();
        Block block = harness.Session.Document.FindBlock(ImageId).Block;
        Assert.Equal(WrapMode.Rectangle, block.WrapMode);
        Assert.Equal(8f, block.WrapMarginPt, 3);
        Assert.Equal(wrapped, VisualLineCount(harness));
    }

    // ---- Z-order -----------------------------------------------------------------------------

    [Fact]
    public void SendBackwardBelowTheTextFrameStopsTheWrap()
    {
        using var harness = new EditorTestHarness(Prose(10));
        int wrapped = VisualLineCount(harness);

        harness.Frames.Select(ImageId);
        Assert.True(harness.Frames.SendBackward());

        // Dense renumbering: photo 0, text 1 — and a block below the text no longer excludes it.
        Assert.Equal(0, harness.Session.Document.FindBlock(ImageId).Block.ZOrder);
        Assert.Equal(1, harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block.ZOrder);
        Assert.NotEqual(wrapped, VisualLineCount(harness));
        Assert.Equal("Send backward", harness.Session.UndoDescription);

        harness.Session.Undo();
        Assert.Equal(wrapped, VisualLineCount(harness));
    }

    [Fact]
    public void BringForwardAtTheTopIsANoOp()
    {
        using var harness = new EditorTestHarness(Prose(3));
        harness.Frames.Select(ImageId);
        Assert.False(harness.Frames.BringForward());
        Assert.False(harness.Session.CanUndo);
    }

    // ---- Linking and overset -----------------------------------------------------------------

    [Fact]
    public void LinkingASecondFrameClearsTheOversetAndFlowsTheStory()
    {
        // A short frame with far too much text: the story is overset before linking.
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        Assert.True(harness.Source.IsOverset);
        Assert.Contains(EditorTestHarness.BlockId, harness.Source.GetOversetTailBlockIds());

        harness.Frames.Select(EditorTestHarness.BlockId);
        Assert.Equal(
            "There is more writing than fits — 'Make the rest fit' (Ctrl+Shift+M) will flow it, "
            + "or make this frame bigger.",
            harness.Frames.StatusMessage);

        string target = harness.Frames.AddTextFrame(0);
        harness.Session.Execute(new Core.Commands.ResizeBlockCommand(
            target, new RectPt(54f, 200f, 400f, 500f)));

        harness.Frames.Select(EditorTestHarness.BlockId);
        Assert.True(harness.Frames.BeginLink());
        Assert.Contains(target, harness.Frames.GetLinkCandidates(0));
        Assert.True(harness.Frames.CompleteLink(target));

        Assert.False(harness.Frames.IsLinkModeActive);
        Assert.Equal(
            EditorTestHarness.StoryId,
            ((TextBlock)harness.Session.Document.FindBlock(target).Block).StoryRef);
        Assert.True(harness.Source.TryGetStoryGeometry(EditorTestHarness.StoryId, out StoryTextGeometry geometry));
        Assert.Equal(2, geometry.FrameCount);
        Assert.False(harness.Source.IsOverset);

        // The linked frame's now-orphaned empty story went away with it, in one undo step.
        Assert.Single(harness.Session.Document.Stories);
        harness.Session.Undo();
        Assert.Equal(2, harness.Session.Document.Stories.Count);
        Assert.True(harness.Source.IsOverset);
    }

    [Fact]
    public void LinkingRefusesTargetsThatWouldLoseTextOrBreakTheChain()
    {
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        string first = harness.Frames.AddTextFrame(0);
        string second = harness.Frames.AddTextFrame(0);
        harness.Controller.TryBeginAt(0, 0f, 0f);

        // Put text in the second frame's story so linking into it would strand that text.
        var secondBlock = (TextBlock)harness.Session.Document.FindBlock(second).Block;
        harness.Session.Execute(new Core.Commands.InsertTextCommand(secondBlock.StoryRef, 0, 0, "Notice"));

        harness.Frames.Select(first);
        harness.Frames.BeginLink();
        Assert.False(harness.Frames.CompleteLink(second));
        Assert.Equal(
            "That frame already has text in it. Empty it first, or pick another frame.",
            harness.Frames.StatusMessage);
        Assert.DoesNotContain(second, harness.Frames.GetLinkCandidates(0));

        // Self-link and non-text targets are refused too.
        Assert.False(harness.Frames.CompleteLink(first));
        Assert.Equal("A frame cannot continue into itself.", harness.Frames.StatusMessage);
    }

    [Fact]
    public void LinkingRefusesAFrameThatAlreadyContinuesAnotherAndRefusesLoops()
    {
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        string middle = harness.Frames.AddTextFrame(0);
        string tail = harness.Frames.AddTextFrame(0);

        harness.Frames.Select(EditorTestHarness.BlockId);
        harness.Frames.BeginLink();
        Assert.True(harness.Frames.CompleteLink(middle));

        // `middle` now has an inbound link; nothing else may point at it.
        harness.Frames.Select(tail);
        harness.Frames.BeginLink();
        Assert.False(harness.Frames.CompleteLink(middle));
        Assert.Equal("That frame already continues another frame.", harness.Frames.StatusMessage);

        // And the head may not be linked back into from its own chain.
        harness.Frames.Select(middle);
        harness.Frames.BeginLink();
        Assert.False(harness.Frames.CompleteLink(EditorTestHarness.BlockId));
        Assert.Equal("Those frames would form a loop.", harness.Frames.StatusMessage);
    }

    [Fact]
    public void KeyboardLinkCursorPicksATargetWithoutDisarmingLinkMode()
    {
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        string first = harness.Frames.AddTextFrame(0);
        string second = harness.Frames.AddTextFrame(0);

        harness.Frames.Select(EditorTestHarness.BlockId);
        Assert.True(harness.Frames.BeginLink());

        // Tab moves the link cursor over the candidates; the SELECTION stays on the source frame,
        // which is what keeps link mode armed.
        Assert.True(harness.Frames.CycleLinkTarget(0, forward: true));
        Assert.Equal(first, harness.Frames.LinkTargetBlockId);
        Assert.True(harness.Frames.CycleLinkTarget(0, forward: true));
        Assert.Equal(second, harness.Frames.LinkTargetBlockId);
        Assert.True(harness.Frames.CycleLinkTarget(0, forward: false));
        Assert.Equal(first, harness.Frames.LinkTargetBlockId);
        Assert.Equal(EditorTestHarness.BlockId, harness.Frames.SelectedBlockId);
        Assert.True(harness.Frames.IsLinkModeActive);

        Assert.True(harness.Frames.CompleteLinkAtTarget());
        Assert.Equal(
            first,
            ((TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block).LinkNext);
        Assert.False(harness.Frames.IsLinkModeActive);
        Assert.Null(harness.Frames.LinkTargetBlockId);
    }

    [Fact]
    public void UnlinkGivesTheDetachedFrameItsOwnEmptyStory()
    {
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        string target = harness.Frames.AddTextFrame(0);
        harness.Frames.Select(EditorTestHarness.BlockId);
        harness.Frames.BeginLink();
        harness.Frames.CompleteLink(target);

        Assert.True(harness.Frames.Unlink());

        var head = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        var detached = (TextBlock)harness.Session.Document.FindBlock(target).Block;
        Assert.Null(head.LinkNext);
        Assert.NotEqual(head.StoryRef, detached.StoryRef);
        Assert.Equal(2, harness.Session.Document.Stories.Count);

        // One head per story again — the adapter lays out two independent chains.
        Assert.True(harness.Source.TryGetStoryGeometry(head.StoryRef, out StoryTextGeometry headGeometry));
        Assert.Equal(1, headGeometry.FrameCount);
        Assert.True(harness.Source.TryGetStoryGeometry(detached.StoryRef, out _));
    }

    [Fact]
    public void DeletingAChainedFrameDetachesItFirst()
    {
        using var harness = new EditorTestHarness(Prose(12), frameHeightPt: 90, withExclusion: false);
        string target = harness.Frames.AddTextFrame(0);
        harness.Frames.Select(EditorTestHarness.BlockId);
        harness.Frames.BeginLink();
        harness.Frames.CompleteLink(target);

        harness.Frames.Select(target);
        Assert.True(harness.Frames.DeleteSelected());

        var head = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        Assert.Null(head.LinkNext);
        Assert.True(harness.Source.TryGetStoryGeometry(head.StoryRef, out StoryTextGeometry geometry));
        Assert.Equal(1, geometry.FrameCount);
    }

    [Fact]
    public void SelectionClearsWhenTheBlockDisappearsUnderneathIt()
    {
        using var harness = new EditorTestHarness(Prose(3));
        string id = harness.Frames.AddTextFrame(0);
        Assert.Equal(id, harness.Frames.SelectedBlockId);

        harness.Session.Undo();
        Assert.Null(harness.Frames.SelectedBlockId);
        Assert.False(harness.Frames.HasSelection);
    }

    /// <summary>
    /// Review §14.2: deleting a frame out of the MIDDLE of a chain heals it across the gap.
    ///
    /// The predecessor's link used to be nulled unconditionally, which left A and C both pointing
    /// at nothing while both still referenced the same story — two heads on one story, so the
    /// article was drawn twice, from its first paragraph, in two places. It also disabled the
    /// story-cleanup guard for that story ever afterwards, since two blocks genuinely did use it.
    /// </summary>
    [Fact]
    public void DeletingTheMiddleOfAChainJoinsTheTwoEndsRatherThanSplittingTheStory()
    {
        using var harness = new EditorTestHarness(Prose(18), frameHeightPt: 90, withExclusion: false);
        string middle = harness.Frames.AddTextFrame(0);
        string tail = harness.Frames.AddTextFrame(0);

        harness.Frames.Select(EditorTestHarness.BlockId);
        harness.Frames.BeginLink();
        Assert.True(harness.Frames.CompleteLink(middle));
        harness.Frames.Select(middle);
        harness.Frames.BeginLink();
        Assert.True(harness.Frames.CompleteLink(tail));

        var headBefore = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        string storyId = headBefore.StoryRef;
        Assert.Equal(middle, headBefore.LinkNext);

        harness.Frames.Select(middle);
        Assert.True(harness.Frames.DeleteSelected());

        // A→C: one chain, still one story, and every frame in it is reached from the one head.
        var head = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        var end = (TextBlock)harness.Session.Document.FindBlock(tail).Block;
        Assert.Equal(tail, head.LinkNext);
        Assert.Null(end.LinkNext);
        Assert.Equal(storyId, end.StoryRef);

        // The decisive one: nothing else points into the chain, so the story has exactly one head.
        List<TextBlock> heads = harness.Session.Document.Pages
            .SelectMany(p => p.Blocks)
            .OfType<TextBlock>()
            .Where(b => b.StoryRef == storyId)
            .Where(b => !harness.Session.Document.Pages
                .SelectMany(p => p.Blocks)
                .OfType<TextBlock>()
                .Any(other => other.LinkNext == b.Id))
            .ToList();
        Assert.Single(heads);
        Assert.Equal(EditorTestHarness.BlockId, heads[0].Id);

        // And the article is laid out once, across the two surviving frames.
        Assert.True(harness.Source.TryGetStoryGeometry(storyId, out StoryTextGeometry geometry));
        Assert.Equal(2, geometry.FrameCount);

        // Undo puts the middle frame, and the chain, back exactly as they were.
        harness.Session.Undo();
        var headAgain = (TextBlock)harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block;
        var middleAgain = (TextBlock)harness.Session.Document.FindBlock(middle).Block;
        Assert.Equal(middle, headAgain.LinkNext);
        Assert.Equal(tail, middleAgain.LinkNext);
    }
}
