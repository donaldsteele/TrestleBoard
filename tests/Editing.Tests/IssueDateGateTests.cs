using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// PLAN.md §12 gate 28, the catalog half (M75 (c)): every command that needs a real issue date
/// refuses with a plain-English reason and a way out, and none of them blames the address book.
///
/// <para>The sentence the owner was actually reading when he reported this was "Nobody in your
/// address book has a birthday in this issue's month" — said of a book with a 3 July birthday in it,
/// about a newsletter that thought it was January 2000. A refusal that names the wrong cause sends
/// somebody to fix a thing that is not broken.</para>
/// </summary>
public sealed class IssueDateGateTests
{
    /// <summary>A newsletter that has been told which issue it is: everything else is normal.</summary>
    private static ActionContext Dated() => new()
    {
        HasDocument = true,
        PageCount = 4,
        PageIndex = 0,
        CanStartFromLastMonth = true,
        HasCoverHeading = true,
        IssueDateChosen = true,
        ExportedPdfThisSession = true,
    };

    private static ActionContext Undated() => Dated() with { IssueDateChosen = false };

    /// <summary>
    /// The eight commands PLAN.md names. Each one puts the issue's month in front of a reader — in
    /// a file name, a PDF's own metadata, a mail subject to the whole lodge — or goes looking for it
    /// in the archive.
    /// </summary>
    public static TheoryData<string> CommandsThatNeedTheIssueDate() =>
    [
        ActionId.ExportPdf,
        ActionId.ExportDraftPdf,
        ActionId.SaveAs,
        ActionId.SendIt,
        ActionId.ShowLastYear,
        ActionId.ReviewNewsletter,
        ActionId.SyncBirthdays,
        ActionId.StartFromLastMonth,
    ];

    [Theory]
    [MemberData(nameof(CommandsThatNeedTheIssueDate))]
    public void EachOneIsRefusedWithAReasonAndTheCommandThatFixesIt(string actionId)
    {
        ActionContext undated = actionId == ActionId.SyncBirthdays
            ? Undated() with { Selection = SelectionKind.Widget, CanEditWidget = true,
                WidgetTypeId = "birthdayList", RosterCount = 4, RosterBirthdaysThisMonth = 0 }
            : Undated();

        ActionAvailability availability = ActionCatalog.Evaluate(actionId, undated);

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Contains("which month and year", availability.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.SetIssueDate, availability.RemedyId);
    }

    /// <summary><b>Guard.</b> It passes against the pre-M75 tree too — those commands were always
    /// available. It is here so a future tightening of the gate cannot make the answer stop
    /// unblocking them, which would be worse than the defect.</summary>
    [Theory]
    [MemberData(nameof(CommandsThatNeedTheIssueDate))]
    public void EachOneIsPossibleAgainOnceTheQuestionIsAnswered(string actionId)
    {
        ActionContext dated = actionId == ActionId.SyncBirthdays
            ? Dated() with { Selection = SelectionKind.Widget, CanEditWidget = true,
                WidgetTypeId = "birthdayList", RosterCount = 4, RosterBirthdaysThisMonth = 2 }
            : Dated();

        Assert.True(ActionCatalog.Evaluate(actionId, dated).IsAvailable);
    }

