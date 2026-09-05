using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M95: a box to set something apart.
///
/// <para><see cref="ShapeKind.Box"/> has been in the model and drawn by the renderer since M2, and
/// the only thing in the whole app that ever made a <see cref="ShapeBlock"/> was the rule, which
/// hardcodes <see cref="ShapeKind.Rule"/>. A committee wanting a coloured panel had to shade a box
/// of writing instead and take whichever of the three fixed looks they were given.</para>
/// </summary>
public sealed class BoxTests
{
    private static ShapeBlock BoxOf(EditorTestHarness harness, string id) =>
        Assert.IsType<ShapeBlock>(harness.Session.Document.FindBlock(id).Block);

    /// <summary>A box arrives as a box, with the colours that were asked for.</summary>
    [Fact]
    public void ABoxArrivesWithTheColoursAskedFor()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        uint fill = PageLooks.BoxColours[0].Argb;
        uint outline = PageLooks.BoxColours[1].Argb;
        string id = harness.Frames.AddBox(0, fill, outline);

        ShapeBlock box = BoxOf(harness, id);
        Assert.Equal(ShapeKind.Box, box.Kind);
        Assert.Equal(fill, box.FillArgb);
        Assert.Equal(outline, box.StrokeArgb);
        Assert.True(box.StrokeWidthPt > 0f, "an outline that was asked for must have a width");
    }

    /// <summary>
    /// A panel is a thing other things sit on, so it lands BEHIND everything else. One that arrived
    /// in front would hide the notice it was drawn for.
    /// </summary>
    [Fact]
    public void ABoxLandsBehindEverythingElse()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: true);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        ShapeBlock box = BoxOf(harness, id);

        foreach (Block other in harness.Session.Document.Pages[0].Blocks)
        {
            if (other.Id != id)
            {
                Assert.True(box.ZOrder < other.ZOrder, $"{other.Id} is behind the box");
            }
        }
    }

    /// <summary>No outline asked for means no outline drawn — width zero, not a hairline.</summary>
    [Fact]
    public void ABoxWithNoOutlineHasNoWidthEither()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        ShapeBlock box = BoxOf(harness, id);

        Assert.Null(box.StrokeArgb);
        Assert.Equal(0f, box.StrokeWidthPt, 3);
    }

    /// <summary>The box is chosen once it is there, so the next keystroke moves it.</summary>
    [Fact]
    public void TheNewBoxIsWhatIsChosen()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        Assert.Equal(id, harness.Frames.SelectedBlockId);
        Assert.True(harness.Frames.SelectionIsAShape);
    }

    /// <summary>Recolouring reaches the block, and one Ctrl+Z puts all three fields back.</summary>
    [Fact]
    public void RecolouringCanBeTakenBack()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        ShapeBlock before = BoxOf(harness, id);
        (uint? fill, uint? stroke, float width) = (before.FillArgb, before.StrokeArgb, before.StrokeWidthPt);

        uint wanted = PageLooks.BoxColours[3].Argb;
        Assert.True(harness.Frames.SetSelectionShapeColours(wanted, PageLooks.BoxColours[4].Argb));
        Assert.Equal(wanted, BoxOf(harness, id).FillArgb);

        harness.Session.Undo();
        Assert.Equal(fill, BoxOf(harness, id).FillArgb);
        Assert.Equal(stroke, BoxOf(harness, id).StrokeArgb);
        Assert.Equal(width, BoxOf(harness, id).StrokeWidthPt, 3);
    }

    /// <summary>Asking for the colours it already has changes nothing.</summary>
    [Fact]
    public void AskingForTheColoursItAlreadyHasChangesNothing()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        ShapeBlock box = BoxOf(harness, id);

        Assert.False(harness.Frames.SetSelectionShapeColours(box.FillArgb, box.StrokeArgb));
    }

    /// <summary>
    /// A box of writing is not a shape: its look is a NAMED style, and arbitrary colours there need
    /// the derived-style machinery M86 brings. Recolouring refuses rather than half-working.
    /// </summary>
    [Fact]
    public void ABoxOfWritingIsNotSomethingThisCanRecolour()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        Assert.False(harness.Frames.SelectionIsAShape);
        Assert.Null(harness.Frames.SelectionShapeColours);
        Assert.False(harness.Frames.SetSelectionShapeColours(0xFF000000, null));
    }

    /// <summary>A new box is on the paper, whatever was chosen when it was asked for.</summary>
    [Fact]
    public void ANewBoxIsOnThePaper()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        string id = harness.Frames.AddBox(0, PageLooks.BoxColours[0].Argb, null);
        RectPt rect = BoxOf(harness, id).FrameRect;
        PageMaster master = harness.Session.Document.GetMaster(
            harness.Session.Document.Pages[0].MasterRef);

        Assert.True(rect.X >= 0f && rect.X + rect.Width <= master.Size.Width + 0.001f);
        Assert.True(rect.Y >= 0f && rect.Y + rect.Height <= master.Size.Height + 0.001f);
    }
}
