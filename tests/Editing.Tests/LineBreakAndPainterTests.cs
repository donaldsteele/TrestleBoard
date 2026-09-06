using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M103: the next line without a new paragraph, and copying how writing looks.
///
/// <para>The soft break is another "the engine already does it and no command reaches it":
/// <c>LineBreakAnalyzer</c> has listed U+2028 among its mandatory breaks since M1,
/// <c>TextLayoutEngine</c> obeys them, and the editor's sanitiser lets it through because it is not
/// a control character. Every part worked and no key put one in.</para>
/// </summary>
public sealed class LineBreakAndPainterTests
{
    private static EditorTestHarness Typing(string text)
    {
        var harness = new EditorTestHarness(text, withExclusion: false);
        harness.ClickIntoFrame();
        return harness;
    }

    // ---- the soft line break --------------------------------------------------------------------

    /// <summary>
    /// It stays in ONE paragraph. That is the whole difference from Enter: a new paragraph takes
    /// the paragraph gap and the first-line indent with it, which is what makes an address on two
    /// lines look wrong.
    /// </summary>
    [Fact]
    public void ItBreaksTheLineWithoutStartingANewParagraph()
    {
        using EditorTestHarness harness = Typing("First line");
        harness.Controller.SelectAll();
        harness.Controller.Move(CaretMotion.Right, extend: false);

        int paragraphs = harness.Story.Paragraphs.Count;
        harness.Controller.InsertLineBreak();
        harness.Controller.InsertText("second line");

        Assert.Equal(paragraphs, harness.Story.Paragraphs.Count);
        Assert.Contains(TextEditorController.LineSeparator, harness.AllText(), StringComparison.Ordinal);
    }

    /// <summary>Enter still makes a new paragraph, so the two gestures stay different things.</summary>
    [Fact]
    public void EnterStillMakesANewParagraph()
    {
        using EditorTestHarness harness = Typing("First");
        harness.Controller.SelectAll();
        harness.Controller.Move(CaretMotion.Right, extend: false);

        int paragraphs = harness.Story.Paragraphs.Count;
        harness.Controller.InsertParagraphBreak();

        Assert.Equal(paragraphs + 1, harness.Story.Paragraphs.Count);
    }

    /// <summary>
    /// The layout engine already treats it as a break. This is the assertion that the feature was
    /// unreachable rather than absent.
    /// </summary>
    [Fact]
    public void TheLayoutEngineAlreadyKnewHowToBreakThere()
    {
        IReadOnlyList<Layout.Breaking.BreakOpportunity> breaks =
            Layout.Breaking.LineBreakAnalyzer.Analyze("one" + TextEditorController.LineSeparator + "two");

        Assert.Contains(breaks, b => b.Kind == Layout.Breaking.BreakKind.Mandatory);
    }

    /// <summary>One Ctrl+Z takes it out again, like any other typed character.</summary>
    [Fact]
    public void TakingItBackRemovesIt()
    {
        using EditorTestHarness harness = Typing("Words");
        harness.Controller.SelectAll();
        harness.Controller.Move(CaretMotion.Right, extend: false);

        harness.Controller.InsertLineBreak();
        Assert.Contains(TextEditorController.LineSeparator, harness.AllText(), StringComparison.Ordinal);

        harness.Session.Undo();
        Assert.DoesNotContain(TextEditorController.LineSeparator, harness.AllText(), StringComparison.Ordinal);
    }

    // ---- copying how writing looks ---------------------------------------------------------------

    /// <summary>
    /// The look carries everything at once, because it carries the style NAME — and everything
    /// about how a run looks is already a named style applied by reference.
    ///
    /// <para>Two paragraphs, which is what somebody actually does: make one heading right, then
    /// make the next one match.</para>
    /// </summary>
    [Fact]
    public void TheLookCarriesEverythingAtOnce()
    {
        // Typed rather than pre-filled: clicking into a frame puts the caret at the START of the
        // words, so splitting there would leave paragraph 0 empty and the heading in paragraph 1.
        using EditorTestHarness harness = Typing("");
        harness.Controller.InsertText("First heading");
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Second heading");

        // Make the FIRST paragraph bold and underlined.
        Assert.True(harness.Controller.SelectRange(EditorTestHarness.StoryId, 0, 0, "First heading".Length));
        harness.Controller.ToggleBold();
        Assert.True(harness.Controller.SelectRange(EditorTestHarness.StoryId, 0, 0, "First heading".Length));
        harness.Controller.ToggleUnderline();

        // Copy its look, from a caret inside it.
        Assert.True(harness.Controller.SelectRange(EditorTestHarness.StoryId, 0, 2, 0));
        Assert.True(harness.Controller.PickUpTheLook());

        // Put it on the second.
        Assert.True(harness.Controller.SelectRange(EditorTestHarness.StoryId, 1, 0, "Second heading".Length));
        Assert.True(harness.Controller.PutTheLookDown());

        Assert.True(harness.Controller.IsBoldActive);
        Assert.True(harness.Controller.IsUnderlineActive);
    }

    /// <summary>
    /// It is NOT forgotten after one use. Somebody making six headings match does it six times, and
    /// a painter that emptied itself would make them pick the look up between each.
    /// </summary>
    [Fact]
    public void ThePickedUpLookSurvivesBeingUsed()
    {
        using EditorTestHarness harness = Typing("one two");
        Assert.True(harness.Controller.PickUpTheLook());
        Assert.True(harness.Controller.HasPickedUpALook);

        harness.Controller.SelectAll();
        harness.Controller.PutTheLookDown();

        Assert.True(harness.Controller.HasPickedUpALook);
    }

    /// <summary>Nothing picked up, nothing done — and the caller can tell.</summary>
    [Fact]
    public void WithNothingPickedUpItRefuses()
    {
        using EditorTestHarness harness = Typing("Words");
        harness.Controller.SelectAll();

        Assert.False(harness.Controller.HasPickedUpALook);
        Assert.False(harness.Controller.PutTheLookDown());
    }

    /// <summary>Putting a look on writing that already has it changes nothing.</summary>
    [Fact]
    public void PuttingItOnWritingThatAlreadyLooksThatWayChangesNothing()
    {
        using EditorTestHarness harness = Typing("Words");
        Assert.True(harness.Controller.PickUpTheLook());

        harness.Controller.SelectAll();
        Assert.False(harness.Controller.PutTheLookDown());
    }

    /// <summary>No caret, nothing to copy from.</summary>
    [Fact]
    public void WithNoCaretThereIsNothingToCopy()
    {
        using var harness = new EditorTestHarness("Words", withExclusion: false);

        Assert.False(harness.Controller.PickUpTheLook());
    }
}
