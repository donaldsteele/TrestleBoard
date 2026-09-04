using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// The file the lodge's own member system produces, as M88 has to cope with it.
///
/// <para><b>Why this fixture is shaped the way it is.</b> Every person in <c>members-full.csv</c> is
/// invented (PLAN.md §0 rule 2) — but its <em>column headers</em> are copied exactly from the real
/// export, because the headers are what the guesser has to get right and a header is not personal
/// data. It also reproduces the three shapes that file has and an ordinary lodge list does not: one
/// row per report item, so a man appears twice; spouses in the same sheet as members; and three
/// telephone columns with no plain "Phone" among them.</para>
///
/// <para>The real file stays in <c>tmp/</c>, gitignored, and no test may read it.</para>
/// </summary>
public sealed class SecretarysFileTests
{
    private static TableSheet Sheet() =>
        TableFileReader.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "members-full.csv"))
            .Sheets[0];

    private static (MergePlan Plan, Dictionary<RosterField, int> Mapping) Import(RosterBook book)
    {
        TableSheet sheet = Sheet();
        Dictionary<RosterField, int> mapping = ColumnGuesser.GuessMapping(sheet, headerRow: 0);
        return (RosterMerge.Plan(book, sheet, 0, mapping), mapping);
    }

    /// <summary>
    /// The guesses, column by column, against the headers the secretary's system actually writes.
    ///
    /// <para>This is the regression table for M88's change to <c>ColumnGuesser</c>: the longest
    /// matching hint now wins, and this is what that has to buy. Three of these rows are guesses the
    /// old first-field-wins scoring got WRONG — "Mobile Phone" as the printed telephone, "Address
    /// Undeliverable" as the street address, and "Highest Degree Date" as the degree itself, which
    /// was a misfire that predates this milestone.</para>
    /// </summary>
    [Theory]
    [InlineData("Member Number", RosterField.MemberNumber)]
    [InlineData("Item Type", RosterField.RowKind)]
    [InlineData("FullName", RosterField.Name)]
    [InlineData("Birthday", RosterField.Birthday)]
    [InlineData("Address", RosterField.AddressLine1)]
    [InlineData("Address2", RosterField.AddressLine2)]
    [InlineData("City", RosterField.City)]
    [InlineData("State", RosterField.State)]
    [InlineData("Zip", RosterField.Zip)]
    [InlineData("Address Undeliverable", RosterField.AddressUndeliverable)]
    [InlineData("Email", RosterField.Email)]
    [InlineData("Home Phone", RosterField.HomePhone)]
    [InlineData("Mobile Phone", RosterField.MobilePhone)]
    [InlineData("Work Phone", RosterField.WorkPhone)]
    [InlineData("Current Office", RosterField.Office)]
    [InlineData("Highest Degree", RosterField.Degree)]
    [InlineData("Highest Degree Date", RosterField.DegreeDate)]
    [InlineData("Masonic Suffix", RosterField.MasonicTitle)]
    public void EveryColumnOfTheLodgesOwnFileIsGuessedRight(string header, RosterField expected)
    {
        TableSheet sheet = Sheet();
        Dictionary<RosterField, int> mapping = ColumnGuesser.GuessMapping(sheet, headerRow: 0);

        int column = sheet.Rows[0].ToList().IndexOf(header);
        Assert.True(column >= 0, $"the fixture has no column headed \"{header}\"");
        Assert.True(mapping.TryGetValue(expected, out int guessed), $"{expected} was mapped to nothing");
        Assert.Equal(column, guessed);
    }

    /// <summary>
    /// The report writes one row per birthday item, so a man with an actual and a masonic birthday
    /// appears twice. His member number is the same in both, so the second row updates the first
    /// rather than adding a second him — which is the whole reason the number is a match key.
    /// </summary>
    [Fact]
    public void ThreeRowsForTheSameManAreOneMan()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        Assert.Equal(10, plan.NewCount);
        Assert.Equal(10, plan.Result.Count);
        Assert.Single(plan.Result.Members, m => m.MemberNumber == "98501");
    }

    /// <summary>
    /// A father and a son, and the defect that made this milestone's match key worth having.
    ///
    /// <para><c>NameMatching.Normalise</c> strips "Jr." and "Sr.", so before M88 these two rows were
    /// the same man: the second overwrote the first, the book came out one person short, and nothing
    /// was said about it. Two such pairs are in the lodge's real list.</para>
    /// </summary>
    [Fact]
    public void AFatherAndASonAreTwoPeople()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        IReadOnlyList<Member> gideons =
            [.. plan.Result.Members.Where(m => m.DisplayName.StartsWith("Gideon", StringComparison.Ordinal))];

        Assert.Equal(2, gideons.Count);
        Assert.Equal(["98507", "98508"], gideons.Select(m => m.MemberNumber).Order());
        Assert.Empty(plan.Questions);
    }

    /// <summary>
    /// Spouses are never people in the book (M88). Two of the three land on their husbands' cards;
    /// the third names a member number the lodge does not have, and is reported rather than dropped.
    /// </summary>
    [Fact]
    public void SpouseRowsLandOnTheirHusbandsCardsAndNeverOnTheRoll()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        Assert.Equal(2, plan.SpouseCount);
        Assert.DoesNotContain(plan.Result.Members, m => m.DisplayName.StartsWith("Alice", StringComparison.Ordinal));

        Member aaron = plan.Result.Members.First(m => m.MemberNumber == "98501");
        Assert.Equal("Alice Placeholder", aaron.SpouseName);
        Assert.Equal("alice.placeholder@example.invalid", aaron.SpouseEmail);
        Assert.Equal("555-0801", aaron.SpousePhone);

        PlannedRow orphan = plan.Unusable.Single();
        Assert.Contains("Clara Anonymous", orphan.Note!, StringComparison.Ordinal);
        Assert.Contains("not in your list", orphan.Note!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lodge's file has Home, Mobile and Work and no plain "Phone" column at all. Something has
    /// to reach the officers table, so an empty telephone is filled from the mobile — and the review
    /// screen is told, because deciding which of a man's numbers is the public one is not something
    /// the app may do quietly.
    /// </summary>
    [Fact]
    public void ThePrintedTelephoneIsFilledFromTheMobileWhenThereIsNoneOfItsOwn()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        Member withMobile = plan.Result.Members.First(m => m.MemberNumber == "98502");
        Assert.Equal("555-0202", withMobile.MobilePhone);
        Assert.Equal("555-0202", withMobile.Phone);

        // Nobody has a mobile; the home number is the only one there is.
        Member homeOnly = plan.Result.Members.First(m => m.MemberNumber == "98503");
        Assert.Equal("555-0103", homeOnly.Phone);
    }

    /// <summary>…and a number the book already has is never overwritten by that rule.</summary>
    [Fact]
    public void AnExistingTelephoneNumberSurvivesTheImport()
    {
        RosterBook book = RosterBook.Empty.With(new Member
        {
            Id = "person-1",
            DisplayName = "Bertram Sample",
            MemberNumber = "98502",
            Phone = "555-0000",
        });

        (MergePlan plan, _) = Import(book);

        Member bertram = plan.Result.Find("person-1")!;
        Assert.Equal("555-0000", bertram.Phone);
        Assert.Equal("555-0202", bertram.MobilePhone);
    }

    /// <summary>Everything else the file holds, on one man's card.</summary>
    [Fact]
    public void OneMansCardCarriesEverythingTheLodgeKnows()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        Member aaron = plan.Result.Members.First(m => m.MemberNumber == "98501");

        Assert.Equal("Aaron Placeholder", aaron.DisplayName);
        Assert.Equal(2, aaron.BirthMonth);
        Assert.Equal(2, aaron.BirthDay);
        Assert.Equal(1957, aaron.BirthYear);
        Assert.Equal("1 Example Street", aaron.AddressLine1);
        Assert.Equal("Anytown", aaron.City);
        Assert.Equal("SC", aaron.State);
        Assert.Equal("29999", aaron.Zip);
        Assert.False(aaron.AddressUndeliverable);
        Assert.Equal("555-0101", aaron.HomePhone);
        Assert.Equal("555-0201", aaron.MobilePhone);
        Assert.Equal("PM", aaron.MasonicTitle);
        Assert.Equal(Degree.MasterMason, aaron.Degree);
        Assert.Equal("1995-05-01", aaron.DegreeDate);
        Assert.Equal("Worshipful Master", aaron.Office);

        // The one whose post comes back, and the two who are not Master Masons.
        Assert.True(plan.Result.Members.First(m => m.MemberNumber == "98503").AddressUndeliverable);
        Assert.Equal(
            Degree.EnteredApprentice,
            plan.Result.Members.First(m => m.MemberNumber == "98505").Degree);
        Assert.Equal(
            Degree.Fellowcraft,
            plan.Result.Members.First(m => m.MemberNumber == "98506").Degree);
    }

    /// <summary>
    /// The year is stored and the printed birthday is unchanged by its presence — M88's promise, at
    /// the one place a real year enters the app.
    /// </summary>
    [Fact]
    public void TheYearIsKeptAndTheBirthdayStillPrintsWithoutIt()
    {
        (MergePlan plan, _) = Import(RosterBook.Empty);

        Member aaron = plan.Result.Members.First(m => m.MemberNumber == "98501");

        Assert.Equal(1957, aaron.BirthYear);
        Assert.Equal("2/2", aaron.BirthdayText);
    }

    /// <summary>Importing the lodge's file twice changes nothing the second time (PLAN.md §12).</summary>
    [Fact]
    public void ImportingTheLodgesFileTwiceChangesNothingTheSecondTime()
    {
        (MergePlan first, _) = Import(RosterBook.Empty);
        (MergePlan second, _) = Import(first.Result);

        Assert.False(second.ChangesAnything);
        Assert.Equal(first.Result.Members, second.Result.Members);
    }
}
