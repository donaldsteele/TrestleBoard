using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M92: choosing several things and having the commands mean it.
///
/// <para>The app has offered four ways to choose more than one thing since M21 — Shift+click, a
/// marquee drag, "Choose everything on this page" and "Also choose the next one" — and of the
/// commands that act on a selection, only lining up and spreading out ever used it. Everything else
/// read the primary alone: choosing five things and pressing Delete left four of them, and the
/// other four were not even drawn as chosen, so there was no way to see what the app thought was
/// selected.</para>
///
/// <para>Every test here is written against the WHOLE selection. Each fails if the command falls
/// back to the primary.</para>
/// </summary>
public sealed class MultiSelectionTests
{
    /// <summary>Three frames on page one, chosen together, with the first as the primary.</summary>
    private static (string A, string B, string C) ThreeChosen(EditorTestHarness harness)
    {
        FrameEditorController frames = harness.Frames;
        string b = frames.AddTextFrame(0);
        string c = frames.AddTextFrame(0);
        frames.SelectAll([EditorTestHarness.BlockId, b, c]);

        Assert.Equal(3, frames.SelectionCount);
        return (EditorTestHarness.BlockId, b, c);
    }

    private static Block BlockOf(EditorTestHarness harness, string id) =>
        harness.Session.Document.FindBlock(id).Block;

    // ---- the commands ----------------------------------------------------------------------

    /// <summary>
    /// Choosing five things and pressing Delete used to leave four of them. One undo step brings
    /// them all back.
    /// </summary>
    [Fact]
    public void DeletingRemovesEverythingChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        Assert.True(harness.Frames.DeleteSelected());

        List<string> left = [.. harness.Session.Document.Pages[0].Blocks.Select(x => x.Id)];
        Assert.DoesNotContain(a, left);
        Assert.DoesNotContain(b, left);
        Assert.DoesNotContain(c, left);

