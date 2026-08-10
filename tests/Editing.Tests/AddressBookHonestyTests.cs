using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// PLAN.md §12 gate 28, the reporting half (M75 (e) and (f)): every refusal about the address book
/// NAMES THE MONTH it is talking about, and an address book that could not be read is never
/// described as an empty one.
///
/// <para>The owner spent two agents tracing a birthday list that would not fill in. What the app
/// told him was "Nobody in your address book has a birthday in this issue's month" — of a book with
/// a July birthday in it, about a newsletter that thought it was January. He read "this issue's
/// month" as July, because that is the issue he was making. A sentence naming January would have
/// ended it in one glance, which is why these tests are about words.</para>
/// </summary>
public sealed class AddressBookHonestyTests
{
    /// <summary>A January newsletter with a birthday list chosen and a full address book.</summary>
    private static ActionContext JanuaryIssue() => new()
    {
        HasDocument = true,
        PageCount = 4,
        HasCoverHeading = true,
        IssueDateChosen = true,
        IssueMonth = 1,
        Selection = SelectionKind.Widget,
        CanEditWidget = true,
        WidgetTypeId = "birthdayList",
        RosterCount = 40,
        RosterBirthdaysThisMonth = 0,
    };

    /// <summary>
    /// M75 (e) defect 1: the refusal names January.
    ///
    /// <para>Failed before the fix — the sentence read "in this issue's month", which is the wording
    /// that let a wrong issue date hide for sixty milestones.</para>
    /// </summary>
    [Fact]
    public void NobodyHasABirthdayThisMonthSaysWhichMonthItMeans()
    {
        ActionAvailability availability =
            ActionCatalog.Evaluate(ActionId.SyncBirthdays, JanuaryIssue());

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Equal(
            "Nobody in your address book has a birthday in January, which is the month this issue "
            + "is for. You can still type a birthday in yourself, or add the missing dates in the "
            + "People window.",
            availability.Reason);
    }

    /// <summary>Every month, so the sentence cannot be right for January by accident.</summary>
    [Theory]
    [InlineData(3, "March")]
    [InlineData(7, "July")]
    [InlineData(12, "December")]
    public void AndItNamesWhicheverMonthTheIssueIsFor(int month, string named)
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.SyncBirthdays, JanuaryIssue() with { IssueMonth = month });

        Assert.Contains(named, availability.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("this issue's month", availability.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// M75 (e) defect 2: the card offers to fill an EMPTY list in, and names the month while it does.
    ///
    /// <para>Failed before the fix — the row was drawn only for a stale list, and a freshly inserted
    /// or template-shipped list is <c>Manual</c>, which is never stale. On a new newsletter the
    /// feature never advertised itself at all.</para>
    /// </summary>
    [Fact]
    public void TheCardOffersToFillInAnEmptyBirthdayList()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            IssueMonth = 7,
            BirthdayListIsEmpty = true,
            RosterCount = 40,
            RosterBirthdaysThisMonth = 3,
            ExportedPdfThisSession = true,
        };

        NextStep step = Assert.Single(
            WhatsNext.Suggestions(context), s => s.ActionId == ActionId.SyncBirthdays);

        Assert.Equal("Fill in the birthday list", step.Title);
        Assert.Equal(
            "The birthday list on the page is empty, and your address book has 3 people born in "
            + "July, which is the month this issue is for.",
            step.Why);
    }

    /// <summary>
    /// And the button that row leads to can actually run — gate 24's rule, which is why the offer
    /// and the availability had to change together.
    /// </summary>
    [Fact]
    public void AndTheCommandThatRowLeadsToIsAvailableWithNothingChosen()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            IssueMonth = 7,
            BirthdayListIsEmpty = true,
            RosterCount = 40,
            RosterBirthdaysThisMonth = 3,
        };

        Assert.True(ActionCatalog.Evaluate(ActionId.SyncBirthdays, context).IsAvailable);
    }

    /// <summary>
    /// The stale row names the month too — the same sentence read beside a cover that says July.
    /// </summary>
    [Fact]
    public void TheStaleRowNamesTheMonthAsWell()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            IssueMonth = 7,
            BirthdayListIsStale = true,
            ExportedPdfThisSession = true,
        };

        NextStep step = Assert.Single(
            WhatsNext.Suggestions(context), s => s.ActionId == ActionId.SyncBirthdays);

        Assert.Equal("Update the birthday list", step.Title);
        Assert.Contains("July", step.Why, StringComparison.Ordinal);
    }

    /// <summary>
    /// M75 (f): an address book that could not be read is not an empty address book, and the three
    /// rules that could say "your address book is empty" now check first.
    ///
    /// <para>Failed before the fix: each of these read "Your address book is empty…" and pointed the
    /// user at Import, which over a file that was merely locked is an invitation to overwrite a good
    /// address book he still has.</para>
    /// </summary>
    [Theory]
    [InlineData(ActionId.SyncBirthdays)]
    [InlineData(ActionId.SyncOfficers)]
    [InlineData(ActionId.ExportPeople)]
    public void AnUnreadableAddressBookIsNeverCalledAnEmptyOne(string actionId)
    {
        // Count zero, exactly as an unreadable book presents: RosterService hands out an empty
        // placeholder when the file will not load, which is the whole trap.
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            IssueMonth = 7,
            Selection = SelectionKind.Widget,
            CanEditWidget = true,
            WidgetTypeId = actionId == ActionId.SyncOfficers ? "officersTable" : "birthdayList",
            RosterCount = 0,
            RosterCouldNotBeRead = true,
        };

        ActionAvailability unreadable = ActionCatalog.Evaluate(actionId, context);
        ActionAvailability empty =
            ActionCatalog.Evaluate(actionId, context with { RosterCouldNotBeRead = false });

        Assert.Equal(ActionAvailabilityKind.Blocked, unreadable.Kind);
        Assert.Equal(ActionCatalog.CouldNotReadTheAddressBook, unreadable.Reason);
        Assert.NotEqual(empty.Reason, unreadable.Reason);
        Assert.DoesNotContain("is empty", unreadable.Reason, StringComparison.Ordinal);

        // No remedy: there is no command in this app that can unlock somebody else's file, and
        // offering Import here would be offering to overwrite the book that could not be read.
        Assert.Null(unreadable.RemedyId);
        Assert.Contains("It is not empty", unreadable.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// M75 (f) on the "what's next" card: telling somebody to fill in an address book he has
    /// already filled in is how a locked file becomes an afternoon's work.
    /// </summary>
    [Fact]
    public void TheCardDoesNotTellHimToFillInABookHeHasAlreadyFilledIn()
    {
        var context = new ActionContext
        {
            HasDocument = true,
            IssueDateChosen = true,
            IssueMonth = 7,
            RosterEmptyButNeeded = true,
            RosterCouldNotBeRead = true,
            ExportedPdfThisSession = true,
        };

        NextStep step = Assert.Single(WhatsNext.Suggestions(context));

        Assert.Equal("Let TrestleBoard reach your address book", step.Title);
        Assert.Contains("could not be read", step.Why, StringComparison.Ordinal);
        Assert.Contains("It is not empty.", step.Why, StringComparison.Ordinal);
    }
}
