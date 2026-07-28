using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Lining things up (PLAN.md §11 M21, §12 gate 18). Two halves: the arithmetic, which is pure, and
/// the promise that however many frames it moves it is <b>one undo step</b> — the criterion the
/// milestone is graded on.
/// </summary>
public sealed class FrameAlignmentTests
{
    private static readonly RectPt[] Three =
    [
        new(10f, 10f, 100f, 50f),
        new(40f, 100f, 60f, 20f),
        new(200f, 300f, 80f, 40f),
    ];

    [Fact]
    public void LiningUpTheLeftEdgesUsesTheLeftmostEdgeAlreadyChosen()
    {
        IReadOnlyList<RectPt> moved = FrameAlignment.Align(Three, FrameAlignmentKind.Left);

        Assert.All(moved, r => Assert.Equal(10f, r.X, 3));

        // Nothing is resized and nothing moves the other way.
        Assert.Equal(Three.Select(r => r.Width), moved.Select(r => r.Width));
        Assert.Equal(Three.Select(r => r.Y), moved.Select(r => r.Y));
    }

    [Fact]
    public void LiningUpTheRightEdgesPutsTheirRightSidesTogether()
    {
        IReadOnlyList<RectPt> moved = FrameAlignment.Align(Three, FrameAlignmentKind.Right);

        Assert.All(moved, r => Assert.Equal(280f, r.X + r.Width, 3));
    }

    [Fact]
    public void LiningUpTheCentresUsesTheMiddleOfWhatIsChosen()
    {
        IReadOnlyList<RectPt> moved = FrameAlignment.Align(Three, FrameAlignmentKind.CentreX);

        // The chosen frames span 10 → 280, so their middle is 145.
        Assert.All(moved, r => Assert.Equal(145f, r.X + (r.Width / 2f), 3));
    }

