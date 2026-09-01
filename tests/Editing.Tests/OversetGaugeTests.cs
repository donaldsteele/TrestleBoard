using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M82: about forty words too long.
///
/// <para>The overset marker has said since M43 that there is more writing than fits, and never how
/// much. "About forty words" is a quantity a committee can act on, because they know what forty
/// words of their own article looks like.</para>
/// </summary>
public sealed class OversetGaugeTests
{
    /// <summary>Words after the break, counted the way a person counts them.</summary>
    [Fact]
    public void TheCountIsTheWordsAfterWhereTheWritingRanOut()
    {
        string[] paragraphs = ["The lodge meets on Tuesday.", "Supper is at six.", "All are welcome."];

        // Everything after the start of the second paragraph: 4 + 3 = 7.
        Assert.Equal(7, OversetWords.CountAfter(paragraphs, 1, 0));

        // From part way through the second: "at six." is two, plus the third paragraph's three.
        Assert.Equal(5, OversetWords.CountAfter(paragraphs, 1, "Supper is ".Length));

        // Nothing after the end.
        Assert.Equal(0, OversetWords.CountAfter(paragraphs, 2, "All are welcome.".Length));
    }

    /// <summary>
    /// <b>"About", never exactly.</b> The precise figure changes with every character typed, and a
    /// number that twitches while you read it reads as the app being unsure. Under ten it counts
    /// exactly, because "about ten" when three are missing is alarming out of proportion.
    /// </summary>
    [Theory]
    [InlineData(1, "About one word too long for this box.")]
    [InlineData(3, "About 3 words too long for this box.")]
    [InlineData(38, "About 40 words too long for this box.")]
    [InlineData(42, "About 40 words too long for this box.")]
    [InlineData(230, "About 250 words too long for this box.")]
    public void TheSentenceRoundsToSomethingAPersonWouldSay(int words, string expected) =>
        Assert.Equal(expected, OversetWords.Describe(words));

    /// <summary>Nothing hidden is said as nothing hidden, not as "about zero words".</summary>
    [Fact]
    public void NothingHiddenSaysSo() => Assert.Equal("All of the writing fits.", OversetWords.Describe(0));

    /// <summary>
    /// The card names the size of the problem and both ways out of it — give the writing more room
    /// here, or give it a room of its own.
    /// </summary>
    [Fact]
    public void TheCardSaysHowMuchAndOffersBothVerbs()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            HasOversetText = true,
            SelectionOversetWords = 41,
        };

        NextStep step = Assert.Single(
            WhatsNext.Suggestions(context),
            s => s.Title == "Make the writing fit");

        Assert.Contains("About 40 words", step.Why, StringComparison.Ordinal);
        Assert.Contains("taller", step.Why, StringComparison.Ordinal);
        Assert.Contains("next page", step.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M46's jargon rule, applied to the one place a milestone about measurement could break it.
    /// The count is words and only words — never lines, which depend on the frame's width, and
    /// never points, which is typography.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(400)]
    public void TheSentenceNeverSpeaksTypography(int words)
    {
        string sentence = OversetWords.Describe(words);

        Assert.DoesNotContain(" pt", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("point", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("line", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overset", sentence, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// M82: the address-book row leads with the verb, as every other row on this card does. It used
    /// to read "A list of people is on the page but the address book is empty" — a sentence about
    /// STATE, addressed to somebody looking for an instruction.
    /// </summary>
    [Fact]
    public void TheAddressBookRowLeadsWithWhatToDo()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            RosterEmptyButNeeded = true,
        };

        NextStep step = Assert.Single(
            WhatsNext.Suggestions(context),
            s => s.Title == "Fill in your address book");

        Assert.StartsWith("Import your member list", step.Why, StringComparison.Ordinal);

        // And it names a command, so the row is something the user can press rather than read.
        Assert.Equal(ActionId.ImportPeople, step.ActionId);
    }

    // ---- Making the box taller ------------------------------------------------------------------

    /// <summary>
    /// The first of the two verbs. A box with room below it grows until the writing fits.
    /// </summary>
    [Fact]
    public void AShortBoxWithRoomBelowItGrowsUntilTheWritingFits()
    {
        using var harness = new EditorTestHarness(Prose(30), frameHeightPt: 90);
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.IsSelectionOverset, "the fixture has to start overset or this proves nothing");

        FrameEditorController.GrowResult result = frames.GrowToFit();

        Assert.True(
            result is FrameEditorController.GrowResult.Fits
                or FrameEditorController.GrowResult.GrewButStillDoesNotFit,
            $"the box should have grown, but the answer was {result}");
        Assert.True(
            harness.Session.Document.FindBlock(EditorTestHarness.BlockId).Block.FrameRect.Height > 90f,
            "the box did not get taller");
    }

    /// <summary>
    /// <b>It stops at the margin M47 draws.</b> A box grown past the text area would put the
    /// newsletter outside the line the app has told the user to keep inside.
    /// </summary>
    [Fact]
    public void ItNeverGrowsPastTheBottomMargin()
    {
        using var harness = new EditorTestHarness(Prose(200), frameHeightPt: 90);
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);

        frames.GrowToFit();

        Core.Model.Document document = harness.Session.Document;
        Core.Model.PageMaster master = document.GetMaster(document.Pages[0].MasterRef);
        Core.Model.RectPt grown = document.FindBlock(EditorTestHarness.BlockId).Block.FrameRect;

        Assert.True(
            grown.Bottom <= master.Size.Height - master.MarginBottomPt + 0.01f,
            $"the box reached {grown.Bottom}, past the text area's {master.Size.Height - master.MarginBottomPt}");
    }

    /// <summary>
    /// A box that already reaches the margin says so and names the other verb, rather than doing
    /// nothing silently.
    /// </summary>
    [Fact]
    public void AboxWithNoRoomSaysSoRatherThanDoingNothing()
    {
        using var harness = new EditorTestHarness(Prose(200), frameHeightPt: 90);
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        frames.GrowToFit();

        Assert.Equal(FrameEditorController.GrowResult.NoRoom, frames.GrowToFit());
    }

    /// <summary>Nothing overset means nothing to do, said as such.</summary>
    [Fact]
    public void AboxThatAlreadyFitsIsLeftAlone()
    {
        using var harness = new EditorTestHarness("A short line.");
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);

        Assert.Equal(FrameEditorController.GrowResult.AlreadyFits, frames.GrowToFit());
    }

    /// <summary>A box being kept in place refuses to be grown, by the same sentence as a drag.</summary>
    [Fact]
    public void ALockedBoxIsNotGrown()
    {
        using var harness = new EditorTestHarness(Prose(30), frameHeightPt: 90);
        FrameEditorController frames = harness.Frames;
        frames.Select(EditorTestHarness.BlockId);
        frames.ToggleLocked();

        Assert.Equal(FrameEditorController.GrowResult.NothingChosen, frames.GrowToFit());
        Assert.Contains("kept in place", frames.StatusMessage, StringComparison.Ordinal);
    }

    private static string Prose(int sentences) => string.Concat(
        Enumerable.Repeat(
            "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ",
            sentences));
}
