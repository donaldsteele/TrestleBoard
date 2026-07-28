using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The search itself (PLAN.md §11 M21, §12 gate 18). Everything here is about ORDER: a find command
/// that returns the right set in the wrong order is a find command that jumps the user around the
/// newsletter, and nobody would be able to say what it was doing.
/// </summary>
public sealed class StoryFinderTests
{
    /// <summary>
    /// Two frames, one story, the second frame continuing the first — the shape a long article
    /// takes. Plus a second story on a later page, so reading order has something to prove.
    /// </summary>
    private static Document TwoStories()
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master-1" });

        document.Stories.Add(Story("story-1", "The stated communication opens at seven.", "Supper follows the stated business."));
        document.Stories.Add(Story("story-2", "The stated committee meets afterwards."));

        var one = new Page { Id = "page-1", MasterRef = "master-1" };
        one.Blocks.Add(new TextBlock
        {
            Id = "text-a",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 200f, 60f),
            LinkNext = "text-b",
        });
        one.Blocks.Add(new TextBlock
        {
            Id = "text-b",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 200f, 200f, 200f),
        });
        document.Pages.Add(one);

        var two = new Page { Id = "page-2", MasterRef = "master-1" };
        two.Blocks.Add(new TextBlock
        {
            Id = "text-c",
            StoryRef = "story-2",
            FrameRect = new RectPt(54f, 54f, 200f, 200f),
        });
        document.Pages.Add(two);

        return document;
    }

    private static Story Story(string id, params string[] paragraphs)
    {
        var story = new Story { Id = id };
        foreach (string text in paragraphs)
        {
            story.Paragraphs.Add(new StoryParagraph
            {
                ParagraphStyleRef = "body",
                Runs = [new StoryRun { Text = text }],
            });
        }

        return story;
    }

    /// <summary>
    /// A story flowing through two frames appears ONCE, at the frame it starts in. Listing it twice
    /// would find every word in it twice, which is how a find command loses a user's trust in one
    /// press of the button.
    /// </summary>
    [Fact]
    public void AStoryFlowingThroughTwoFramesIsSearchedOnce()
    {
        Assert.Equal(["story-1", "story-2"], StoryFinder.StoryOrder(TwoStories()));
    }

    [Fact]
    public void HitsComeBackInReadingOrderAcrossParagraphsAndStories()
    {
        IReadOnlyList<FindHit> hits = StoryFinder.All(TwoStories(), "stated", matchCase: true);

        Assert.Equal(3, hits.Count);
        Assert.Equal(("story-1", 0), (hits[0].StoryId, hits[0].ParagraphIndex));
        Assert.Equal(("story-1", 1), (hits[1].StoryId, hits[1].ParagraphIndex));
        Assert.Equal(("story-2", 0), (hits[2].StoryId, hits[2].ParagraphIndex));
    }

    /// <summary>
    /// The second paragraph of story-1 is laid out in the SECOND frame of the chain. Reaching it is
    /// M21's acceptance criterion, and it is true here for the reason it should be: a chain is one
    /// story, so nothing about it is a special case.
    /// </summary>
    [Fact]
    public void FindNextWalksOnFromOneHitToTheNextAndWrapsRoundAtTheEnd()
    {
        Document document = TwoStories();

        FindHit first = Assert.IsType<FindHit>(
            StoryFinder.Next(document, "stated", after: null, matchCase: true, out bool wrappedFirst));
        Assert.False(wrappedFirst);
        Assert.Equal(0, first.ParagraphIndex);

        FindHit second = Assert.IsType<FindHit>(
            StoryFinder.Next(document, "stated", first, matchCase: true, out _));
        Assert.Equal(("story-1", 1), (second.StoryId, second.ParagraphIndex));

        FindHit third = Assert.IsType<FindHit>(
            StoryFinder.Next(document, "stated", second, matchCase: true, out _));
        Assert.Equal("story-2", third.StoryId);

        FindHit round = Assert.IsType<FindHit>(
            StoryFinder.Next(document, "stated", third, matchCase: true, out bool wrapped));
        Assert.True(wrapped);
        Assert.Equal(first, round);
    }

    [Fact]
    public void CaseIsIgnoredUnlessItIsAskedFor()
    {
        Document document = TwoStories();

        Assert.Empty(StoryFinder.All(document, "STATED", matchCase: true));
        Assert.Equal(3, StoryFinder.All(document, "STATED", matchCase: false).Count);
    }

    /// <summary>Overlapping matches are counted once each, not once per character.</summary>
    [Fact]
    public void RepeatedLettersFindOneMatchPerOccurrence()
    {
        var document = new Document();
        document.PageMasters.Add(new PageMaster { Id = "master-1" });
        document.Stories.Add(Story("story-1", "aaaa"));
        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        page.Blocks.Add(new TextBlock
        {
            Id = "text-a",
            StoryRef = "story-1",
            FrameRect = new RectPt(0f, 0f, 100f, 100f),
        });
        document.Pages.Add(page);

        Assert.Equal(2, StoryFinder.All(document, "aa", matchCase: true).Count);
    }

    /// <summary>An empty search finds nothing rather than everything.</summary>
    [Fact]
    public void SearchingForNothingFindsNothing()
    {
        Assert.Empty(StoryFinder.All(TwoStories(), "", matchCase: false));
        Assert.Null(StoryFinder.Next(TwoStories(), "", after: null, matchCase: false, out _));
    }

    /// <summary>
    /// A story no frame shows is not searched: selecting a hit in it would scroll the user to
    /// nowhere and leave the caret somewhere they cannot see.
    /// </summary>
    [Fact]
    public void AStoryWithNoFrameIsNotSearched()
    {
        Document document = TwoStories();
        document.Stories.Add(Story("story-orphan", "The stated words nobody can see."));

        Assert.Equal(3, StoryFinder.All(document, "stated", matchCase: true).Count);
    }
}
