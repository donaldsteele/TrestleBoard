using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M104: what colour the emblem is.
///
/// <para><see cref="VectorBlock.InkArgb"/> has been on the block since M65, is copied by
/// <c>BlockCopier</c>, and is what the renderer paints every part with — and the only thing that
/// ever set it was the moment of insertion. Built, working, and unreachable.</para>
/// </summary>
public sealed class EmblemColourTests
{
    private const uint Navy = PageLooks.LodgeInkArgb;

    private static string AddEmblem(EditorTestHarness harness, uint ink = VectorBlock.DefaultInkArgb)
    {
        harness.Session.Document.Pages[0].Blocks.Add(new VectorBlock
        {
            Id = "drawing-1",
            FrameRect = new RectPt(72, 72, 96, 96),
            ViewBoxWidth = 100,
            ViewBoxHeight = 100,
            Parts = [new VectorPart { PathData = "M10,10 L90,90", StrokeWidth = 2 }],
            AltText = "A placeholder drawing.",
            InkArgb = ink,
        });

        return "drawing-1";
    }

    private static VectorBlock Drawing(EditorTestHarness harness) =>
        (VectorBlock)harness.Session.Document.FindBlock("drawing-1").Block;

    [Fact]
    public void TheEmblemArrivesBlackAndCanBeMadeAnotherColour()
    {
        using var harness = new EditorTestHarness("An article.");
        FrameEditorController frames = harness.Frames;
        frames.Select(AddEmblem(harness));

        Assert.Equal(VectorBlock.DefaultInkArgb, frames.SelectionEmblemInk);
        Assert.True(frames.SetSelectionEmblemInk(Navy));
        Assert.Equal(Navy, Drawing(harness).InkArgb);
    }

    /// <summary>One Ctrl+Z puts the colour back — it is a document change like any other.</summary>
    [Fact]
    public void TakingItBackPutsTheOldColourBack()
    {
        using var harness = new EditorTestHarness("An article.");
        harness.Frames.Select(AddEmblem(harness));
        harness.Frames.SetSelectionEmblemInk(Navy);

        harness.Session.Undo();

        Assert.Equal(VectorBlock.DefaultInkArgb, Drawing(harness).InkArgb);
    }

    /// <summary>
    /// Two recolourings are two undo steps. Merging them would mean one Ctrl+Z jumping past a
    /// colour the user chose and looked at.
    /// </summary>
    [Fact]
    public void TwoRecolouringsAreTwoUndoSteps()
    {
        using var harness = new EditorTestHarness("An article.");
        harness.Frames.Select(AddEmblem(harness));

        Assert.True(harness.Frames.SetSelectionEmblemInk(Navy));
        Assert.True(harness.Frames.SetSelectionEmblemInk(0xFF8C2A2Au));

        harness.Session.Undo();
        Assert.Equal(Navy, Drawing(harness).InkArgb);

        harness.Session.Undo();
        Assert.Equal(VectorBlock.DefaultInkArgb, Drawing(harness).InkArgb);
    }

    /// <summary>Choosing the colour it already is changes nothing and says so.</summary>
    [Fact]
    public void ChoosingTheColourItAlreadyIsChangesNothing()
    {
        using var harness = new EditorTestHarness("An article.");
        harness.Frames.Select(AddEmblem(harness, Navy));

        Assert.False(harness.Frames.SetSelectionEmblemInk(Navy));
    }

    /// <summary>A frame of writing is not a drawing, and the controller refuses rather than guesses.</summary>
    [Fact]
    public void AFrameOfWritingHasNoInkColour()
    {
        using var harness = new EditorTestHarness("An article.");
        harness.Frames.Select(EditorTestHarness.BlockId);

        Assert.False(harness.Frames.SelectionIsAnEmblem);
        Assert.Null(harness.Frames.SelectionEmblemInk);
        Assert.False(harness.Frames.SetSelectionEmblemInk(Navy));
    }

    /// <summary>
    /// A box refuses TOWARDS the command that recolours a box. The two commands answer different
    /// questions, and somebody who picked the wrong one must not be left guessing which.
    /// </summary>
    [Fact]
    public void ABoxIsSentToTheCommandThatRecoloursABox()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.EmblemColour,
            Chosen(SelectionKind.Shape));

        Assert.False(availability.IsAvailable);
        Assert.Equal(ActionId.ShapeColours, availability.RemedyId);
    }

    /// <summary>And a drawing can actually reach it — the point of the milestone.</summary>
    [Fact]
    public void ADrawingCanReachIt()
    {
        Assert.True(
            ActionCatalog.Evaluate(ActionId.EmblemColour, Chosen(SelectionKind.Drawing)).IsAvailable);
    }

    private static ActionContext Chosen(SelectionKind kind) => new()
    {
        HasDocument = true,
        PageCount = 3,
        PageIndex = 0,
        IssueDateChosen = true,
        Selection = kind,
        SelectedBlockId = "b1",
    };
}
