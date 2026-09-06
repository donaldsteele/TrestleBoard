using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M107: leaving the front page out of the numbering.
///
/// <para>The cover already says the lodge and the month in letters an inch high. Saying them again
/// in grey 9pt underneath is the app talking over its own user, and no printed newsletter the
/// committee has produced numbered its own front page.</para>
/// </summary>
public sealed class FrontPageNumberingTests
{
    private static ActionContext Newsletter(bool footerShowing) => new()
    {
        HasDocument = true,
        PageCount = 6,
        PageIndex = 0,
        IssueDateChosen = true,
        PageFooterShowing = footerShowing,
    };

    /// <summary>With the line on, the question can be asked.</summary>
    [Fact]
    public void WithTheLineOnItCanBeReached()
    {
        Assert.True(
            ActionCatalog.Evaluate(ActionId.FooterNotOnFrontPage, Newsletter(footerShowing: true))
                .IsAvailable);
    }

    /// <summary>
    /// With the line off altogether the question is meaningless, and the refusal offers the toggle
    /// that turns it on — rather than leaving somebody to work out which of the two they wanted.
    /// </summary>
    [Fact]
    public void WithTheLineOffItRefusesTowardsTheToggleThatTurnsItOn()
    {
        ActionAvailability availability = ActionCatalog.Evaluate(
            ActionId.FooterNotOnFrontPage, Newsletter(footerShowing: false));

        Assert.False(availability.IsAvailable);
        Assert.Equal(ActionId.ShowPageFooter, availability.RemedyId);
    }

    /// <summary>And with nothing open at all, it is the ordinary no-newsletter refusal.</summary>
    [Fact]
    public void WithNoNewsletterItIsRefusedTowardsStartingOne()
    {
        ActionAvailability availability =
            ActionCatalog.Evaluate(ActionId.FooterNotOnFrontPage, new ActionContext());

        Assert.False(availability.IsAvailable);
        Assert.Equal(ActionId.NewFromTemplate, availability.RemedyId);
    }
}