        harness.Session.Undo();
        Assert.Equal(3, harness.Session.Document.Pages[0].Blocks.Count);
    }

    /// <summary>"Make another like this" makes another of each, as one undo step.</summary>
    [Fact]
    public void DuplicatingCopiesEverythingChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        ThreeChosen(harness);

        Assert.NotNull(harness.Frames.DuplicateSelected());
        Assert.Equal(6, harness.Session.Document.Pages[0].Blocks.Count);

        // And the copies are what is now chosen, so the next keystroke moves them.
        Assert.Equal(3, harness.Frames.SelectionCount);

        harness.Session.Undo();
        Assert.Equal(3, harness.Session.Document.Pages[0].Blocks.Count);
    }

    /// <summary>An arrow key moves the whole selection, together, in one step.</summary>
    [Fact]
    public void NudgingMovesEverythingChosenByTheSameAmount()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        RectPt beforeB = BlockOf(harness, b).FrameRect;
        RectPt beforeC = BlockOf(harness, c).FrameRect;

        Assert.True(harness.Frames.Nudge(1f, 0f));

        Assert.Equal(beforeB.X + FrameEditorController.NudgeStepPt, BlockOf(harness, b).FrameRect.X, 3);
        Assert.Equal(beforeC.X + FrameEditorController.NudgeStepPt, BlockOf(harness, c).FrameRect.X, 3);

        harness.Session.Undo();
        Assert.Equal(beforeB.X, BlockOf(harness, b).FrameRect.X, 3);
        Assert.Equal(beforeC.X, BlockOf(harness, c).FrameRect.X, 3);
        Assert.NotEqual(string.Empty, a);
    }

    /// <summary>
    /// A frame kept in place stays put while the rest of the selection moves. The point of pinning
    /// one thing down is that it does not stop everything else working.
    /// </summary>
    [Fact]
    public void AFrameKeptInPlaceStaysPutWhileTheOthersMove()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        harness.Frames.Select(c);
        Assert.True(harness.Frames.ToggleLocked());
        Assert.True(BlockOf(harness, c).Locked);

        harness.Frames.SelectAll([a, b, c]);
        RectPt pinned = BlockOf(harness, c).FrameRect;
        RectPt free = BlockOf(harness, b).FrameRect;

        Assert.True(harness.Frames.Nudge(0f, 1f));

        Assert.Equal(pinned.Y, BlockOf(harness, c).FrameRect.Y, 3);
        Assert.Equal(free.Y + FrameEditorController.NudgeStepPt, BlockOf(harness, b).FrameRect.Y, 3);
    }

    /// <summary>
    /// The primary decides which way a toggle goes and everything follows it, so a mixed selection
    /// ends up agreeing. Each flipping to its own opposite would need pressing twice to mean
    /// anything.
    /// </summary>
    [Fact]
    public void ATogglePutsTheWholeSelectionTheSameWay()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        // Put one of them out of step first.
        harness.Frames.Select(b);
        Assert.True(harness.Frames.ToggleLocked());
        Assert.True(BlockOf(harness, b).Locked);

        harness.Frames.SelectAll([a, b, c]);
        Assert.True(harness.Frames.ToggleLocked());

        Assert.True(BlockOf(harness, a).Locked);
        Assert.True(BlockOf(harness, b).Locked);
        Assert.True(BlockOf(harness, c).Locked);
    }

    /// <summary>A border goes round every chosen box, not just the last one clicked.</summary>
    [Fact]
    public void ABorderGoesRoundEverythingChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        Assert.True(harness.Frames.ToggleBorder());

        Assert.True(PageLooks.HasBorder(BlockOf(harness, a).FrameStyleRef));
        Assert.True(PageLooks.HasBorder(BlockOf(harness, b).FrameStyleRef));
        Assert.True(PageLooks.HasBorder(BlockOf(harness, c).FrameStyleRef));
    }

    /// <summary>Text flows around everything chosen, in one undo step.</summary>
    [Fact]
    public void WrapAppliesToEverythingChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, string c) = ThreeChosen(harness);

        Assert.True(harness.Frames.ToggleWrap());

        Assert.Equal(WrapMode.Rectangle, BlockOf(harness, a).WrapMode);
        Assert.Equal(WrapMode.Rectangle, BlockOf(harness, b).WrapMode);
        Assert.Equal(WrapMode.Rectangle, BlockOf(harness, c).WrapMode);

        harness.Session.Undo();
        Assert.Equal(WrapMode.None, BlockOf(harness, b).WrapMode);
    }

    /// <summary>
    /// A group sent to the back arrives at the back KEEPING its own order. Restacking each chosen
    /// block in turn would shuffle them against one another, so two frames sent back together would
    /// come out in the opposite order from the one on screen.
    /// </summary>
    [Fact]
    public void SendingSeveralToTheBackKeepsTheirOrderAmongThemselves()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        FrameEditorController frames = harness.Frames;

        string b = frames.AddTextFrame(0);
        string c = frames.AddTextFrame(0);
        string d = frames.AddTextFrame(0);

        // b sits below c in the stack; both must still be that way round at the back.
        frames.SelectAll([b, c]);
        Assert.True(frames.SendToBack());

        List<string> order = [.. harness.Session.Document.Pages[0].Blocks
            .OrderBy(x => x.ZOrder)
            .Select(x => x.Id)];

        Assert.Equal(0, order.IndexOf(b));
        Assert.Equal(1, order.IndexOf(c));
        Assert.True(order.IndexOf(d) > order.IndexOf(c));
        Assert.True(order.IndexOf(EditorTestHarness.BlockId) > order.IndexOf(c));
    }

    // ---- being able to SEE what is chosen ----------------------------------------------------

    /// <summary>
    /// Every chosen thing is drawn as chosen. Before this the overlay carried one rect, so the app
    /// could not show what it thought was selected — which matters most for lining up, the one
    /// command that has always needed several.
    /// </summary>
    [Fact]
    public void EveryChosenThingIsDrawnAsChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        ThreeChosen(harness);

        Rendering.FrameOverlay overlay = harness.Frames.BuildOverlay(0);

        Assert.NotNull(overlay.SelectedRect);
        Assert.NotNull(overlay.AlsoSelectedRects);
        Assert.Equal(2, overlay.AlsoSelectedRects!.Count);
    }

    /// <summary>One thing chosen draws one outline and nothing else — the M16 baselines depend on it.</summary>
    [Fact]
    public void ChoosingOneThingDrawsExactlyWhatItAlwaysDid()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        Rendering.FrameOverlay overlay = harness.Frames.BuildOverlay(0);

        Assert.NotNull(overlay.SelectedRect);
        Assert.Empty(overlay.AlsoSelectedRects ?? []);
    }

    // ---- what the commands CALL themselves ---------------------------------------------------

    /// <summary>
    /// With several things chosen, a command that acts on all of them says so. A label promising
    /// less than the command does is the M55 defect, and doing MORE than you said is as alarming to
    /// this audience as doing less.
    /// </summary>
    [Theory]
    [InlineData(Actions.ActionId.Duplicate, "Make another of each")]
    [InlineData(Actions.ActionId.DeleteFrame, "Delete these")]
    [InlineData(Actions.ActionId.ToggleLocked, "Keep them where they are")]
    [InlineData(Actions.ActionId.ToggleBorder, "Put a border round them")]
    [InlineData(Actions.ActionId.MoveToNextPage, "Move them to the next page")]
    public void ACommandActingOnSeveralThingsSaysSo(string actionId, string expected)
    {
        var several = new Actions.ActionContext
        {
            HasDocument = true,
            Selection = Actions.SelectionKind.TextFrame,
            SelectionCount = 3,
        };

        Assert.Equal(expected, Actions.ActionCatalog.TitleFor(actionId, several));
    }

    /// <summary>
    /// One thing chosen keeps the wording it has always had — the M18 rule, so a surface that has
    /// not been taught about multi-selection still reads correctly.
    /// </summary>
    [Fact]
    public void OneThingChosenKeepsTheWordingItAlwaysHad()
    {
        var one = new Actions.ActionContext
        {
            HasDocument = true,
            Selection = Actions.SelectionKind.TextFrame,
            SelectionCount = 1,
        };

        Assert.Equal(
            Actions.ActionCatalog.Get(Actions.ActionId.Duplicate).Title,
            Actions.ActionCatalog.TitleFor(Actions.ActionId.Duplicate, one));
    }

    // ---- dragging ----------------------------------------------------------------------------

    /// <summary>
    /// Dragging one of several chosen frames takes them all, and one Ctrl+Z puts them all back.
    /// Having to press it once per frame is not undoing what the user did.
    /// </summary>
    [Fact]
    public void DraggingOneOfThemTakesThemAllAndUndoesInOneStep()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, _) = ThreeChosen(harness);

        RectPt startA = BlockOf(harness, a).FrameRect;
        RectPt startB = BlockOf(harness, b).FrameRect;

        Assert.True(harness.Frames.BeginDrag(
            Layout.Editing.FrameHandle.Body, startA.X + 5f, startA.Y + 5f));
        harness.Frames.DragTo(startA.X + 45f, startA.Y + 5f, snap: false);
        harness.Frames.EndDrag(commit: true);

        float movedA = BlockOf(harness, a).FrameRect.X - startA.X;
        float movedB = BlockOf(harness, b).FrameRect.X - startB.X;
        Assert.True(movedA > 1f, $"the dragged frame barely moved: {movedA}");
        Assert.Equal(movedA, movedB, 3);

        harness.Session.Undo();
        Assert.Equal(startA.X, BlockOf(harness, a).FrameRect.X, 3);
        Assert.Equal(startB.X, BlockOf(harness, b).FrameRect.X, 3);
    }

    /// <summary>
    /// A resize handle is an edge of ONE frame, so it resizes that frame alone. There is no sense
    /// in which several frames share a corner.
    /// </summary>
    [Fact]
    public void ResizingByAHandleTouchesOnlyTheFrameWhoseHandleItIs()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        (string a, string b, _) = ThreeChosen(harness);

        RectPt startA = BlockOf(harness, a).FrameRect;
        RectPt startB = BlockOf(harness, b).FrameRect;

        Assert.True(harness.Frames.BeginDrag(
            Layout.Editing.FrameHandle.BottomRight, startA.Right, startA.Bottom));
        harness.Frames.DragTo(startA.Right + 30f, startA.Bottom + 30f, snap: false);
        harness.Frames.EndDrag(commit: true);

        Assert.NotEqual(startA.Width, BlockOf(harness, a).FrameRect.Width, 3);
        Assert.Equal(startB.Width, BlockOf(harness, b).FrameRect.Width, 3);
        Assert.Equal(startB.X, BlockOf(harness, b).FrameRect.X, 3);
    }
}
