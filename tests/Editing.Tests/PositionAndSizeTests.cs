using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M94: typing exactly where a thing goes and how big it is.
///
/// <para>Until this, geometry could be set only by dragging with the mouse or pressing an arrow key
/// a point at a time — and a long, precise drag is the fine-motor task PLAN.md §6 exists to avoid.
/// Somebody with a tremor could not put a box where last month's issue had it.</para>
/// </summary>
public sealed class PositionAndSizeTests
{
    private static RectPt RectOf(EditorTestHarness harness, string id) =>
        harness.Session.Document.FindBlock(id).Block.FrameRect;

    /// <summary>The numbers typed are the numbers the frame ends up with.</summary>
    [Fact]
    public void TheFrameEndsUpExactlyWhereItWasAsked()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        var wanted = new RectPt(72f, 144f, 216f, 108f);
        Assert.True(harness.Frames.SetSelectionGeometry(wanted));

        RectPt got = RectOf(harness, EditorTestHarness.BlockId);
        Assert.Equal(wanted.X, got.X, 3);
        Assert.Equal(wanted.Y, got.Y, 3);
        Assert.Equal(wanted.Width, got.Width, 3);
        Assert.Equal(wanted.Height, got.Height, 3);
    }

    /// <summary>
    /// Moving and resizing at once is ONE undo step. Two would mean pressing Ctrl+Z twice to undo
    /// one thing the user did.
    /// </summary>
    [Fact]
    public void MovingAndResizingAtOnceIsOneUndoStep()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);
        RectPt before = RectOf(harness, EditorTestHarness.BlockId);

        Assert.True(harness.Frames.SetSelectionGeometry(new RectPt(90f, 180f, 300f, 200f)));
        harness.Session.Undo();

        RectPt got = RectOf(harness, EditorTestHarness.BlockId);
        Assert.Equal(before.X, got.X, 3);
        Assert.Equal(before.Y, got.Y, 3);
        Assert.Equal(before.Width, got.Width, 3);
        Assert.Equal(before.Height, got.Height, 3);
    }

    /// <summary>
    /// A typed number can leave the paper in a way a drag never can, so it is clamped back on.
    /// A frame the user cannot see is a change that appears not to have happened.
    /// </summary>
    [Fact]
    public void ANumberThatWouldLeaveThePaperIsBroughtBackOntoIt()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        Assert.True(harness.Frames.SetSelectionGeometry(new RectPt(5000f, 5000f, 200f, 100f)));

        RectPt got = RectOf(harness, EditorTestHarness.BlockId);
        PageMaster master = harness.Session.Document.GetMaster(
            harness.Session.Document.Pages[0].MasterRef);

        Assert.True(got.X + got.Width <= master.Size.Width + 0.001f, $"off the right: {got.X}");
        Assert.True(got.Y + got.Height <= master.Size.Height + 0.001f, $"off the bottom: {got.Y}");
    }

    /// <summary>Typing the numbers it already has changes nothing and says so.</summary>
    [Fact]
    public void TypingTheNumbersItAlreadyHasChangesNothing()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        RectPt current = RectOf(harness, EditorTestHarness.BlockId);
        Assert.False(harness.Frames.SetSelectionGeometry(current));
    }

    /// <summary>
    /// A frame kept in place refuses, with the same sentence the drag gives — two ways in, one rule
    /// (M28). A second route that quietly ignored the lock would make the lock worthless.
    /// </summary>
    [Fact]
    public void AFrameKeptInPlaceRefusesAndSaysWhy()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);
        Assert.True(harness.Frames.ToggleLocked());

        RectPt before = RectOf(harness, EditorTestHarness.BlockId);
        Assert.False(harness.Frames.SetSelectionGeometry(new RectPt(90f, 180f, 300f, 200f)));

        Assert.Equal(before.X, RectOf(harness, EditorTestHarness.BlockId).X, 3);
        Assert.NotNull(harness.Frames.StatusMessage);
    }

    /// <summary>Nothing chosen, nothing done, and the caller can tell.</summary>
    [Fact]
    public void WithNothingChosenItRefuses()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.ClearSelection();

        Assert.False(harness.Frames.SetSelectionGeometry(new RectPt(10f, 10f, 100f, 100f)));
    }
}
