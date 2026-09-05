using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M93: how spaced out the writing is, and whether paragraphs start pushed in.
///
/// <para>These four fields — line spacing, the gap above and below a paragraph, and the first-line
/// indent — have been honoured by the layout engine since M1, are set by every template, are saved
/// and loaded, and until now could be changed by no command in the application. The hard half was
/// finished and tested; only the verb was missing.</para>
/// </summary>
public sealed class WritingLookTests
{
    private static WritingLookController Look(EditorTestHarness harness) => new(harness.Session);

    private static ParagraphStyleDef Body(EditorTestHarness harness) =>
        harness.Session.Document.StyleSheet.GetParagraphStyle(WritingLookController.BodyStyleName);

    /// <summary>Choosing "more spread out" actually reaches the style the layout engine reads.</summary>
    [Fact]
    public void ChoosingMoreSpreadOutChangesWhatTheLayoutEngineReads()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        WritingLookController look = Look(harness);

        float before = Body(harness).LineSpacing;
        Assert.True(look.SetSpacing(WritingLookController.Spacing.Roomy));

        (float lineSpacing, float spaceAfter) =
            WritingLookController.NumbersFor(WritingLookController.Spacing.Roomy);
        Assert.Equal(lineSpacing, Body(harness).LineSpacing, 3);
        Assert.Equal(spaceAfter, Body(harness).SpaceAfterPt, 3);
        Assert.NotEqual(before, Body(harness).LineSpacing);
    }

    /// <summary>One Ctrl+Z puts every one of the four fields back.</summary>
    [Fact]
    public void TakingItBackRestoresTheSpacingExactly()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        ParagraphStyleDef body = Body(harness);
        (float spacing, float before, float after, float indent) =
            (body.LineSpacing, body.SpaceBeforePt, body.SpaceAfterPt, body.FirstLineIndentPt);

        Assert.True(Look(harness).SetSpacing(WritingLookController.Spacing.Tight));
        harness.Session.Undo();

        Assert.Equal(spacing, Body(harness).LineSpacing, 3);
        Assert.Equal(before, Body(harness).SpaceBeforePt, 3);
        Assert.Equal(after, Body(harness).SpaceAfterPt, 3);
        Assert.Equal(indent, Body(harness).FirstLineIndentPt, 3);
    }

    /// <summary>
    /// Setting the indent leaves the spacing alone. The command's fields are nullable precisely so
    /// that one decision does not quietly overwrite the other — and a revert that restored all four
    /// from one snapshot would look correct while resetting the three nobody asked about.
    /// </summary>
    [Fact]
    public void TurningTheIndentOnLeavesTheSpacingAlone()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        WritingLookController look = Look(harness);

        Assert.True(look.SetSpacing(WritingLookController.Spacing.Roomy));
        float spacing = Body(harness).LineSpacing;
        float after = Body(harness).SpaceAfterPt;

        Assert.True(look.SetFirstLineIndent(true));

        Assert.Equal(WritingLookController.IndentPt, Body(harness).FirstLineIndentPt, 3);
        Assert.Equal(spacing, Body(harness).LineSpacing, 3);
        Assert.Equal(after, Body(harness).SpaceAfterPt, 3);
    }

    /// <summary>Turning it off puts it flat against the margin, not back to the template's value.</summary>
    [Fact]
    public void TurningTheIndentOffLeavesNoIndent()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        WritingLookController look = Look(harness);

        Assert.True(look.SetFirstLineIndent(true));
        Assert.True(look.FirstLineIsIndented);

        Assert.True(look.SetFirstLineIndent(false));
        Assert.False(look.FirstLineIsIndented);
        Assert.Equal(0f, Body(harness).FirstLineIndentPt, 3);
    }

    /// <summary>
    /// Asking for what is already there is refused, so the shell says "nothing has changed" instead
    /// of announcing a change and leaving an empty step on the undo stack (M70(c)).
    /// </summary>
    [Fact]
    public void AskingForWhatIsAlreadyThereChangesNothing()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        WritingLookController look = Look(harness);

        Assert.True(look.SetSpacing(WritingLookController.Spacing.Tight));
        bool couldUndo = harness.Session.CanUndo;

        Assert.False(look.SetSpacing(WritingLookController.Spacing.Tight));
        Assert.False(look.SetFirstLineIndent(look.FirstLineIsIndented));

        // No second step was pushed: one undo is still all it takes to get back.
        Assert.True(couldUndo);
        harness.Session.Undo();
        Assert.False(harness.Session.CanUndo);
    }

    /// <summary>
    /// The dialog is told which of the three the newsletter is set to, and it must be the truth:
    /// showing the wrong one selected is the app telling the user something untrue about their file.
    /// </summary>
    [Fact]
    public void TheCurrentChoiceIsReportedBack()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        WritingLookController look = Look(harness);

        foreach (WritingLookController.Spacing spacing in Enum.GetValues<WritingLookController.Spacing>())
        {
            look.SetSpacing(spacing);
            Assert.Equal(spacing, look.CurrentSpacing);
        }
    }

    /// <summary>
    /// A newsletter matching none of the three reports none, rather than the nearest. A file written
    /// by a later version, or edited by hand, legitimately sits between them.
    /// </summary>
    [Fact]
    public void ASpacingTheAppDoesNotOfferIsReportedAsNoneOfThem()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        Body(harness).LineSpacing = 1.77f;

        Assert.Null(Look(harness).CurrentSpacing);
    }

    /// <summary>
    /// Normal is exactly what the templates ship, so a user who wanders through the three choices
    /// and comes back to Normal is where they started — without needing Ctrl+Z.
    /// </summary>
    [Fact]
    public void NormalIsWhatTheTemplatesShipWith()
    {
        var document = new Document();
        Core.Templates.StandardStyles.Add(document);
        ParagraphStyleDef body =
            document.StyleSheet.GetParagraphStyle(WritingLookController.BodyStyleName);

        (float lineSpacing, float spaceAfter) =
            WritingLookController.NumbersFor(WritingLookController.Spacing.Normal);

        Assert.Equal(body.LineSpacing, lineSpacing, 3);
        Assert.Equal(body.SpaceAfterPt, spaceAfter, 3);
    }
}
