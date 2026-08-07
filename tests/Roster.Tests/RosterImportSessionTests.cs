using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The six screens, driven with no window attached (PLAN.md §11 M12). Every sentence the user reads
/// during an import is asserted here rather than in a headless UI test, which is the whole reason
/// the session holds the state and the window only renders it.
/// </summary>
public sealed class RosterImportSessionTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void TheFirstScreenPromisesThatNothingChangesYet()
    {
        var session = new RosterImportSession(RosterBook.Empty);

        Assert.Equal(ImportStep.ChooseFile, session.Step);
        Assert.Equal("Where is the list?", session.Title);
        Assert.Contains("Nothing changes until you say so", session.Explanation, StringComparison.Ordinal);
    }

    /// <summary>A screen with one possible answer is a screen that wastes the user's time.</summary>
    [Fact]
    public void TheSheetScreenIsSkippedWhenThereIsOnlyOneSheet()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));

        Assert.Equal(ImportStep.ChooseHeaderRow, session.Step);
        Assert.Single(session.SheetNames);
    }

    [Fact]
    public void TheTitleRowIsGuessedAndTheColumnsGuessedFromIt()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));

        Assert.Equal(0, session.HeaderRow);
        Assert.Equal(0, session.Mapping[RosterField.Name]);
        Assert.Equal(1, session.Mapping[RosterField.Birthday]);
        Assert.Equal(2, session.Mapping[RosterField.Phone]);
        Assert.Equal(3, session.Mapping[RosterField.Email]);
        Assert.Equal(4, session.Mapping[RosterField.Office]);
    }

    /// <summary>
    /// A list with no titles at all is common — a pasted column of names — and guessing one would
    /// quietly lose the first person.
    /// </summary>
    [Fact]
    public void AListWithNoTitleRowIsRecognisedAsHavingNone()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-headerless.csv"));

        Assert.Equal(-1, session.HeaderRow);
        session.ChooseHeaderRow(-1);
        session.MapColumn(RosterField.Name, 0);
        session.GoToReview();

        Assert.Equal(2, session.Plan().NewCount);
        Assert.Contains(session.Plan().Result.Members, m => m.DisplayName == "Aaron Placeholder");
    }

    [Fact]
    public void TheTitleRowScreenShowsTheFirstFewRowsToChooseFrom()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));

        Assert.Equal(ColumnGuesser.HeaderCandidateRows, session.HeaderCandidates.Count);
        Assert.Equal("Name", session.HeaderCandidates[0][0]);
        Assert.Equal("Aaron Placeholder", session.HeaderCandidates[1][0]);
    }

    /// <summary>
    /// Each column is described by letter, title AND its first values, so the user recognises their
    /// own data instead of decoding a header they may not have written.
    /// </summary>
    [Fact]
    public void EachColumnIsDescribedByWhatIsInItNotJustByItsTitle()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));

        TableColumn phone = session.Columns[2];
        Assert.Equal("C", phone.Letter);
        Assert.Equal("Phone", phone.Header);
        Assert.Contains("555-0101", phone.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheNameIsRequiredAndTheReasonIsSaidOutLoud()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));
        session.ChooseHeaderRow(0);

        session.MapColumn(RosterField.Name, null);
        Assert.False(session.CanReview);
        Assert.Contains("nobody to add", session.WhyNotReady!, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(session.GoToReview);

        session.MapColumn(RosterField.Name, 0);
        Assert.True(session.CanReview);
        Assert.Null(session.WhyNotReady);
    }

    [Fact]
    public void TheWholeFlowAddsAHundredPeopleAndSaysSo()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));
        session.ChooseHeaderRow(session.HeaderRow);
        session.GoToReview();

        MergePlan plan = session.Plan();
        Assert.Equal(100, plan.NewCount);
        Assert.Contains("100 people are new.", plan.Summary());

        RosterBook book = session.Commit();
        Assert.Equal(ImportStep.Done, session.Step);
        Assert.Equal(100, book.Count);
        Assert.Equal("Your address book now has 100 people.", RosterImportSession.DoneMessage(book.Count));
    }

    /// <summary>Going back must not skip a screen the user was never shown, or ask one twice.</summary>
    [Fact]
    public void BackRetracesTheScreensTheUserActuallySaw()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-100.csv"));
        session.ChooseHeaderRow(0);
        session.GoToReview();

        session.Back();
        Assert.Equal(ImportStep.MapColumns, session.Step);
        session.Back();
        Assert.Equal(ImportStep.ChooseHeaderRow, session.Step);

        // One sheet, so the sheet screen was never shown and must not appear on the way back.
        session.Back();
        Assert.Equal(ImportStep.ChooseFile, session.Step);
    }

    [Fact]
    public void ARowThatCouldNotBeUsedCanBeSavedToAFileRatherThanLost()
    {
        var session = new RosterImportSession(RosterBook.Empty);
        session.ChooseFile(Fixture("members-hazards.csv"));
        session.ChooseHeaderRow(0);
        session.MapColumn(RosterField.Name, 0);
        session.MapColumn(RosterField.Phone, 2);
        session.GoToReview();

        string text = session.UnusableRowsText();
        Assert.Contains("Rows TrestleBoard could not use", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTheAppCannotReadRefusesInWordsTheUserCanActOn()
    {
        var session = new RosterImportSession(RosterBook.Empty);

        TableReadException error = Assert.Throws<TableReadException>(
            () => session.ChooseFile(Fixture("old-format.xls")));

        Assert.Contains("Save As", error.Message, StringComparison.Ordinal);
        Assert.Equal(ImportStep.ChooseFile, session.Step);
    }

    /// <summary>Re-importing the same file through the whole flow is the acceptance criterion.</summary>
    [Fact]
    public void ImportingTheSameFileTwiceThroughTheFlowChangesNothing()
    {
        var first = new RosterImportSession(RosterBook.Empty);
        first.ChooseFile(Fixture("members-100.csv"));
        first.ChooseHeaderRow(first.HeaderRow);
        first.GoToReview();
        RosterBook after = first.Commit();

        var second = new RosterImportSession(after);
        second.ChooseFile(Fixture("members-100.csv"));
        second.ChooseHeaderRow(second.HeaderRow);
        second.GoToReview();

        Assert.False(second.Plan().ChangesAnything);
        Assert.Equal(after.Members, second.Commit().Members);
    }
    /// <summary>
    /// Review §14.2: a header hint has to sit on word boundaries.
    ///
    /// <para>A bare substring match misfired in one direction every time — "Member Number" scored
    /// as Name on the hint "member", "Mailing address" as Email on "mail", "Member No." as Phone on
    /// "number". The mapping screen says "We guessed these. Change any that are wrong.", so a wrong
    /// guess is correctable — but only by somebody who notices, and the point of guessing is that
    /// they should not have to check every column.</para>
    /// </summary>
    [Theory]
    [InlineData("Member Number", RosterField.Name)]
    [InlineData("Mailing address", RosterField.Email)]
    [InlineData("Membership", RosterField.Name)]
    public void AHeaderDoesNotMatchAHintBuriedInsideAnotherWord(string header, RosterField wrongGuess)
    {
        var sheet = new TableSheet("Sheet1", [[header], ["12"], ["13"]]);
        Dictionary<RosterField, int> mapping = ColumnGuesser.GuessMapping(sheet, headerRow: 0);

        Assert.False(
            mapping.TryGetValue(wrongGuess, out int column) && column == 0,
            $"\"{header}\" was guessed as {wrongGuess}");
    }

    /// <summary>And the headers that genuinely do name a field still match.</summary>
    [Theory]
    [InlineData("Member Name", RosterField.Name)]
    [InlineData("Name", RosterField.Name)]
    [InlineData("E-Mail", RosterField.Email)]
    [InlineData("Phone", RosterField.Phone)]
    public void ARealHeaderStillMatchesItsField(string header, RosterField expected)
    {
        var sheet = new TableSheet("Sheet1", [[header], ["something"], ["another"]]);
        Dictionary<RosterField, int> mapping = ColumnGuesser.GuessMapping(sheet, headerRow: 0);

        Assert.True(mapping.TryGetValue(expected, out int column), $"\"{header}\" matched nothing");
        Assert.Equal(0, column);
    }

}
