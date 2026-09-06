using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M109: pulling a paragraph in from both sides.
///
/// <para><c>ParagraphStyleDef</c> carried only a FIRST-LINE indent, which marks where a paragraph
/// begins. Nothing could set a paragraph apart from the ones around it — the announcement pulled in
/// on both sides, the quotation from the Grand Master — and the layout engine had no left or right
/// indent to honour if anything had asked.</para>
/// </summary>
public sealed class PullItInTests
{
    private static EditorTestHarness Typing(string text)
    {
        var harness = new EditorTestHarness(text, withExclusion: false);
        harness.ClickIntoFrame();
        return harness;
    }

    private static ParagraphStyleDef StyleOf(EditorTestHarness harness, int paragraph)
    {
        string name = harness.Story.Paragraphs[paragraph].ParagraphStyleRef;
        return harness.Session.Document.StyleSheet.GetParagraphStyle(name);
    }

    [Fact]
    public void PullingItInGivesItAnIndentOnBothSides()
    {
        using EditorTestHarness harness = Typing("A notice the committee wants set apart.");
        harness.Controller.SelectAll();

        Assert.False(harness.Controller.IsPulledIn);
        Assert.True(harness.Controller.SetPulledIn(true));

        ParagraphStyleDef style = StyleOf(harness, 0);
        Assert.Equal(ParagraphAlignmentNames.PulledInPt, style.LeftIndentPt);
        Assert.Equal(ParagraphAlignmentNames.PulledInPt, style.RightIndentPt);
    }

    /// <summary>Choosing it again puts the paragraph back, and back onto its plain role.</summary>
    [Fact]
    public void ChoosingItAgainPutsItBack()
    {
        using EditorTestHarness harness = Typing("A notice.");
        harness.Controller.SelectAll();
        harness.Controller.SetPulledIn(true);

        harness.Controller.SelectAll();
        Assert.True(harness.Controller.SetPulledIn(false));

        Assert.Equal(0f, StyleOf(harness, 0).LeftIndentPt);
        Assert.Equal(0f, StyleOf(harness, 0).RightIndentPt);
    }

    /// <summary>
    /// <b>The two verbs compose, and this is the reason they share one naming grammar.</b> Centring
    /// a paragraph that had been pulled in used to name a style with no indent in it, which would
    /// have un-pulled it silently — a wrong answer nobody would have been told about.
    /// </summary>
    [Fact]
    public void CentringAPulledInParagraphLeavesItPulledIn()
    {
        using EditorTestHarness harness = Typing("A quotation from the Grand Master.");
        harness.Controller.SelectAll();
        harness.Controller.SetPulledIn(true);

        harness.Controller.SelectAll();
        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));

        ParagraphStyleDef style = StyleOf(harness, 0);
        Assert.Equal(TextAlignment.Center, style.Align);
        Assert.Equal(ParagraphAlignmentNames.PulledInPt, style.LeftIndentPt);
        Assert.True(harness.Controller.IsPulledIn);
    }

    /// <summary>And the other way round: pulling in a centred paragraph leaves it centred.</summary>
    [Fact]
    public void PullingInACentredParagraphLeavesItCentred()
    {
        using EditorTestHarness harness = Typing("A heading over a notice.");
        harness.Controller.SelectAll();
        harness.Controller.SetAlignment(TextAlignment.Center);

        harness.Controller.SelectAll();
        Assert.True(harness.Controller.SetPulledIn(true));

        ParagraphStyleDef style = StyleOf(harness, 0);
        Assert.Equal(TextAlignment.Center, style.Align);
        Assert.Equal(ParagraphAlignmentNames.PulledInPt, style.RightIndentPt);
    }

    /// <summary>
    /// It rides on a DERIVED style, so the role it came from is untouched. Otherwise pulling one
    /// notice in would pull every paragraph of body text in with it.
    /// </summary>
    [Fact]
    public void TheRoleItCameFromIsUntouched()
    {
        // Typed rather than pre-filled: clicking into a frame puts the caret at the START of the
        // words, so splitting there would leave paragraph 0 empty (the M103 lesson).
        using EditorTestHarness harness = Typing("");
        harness.Controller.InsertText("First.");
        string role = harness.Story.Paragraphs[0].ParagraphStyleRef;
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second.");

        Assert.True(harness.Controller.SelectRange(EditorTestHarness.StoryId, 0, 0, "First.".Length));
        Assert.True(harness.Controller.SetPulledIn(true));

        Assert.Equal(0f, harness.Session.Document.StyleSheet.GetParagraphStyle(role).LeftIndentPt);
        Assert.Equal(role, harness.Story.Paragraphs[1].ParagraphStyleRef);
    }

    /// <summary>One Ctrl+Z takes it back, however many paragraphs were chosen.</summary>
    [Fact]
    public void TakingItBackIsOneStep()
    {
        using EditorTestHarness harness = Typing("");
        harness.Controller.InsertText("First.");
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second.");
        harness.Controller.SelectAll();

        harness.Controller.SetPulledIn(true);
        harness.Session.Undo();

        Assert.Equal(0f, StyleOf(harness, 0).LeftIndentPt);
        Assert.Equal(0f, StyleOf(harness, 1).LeftIndentPt);
    }

    /// <summary>Asking for what it already is changes nothing, so the shell can say so.</summary>
    [Fact]
    public void AskingForWhatItAlreadyIsChangesNothing()
    {
        using EditorTestHarness harness = Typing("A notice.");
        harness.Controller.SelectAll();

        Assert.False(harness.Controller.SetPulledIn(false));
    }

    // ---- the naming grammar ----------------------------------------------------------------------

    /// <summary>
    /// Every combination has its own name, every one bases back to the role, and the two facts can
    /// be read back off the name — which is what lets either verb be applied without consulting the
    /// definition it is about to replace.
    /// </summary>
    [Theory]
    [InlineData(TextAlignment.Left, false, "body")]
    [InlineData(TextAlignment.Left, true, "body~in")]
    [InlineData(TextAlignment.Center, false, "body~centred")]
    [InlineData(TextAlignment.Center, true, "body~centred-in")]
    [InlineData(TextAlignment.Right, false, "body~right")]
    [InlineData(TextAlignment.Right, true, "body~right-in")]
    public void EveryCombinationHasItsOwnNameAndReadsBack(
        TextAlignment alignment, bool pulledIn, string expected)
    {
        Assert.Equal(expected, ParagraphAlignmentNames.NameFor("body", alignment, pulledIn));
        Assert.Equal("body", ParagraphAlignmentNames.RoleOf(expected));
        Assert.Equal(alignment, ParagraphAlignmentNames.AlignmentOf(expected));
        Assert.Equal(pulledIn, ParagraphAlignmentNames.IsPulledIn(expected));

        // And it is idempotent: naming from an already-derived name gives the same name back, which
        // is what stops "body~centred~centred" ever being minted.
        Assert.Equal(expected, ParagraphAlignmentNames.NameFor(expected, alignment, pulledIn));
    }

    /// <summary>
    /// Left and pulled in needs the separator of its own — "body-in" would be read as a role called
    /// "body-in", and the derived style would stop deriving.
    /// </summary>
    [Fact]
    public void LeftAndPulledInIsStillADerivedName()
    {
        string name = ParagraphAlignmentNames.NameFor("body", TextAlignment.Left, pulledIn: true);

        Assert.Contains(StyleOverrides.Separator, name);
        Assert.Equal("body", ParagraphAlignmentNames.RoleOf(name));
    }
}
