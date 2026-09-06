using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using HeadlessUnitTestSession = Avalonia.Headless.HeadlessUnitTestSession;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M105: what this newsletter is called.
///
/// <para>The lodge's name prints in every page footer, goes out as the PDF's Author and is half the
/// email subject; the title names the exported file; the meeting rule is what next month's dates are
/// worked out from. All three lived in <see cref="DocumentMetadata"/> with exactly one writer — the
/// cover heading's wizard — so a newsletter with no cover heading could not say which lodge it
/// belonged to at all.</para>
///
/// <para>Every lodge named here is fictional (PLAN.md §0).</para>
/// </summary>
public sealed class AboutThisNewsletterTests
{
    private static ActionContext Newsletter(bool coverHeading) => new()
    {
        HasDocument = true,
        PageCount = 4,
        PageIndex = 0,
        IssueDateChosen = true,
        HasCoverHeading = coverHeading,
    };

    /// <summary>
    /// <b>The point of the milestone.</b> Without a cover heading the issue-date question is
    /// refused — correctly, it is asked on the banner — and before M105 that refusal took the lodge
    /// name with it, because the wizard was the only writer of any of the three.
    /// </summary>
    [Fact]
    public void ANewsletterWithNoCoverHeadingCanStillSayWhichLodgeItIs()
    {
        Assert.False(
            ActionCatalog.Evaluate(ActionId.SetIssueDate, Newsletter(coverHeading: false)).IsAvailable);

        Assert.True(
            ActionCatalog.Evaluate(ActionId.AboutThisNewsletter, Newsletter(coverHeading: false))
                .IsAvailable);
    }

    /// <summary>With no newsletter open there is nothing to be called anything.</summary>
    [Fact]
    public void WithNoNewsletterItIsRefusedTowardsStartingOne()
    {
        ActionAvailability availability =
            ActionCatalog.Evaluate(ActionId.AboutThisNewsletter, new ActionContext());

        Assert.False(availability.IsAvailable);
        Assert.Equal(ActionId.NewFromTemplate, availability.RemedyId);
    }

    /// <summary>
    /// The issue date is shown and not asked, so the sentence has to say where it IS asked —
    /// otherwise the window silently withholds the one fact the reader came looking for.
    /// </summary>
    [Fact]
    public void ItSaysWhichIssueThisIsAndWhereToChangeIt()
    {
        string line = AboutThisNewsletterDialog.IssueLine(
            new DocumentMetadata { IssueMonth = 9, IssueYear = 2026, IssueDateChosen = true });

        Assert.Contains("September 2026", line, System.StringComparison.Ordinal);
        Assert.Contains("Which issue is this?", line, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// And when nobody has said, it says nobody has said — rather than printing January 2000, which
    /// is the model default M75 went to some trouble to stop being read as an answer.
    /// </summary>
    [Fact]
    public void WhenNobodyHasSaidWhichIssueItSaysSo()
    {
        string line = AboutThisNewsletterDialog.IssueLine(new DocumentMetadata());

        Assert.Contains("Nobody has said", line, System.StringComparison.Ordinal);
        Assert.DoesNotContain("2000", line, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The three answers come back trimmed. A lodge name with a trailing space is a lodge name that
    /// will not match itself in a subject line or a footer, and nobody can see the difference.
    /// </summary>
    [Fact]
    public async Task WhatIsTypedComesBackTrimmed()
    {
        await HeadlessSession.Instance.Dispatch(
            () =>
            {
                var dialog = new AboutThisNewsletterDialog(new DocumentMetadata
                {
                    LodgeName = "  Placeholder Lodge No. 000  ",
                    Title = " Trestle Board ",
                    MeetingRule = " 1st Tuesday ",
                });

                Assert.Equal("Placeholder Lodge No. 000", dialog.LodgeName);
                Assert.Equal("Trestle Board", dialog.NewsletterTitle);
                Assert.Equal("1st Tuesday", dialog.MeetingRule);
                Assert.False(dialog.Confirmed);
                return true;
            },
            TestContext.Current.CancellationToken);
    }

}