    /// <summary>
    /// The exact falsehood M75 was reported against. With no issue date the projection filters the
    /// address book to January, so the count is zero and the next rule down would announce a quiet
    /// month — confidently, about a book full of birthdays.
    /// </summary>
    [Fact]
    public void ABirthdayListIsNeverRefusedByBlamingTheAddressBookForTheMissingDate()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.SyncBirthdays,
            Undated() with
            {
                Selection = SelectionKind.Widget,
                CanEditWidget = true,
                WidgetTypeId = "birthdayList",
                RosterCount = 40,
                RosterBirthdaysThisMonth = 0,
            });

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.DoesNotContain("Nobody in your address book", availability.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.SetIssueDate, availability.RemedyId);
    }

    /// <summary>
    /// The two that are deliberately NOT gated, and the reasons are different. Spelling and reading
    /// aloud are about the words on the page; a template is a layout with the date taken out of it
    /// on purpose, so refusing to make one until a date is filled in would refuse the one thing that
    /// is meant to have none.
    ///
    /// <para><b>Guard</b>: green before M75 as well, since nothing was gated then. Its job is to
    /// stop the gate spreading.</para>
    /// </summary>
    [Theory]
    [InlineData(ActionId.CheckSpelling)]
    [InlineData(ActionId.ReadAloud)]
    [InlineData(ActionId.SaveAsTemplate)]
    [InlineData(ActionId.Save)]
    public void TheCommandsThatDoNotCareAreLeftAlone(string actionId) =>
        Assert.True(ActionCatalog
            .Evaluate(actionId, Undated() with { HasUnsavedChanges = true })
            .IsAvailable);

    // ---- the ask itself -----------------------------------------------------------------------
    // Three guards: ActionId.SetIssueDate did not exist before M75, so none of them could have been
    // written, let alone failed, against the old tree.

    [Fact]
    public void TheQuestionCanBeAskedWheneverThereIsACoverHeadingToAskItOn()
    {
        Assert.True(ActionCatalog.Evaluate(ActionId.SetIssueDate, Undated()).IsAvailable);

        // And afterwards, because the ask is also the correction: a wrong month typed once and
        // unfixable would be worse than no ask at all.
        Assert.True(ActionCatalog.Evaluate(ActionId.SetIssueDate, Dated()).IsAvailable);
    }

    [Fact]
    public void WithNoCoverHeadingItSaysSoAndOffersToAddOne()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.SetIssueDate,
            Undated() with { HasCoverHeading = false });

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Contains("no cover heading", availability.Reason, StringComparison.Ordinal);
        Assert.Equal(ActionId.InsertCoverBanner, availability.RemedyId);
    }

    [Fact]
    public void WithNoNewsletterAtAllItSaysThatInstead()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(ActionId.SetIssueDate, ActionContext.Empty);

        Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
        Assert.Equal(ActionId.NewFromTemplate, availability.RemedyId);
    }

    // ---- the "what's next" card ----------------------------------------------------------------

    [Fact]
    public void TheCardLeadsWithTheQuestionWhileItIsUnanswered()
    {
        IReadOnlyList<NextStep> steps = WhatsNext.Suggestions(
            Undated() with { ExportedPdfThisSession = false, HasPicturePlaceholder = true });

        Assert.Equal(ActionId.SetIssueDate, steps[0].ActionId);
        Assert.Contains("which month and year", steps[0].Why, StringComparison.Ordinal);
    }

    /// <summary><b>Guard</b>: trivially true before M75, when the card never mentioned it at all.</summary>
    [Fact]
    public void OnceAnsweredTheCardStopsMentioningIt() =>
        Assert.DoesNotContain(WhatsNext.Suggestions(Dated()), s => s.ActionId == ActionId.SetIssueDate);

    /// <summary>
    /// The M11 rule this milestone must not break: nothing may become unavailable without saying
    /// why. Asserted over every command in the catalog against an undated newsletter, because eight
    /// new refusals is eight new chances to leave a reason empty.
    ///
    /// <para><b>Guard</b>: <c>ActionAvailability</c> has refused an empty reason at construction
    /// since M11, so this cannot fail today. It is here because eight new refusals is where it
    /// would first start to.</para>
    /// </summary>
    [Fact]
    public void NoRefusalOnAnUndatedNewsletterIsSilent()
    {
        foreach (EditorAction action in ActionCatalog.All)
        {
            ActionAvailability availability = ActionCatalog.Evaluate(action.Id, Undated());
            Assert.False(
                availability.Kind != ActionAvailabilityKind.Available
                    && string.IsNullOrWhiteSpace(availability.Reason),
                $"{action.Id} refuses without saying why");
        }
    }
}
