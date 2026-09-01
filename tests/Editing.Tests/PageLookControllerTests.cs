using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M79's editing half: the two toggles and the line across the page.
/// </summary>
public sealed class PageLookControllerTests
{
    /// <summary>
    /// Two independent toggles give all four looks with two verbs the user already understands —
    /// no dialog, no list of looks to read.
    /// </summary>
    [Fact]
    public void TheTwoTogglesAreIndependentAndReachAllFourLooks()
    {
        using var harness = new EditorTestHarness("A short article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        string id = frames.AddTextFrame(0);
        frames.Select(id);

        Assert.False(frames.SelectionHasBorder);
        Assert.False(frames.SelectionHasShade);

        Assert.True(frames.ToggleBorder());
        Assert.True(frames.SelectionHasBorder);
        Assert.False(frames.SelectionHasShade);

        Assert.True(frames.ToggleShade());
        Assert.True(frames.SelectionHasBorder);
        Assert.True(frames.SelectionHasShade);

        Assert.True(frames.ToggleBorder());
        Assert.False(frames.SelectionHasBorder);
        Assert.True(frames.SelectionHasShade);
    }

    /// <summary>
    /// One Ctrl+Z takes the look off, and the style it added goes with it. A newsletter that has
    /// never been given a border must be identical to one whose border was turned on and undone.
    /// </summary>
    [Fact]
    public void UndoTakesTheLookOffAndTheStyleWithIt()
    {
        using var harness = new EditorTestHarness("A short article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        string id = frames.AddTextFrame(0);
        frames.Select(id);

        frames.ToggleBorder();
        Assert.Contains(harness.Session.Document.StyleSheet.FrameStyles, s => s.Name == PageLooks.BorderStyleName);

        harness.Session.Undo();

        Assert.False(frames.SelectionHasBorder);
        Assert.DoesNotContain(
            harness.Session.Document.StyleSheet.FrameStyles,
            s => s.Name == PageLooks.BorderStyleName);
    }

    /// <summary>
    /// The line arrives the width of the text area and level, so nobody has to drag a hairline into
    /// place — the fine-motor task §6 exists to avoid.
    /// </summary>
    [Fact]
    public void TheLineSpansTheTextAreaAndIsLevel()
    {
        using var harness = new EditorTestHarness("A short article for the trestle board.");
        Document document = harness.Session.Document;
        PageMaster master = document.GetMaster(document.Pages[0].MasterRef);

        string id = harness.Frames.AddRuleAcrossThePage(0);

        var rule = Assert.IsType<ShapeBlock>(document.FindBlock(id).Block);
        Assert.Equal(ShapeKind.Rule, rule.Kind);
        Assert.Equal(master.MarginLeftPt, rule.FrameRect.X, 3);
        Assert.Equal(
            master.Size.Width - master.MarginLeftPt - master.MarginRightPt,
            rule.FrameRect.Width,
            3);
        Assert.Equal(PageLooks.RuleBlockHeightPt, rule.FrameRect.Height, 3);
        Assert.True(rule.StrokeWidthPt >= PageLooks.MinimumStrokeWidthPt);
    }

    /// <summary>
    /// "A line across the page" means, when a heading is chosen, a line under THAT — which is what
    /// somebody who has just finished typing a heading is asking for.
    /// </summary>
    [Fact]
    public void TheLineLandsUnderWhateverIsChosen()
    {
        using var harness = new EditorTestHarness("A short article for the trestle board.");
        FrameEditorController frames = harness.Frames;
        string heading = frames.AddTextFrame(0);
        frames.Select(heading);
        RectPt headingRect = harness.Session.Document.FindBlock(heading).Block.FrameRect;

        string rule = frames.AddRuleAcrossThePage(0);

        RectPt ruleRect = harness.Session.Document.FindBlock(rule).Block.FrameRect;
        Assert.True(
            ruleRect.Y >= headingRect.Bottom,
            $"the line landed at {ruleRect.Y}, above the bottom of the chosen frame at {headingRect.Bottom}");
    }

    /// <summary>Nothing chosen means nothing happens, and the toggle says so rather than claiming a
    /// change nobody made.</summary>
    [Fact]
    public void WithNothingChosenTheTogglesRefuseRatherThanPretend()
    {
        using var harness = new EditorTestHarness("A short article for the trestle board.");

        Assert.False(harness.Frames.ToggleBorder());
        Assert.False(harness.Frames.ToggleShade());
    }

    /// <summary>
    /// Both act on a chosen block of any kind. A bordered photograph and a bordered paragraph are
    /// drawn by the same code, so the catalog must not invent a restriction the drawing does not
    /// have.
    /// </summary>
    [Theory]
    [InlineData(SelectionKind.TextFrame)]
    [InlineData(SelectionKind.Photo)]
    [InlineData(SelectionKind.Widget)]
    [InlineData(SelectionKind.Shape)]
    public void BothVerbsAreOfferedForEveryKindOfChosenThing(SelectionKind kind)
    {
        var context = new ActionContext { HasDocument = true, Selection = kind };

        Assert.True(ActionCatalog.Evaluate(ActionId.ToggleBorder, context).IsAvailable);
        Assert.True(ActionCatalog.Evaluate(ActionId.ToggleShade, context).IsAvailable);
    }
}
