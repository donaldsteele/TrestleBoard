using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M81: make another like this, and keep it where it is.
/// </summary>
public sealed class DuplicateAndLockTests
{
    /// <summary>
    /// <b>This is how a novice makes a second event card.</b> Copy has meant WORDS since M4, so the
    /// only way to a second announcement box was to run the wizard again and re-answer every
    /// question.
    /// </summary>
    [Fact]
    public void TheCopyLandsBelowAndIsTheOneNowChosen()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        string original = frames.AddTextFrame(0);
        frames.Select(original);
        RectPt from = harness.Session.Document.FindBlock(original).Block.FrameRect;

        string copy = Assert.IsType<string>(frames.DuplicateSelected());

        Assert.NotEqual(original, copy);
        Assert.Equal(copy, frames.SelectedBlockId);

        RectPt to = harness.Session.Document.FindBlock(copy).Block.FrameRect;
        Assert.True(to.Y > from.Y, "a copy on top of the original reads as nothing having happened");
        Assert.Equal(from.Width, to.Width, 3);
        Assert.Equal(from.Height, to.Height, 3);
    }

    /// <summary>
    /// A copy of a frame of writing gets a story of ITS OWN with the same words in it. Sharing the
    /// story is what a LINK is, and a copy is not a continuation — two blocks on one story would
    /// mean typing in one changed the other.
    /// </summary>
    [Fact]
    public void ACopyOfWritingGetsItsOwnStoryWithTheSameWords()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);

        string copyId = Assert.IsType<string>(frames.DuplicateSelected());

        Document document = harness.Session.Document;
        var original = (Core.Model.TextBlock)document.FindBlock(EditorTestHarness.BlockId).Block;
        var copy = (Core.Model.TextBlock)document.FindBlock(copyId).Block;

        Assert.NotEqual(original.StoryRef, copy.StoryRef);
        Assert.Equal(
            Text(document.GetStory(original.StoryRef)),
            Text(document.GetStory(copy.StoryRef)));

        // And a copy continues nothing, whatever the original did.
        Assert.Null(copy.LinkNext);
    }

    /// <summary>One command, one Ctrl+Z — the copy and its story go together.</summary>
    [Fact]
    public void OneUndoTakesTheWholeCopyBack()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        int storiesBefore = harness.Session.Document.Stories.Count;
        int blocksBefore = harness.Session.Document.Pages[0].Blocks.Count;

        frames.DuplicateSelected();
        harness.Session.Undo();

        Assert.Equal(storiesBefore, harness.Session.Document.Stories.Count);
        Assert.Equal(blocksBefore, harness.Session.Document.Pages[0].Blocks.Count);
    }

    /// <summary>
    /// A copy the user has just asked for is a copy they are about to move, so it never arrives
    /// pinned — that would refuse the very next thing they do.
    /// </summary>
    [Fact]
    public void ACopyOfALockedThingCanBeMoved()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        frames.ToggleLocked();
        Assert.True(frames.SelectionIsLocked);

        frames.DuplicateSelected();

        Assert.False(frames.SelectionIsLocked);
    }

    /// <summary>
    /// <b>A locked block refuses the gesture and SAYS SO.</b> A drag that silently does nothing is
    /// the M11 failure: the user tries harder, then decides the app is broken.
    /// </summary>
    [Fact]
    public void ALockedBlockRefusesToBeDraggedAndSaysWhy()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        RectPt before = harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block.FrameRect;
        frames.ToggleLocked();

        Assert.False(frames.BeginDrag(FrameHandle.Body, before.X + 5f, before.Y + 5f));
        Assert.False(frames.Nudge(1, 0));
        Assert.False(frames.NudgeResize(1, 0));

        Assert.Equal(
            before,
            harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block.FrameRect);
        Assert.Contains("kept in place", frames.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("Let it move", frames.StatusMessage!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Position and size ONLY. A locked block is still chosen, still typed into, still deleted —
    /// which is what stops the lock from being a mode the user has to be taught.
    /// </summary>
    [Fact]
    public void ALockedBlockCanStillBeChosenEditedAndDeleted()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        frames.ToggleLocked();

        // Still chosen, and choosing it again works.
        frames.Select(EditorTestHarness.BlockId);
        Assert.Equal(EditorTestHarness.BlockId, frames.SelectedBlockId);

        // Still deleted.
        Assert.True(frames.DeleteSelected());
        Assert.False(
            harness.Session.Document.TryFindBlock(EditorTestHarness.BlockId, out _, out _),
            "a locked block that cannot be deleted is a frame the user can never be rid of");
    }

    /// <summary>Letting it move again puts it back the way it was.</summary>
    [Fact]
    public void UnlockingLetsItMoveAgain()
    {
        using var harness = new EditorTestHarness("An article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);

        frames.ToggleLocked();
        Assert.True(frames.SelectionIsLocked);

        frames.ToggleLocked();
        Assert.False(frames.SelectionIsLocked);
        Assert.True(frames.Nudge(1, 0));
    }

    private static string Text(Story story) =>
        string.Concat(story.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));
}
