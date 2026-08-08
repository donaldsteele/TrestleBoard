using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// The "what's next" card (PLAN.md §11 M11). It is a checklist, not a robot: nothing here touches
/// the document, and every row says why it is being suggested.
/// </summary>
public sealed class WhatsNextTests
{
    private static ActionContext Open() => new()
    {
        HasDocument = true,
        PageCount = 4,
        CanStartFromLastMonth = true,
        ExportedPdfThisSession = true,
    };

    [Fact]
    public void WithNothingOpenTheOnlySuggestionIsToStartOne()
    {
        IReadOnlyList<NextStep> steps = WhatsNext.Suggestions(ActionContext.Empty);

        NextStep only = Assert.Single(steps);
        Assert.Equal(ActionId.NewFromTemplate, only.ActionId);
    }

    [Fact]
    public void AFinishedIssueHasNothingLeftToSay()
    {
        Assert.Empty(WhatsNext.Suggestions(Open()));
    }

    /// <summary>
    /// M18. A first issue exported with grey rectangles where the photographs should be is the most
    /// visible way this app can let somebody down, and until M18 nothing said a word about it —
    /// there was no command that could fill a placeholder in.
    /// </summary>
    [Fact]
    public void EmptyPictureFramesAreNoticed()
    {
        NextStep only = Assert.Single(WhatsNext.Suggestions(Open() with { HasPicturePlaceholder = true }));

        Assert.Equal(ActionId.ReplacePicture, only.ActionId);
        Assert.Contains("photos", only.Title, StringComparison.OrdinalIgnoreCase);
        // M46: "placeholders" is a word for people who build software.
        Assert.Contains("empty picture frames", only.Why, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnwrittenArticleIsNoticed()
    {
        IReadOnlyList<NextStep> steps = WhatsNext.Suggestions(Open() with { HasUnwrittenArticle = true });

        NextStep only = Assert.Single(steps);
        Assert.Contains("articles", only.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The carry-forward story told end to end: last month's issue comes forward with its prompts
    /// intact, no PDF has been made yet, and the card is what finally says so.
    /// </summary>
    [Fact]
    public void JustAfterCarryingForwardTheCardListsWhatIsLeft()
    {
        ActionContext context = Open() with
        {
            HasUnwrittenArticle = true,
            CoverDateMissing = true,
            ExportedPdfThisSession = false,
        };

        List<string?> actions = WhatsNext.Suggestions(context).Select(s => s.ActionId).ToList();

        Assert.Equal([ActionId.EditWidget, null, ActionId.ExportPdf], actions);
    }

    [Fact]
    public void EverySuggestionSaysWhyItIsThere()
    {
        ActionContext everything = Open() with
        {
            HasUnwrittenArticle = true,
            CoverDateMissing = true,
            HasOversetText = true,
            RosterEmptyButNeeded = true,
            BirthdayListIsStale = true,
            ExportedPdfThisSession = false,
        };

        IReadOnlyList<NextStep> steps = WhatsNext.Suggestions(everything);

        Assert.Equal(6, steps.Count);
        Assert.All(steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Title));
            Assert.EndsWith(".", step.Why, StringComparison.Ordinal);
        });
    }

    /// <summary>Every action a row points at must be a real one, or the row is a dead end.</summary>
    [Fact]
    public void EverySuggestedActionExistsInTheCatalog()
    {
        ActionContext everything = Open() with
        {
            HasUnwrittenArticle = true,
            CoverDateMissing = true,
            HasOversetText = true,
            BirthdayListIsStale = true,
            ExportedPdfThisSession = false,
        };

        foreach (NextStep step in WhatsNext.Suggestions(everything))
        {
            if (step.ActionId is { } id)
            {
                Assert.True(ActionCatalog.TryGet(id, out _), $"unknown action id in a suggestion: {id}");
            }
        }
    }
}
