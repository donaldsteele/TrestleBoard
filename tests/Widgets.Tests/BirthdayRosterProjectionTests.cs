using System.Text.Json;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// PLAN.md §12 gate 10, the roster-to-widget gate: hand-edited rows survive a re-sync, deliberately
/// removed members are not resurrected, and applying the projection twice is the same as applying it
/// once. Every person here is fictional (§0), and every one of them is built in this file — the real
/// address book lives in <c>%AppData%</c> and no test may ever read it.
/// </summary>
public sealed class BirthdayRosterProjectionTests
{
    private const int March = 3;
    private const int April = 4;

    [Fact]
    public void ProjectingOntoAnEmptyListAddsEveryoneWithABirthdayInTheMonth()
    {
        BirthdayProjection plan = BirthdayRosterProjection.Plan(new BirthdayListData(), Book(), March);

        Assert.Equal(["A. Placeholder", "B. Sample"], plan.Additions.Select(e => e.Name));
        Assert.Empty(plan.Removals);
        Assert.Empty(plan.KeptManual);
        Assert.True(plan.ChangesAnything);
        Assert.Equal(BirthdayListSource.Roster, plan.Result.Source);
        Assert.Equal(March, plan.Result.SourceMonth);
        Assert.All(plan.Result.Entries, e => Assert.False(e.IsManual));
    }

    [Fact]
    public void ApplyingTwiceIsTheSameAsApplyingOnce()
    {
        IReadOnlyList<Member> book = Book();

        BirthdayListData once = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;
        BirthdayProjection second = BirthdayRosterProjection.Plan(once, book, March);

        Assert.False(second.ChangesAnything);
        Assert.Empty(second.Additions);
        Assert.Empty(second.Removals);
        Assert.Empty(second.Updates);
        Assert.Equal(Printed(once), Printed(second.Result));
        Assert.Equal(once.RosterFingerprint, second.Result.RosterFingerprint);
    }

    [Fact]
    public void PlanNeverMutatesTheListItWasGiven()
    {
        var current = new BirthdayListData();

        BirthdayRosterProjection.Plan(current, Book(), March);

        Assert.Empty(current.Entries);
        Assert.Equal(BirthdayListSource.Manual, current.Source);
        Assert.Null(current.SourceMonth);
    }

    [Fact]
    public void AHandTypedRowIsNeverTouchedAndNeverRemoved()
    {
        var current = new BirthdayListData
        {
            Entries = { new BirthdayEntry { Name = "Z. Visitor", Month = March, Day = 30 } },
        };

        BirthdayProjection plan = BirthdayRosterProjection.Plan(current, Book(), March);

        Assert.Empty(plan.Removals);
        BirthdayEntry kept = Assert.Single(plan.KeptManual);
        Assert.Equal("Z. Visitor", kept.Name);
        Assert.Equal("Z. Visitor", plan.Result.Entries[0].Name);
        Assert.Equal(3, plan.Result.Entries.Count);
    }

    [Fact]
    public void AGeneratedRowTheUserEditedByHandSurvivesAReSyncUnchanged()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;

        // Exactly what the wizard and the grid editor do — one shared setter lambda, which is what
        // sets IsManual (PLAN.md §11 M13).
        IWizardStep step = ListStep();
        step.SetValue(generated, "name", 0, "A. Placeholder Jr.");
        Assert.True(generated.Entries[0].IsManual);

        BirthdayProjection plan = BirthdayRosterProjection.Plan(generated, book, March);

