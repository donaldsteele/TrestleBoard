using Avalonia.Controls;
using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The officers wizard after M13: a pick rather than a typing job, a blank phone box filled in for
/// free, and one question on the review screen about sending a corrected number back the other way.
/// Every person here is fictional (PLAN.md §0) and is handed to the window explicitly — the wizard
/// never reaches for an address book (§5).
///
/// The windows are built and rendered but never shown, for the reason <c>WidgetShellTests</c> gives:
/// a shown headless window runs a real layout pass, and this suite's platform has no font manager
/// behind it.
/// </summary>
public sealed class OfficersPickerShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static readonly PersonSuggestion[] People =
    [
        new("m-1", "A. Placeholder", "555-0100"),
        new("m-2", "B. Sample", null),
    ];

    [Fact]
    public void TheOfficersNameFieldAsksForAPersonSoTheWindowOffersThePicker()
    {
        WizardField name = new OfficersTableDefinition().Wizard.Steps
            .SelectMany(s => s.Fields)
            .First(f => f.Key == "name");

        Assert.Equal(WizardFieldKind.Person, name.Kind);
    }

    [Fact]
    public async Task PickingSomebodyFillsInABlankPhoneNumberFromTheAddressBook()
    {
        await Session.Dispatch(() =>
        {
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Name(window).Text = "A. Placeholder";

            Assert.Equal("A. Placeholder", session.GetValue("name"));
            Assert.Equal("555-0100", session.GetValue("phone"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task APhoneNumberTheUserAlreadyTypedIsNeverOverwritten()
    {
        await Session.Dispatch(() =>
        {
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Type(Phone(window), "555-0177");
            Name(window).Text = "A. Placeholder";

            Assert.Equal("555-0177", session.GetValue("phone"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnOfficerWhoIsNotInTheAddressBookIsStillTypeable()
    {
        await Session.Dispatch(() =>
        {
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Name(window).Text = "Z. Visitor";

            Assert.Equal("Z. Visitor", session.GetValue("name"));
            Assert.Equal("", session.GetValue("phone"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACorrectedNumberIsOfferedBackToTheAddressBookAndOnlyIfTheUserSaysSo()
    {
        await Session.Dispatch(() =>
        {
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Name(window).Text = "A. Placeholder";
            Type(Phone(window), "555-0188");

            GoToReview(window, session);

            CheckBox offer = Assert.Single(window.ScreenControlsForTest.OfType<CheckBox>());
            Assert.Contains("A. Placeholder", offer.Content?.ToString() ?? "", StringComparison.Ordinal);

            // Off by default: keeping the book fresh is a favour the user does, not something that
            // happens to them.
            Assert.NotEqual(true, offer.IsChecked);
            Assert.Empty(window.PhoneWriteBacks);

            offer.IsChecked = true;

            (string memberId, string phone) = Assert.Single(window.PhoneWriteBacks);
            Assert.Equal("m-1", memberId);
            Assert.Equal("555-0188", phone);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NobodyIsAskedAboutAPhoneNumberTheyDidNotChange()
    {
        await Session.Dispatch(() =>
        {
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Name(window).Text = "A. Placeholder";
            GoToReview(window, session);

            Assert.Empty(window.ScreenControlsForTest.OfType<CheckBox>());
            Assert.Empty(window.PhoneWriteBacks);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NobodyIsAskedAboutSomebodyWhoWasOnlyTypedIn()
    {
        await Session.Dispatch(() =>
        {
            // No pick means no address-book entry to write back to, whatever is typed in the phone
            // box — the write-back is only ever offered about a brother the user chose.
            (WizardWindow window, WizardSession session) = OnTheFirstOfficer();

            Name(window).Text = "Z. Visitor";
            Type(Phone(window), "555-0199");
            GoToReview(window, session);

            Assert.Empty(window.ScreenControlsForTest.OfType<CheckBox>());
            Assert.Empty(window.PhoneWriteBacks);
        }, TestContext.Current.CancellationToken);
    }

    // ---- scaffolding ---------------------------------------------------------------------------

    /// <summary>An officers wizard rendered on the Worshipful Master's screen.</summary>
    private static (WizardWindow Window, WizardSession Session) OnTheFirstOfficer()
    {
        WizardSession session = WizardSession.Create(
            new OfficersTableDefinition(), null, 1, new WidgetSeed("Sample Lodge 000", 7, 2026, "1st Tuesday"));
        var window = new WizardWindow(session, People);
        window.RenderForTest();

        Assert.True(session.TryGoNext());
        window.RenderForTest();
        Assert.Equal("Worshipful Master", session.ScreenTitle);
        return (window, session);
    }

    private static void GoToReview(WizardWindow window, WizardSession session)
    {
        Assert.True(session.TryGoTo(session.ScreenCount - 1));
        Assert.True(session.IsReviewScreen);
        window.RenderForTest();
    }

    /// <summary>
    /// Types into a box the way a person does. Avalonia raises <c>TextChanged</c> through the
    /// dispatcher, so without draining it the wizard has not heard the keystroke yet and the next
    /// step of the test would be reasoning about a value nobody has been told about.
    /// </summary>
    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static AutoCompleteBox Name(WizardWindow window) =>
        window.ScreenControlsForTest.OfType<AutoCompleteBox>().First();

    private static TextBox Phone(WizardWindow window) => window.ScreenControlsForTest
        .OfType<TextBox>()
        .First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Phone number");
}
