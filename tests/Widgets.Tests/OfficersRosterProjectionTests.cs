using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// PLAN.md §11 M19: the officers table is generated from the address book, and every rule M13's
/// deferral was protecting still holds. Sync twice equals sync once; a hand-edited row survives; an
/// unrecognised office never reaches the page; two claimants are never resolved by the app.
///
/// Every person here is fictional (§0) and built in this file — the real address book lives in
/// <c>%AppData%</c> and no test may ever read it.
/// </summary>
public sealed class OfficersRosterProjectionTests
{
    private static readonly OfficersTableDefinition Definition = new();

    [Fact]
    public void ProjectingOntoAnEmptyTableProposesEveryOfficeTheBookKnows()
    {
        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), Book());

        Assert.Equal(
            ["Worshipful Master", "Senior Warden", "Treasurer"],
            plan.Proposals.Select(p => p.Position));
        Assert.True(plan.ChangesAnything);
        Assert.Empty(plan.KeptManual);
        Assert.All(plan.Proposals, p => Assert.False(p.IsAmbiguous));
    }

    [Fact]
    public void ProposalsComeBackInTheOrderTheOfficesArePrinted()
    {
        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), Book());

        List<int> printedIndexes =
            [.. plan.Proposals.Select(p => OfficersTableData.StandardPositions.ToList().IndexOf(p.Position))];
        Assert.Equal(printedIndexes.Order(), printedIndexes);
    }

    [Fact]
    public void ApplyingTwiceIsTheSameAsApplyingOnce()
    {
        IReadOnlyList<Member> book = Book();
        OfficersProjection first = OfficersRosterProjection.Plan(Empty(), book);
        OfficersTableData once = OfficersRosterProjection.Apply(Empty(), first.DefaultDecisions, first.Fingerprint);

        OfficersProjection second = OfficersRosterProjection.Plan(once, book);

        Assert.False(second.ChangesAnything);
        Assert.Empty(second.Proposals);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(Printed(once), Printed(
            OfficersRosterProjection.Apply(once, second.DefaultDecisions, second.Fingerprint)));
    }

    [Fact]
    public void PlanNeverMutatesTheTableItWasGiven()
    {
        OfficersTableData current = Empty();

        OfficersRosterProjection.Plan(current, Book());

        Assert.All(current.Officers, o => Assert.Equal("", o.Name));
        Assert.Equal(OfficersTableSource.Manual, current.Source);
        Assert.Null(current.RosterFingerprint);
    }

    [Fact]
    public void TheTwelveRowsAndTheirOrderSurviveASync()
    {
        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), Book());
        OfficersTableData result = OfficersRosterProjection.Apply(Empty(), plan.DefaultDecisions, plan.Fingerprint);

        Assert.Equal(OfficersTableData.StandardPositions, result.Officers.Select(o => o.Position));
        Assert.Equal("(vacant)", result.VacantText);
    }

    [Fact]
    public void AHandEditedRowIsListedAsTheUsersAndIsLeftExactlyAsItWas()
    {
        OfficersTableData current = Empty();
        Row(current, "Treasurer").Name = "H. Handtyped";
        Row(current, "Treasurer").IsManual = true;

        OfficersProjection plan = OfficersRosterProjection.Plan(current, Book());

        Assert.Contains(plan.KeptManual, e => e.Position == "Treasurer");
        Assert.DoesNotContain(plan.Proposals, p => p.Position == "Treasurer");

        OfficersTableData result = OfficersRosterProjection.Apply(current, plan.DefaultDecisions, plan.Fingerprint);
        Assert.Equal("H. Handtyped", Row(result, "Treasurer").Name);
        Assert.True(Row(result, "Treasurer").IsManual);
    }

    /// <summary>
    /// Belt and braces: even a caller that made up a decision for a hand-edited row cannot overwrite
    /// the user's typing. The rule lives in <c>Apply</c>, not only in <c>Plan</c>.
    /// </summary>
    [Fact]
    public void ApplyRefusesToOverwriteAHandEditedRowEvenIfToldTo()
    {
        OfficersTableData current = Empty();
        Row(current, "Secretary").Name = "H. Handtyped";
        Row(current, "Secretary").IsManual = true;

        OfficersTableData result = OfficersRosterProjection.Apply(
            current,
            [new OfficerDecision("Secretary", new OfficerCandidate("m-x", "W. Wrong", null))],
            "fingerprint");

        Assert.Equal("H. Handtyped", Row(result, "Secretary").Name);
    }

    [Fact]
    public void AnUnrecognisedOfficeIsShownByNameAndNeverReachesThePage()
    {
        List<Member> book = [.. Book(), Person("m-hist", "E. Historian", "Historian")];

        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), book);
        OfficersTableData result = OfficersRosterProjection.Apply(Empty(), plan.DefaultDecisions, plan.Fingerprint);

        Assert.Contains(plan.Unrecognised, u => u.Name == "E. Historian" && u.Office == "Historian");
        Assert.DoesNotContain(result.Officers, o => o.Name == "E. Historian");
    }

    [Fact]
    public void TwoClaimantsForOneOfficeAreNeverResolvedByTheApp()
    {
        List<Member> book = [.. Book(), Person("m-rival", "R. Rival", "Senior Warden")];

        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), book);
        OfficerProposal warden = plan.Proposals.Single(p => p.Position == "Senior Warden");

        Assert.True(warden.IsAmbiguous);
        Assert.Equal(2, warden.Candidates.Count);
        Assert.Null(warden.Only);
        Assert.DoesNotContain(plan.DefaultDecisions, d => d.Position == "Senior Warden");

        OfficersTableData result = OfficersRosterProjection.Apply(Empty(), plan.DefaultDecisions, plan.Fingerprint);
        Assert.Equal("", Row(result, "Senior Warden").Name);
    }

    [Fact]
    public void OnceTheUserHasChosenBetweenTwoClaimantsTheNextSyncStopsAsking()
    {
        List<Member> book = [.. Book(), Person("m-rival", "R. Rival", "Senior Warden")];
        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), book);
        OfficerProposal warden = plan.Proposals.Single(p => p.Position == "Senior Warden");

        OfficersTableData chosen = OfficersRosterProjection.Apply(
            Empty(), [new OfficerDecision("Senior Warden", warden.Candidates[1])], plan.Fingerprint);

        OfficersProjection again = OfficersRosterProjection.Plan(chosen, book);
        Assert.DoesNotContain(again.Proposals, p => p.Position == "Senior Warden");
    }

    [Fact]
    public void AnOfficeNobodyClaimsAnyMoreGoesBackToBeingAPrintedVacancy()
    {
        IReadOnlyList<Member> book = Book();
        OfficersProjection first = OfficersRosterProjection.Plan(Empty(), book);
        OfficersTableData filled = OfficersRosterProjection.Apply(Empty(), first.DefaultDecisions, first.Fingerprint);

        List<Member> resigned = [.. book.Where(m => m.Id != "m-treasurer")];
        OfficersProjection plan = OfficersRosterProjection.Plan(filled, resigned);
        OfficerProposal treasurer = plan.Proposals.Single(p => p.Position == "Treasurer");

        Assert.True(treasurer.IsVacancy);
        OfficersTableData result = OfficersRosterProjection.Apply(filled, plan.DefaultDecisions, plan.Fingerprint);
        Assert.Equal("", Row(result, "Treasurer").Name);
        Assert.Null(Row(result, "Treasurer").MemberId);
    }

    /// <summary>
    /// A name somebody typed with no member behind it is not the projection's to take away, even
    /// when the address book puts nobody in that office.
    /// </summary>
    [Fact]
    public void ANameTypedWithNoMemberBehindItIsNeverClearedByASync()
    {
        OfficersTableData current = Empty();
        Row(current, "Chaplain").Name = "T. Typed";

        OfficersProjection plan = OfficersRosterProjection.Plan(current, Book());

        Assert.DoesNotContain(plan.Proposals, p => p.Position == "Chaplain");
    }

    [Fact]
    public void SomebodyMadeInactiveHoldsNoOfficeAsFarAsTheNewsletterIsConcerned()
    {
        List<Member> book = [.. Book().Select(m => m.Id == "m-master" ? m with { IsActive = false } : m)];

        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), book);

        Assert.DoesNotContain(plan.Proposals, p => p.Position == "Worshipful Master");
    }

    [Fact]
    public void TheFingerprintChangesIfAndOnlyIfAContributingFieldChanges()
    {
        IReadOnlyList<Member> book = Book();
        string baseline = OfficersRosterProjection.Fingerprint(book);

        // A birthday is not a contributing field: it reaches the birthday list, never this table.
        List<Member> birthdayMoved =
            [.. book.Select(m => m.Id == "m-master" ? m with { BirthMonth = 5, BirthDay = 1 } : m)];
        Assert.Equal(baseline, OfficersRosterProjection.Fingerprint(birthdayMoved));

        List<Member> renamed =
            [.. book.Select(m => m.Id == "m-master" ? m with { DisplayName = "N. Newname" } : m)];
        Assert.NotEqual(baseline, OfficersRosterProjection.Fingerprint(renamed));

        List<Member> rung = [.. book.Select(m => m.Id == "m-master" ? m with { Phone = "555-0199" } : m)];
        Assert.NotEqual(baseline, OfficersRosterProjection.Fingerprint(rung));

        List<Member> promoted =
            [.. book.Select(m => m.Id == "m-treasurer" ? m with { Office = "Secretary" } : m)];
        Assert.NotEqual(baseline, OfficersRosterProjection.Fingerprint(promoted));
    }

    [Fact]
    public void TheFingerprintDoesNotDependOnWhatOrderTheBookIsIn()
    {
        List<Member> shuffled = [.. Enumerable.Reverse(Book())];
        Assert.Equal(OfficersRosterProjection.Fingerprint(Book()), OfficersRosterProjection.Fingerprint(shuffled));
    }

    [Fact]
    public void AHandTypedTableIsNeverCalledStale()
    {
        OfficersTableData typed = Empty();
        Row(typed, "Worshipful Master").Name = "H. Handtyped";

        Assert.False(OfficersRosterProjection.IsStale(typed, Book()));
    }

    [Fact]
    public void AGeneratedTableGoesStaleWhenSomebodysDetailsChange()
    {
        OfficersProjection plan = OfficersRosterProjection.Plan(Empty(), Book());
        OfficersTableData filled = OfficersRosterProjection.Apply(Empty(), plan.DefaultDecisions, plan.Fingerprint);

        Assert.False(OfficersRosterProjection.IsStale(filled, Book()));

        List<Member> moved = [.. Book().Select(m => m.Id == "m-master" ? m with { Phone = "555-0198" } : m)];
        Assert.True(OfficersRosterProjection.IsStale(filled, moved));
    }

    [Fact]
    public void CountForCountsOfficesRatherThanPeople()
    {
        List<Member> book = [.. Book(), Person("m-rival", "R. Rival", "Senior Warden")];

        Assert.Equal(3, OfficersRosterProjection.CountFor(book));
        Assert.Equal(0, OfficersRosterProjection.CountFor([]));
    }

    // ---- the v1 → v2 migration -------------------------------------------------------------------

    [Fact]
    public void AV1PayloadMigratesToAHandTypedTable()
    {
        JsonNode v1 = JsonNode.Parse("""
            {
              "heading": "Lodge Officers",
              "vacantText": "(vacant)",
              "officers": [ { "position": "Worshipful Master", "name": "A. Placeholder" } ]
            }
            """)!;

        Assert.True(Definition.TryMigrateStep(v1, 1, out JsonNode upgraded, out int toVersion));
        Assert.Equal(2, toVersion);
        Assert.Equal(OfficersTableSource.Manual, upgraded["source"]!.GetValue<string>());
    }

    [Fact]
    public void AV1PayloadReadsBackAsATableNoSyncWouldTouch()
    {
        JsonElement v1 = JsonSerializer.Deserialize<JsonElement>("""
            {
              "heading": "Lodge Officers",
              "vacantText": "(vacant)",
              "officers": [ { "position": "Worshipful Master", "name": "A. Placeholder" } ]
            }
            """);

        Assert.True(Definition.TryReadData(v1, 1, out object typed));
        var data = (OfficersTableData)typed;

        Assert.Equal(OfficersTableSource.Manual, data.Source);
        Assert.False(OfficersRosterProjection.IsStale(data, Book()));
        Assert.All(data.Officers, o => Assert.False(o.IsManual));
    }

    /// <summary>
    /// The equality guard in the setters is load-bearing, not tidiness: both editors write the box's
    /// value back on LostFocus, so walking through the twelve screens without typing a character must
    /// not mark every row manual and freeze the whole table against every future re-sync.
    /// </summary>
    [Fact]
    public void OnlyTheRowTheUserActuallyTypedIntoBecomesHisOwn()
    {
        Wizards.WizardSession session =
            Wizards.WizardSession.Create(Definition, null, 1, WidgetTestData.Seed);

        Assert.True(session.TryGoNext());          // the heading screen → Worshipful Master
        session.SetValue("name", "T. Typed");

        for (int i = 0; i < 12; i++)
        {
            Assert.True(session.TryGoNext());      // through the other eleven, touching nothing
        }

        Assert.True(session.IsReviewScreen);
        Assert.True(session.TryCommit(out JsonElement written, out int version, out _));
        Assert.True(Definition.TryReadData(written, version, out object typed));
        var data = (OfficersTableData)typed;

        Assert.True(Row(data, "Worshipful Master").IsManual);
        Assert.All(
            data.Officers.Where(o => o.Position != "Worshipful Master"),
            o => Assert.False(o.IsManual));

        // And the projection therefore leaves his row alone while filling the others in.
        OfficersProjection plan = OfficersRosterProjection.Plan(data, Book());
        Assert.Contains(plan.KeptManual, e => e.Position == "Worshipful Master");
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    private static OfficersTableData Empty() => Definition.CreateEmpty(WidgetTestData.Seed);

    private static OfficerEntry Row(OfficersTableData data, string position) =>
        data.Officers.Single(o => o.Position == position);

    private static Member Person(string id, string name, string? office, string? phone = null) => new()
    {
        Id = id,
        DisplayName = name,
        Office = office,
        Phone = phone,
    };

    /// <summary>
    /// Three offices filled in, one man with no office at all, and one office written the short way
    /// a secretary actually writes it.
    /// </summary>
    private static List<Member> Book() =>
    [
        Person("m-master", "A. Placeholder", "Worshipful Master", "555-0100"),
        Person("m-warden", "B. Sample", "SW"),
        Person("m-treasurer", "C. Example", "Treas.", "555-0102"),
        Person("m-nobody", "D. Nobody", null),
    ];

    private static string Printed(OfficersTableData data) =>
        string.Join("|", data.Officers.Select(o => $"{o.Position}={o.Name}/{o.Phone}"));
}
