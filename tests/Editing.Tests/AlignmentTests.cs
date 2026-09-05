using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M96: which way the writing is lined up.
///
/// <para><see cref="TextAlignment"/>, <see cref="ParagraphStyleDef.Align"/> and the shift
/// arithmetic in <c>TextLayoutEngine</c> have all been live since M1, and two sample styles use
/// them — and no command could reach any of it. A committee wanting a centred heading had to mint a
/// style by hand.</para>
///
/// <para>Alignment rides on a DERIVED paragraph style, exactly as bold and italic ride on derived
/// character styles, so nothing carries direct formatting and the constraint locked in §1 holds.</para>
/// </summary>
public sealed class AlignmentTests
{
    private static string StyleRefOf(EditorTestHarness harness, int paragraph = 0) =>
        harness.Story.Paragraphs[paragraph].ParagraphStyleRef;

    private static ParagraphStyleDef StyleOf(EditorTestHarness harness, int paragraph = 0) =>
        harness.Session.Document.StyleSheet.GetParagraphStyle(StyleRefOf(harness, paragraph));

    /// <summary>Centring reaches the field the layout engine actually reads.</summary>
    [Fact]
    public void CentringChangesWhatTheLayoutEngineReads()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        harness.ClickIntoFrame();

        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));
        Assert.Equal(TextAlignment.Center, StyleOf(harness).Align);
    }

    /// <summary>
    /// It mints a DERIVED style rather than editing the role, so centring one paragraph does not
    /// centre every other paragraph in the newsletter that shares its role.
    /// </summary>
    [Fact]
    public void CentringOneParagraphLeavesTheRoleAlone()
    {
        using var harness = new EditorTestHarness("First.", withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second.");

        // Caret is in the second paragraph; centre only that one.
        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));

        Assert.Equal(TextAlignment.Center, StyleOf(harness, 1).Align);
        Assert.Equal(TextAlignment.Left, StyleOf(harness, 0).Align);

        ParagraphStyleDef role = harness.Session.Document.StyleSheet.GetParagraphStyle("body");
        Assert.Equal(TextAlignment.Left, role.Align);
    }

    /// <summary>
    /// The derived style keeps everything else about the role — its character style, its spacing,
    /// its indent. Centring a heading must not quietly change how much air is around it.
    /// </summary>
    [Fact]
    public void TheDerivedStyleKeepsEverythingElseAboutTheRole()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        ParagraphStyleDef role = harness.Session.Document.StyleSheet.GetParagraphStyle("body");

        harness.ClickIntoFrame();
        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));

        ParagraphStyleDef derived = StyleOf(harness);
        Assert.Equal(role.CharacterStyleRef, derived.CharacterStyleRef);
        Assert.Equal(role.LineSpacing, derived.LineSpacing, 3);
        Assert.Equal(role.SpaceBeforePt, derived.SpaceBeforePt, 3);
        Assert.Equal(role.SpaceAfterPt, derived.SpaceAfterPt, 3);
        Assert.Equal(role.FirstLineIndentPt, derived.FirstLineIndentPt, 3);
    }

    /// <summary>
    /// Going back to left names the ROLE again rather than minting a "left" override. Every style
    /// in the templates is already left, so a document that centres nothing looks exactly as it did
    /// before this existed — which is what keeps the canonical form small.
    /// </summary>
    [Fact]
    public void GoingBackToLeftNamesTheRoleAgain()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        harness.ClickIntoFrame();

        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));
        Assert.NotEqual("body", StyleRefOf(harness));

        Assert.True(harness.Controller.SetAlignment(TextAlignment.Left));
        Assert.Equal("body", StyleRefOf(harness));
    }

    /// <summary>Asking for the way it already is changes nothing and says so.</summary>
    [Fact]
    public void AskingForTheWayItAlreadyIsChangesNothing()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        harness.ClickIntoFrame();

        Assert.False(harness.Controller.SetAlignment(TextAlignment.Left));

        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));
        Assert.False(harness.Controller.SetAlignment(TextAlignment.Center));
    }

    /// <summary>One Ctrl+Z takes the whole thing back, style and all.</summary>
    [Fact]
    public void TakingItBackRestoresTheParagraphAndRemovesTheStyle()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        harness.ClickIntoFrame();

        int stylesBefore = harness.Session.Document.StyleSheet.ParagraphStyles.Count;
        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));

        harness.Session.Undo();

        Assert.Equal("body", StyleRefOf(harness));
        Assert.Equal(stylesBefore, harness.Session.Document.StyleSheet.ParagraphStyles.Count);
    }

    /// <summary>
    /// The derived style is minted once and reused. Centring a second paragraph must not leave two
    /// styles in the sheet that mean the same thing.
    /// </summary>
    [Fact]
    public void TheDerivedStyleIsMintedOnceAndReused()
    {
        using var harness = new EditorTestHarness("First.", withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second.");

        harness.Controller.SelectAll();
        Assert.True(harness.Controller.SetAlignment(TextAlignment.Center));

        Assert.Equal(StyleRefOf(harness, 0), StyleRefOf(harness, 1));
        string derived = StyleRefOf(harness);
        Assert.Single(
            harness.Session.Document.StyleSheet.ParagraphStyles,
            s => s.Name == derived);
    }

    /// <summary>A whole highlighted run of paragraphs lines up together, in one undo step.</summary>
    [Fact]
    public void HighlightingSeveralParagraphsLinesThemAllUp()
    {
        using var harness = new EditorTestHarness("First.", withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second.");
        harness.Controller.SelectAll();

        Assert.True(harness.Controller.SetAlignment(TextAlignment.Right));
        Assert.Equal(TextAlignment.Right, StyleOf(harness, 0).Align);
        Assert.Equal(TextAlignment.Right, StyleOf(harness, 1).Align);

        harness.Session.Undo();
        Assert.Equal(TextAlignment.Left, StyleOf(harness, 0).Align);
        Assert.Equal(TextAlignment.Left, StyleOf(harness, 1).Align);
    }

    /// <summary>The app can say which way it is lined up, so a pressed button cannot lie.</summary>
    [Fact]
    public void TheAppKnowsWhichWayItIsLinedUp()
    {
        using var harness = new EditorTestHarness("A heading.", withExclusion: false);
        harness.ClickIntoFrame();

        Assert.Equal(TextAlignment.Left, harness.Controller.CurrentAlignment);
        harness.Controller.SetAlignment(TextAlignment.Center);
        Assert.Equal(TextAlignment.Center, harness.Controller.CurrentAlignment);
    }

    /// <summary>The naming convention keeps bold and italic working inside an aligned paragraph.</summary>
    [Fact]
    public void AnAlignedStyleStillKnowsWhichRoleItCameFrom()
    {
        Assert.Equal("body", ParagraphAlignmentNames.RoleOf("body~centred"));
        Assert.Equal("heading", ParagraphAlignmentNames.RoleOf("heading~right"));
        Assert.Equal("body", ParagraphAlignmentNames.RoleOf("body"));
    }
}
