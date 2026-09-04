using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Settings;
using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The two-tab person form (M88), and the four ways a form in two halves goes wrong.
///
/// <para>Every person here is fictional and the suite runs against a temporary app-state root, so
/// none of this can reach a real address book (PLAN.md §0 rule 5).</para>
/// </summary>
public sealed class PeopleFormTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private const int Basics = 0;
    private const int AddressAndMore = 1;

    private static RosterService NewRoster([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "people-form-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new RosterService(new RosterStore(path));
    }

    /// <summary>
    /// <b>The completeness gate.</b> Every stored text property on <see cref="Member"/> has a box on
    /// this form, or is on the short list below with a reason.
    ///
    /// <para>M88 took the form from seven fields to twenty-two, and the failure mode it is guarding
    /// against is the quietest one in the app: a property added to the model, wired through the
    /// importer and the export, and never given a box — so the committee can receive the field from
    /// a spreadsheet and then never see or correct it. <c>Notes</c> was exactly that for seventy-six
    /// milestones.</para>
    /// </summary>
    [Fact]
    public async Task EveryTextFieldOnAMemberCanBeTypedOnThisForm()
    {
        (string Property, string Why)[] notOnTheForm =
        [
            (nameof(Member.Id), "machinery: assigned by the roster, never typed"),
            (nameof(Member.SortName), "no UI since M12; filing order is not something the committee "
                + "has asked to set, and a box for it would need explaining"),
            (nameof(Member.DegreeDate), "shaped: its own box, parsed and refused in words"),
            (nameof(Member.DegreeKind), "M12's raised/initiated, kept for old books and old files; "
                + "M88 shows Degree instead and nothing writes this from the form"),
            (nameof(Member.PassedOn), "shaped: a tick box and a date that appears with it"),
            (nameof(Member.Degree), "shaped: a combo box of the three degrees"),
        ];

        await Session.Dispatch(() =>
        {
            var window = new PeopleWindow(NewRoster());

            string[] stored = [.. typeof(Member).GetProperties()
                .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
                .Select(p => p.Name)];

            string[] expected = [.. stored.Except(notOnTheForm.Select(n => n.Property))];
            string[] actual = [.. window.FormFieldsForTest];

            Assert.Equal(
                [.. expected.Order(StringComparer.Ordinal)],
                [.. actual.Order(StringComparer.Ordinal)]);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An edit typed on the second tab is an unsaved edit, exactly as one typed on the first is.
    /// Without this, walking away from a newly typed address loses it in silence — the M40 question
    /// would simply never be asked.
    /// </summary>
    [Fact]
    public async Task AnEditOnTheSecondTabCountsAsAnUnsavedEdit()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(
                new Member { Id = "person-1", DisplayName = "A. Placeholder" },
                "Add A. Placeholder");

            var window = new PeopleWindow(roster);
            window.SelectForTest("person-1");
            Assert.False(window.FormHasUnsavedEditsForTest);

            window.SelectedTabForTest = AddressAndMore;
            Assert.False(window.FormHasUnsavedEditsForTest, "looking at a tab is not editing");

            window.TypeForTest(nameof(Member.City), "Anytown");
            Assert.True(window.FormHasUnsavedEditsForTest);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Saving writes both tabs, whichever one happens to be showing.</summary>
    [Fact]
    public async Task SavingFromTheFirstTabStillWritesWhatWasTypedOnTheSecond()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(
                new Member { Id = "person-1", DisplayName = "A. Placeholder" },
                "Add A. Placeholder");

            var window = new PeopleWindow(roster);
            window.SelectForTest("person-1");

            window.SelectedTabForTest = AddressAndMore;
            window.TypeForTest(nameof(Member.AddressLine1), "1 Example Street");
            window.TypeForTest(nameof(Member.City), "Anytown");
            window.TypeForTest(nameof(Member.MobilePhone), "555-0202");
            window.TypeForTest(nameof(Member.SpouseName), "B. Fictitious");
            window.TypeForTest(nameof(Member.Notes), "Telephone in the evening.");

            window.SelectedTabForTest = Basics;
            Assert.True(window.SaveForTest());

            Member saved = roster.Book.Find("person-1")!;
            Assert.Equal("1 Example Street", saved.AddressLine1);
            Assert.Equal("Anytown", saved.City);
            Assert.Equal("555-0202", saved.MobilePhone);
            Assert.Equal("B. Fictitious", saved.SpouseName);
            Assert.Equal("Telephone in the evening.", saved.Notes);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A refusal about a field on the other tab brings that tab forward before taking the focus.
    /// Otherwise the caret lands on a control nobody can see, and a screen reader reads out a box
    /// its user has no way to reach.
    /// </summary>
    [Fact]
    public async Task ARefusalShowsTheTabTheOffendingFieldIsOn()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            var window = new PeopleWindow(roster);

            window.AddForTest("A. Placeholder", string.Empty, string.Empty);
            window.SelectedTabForTest = AddressAndMore;

            // The name is emptied while the second tab is showing: the refusal is about a box on the
            // first, so the first is what has to come forward.
            window.BoxForTest(nameof(Member.DisplayName)).Text = string.Empty;
            Assert.False(window.SaveForTest());

            Assert.Equal(Basics, window.SelectedTabForTest);
            Assert.Contains("Type a name first", window.StatusTextForTest, StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The year is refused in words, like the birthday above it — never dropped in silence, which is
    /// what somebody who watched their typing disappear would have to guess at.
    /// </summary>
    [Fact]
    public async Task AYearThatIsNotAYearIsRefusedInWords()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            var window = new PeopleWindow(roster);
            window.AddForTest("A. Placeholder", "7/4", "555-0100");

            window.BirthYearBoxForTest.Text = "fifty-seven";
            Assert.False(window.SaveForTest());
            Assert.Contains("four digits", window.StatusTextForTest, StringComparison.Ordinal);

            window.BirthYearBoxForTest.Text = "1957";
            Assert.True(window.SaveForTest());
            Assert.Equal(1957, roster.Book.Members[0].BirthYear);

            // …and the printed birthday is still the month and the day, with no year in it (M88).
            Assert.Equal("7/4", roster.Book.Members[0].BirthdayText);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The search box looks at what a committee member would search by — including, from M88, a
    /// member number read off a card and the street somebody lives on.
    /// </summary>
    [Fact]
    public async Task SearchingFindsAPersonByMemberNumberOrStreet()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(
                new Member
                {
                    Id = "person-1",
                    DisplayName = "A. Placeholder",
                    MemberNumber = "98501",
                    AddressLine1 = "1 Example Street",
                },
                "Add A. Placeholder");
            roster.Save(
                new Member { Id = "person-2", DisplayName = "B. Sample" },
                "Add B. Sample");

            var window = new PeopleWindow(roster);

            window.SearchBoxForTest.Text = "98501";
            Assert.Equal(["person-1"], window.ShownForTest.Select(m => m.Id));

            window.SearchBoxForTest.Text = "Example Street";
            Assert.Equal(["person-1"], window.ShownForTest.Select(m => m.Id));
        }, TestContext.Current.CancellationToken);
    }
}