    /// <summary>
    /// Idempotence, which is what "align to the selection's own bounds" buys: pressing the same
    /// command twice moves nothing the second time, so a shaky hand costs nothing.
    /// </summary>
    [Fact]
    public void LiningUpTwiceChangesNothingTheSecondTime()
    {
        IReadOnlyList<RectPt> once = FrameAlignment.Align(Three, FrameAlignmentKind.Top);
        IReadOnlyList<RectPt> twice = FrameAlignment.Align(once, FrameAlignmentKind.Top);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void SpacingOutEvenlyLeavesTheEndsWhereTheyAreAndEqualisesTheGaps()
    {
        RectPt[] rects =
        [
            new(0f, 0f, 100f, 10f),
            new(120f, 0f, 20f, 10f),
            new(400f, 0f, 100f, 10f),
        ];

        IReadOnlyList<RectPt> moved = FrameAlignment.Distribute(rects, horizontal: true);

        Assert.Equal(0f, moved[0].X, 3);
        Assert.Equal(400f, moved[2].X, 3);

        float firstGap = moved[1].X - (moved[0].X + moved[0].Width);
        float secondGap = moved[2].X - (moved[1].X + moved[1].Width);
        Assert.Equal(firstGap, secondGap, 3);
    }

    /// <summary>The answer must line up with the ids it was asked about, whatever order they came in.</summary>
    [Fact]
    public void SpacingOutKeepsTheCallersOrderEvenWhenTheFramesAreOutOfOrder()
    {
        RectPt[] rects =
        [
            new(400f, 0f, 100f, 10f),
            new(0f, 0f, 100f, 10f),
            new(120f, 0f, 20f, 10f),
        ];

        IReadOnlyList<RectPt> moved = FrameAlignment.Distribute(rects, horizontal: true);

        Assert.Equal(400f, moved[0].X, 3);
        Assert.Equal(0f, moved[1].X, 3);
    }

    [Fact]
    public void SpacingOutTwoThingsDoesNothing()
    {
        RectPt[] two = [Three[0], Three[1]];
        Assert.Equal(two, FrameAlignment.Distribute(two, horizontal: true));
    }

    // ---- Through the controller, which is where the undo step is decided --------------------

    /// <summary>
    /// <b>M21's acceptance criterion.</b> Three frames lined up is ONE undo step, and undoing it
    /// puts all three back exactly where they were.
    /// </summary>
    [Fact]
    public void LiningUpThreeFramesIsOneUndoStep()
    {
        using var harness = new EditorTestHarness("Notice of the stated communication.");
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        string c = frames.AddTextFrame(0);
        MoveTo(harness, a, 10f, 10f);
        MoveTo(harness, b, 60f, 120f);
        MoveTo(harness, c, 120f, 240f);

        frames.Select(a);
        Assert.True(frames.AddToSelection(b));
        Assert.True(frames.AddToSelection(c));
        Assert.Equal(3, frames.SelectionCount);

        Assert.True(frames.Align(FrameAlignmentKind.Left));
        Assert.All([a, b, c], id => Assert.Equal(10f, RectOf(harness, id).X, 3));

        // One step, named in the user's words: this is what the Edit menu says after "Undo".
        Assert.Equal("Line up the left edges", harness.Session.UndoDescription);

        // And ONE Ctrl+Z puts all three back — the criterion, stated as an assertion.
        harness.Session.Undo();
        Assert.Equal(10f, RectOf(harness, a).X, 3);
        Assert.Equal(60f, RectOf(harness, b).X, 3);
        Assert.Equal(120f, RectOf(harness, c).X, 3);

        harness.Session.Redo();
        Assert.All([a, b, c], id => Assert.Equal(10f, RectOf(harness, id).X, 3));
    }

    /// <summary>Already lined up means nothing to take back — no empty step on the undo stack.</summary>
    [Fact]
    public void LiningUpSomethingAlreadyLinedUpPutsNothingOnTheUndoStack()
    {
        using var harness = new EditorTestHarness("Notice.");
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        MoveTo(harness, a, 10f, 10f);
        MoveTo(harness, b, 10f, 200f);

        frames.Select(a);
        frames.AddToSelection(b);

        string? topOfStack = harness.Session.UndoDescription;
        Assert.False(frames.Align(FrameAlignmentKind.Left));
        Assert.Equal(topOfStack, harness.Session.UndoDescription);
        Assert.Equal("They are already lined up.", frames.StatusMessage);
    }

    [Fact]
    public void OneThingChosenIsNotEnoughToLineAnythingUp()
    {
        using var harness = new EditorTestHarness("Notice.");
        string a = harness.Frames.AddTextFrame(0);
        harness.Frames.Select(a);

        Assert.False(harness.Frames.Align(FrameAlignmentKind.Left));
        Assert.False(harness.Frames.Distribute(horizontal: true));
    }

    /// <summary>Shift+click on something already chosen takes it out again.</summary>
    [Fact]
    public void ChoosingTheSameThingAgainTakesItOutOfTheSelection()
    {
        using var harness = new EditorTestHarness("Notice.");
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        frames.Select(a);
        frames.AddToSelection(b);
        Assert.Equal(2, frames.SelectionCount);

        frames.AddToSelection(b);
        Assert.Equal(1, frames.SelectionCount);
        Assert.Equal(a, frames.SelectedBlockId);

        // And un-choosing the last one leaves nothing chosen at all.
        frames.AddToSelection(a);
        Assert.Equal(0, frames.SelectionCount);
        Assert.Null(frames.SelectedBlockId);
    }

    /// <summary>Choosing an ordinary single thing forgets the rest, as every other program does.</summary>
    [Fact]
    public void AnOrdinaryClickForgetsTheOthers()
    {
        using var harness = new EditorTestHarness("Notice.");
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        frames.Select(a);
        frames.AddToSelection(b);

        frames.Select(a);
        Assert.Equal(1, frames.SelectionCount);
        Assert.Equal([a], frames.SelectedBlockIds);
    }

    /// <summary>
    /// A deleted block never stays in the set: asking the layout for the rectangle of something
    /// that is not there any more would throw in the middle of an align.
    /// </summary>
    [Fact]
    public void DeletingOneOfTheChosenThingsDropsItFromTheSelection()
    {
        using var harness = new EditorTestHarness("Notice.");
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        frames.Select(b);
        frames.DeleteSelected();

        frames.Select(a);
        Assert.Equal([a], frames.SelectedBlockIds);
        Assert.Equal(1, frames.SelectionCount);
    }

    private static RectPt RectOf(EditorTestHarness harness, string blockId) =>
        harness.Session.Document.FindBlock(blockId).Block.FrameRect;

    private static void MoveTo(EditorTestHarness harness, string blockId, float x, float y)
    {
        RectPt rect = RectOf(harness, blockId);
        harness.Session.Execute(new Core.Commands.MoveBlockCommand(
            blockId, rect with { X = x, Y = y }));
    }

}