        Assert.Empty(plan.Removals);
        Assert.Empty(plan.Updates);
        Assert.Equal("A. Placeholder Jr.", plan.Result.Entries[0].Name);
        Assert.Contains(plan.KeptManual, e => e.Name == "A. Placeholder Jr.");
    }

    [Fact]
    public void TabbingPastAFieldWithoutTypingDoesNotMarkTheRowAsTheUsers()
    {
        // Both windows write the box back on LostFocus, so an unchanged write must not claim
        // authorship — otherwise a generated list freezes the moment somebody reads it.
        BirthdayListData generated =
            BirthdayRosterProjection.Plan(new BirthdayListData(), Book(), March).Result;
        IWizardStep step = ListStep();

        step.SetValue(generated, "name", 0, generated.Entries[0].Name);
        step.SetValue(generated, "date", 0, "3/15");

        Assert.False(generated.Entries[0].IsManual);
    }

    [Fact]
    public void ARowTheUserDeletedIsRememberedAndNeverComesBack()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;
        string removedId = generated.Entries[0].MemberId!;

        Assert.True(ListStep().RemoveRow(generated, 0));
        Assert.Equal([removedId], generated.RemovedMemberIds);

        BirthdayProjection plan = BirthdayRosterProjection.Plan(generated, book, March);

        Assert.Empty(plan.Additions);
        Assert.DoesNotContain(plan.Result.Entries, e => e.MemberId == removedId);
        Assert.Contains(removedId, plan.Result.RemovedMemberIds);
    }

    [Fact]
    public void ARemovalIsForgottenWhenTheIssueMovesToAnotherMonth()
    {
        // Carry-forward: a decision about March's list says nothing about April's.
        IReadOnlyList<Member> book = Book();
        BirthdayListData march = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;
        Assert.True(ListStep().RemoveRow(march, 0));

        BirthdayProjection april = BirthdayRosterProjection.Plan(march, book, April);

        Assert.Empty(april.Result.RemovedMemberIds);
        Assert.Equal(["C. Example"], april.Result.Entries.Select(e => e.Name));
        Assert.Single(april.Removals);
    }

    [Fact]
    public void ARenamedBrotherIsBroughtUpToDateInPlaceRatherThanReAdded()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;

        List<Member> renamed = [.. book];
        renamed[0] = renamed[0] with { DisplayName = "A. Placeholder II" };

        BirthdayProjection plan = BirthdayRosterProjection.Plan(generated, renamed, March);

        Assert.Empty(plan.Additions);
        Assert.Empty(plan.Removals);
        Assert.Single(plan.Updates);
        Assert.Equal("A. Placeholder II", plan.Result.Entries[0].Name);
        Assert.Equal(2, plan.Result.Entries.Count);
    }

    [Fact]
    public void TheStoredOrderOfSurvivorsIsLeftAloneAndNewPeopleGoOnTheEnd()
    {
        IReadOnlyList<Member> book = Book();
        var current = new BirthdayListData
        {
            Source = BirthdayListSource.Roster,
            SourceMonth = March,
            Entries =
            {
                new BirthdayEntry { Name = "B. Sample", Month = March, Day = 22, MemberId = "m-2" },
                new BirthdayEntry { Name = "Z. Visitor", Month = March, Day = 30 },
            },
        };

        BirthdayProjection plan = BirthdayRosterProjection.Plan(current, book, March);

        Assert.Equal(["B. Sample", "Z. Visitor", "A. Placeholder"], plan.Result.Entries.Select(e => e.Name));
    }

    [Fact]
    public void AnInactiveMemberIsNotPrintedAndDisappearsFromAGeneratedList()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;

        List<Member> changed = [.. book];
        changed[1] = changed[1] with { IsActive = false };

        BirthdayProjection plan = BirthdayRosterProjection.Plan(generated, changed, March);

        Assert.Single(plan.Removals);
        Assert.Equal(["A. Placeholder"], plan.Result.Entries.Select(e => e.Name));
    }

    // ---- fingerprint and staleness -----------------------------------------------------------

    [Fact]
    public void TheFingerprintChangesWhenAContributingFieldChangesAndNotOtherwise()
    {
        IReadOnlyList<Member> book = Book();
        string baseline = BirthdayRosterProjection.Fingerprint(book, March);

        List<Member> phoneChanged = [.. book];
        phoneChanged[0] = phoneChanged[0] with { Phone = "555-0199", Office = "Tyler", Notes = "n" };
        Assert.Equal(baseline, BirthdayRosterProjection.Fingerprint(phoneChanged, March));

        List<Member> dayChanged = [.. book];
        dayChanged[0] = dayChanged[0] with { BirthDay = 16 };
        Assert.NotEqual(baseline, BirthdayRosterProjection.Fingerprint(dayChanged, March));

        List<Member> nameChanged = [.. book];
        nameChanged[0] = nameChanged[0] with { DisplayName = "A. Placeholder II" };
        Assert.NotEqual(baseline, BirthdayRosterProjection.Fingerprint(nameChanged, March));

        Assert.NotEqual(baseline, BirthdayRosterProjection.Fingerprint(book, April));
    }

    [Fact]
    public void AListSomebodyTypedIsNeverCalledStale()
    {
        var typed = new BirthdayListData
        {
            Entries = { new BirthdayEntry { Name = "Z. Visitor", Month = March, Day = 30 } },
        };

        Assert.False(BirthdayRosterProjection.IsStale(typed, Book(), March));
        Assert.False(BirthdayRosterProjection.IsStale(typed, Book(), April));
    }

    [Fact]
    public void AGeneratedListGoesStaleWhenTheMonthMovesOrTheBookChanges()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;

        Assert.False(BirthdayRosterProjection.IsStale(generated, book, March));
        Assert.True(BirthdayRosterProjection.IsStale(generated, book, April));

        List<Member> extra = [.. book, Person("m-9", "Y. Newcomer", March, 8)];
        Assert.True(BirthdayRosterProjection.IsStale(generated, extra, March));
    }

    [Fact]
    public void SomebodyTakenOffTheListDoesNotKeepItLookingStale()
    {
        IReadOnlyList<Member> book = Book();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), book, March).Result;
        Assert.True(ListStep().RemoveRow(generated, 0));
        generated = BirthdayRosterProjection.Plan(generated, book, March).Result;

        Assert.False(BirthdayRosterProjection.IsStale(generated, book, March));
    }

    [Fact]
    public void CountForAnswersWhetherTheInsertWizardShouldOfferItsExtraScreen()
    {
        Assert.Equal(2, BirthdayRosterProjection.CountFor(Book(), March));
        Assert.Equal(1, BirthdayRosterProjection.CountFor(Book(), April));
        Assert.Equal(0, BirthdayRosterProjection.CountFor([], March));
        Assert.Equal(0, BirthdayRosterProjection.CountFor(Book(), 6));
    }

    // ---- the v1 payload -------------------------------------------------------------------------

    [Fact]
    public void AV1PayloadBecomesATypedListRatherThanAGeneratedOne()
    {
        var definition = new BirthdayListDefinition();
        const string v1 = """
            {
              "heading": "Birthdays",
              "entries": [ { "name": "A. Placeholder", "month": 3, "day": 15 } ],
              "closingNote": "Miss anyone? Let us know."
            }
            """;
        using JsonDocument document = JsonDocument.Parse(v1);

        Assert.True(definition.TryReadData(document.RootElement.Clone(), 1, out object typedObj));
        var typed = Assert.IsType<BirthdayListData>(typedObj);

        Assert.Equal(BirthdayListSource.Manual, typed.Source);
        Assert.Null(typed.SourceMonth);
        Assert.Empty(typed.RemovedMemberIds);
        Assert.Null(typed.Entries[0].MemberId);
        Assert.Equal("Miss anyone? Let us know.", typed.ClosingNote);

        // And so the projection treats that row as the user's, exactly as it was.
        BirthdayProjection plan = BirthdayRosterProjection.Plan(typed, Book(), March);
        Assert.Single(plan.KeptManual);
        Assert.Equal(2, definition.CurrentDataVersion);
    }

    [Fact]
    public void TheProvenanceFieldsSurviveTheCodecRoundTrip()
    {
        var definition = new BirthdayListDefinition();
        BirthdayListData generated = BirthdayRosterProjection.Plan(new BirthdayListData(), Book(), March).Result;
        generated.RemovedMemberIds.Add("m-9");

        JsonElement written = definition.WriteData(generated);
        Assert.True(definition.TryReadData(written, definition.CurrentDataVersion, out object readObj));
        var read = Assert.IsType<BirthdayListData>(readObj);

        Assert.Equal(generated.Source, read.Source);
        Assert.Equal(generated.SourceMonth, read.SourceMonth);
        Assert.Equal(generated.RosterFingerprint, read.RosterFingerprint);
        Assert.Equal(generated.RemovedMemberIds, read.RemovedMemberIds);
        Assert.Equal(generated.Entries[0].MemberId, read.Entries[0].MemberId);
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    private static IWizardStep ListStep() =>
        new BirthdayListDefinition().Wizard.Steps.First(s => s is IWizardListStep);

    private static string Printed(BirthdayListData data) =>
        string.Join('|', data.Entries.Select(e => $"{e.Name}:{e.Month}/{e.Day}:{e.MemberId}:{e.IsManual}"));

    private static Member Person(string id, string name, int month, int day) => new()
    {
        Id = id,
        DisplayName = name,
        BirthMonth = month,
        BirthDay = day,
    };

    private static List<Member> Book() =>
    [
        Person("m-1", "A. Placeholder", March, 15),
        Person("m-2", "B. Sample", March, 22),
        Person("m-3", "C. Example", April, 2),
        new() { Id = "m-4", DisplayName = "D. Fictional", Phone = "555-0103" },
    ];
}
