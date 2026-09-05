using TrestleBoard.Core.Text;
using Xunit;

using LetterCase = TrestleBoard.Editing.TextEditorController.LetterCase;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M98: three verbs every word processor has had for thirty years and this had none of —
/// changing capitals, counting words, and putting in a character the keyboard has no key for.
/// </summary>
public sealed class EverydayVerbTests
{
    private static EditorTestHarness Typed(string text)
    {
        var harness = new EditorTestHarness(text, withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.SelectAll();
        return harness;
    }

    // ---- capitals ------------------------------------------------------------------------------

    [Theory]
    [InlineData(LetterCase.Upper, "THE STATED COMMUNICATION")]
    [InlineData(LetterCase.Lower, "the stated communication")]
    [InlineData(LetterCase.Title, "The Stated Communication")]
    public void ChangingCapitalsDoesWhatItSays(LetterCase letterCase, string expected)
    {
        using EditorTestHarness harness = Typed("the STATED communication");

        Assert.True(harness.Controller.ChangeCase(letterCase));
        Assert.Equal(expected, harness.AllText());
    }

    /// <summary>
    /// One capital per word must bring a word that is ALREADY in capitals down again.
    /// <c>TextInfo.ToTitleCase</c> refuses to — it leaves "THE" as "THE" — and a heading pasted in
    /// from a Word document in capitals is the one input somebody reaches for this to fix.
    /// </summary>
    [Fact]
    public void OneCapitalPerWordBringsShoutingBackDown()
    {
        using EditorTestHarness harness = Typed("THE STATED COMMUNICATION");

        Assert.True(harness.Controller.ChangeCase(LetterCase.Title));
        Assert.Equal("The Stated Communication", harness.AllText());
    }

    /// <summary>An apostrophe does not start a new word, or O'Brien becomes O'brien.</summary>
    [Fact]
    public void AnApostropheDoesNotStartANewWord()
    {
        using EditorTestHarness harness = Typed("brother o'brien's message");

        Assert.True(harness.Controller.ChangeCase(LetterCase.Title));
        Assert.Equal("Brother O'brien's Message", harness.AllText());
    }

    /// <summary>Asking for what it already is changes nothing and leaves no undo step.</summary>
    [Fact]
    public void AskingForWhatItAlreadyIsChangesNothing()
    {
        using EditorTestHarness harness = Typed("THE STATED COMMUNICATION");

        Assert.False(harness.Controller.ChangeCase(LetterCase.Upper));
    }

    /// <summary>Nothing highlighted, nothing done — the caller can tell.</summary>
    [Fact]
    public void WithNothingHighlightedItRefuses()
    {
        using var harness = new EditorTestHarness("Some words.", withExclusion: false);
        harness.ClickIntoFrame();

        Assert.False(harness.Controller.ChangeCase(LetterCase.Upper));
    }

    /// <summary>One Ctrl+Z takes the whole change back, however many runs it touched.</summary>
    [Fact]
    public void TakingItBackRestoresTheOriginalWording()
    {
        using EditorTestHarness harness = Typed("the stated communication");

        Assert.True(harness.Controller.ChangeCase(LetterCase.Upper));
        harness.Session.Undo();

        Assert.Equal("the stated communication", harness.AllText());
    }

    /// <summary>
    /// The styling survives. The words are replaced run by run with each run's own character style
    /// put back, rather than deleted and retyped as one plain string — so a bold word inside the
    /// highlight is still bold afterwards.
    /// </summary>
    [Fact]
    public void ChangingCapitalsKeepsTheStylingOnTheWords()
    {
        using var harness = new EditorTestHarness("plain bold plain", withExclusion: false);
        harness.ClickIntoFrame();

        // Bold just the middle word, the way every other test in this project selects a range.
        for (int i = 0; i < 6; i++)
        {
            harness.Controller.Move(CaretMotion.Right, extend: false);
        }

        for (int i = 0; i < 4; i++)
        {
            harness.Controller.Move(CaretMotion.Right, extend: true);
        }

        harness.Controller.ToggleBold();

        int styledRuns = harness.Story.Paragraphs[0].Runs.Count(r => r.CharacterStyleRef is not null);
        Assert.True(styledRuns > 0, "the fixture did not actually bold anything");

        harness.Controller.SelectAll();
        Assert.True(harness.Controller.ChangeCase(LetterCase.Upper));

        Assert.Equal("PLAIN BOLD PLAIN", harness.AllText());
        Assert.Equal(
            styledRuns,
            harness.Story.Paragraphs[0].Runs.Count(r => r.CharacterStyleRef is not null));
    }

    // ---- counting ------------------------------------------------------------------------------

    [Fact]
    public void CountingTheWordsCountsTheWords()
    {
        using var harness = new EditorTestHarness("One two three four five.", withExclusion: false);
        harness.ClickIntoFrame();

        (int words, int characters, int? highlighted) = Assert.NotNull(harness.Controller.CountWords());
        Assert.Equal(5, words);
        Assert.Equal("One two three four five.".Length, characters);
        Assert.Null(highlighted);
    }

    /// <summary>With a highlight there is a second number, and it is about the highlight.</summary>
    [Fact]
    public void HighlightingSomeOfItAddsASecondNumber()
    {
        using var harness = new EditorTestHarness("One two three four five.", withExclusion: false);
        harness.ClickIntoFrame();
        for (int i = 0; i < 7; i++)
        {
            harness.Controller.Move(CaretMotion.Right, extend: true);
        }

        (_, _, int? highlighted) = Assert.NotNull(harness.Controller.CountWords());
        Assert.Equal(2, highlighted);
    }

    /// <summary>
    /// Paragraph breaks are structure, not writing, so they are not counted as letters — nobody
    /// asking how long an article is means to count the gaps between its paragraphs.
    /// </summary>
    [Fact]
    public void ParagraphBreaksAreNotCountedAsLetters()
    {
        using var harness = new EditorTestHarness("One", withExclusion: false);
        harness.ClickIntoFrame();
        harness.Controller.InsertParagraphBreak();
        harness.Controller.InsertText("Two");

        (int words, int characters, _) = Assert.NotNull(harness.Controller.CountWords());
        Assert.Equal(2, words);
        Assert.Equal(6, characters);
    }

    /// <summary>No caret, no answer — and the caller can tell rather than being given a zero.</summary>
    [Fact]
    public void WithNoCaretThereIsNoCount()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        Assert.Null(harness.Controller.CountWords());
    }

    /// <summary>The one definition of a word, used for every number the app says.</summary>
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData("one  two", 2)]
    [InlineData("one\ntwo three", 3)]
    public void AWordIsARunOfNonSpace(string text, int expected) =>
        Assert.Equal(expected, TextEditorController.WordsIn(text));
}
