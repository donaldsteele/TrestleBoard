using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The merge policy (PLAN.md §11 M12, §12 gate 9). Every person here is fictional (§0 rule 2).
///
/// <b>Idempotence is the key test.</b> Import a file, import the identical file again, assert
/// nothing changed — that one property guards the whole rule set, because every way of getting the
/// matching wrong shows up as a second import doing something.
/// </summary>
public sealed class RosterMergeTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static TableSheet Sheet(string text) => CsvTableReader.ReadText(text);

    private static Dictionary<RosterField, int> Mapping(TableSheet sheet, int headerRow) =>
        ColumnGuesser.GuessMapping(sheet, headerRow);

    private static MergePlan Import(RosterBook book, TableSheet sheet, MergeOptions? options = null)
    {
        int headerRow = ColumnGuesser.GuessHeaderRow(sheet);
        return RosterMerge.Plan(book, sheet, headerRow, Mapping(sheet, headerRow), options);
    }

    /// <summary>The acceptance criterion itself, over the hundred-person fixture.</summary>
    [Fact]
    public void ImportingTheSameFileTwiceChangesNothingTheSecondTime()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("members-100.csv")).Sheets[0];

        MergePlan first = Import(RosterBook.Empty, sheet);
        Assert.Equal(100, first.NewCount);
        Assert.True(first.ChangesAnything);

        MergePlan second = Import(first.Result, sheet);
        Assert.False(second.ChangesAnything);
        Assert.Equal(0, second.NewCount);
        Assert.Equal(0, second.UpdatedCount);
        Assert.Equal(100, second.UnchangedCount);
        Assert.Equal(first.Result.Members, second.Result.Members);
    }

    /// <summary>The same list as a spreadsheet and as a CSV must produce the same address book.</summary>
    [Fact]
    public void TheSpreadsheetAndTheCsvProduceTheSameAddressBook()
    {
        MergePlan fromCsv = Import(RosterBook.Empty, TableFileReader.Read(Fixture("members-100.csv")).Sheets[0]);
        MergePlan fromXlsx = Import(RosterBook.Empty, TableFileReader.Read(Fixture("members-100.xlsx")).Sheets[0]);

        Assert.Equal(100, fromXlsx.NewCount);
        Assert.Equal(
            fromCsv.Result.Members.Select(m => (m.Id, m.DisplayName, m.BirthMonth, m.BirthDay, m.Phone, m.Email)),
            fromXlsx.Result.Members.Select(m => (m.Id, m.DisplayName, m.BirthMonth, m.BirthDay, m.Phone, m.Email)));
    }

    /// <summary>
    /// PLAN §12 gate 9, in one test: importing a list of telephone numbers must not clear anybody's
    /// birthday. This is the rule that makes an import safe to try.
    /// </summary>
    [Fact]
    public void APhoneOnlyImportLeavesEveryBirthdayIntact()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            BirthMonth = 7,
            BirthDay = 4,
            Email = "aaron@example.invalid",
            Office = "Worshipful Master",
        });

        MergePlan plan = Import(book, Sheet("Name,Phone\nAaron Placeholder,555-0100\n"));

        Member after = Assert.Single(plan.Result.Members);
        Assert.Equal("555-0100", after.Phone);
        Assert.Equal(7, after.BirthMonth);
        Assert.Equal(4, after.BirthDay);
        Assert.Equal("Worshipful Master", after.Office);
        Assert.Equal("aaron@example.invalid", after.Email);
    }

    /// <summary>A blank cell in a mapped column is not an instruction to clear the field.</summary>
    [Fact]
    public void AnEmptyCellInAMappedColumnLeavesTheValueAlone()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            Phone = "555-0100",
        });

        MergePlan plan = Import(book, Sheet("Name,Phone\nAaron Placeholder,\n"));

        Assert.Equal("555-0100", Assert.Single(plan.Result.Members).Phone);
    }

    /// <summary>An import can add and can update. It has no way at all of removing anybody.</summary>
    [Fact]
    public void AnImportNeverRemovesAnybody()
    {
        RosterBook book = RosterBook.Empty
            .With(new Member { Id = "person-1", DisplayName = "Aaron Placeholder" })
            .With(new Member { Id = "person-2", DisplayName = "Bertram Sample" });

        MergePlan plan = Import(book, Sheet("Name,Phone\nCyrus Example,555-0102\n"));

        Assert.Equal(3, plan.Result.Count);
        Assert.NotNull(plan.Result.Find("person-1"));
        Assert.NotNull(plan.Result.Find("person-2"));
    }

    [Fact]
    public void LeaveThemAloneInsteadAddsTheNewPeopleAndTouchesNobodyElse()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            Phone = "555-0100",
        });

        MergePlan plan = Import(
            book,
            Sheet("Name,Phone\nAaron Placeholder,555-9999\nBertram Sample,555-0101\n"),
            new MergeOptions { UpdateExisting = false });

        Assert.Equal("555-0100", plan.Result.Find("person-1")!.Phone);
        Assert.Equal(1, plan.NewCount);
        Assert.Equal(1, plan.LeftAloneCount);
    }

    /// <summary>Exact id first: this is what makes export → edit in Excel → re-import lossless.</summary>
    [Fact]
    public void TheIdColumnMatchesEvenWhenTheNameChanged()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            Phone = "555-0100",
        });

        MergePlan plan = Import(
            book,
            Sheet("TrestleBoard ID,Name,Phone\nperson-1,Aaron Placeholder Junior,555-0100\n"));

        Member only = Assert.Single(plan.Result.Members);
        Assert.Equal("person-1", only.Id);
        Assert.Equal("Aaron Placeholder Junior", only.DisplayName);
        Assert.Equal(1, plan.UpdatedCount);
    }

    [Fact]
    public void AnEmailMatchesEvenWhenTheNameIsWrittenDifferently()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Aaron Placeholder",
            Email = "aaron@example.invalid",
        });

        MergePlan plan = Import(
            book,
            Sheet("Name,Email,Phone\n\"Placeholder, A.\",AARON@example.invalid,555-0100\n"));

        Member only = Assert.Single(plan.Result.Members);
        Assert.Equal("person-1", only.Id);
        Assert.Equal("555-0100", only.Phone);
    }

    [Theory]
    [InlineData("Aaron Placeholder", "aaron placeholder")]
    [InlineData("  Aaron   Placeholder ", "aaron placeholder")]
    [InlineData("Placeholder, Aaron", "aaron placeholder")]
    [InlineData("Aaron Placeholder Jr.", "aaron placeholder")]
    [InlineData("Aaron Placeholder, III", "aaron placeholder")]
    [InlineData("AARON O'PLACEHOLDER", "aaron o placeholder")]
    public void NamesAreComparedInTheFormPeopleActuallyWriteThem(string written, string expected) =>
        Assert.Equal(expected, NameMatching.Normalise(written));

    /// <summary>
    /// The negative half, and the one that matters most: two distinct brothers must never be merged
    /// without being asked about.
    /// </summary>
    [Fact]
    public void TwoDistinctBrothersAreNeverMergedOnTheirOwn()
    {
        RosterBook book = RosterBook.Empty
            .With(new Member { Id = "person-1", DisplayName = "Aaron Placeholder" })
            .With(new Member { Id = "person-2", DisplayName = "Bertram Placeholder" });

        MergePlan plan = Import(book, Sheet("Name,Phone\nCyrus Placeholder,555-0102\n"));

        Assert.Equal(3, plan.Result.Count);
        Assert.Empty(plan.Questions);
        Assert.Equal("Cyrus Placeholder", plan.Result.Members[^1].DisplayName);
    }

    /// <summary>Close enough to ask about is not close enough to act on.</summary>
    [Fact]
    public void ANearlyIdenticalNameIsAQuestionRatherThanAnAction()
    {
        RosterBook book = RosterBook.Empty
            .With(new Member { Id = "person-1", DisplayName = "Aaron Placeholder", Phone = "555-0100" });

        MergePlan plan = Import(book, Sheet("Name,Phone\nA. Placeholder,555-9999\n"));

        DuplicateQuestion question = Assert.Single(plan.Questions);
        Assert.Equal("person-1", question.ExistingMemberId);
        Assert.Contains("same person", question.Question, StringComparison.Ordinal);

        // Nothing has been done about it: the book is untouched until the question is answered.
        Assert.Equal(1, plan.Result.Count);
        Assert.Equal("555-0100", plan.Result.Find("person-1")!.Phone);
    }

    [Fact]
    public void AnsweringSamePersonUpdatesAndAnsweringDifferentPersonAdds()
    {
        RosterBook book = RosterBook.Empty
            .With(new Member { Id = "person-1", DisplayName = "Aaron Placeholder", Phone = "555-0100" });
        TableSheet sheet = Sheet("Name,Phone\nA. Placeholder,555-9999\n");

        MergePlan same = Import(book, sheet, new MergeOptions
        {
            Answers = new Dictionary<int, DuplicateAnswer> { [2] = DuplicateAnswer.SamePerson },
        });
        Assert.Equal(1, same.Result.Count);
        Assert.Equal("555-9999", same.Result.Find("person-1")!.Phone);

        MergePlan different = Import(book, sheet, new MergeOptions
        {
            Answers = new Dictionary<int, DuplicateAnswer> { [2] = DuplicateAnswer.DifferentPerson },
        });
        Assert.Equal(2, different.Result.Count);
        Assert.Equal("555-0100", different.Result.Find("person-1")!.Phone);
    }

    /// <summary>A row with no name in it is reported, never silently dropped.</summary>
    [Fact]
    public void ARowWithNoNameIsReportedRatherThanDiscardedQuietly()
    {
        MergePlan plan = Import(RosterBook.Empty, Sheet("Name,Phone\nAaron Placeholder,555-0100\n,555-0101\n"));

        PlannedRow unusable = Assert.Single(plan.Unusable);
        Assert.Equal(3, unusable.RowNumber);
        Assert.Contains("no name", unusable.Note!, StringComparison.Ordinal);
        Assert.Contains("1 row we couldn't use.", plan.Summary());
    }

    /// <summary>The hazard fixture end to end: the awkward file imports as four sensible people.</summary>
    [Fact]
    public void TheAwkwardFileImportsAsPeopleRatherThanAsRubbish()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("members-hazards.csv")).Sheets[0];
        MergePlan plan = Import(RosterBook.Empty, sheet);

        Assert.Equal(4, plan.NewCount);

        Member aaron = plan.Result.Members[0];
        Assert.Equal("Placeholder, Aaron", aaron.DisplayName);
        Assert.Equal("555-0100", aaron.Phone);
        Assert.Equal("7/4", aaron.BirthdayText);

        // 45123 is a date serial, not a birthday in the year forty-five thousand.
        Assert.Equal("7/16", plan.Result.Members[1].BirthdayText);
        Assert.Equal("7/9", plan.Result.Members[2].BirthdayText);
    }

    [Fact]
    public void TheReviewScreenSaysWhatWillHappenInPlainCounts()
    {
        RosterBook book = RosterBook.Empty
            .With(new Member { Id = "person-1", DisplayName = "Aaron Placeholder", Phone = "555-0100" });

        MergePlan plan = Import(
            book,
            Sheet("Name,Phone\nAaron Placeholder,555-9999\nBertram Sample,555-0101\n"));

        Assert.Contains("1 person is new.", plan.Summary());
        Assert.Contains("1 is already in your list — we'll update their details.", plan.Summary());
    }
}
