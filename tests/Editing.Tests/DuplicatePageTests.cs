using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M100: a whole page copied, with everything on it.
///
/// <para>A trestle board repeats its own shape — a photo page this month is a photo page next month
/// with different photographs in it — and the committee's way of making the second one was to build
/// it again frame by frame. <c>item.duplicate</c> has copied ONE thing since M81; nothing has ever
/// copied a page.</para>
/// </summary>
public sealed class DuplicatePageTests
{
    private static PageFlowController Flow(EditorTestHarness harness) =>
        new(harness.Session, harness.Source);

    /// <summary>The copy lands directly after the page it came from, holding the same things.</summary>
    [Fact]
    public void TheCopyLandsAfterItWithEverythingOnIt()
    {
        using var harness = new EditorTestHarness("A notice.", withExclusion: true);
        int blocksBefore = harness.Session.Document.Pages[0].Blocks.Count;

        string id = Assert.IsType<string>(Flow(harness).DuplicatePage(0));

        Assert.Equal(2, harness.Session.Document.Pages.Count);
        Assert.Equal(id, harness.Session.Document.Pages[1].Id);
        Assert.Equal(blocksBefore, harness.Session.Document.Pages[1].Blocks.Count);
    }

    /// <summary>
    /// Everything is in the same place and the same order. A copied page that arrived rearranged
    /// would defeat the whole reason for asking for one.
    /// </summary>
    [Fact]
    public void EverythingIsWhereItWasAndInTheSameOrder()
    {
        using var harness = new EditorTestHarness("A notice.", withExclusion: true);
        Flow(harness).DuplicatePage(0);

        List<Block> from = [.. harness.Session.Document.Pages[0].Blocks.OrderBy(b => b.ZOrder)];
        List<Block> to = [.. harness.Session.Document.Pages[1].Blocks.OrderBy(b => b.ZOrder)];

        Assert.Equal(from.Count, to.Count);
        for (int i = 0; i < from.Count; i++)
        {
            Assert.Equal(from[i].FrameRect.X, to[i].FrameRect.X, 3);
            Assert.Equal(from[i].FrameRect.Y, to[i].FrameRect.Y, 3);
            Assert.Equal(from[i].FrameRect.Width, to[i].FrameRect.Width, 3);
            Assert.Equal(from[i].ZOrder, to[i].ZOrder);
            Assert.Equal(from[i].GetType(), to[i].GetType());
        }
    }

    /// <summary>
    /// A copied piece of writing gets a story of its own holding the same words — never the same
    /// story, because two blocks on one story is what a LINK is, and a copy is not a continuation.
    /// </summary>
    [Fact]
    public void CopiedWritingGetsAStoryOfItsOwnHoldingTheSameWords()
    {
        using var harness = new EditorTestHarness("Notice to the brethren.", withExclusion: false);
        Flow(harness).DuplicatePage(0);

        var original = (Core.Model.TextBlock)harness.Session.Document.Pages[0].Blocks[0];
        var copy = (Core.Model.TextBlock)harness.Session.Document.Pages[1].Blocks[0];

        Assert.NotEqual(original.StoryRef, copy.StoryRef);
        Assert.Equal(
            Core.Text.StoryNavigator.GetParagraphText(
                harness.Session.Document.GetStory(original.StoryRef).Paragraphs[0]),
            Core.Text.StoryNavigator.GetParagraphText(
                harness.Session.Document.GetStory(copy.StoryRef).Paragraphs[0]));
    }

    /// <summary>A picture on the copied page shares the bytes already in the file.</summary>
    [Fact]
    public void ACopiedPictureSharesTheBytesAlreadyInTheFile()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: true);
        Flow(harness).DuplicatePage(0);

        ImageFrame original = harness.Session.Document.Pages[0].Blocks.OfType<ImageFrame>().First();
        ImageFrame copy = harness.Session.Document.Pages[1].Blocks.OfType<ImageFrame>().First();

        Assert.Equal(original.AssetRef, copy.AssetRef);
    }

    /// <summary>Every copied block gets an id of its own — the M91 batch-minting trap.</summary>
    [Fact]
    public void EveryCopiedBlockGetsAnIdOfItsOwn()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: true);
        Flow(harness).DuplicatePage(0);

        List<string> ids = [.. harness.Session.Document.Pages
            .SelectMany(p => p.Blocks)
            .Select(b => b.Id)];

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        List<string> stories = [.. harness.Session.Document.Stories.Select(s => s.Id)];
        Assert.Equal(stories.Count, stories.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>One Ctrl+Z takes the whole page back, blocks, stories and all.</summary>
    [Fact]
    public void OneUndoTakesTheWholePageBack()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: true);
        int storiesBefore = harness.Session.Document.Stories.Count;

        Flow(harness).DuplicatePage(0);
        harness.Session.Undo();

        Assert.Single(harness.Session.Document.Pages);
        Assert.Equal(storiesBefore, harness.Session.Document.Stories.Count);
    }

    /// <summary>The copy is on the same paper as the page it came from.</summary>
    [Fact]
    public void TheCopyIsOnTheSamePaper()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        Flow(harness).DuplicatePage(0);

        Assert.Equal(
            harness.Session.Document.Pages[0].MasterRef,
            harness.Session.Document.Pages[1].MasterRef);
    }

    /// <summary>A page that is not there is refused rather than throwing at the caller.</summary>
    [Fact]
    public void APageThatIsNotThereIsRefused()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);

        Assert.Null(Flow(harness).DuplicatePage(7));
        Assert.Null(Flow(harness).DuplicatePage(-1));
    }
}
