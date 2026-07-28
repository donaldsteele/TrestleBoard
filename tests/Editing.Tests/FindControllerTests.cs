using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Find and replace (PLAN.md §11 M21, §12 gate 18). The headline test is the acceptance criterion:
/// <b>find reaches the second frame of a linked chain</b> — and it does so with no code that knows
/// anything about chains, because a chain is one story and this searches stories.
/// </summary>
public sealed class FindControllerTests : IDisposable
{
    private static readonly Lazy<FontStore> Fonts = new(BundledFonts.CreateDefaultStore);

    /// <summary>The first frame holds about two lines; everything after that lands in the second.</summary>
    private const string LongArticle =
        "Brethren, the stated communication opens at seven o'clock in the evening. "
        + "Supper is served beforehand in the dining room, and the Tyler will admit "
        + "latecomers between the degrees. The Secretary asks that every brother check "
        + "the roll and correct his telephone number before the stated close of business.";

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _source;
    private readonly TextEditorController _editor;
    private readonly FindController _find;

    public FindControllerTests()
    {
        Document document = BuildChainedDocument();
        _session = new DocumentSession(document);
        _source = DocumentRenderSource.CreateEditable(
            document, new Dictionary<string, byte[]>(), Fonts.Value, _session, options: null, widgets: null);
        _editor = new TextEditorController(_session, _source, new FakeClipboard());
        _find = new FindController(_session, _editor);
    }

    public void Dispose() => _source.Dispose();

    /// <summary>Two frames on two pages, linked, sharing one story — a long article.</summary>
    private static Document BuildChainedDocument()
    {
        var document = new Document();
        document.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = BundledFonts.BodyFamily,
            SizePt = 12f,
        });
        document.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef
        {
            Name = "body",
            CharacterStyleRef = "body",
            LineSpacing = 1.2f,
        });
        document.PageMasters.Add(new PageMaster { Id = "master-1" });

        var story = new Story { Id = "story-1" };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = "body",
            Runs = [new StoryRun { Text = LongArticle }],
        });
        document.Stories.Add(story);

        var one = new Page { Id = "page-1", MasterRef = "master-1" };
        one.Blocks.Add(new TextBlock
        {
            Id = "text-head",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 300f, 34f),
            LinkNext = "text-tail",
        });
        document.Pages.Add(one);

        var two = new Page { Id = "page-2", MasterRef = "master-1" };
        two.Blocks.Add(new TextBlock
        {
            Id = "text-tail",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 300f, 500f),
        });
        document.Pages.Add(two);

        return document;
    }

    /// <summary>
    /// <b>M21's acceptance criterion.</b> The words are near the end of the article, which is laid
    /// out in the SECOND frame of the chain, on the second page — and the editor lands there.
    /// </summary>
    [Fact]
    public void FindReachesTheSecondFrameOfALinkedChain()
    {
        _find.SearchText = "telephone";
        Assert.True(_find.FindNext());

        Assert.True(_editor.IsActive);
        Assert.Equal("text-tail", _editor.BlockId);
        Assert.Equal("telephone", _editor.SelectedText);
    }

    /// <summary>The first hit is in the first frame, so the two really are told apart.</summary>
    [Fact]
    public void AHitInTheFirstFrameStaysInTheFirstFrame()
    {
        _find.SearchText = "Brethren";
        Assert.True(_find.FindNext());

        Assert.Equal("text-head", _editor.BlockId);
    }

    [Fact]
    public void FindNextWalksOnAndSaysWhenItHasCarriedOnFromTheBeginning()
    {
        _find.SearchText = "stated";
        Assert.Equal(2, _find.CountAll());

        Assert.True(_find.FindNext());
        int first = _find.Current!.Value.Offset;
        Assert.True(_find.FindNext());
        Assert.True(_find.Current!.Value.Offset > first);

        // Past the last one, so this is the wrap.
        Assert.True(_find.FindNext());

        Assert.Equal(first, _find.Current!.Value.Offset);
        Assert.Contains("Carried on from the beginning", _find.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The empty result names what was NOT searched. A search that silently skipped the officers
    /// table would teach the user the words are not in the newsletter when they are.
    /// </summary>
    [Fact]
    public void NothingFoundSaysSoAndSaysWhatWasNotSearched()
    {
        _find.SearchText = "Zamboni";

        Assert.False(_find.FindNext());
        Assert.Contains("is not in the writing on the page", _find.StatusMessage, StringComparison.Ordinal);
        Assert.Contains(FindController.WidgetsNotSearched, _find.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacingOneIsOneUndoStepAndPutsTheOldWordsBack()
    {
        _find.SearchText = "Secretary";
        _find.ReplacementText = "Treasurer";
        Assert.True(_find.FindNext());

        string before = Text();
        Assert.True(_find.ReplaceCurrent());
        Assert.Contains("Treasurer", Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("Secretary", Text(), StringComparison.Ordinal);

        _session.Undo();
        Assert.Equal(before, Text());
    }

    /// <summary>
    /// Replacing everything is one thing the user did, so it is one Ctrl+Z. Forty presses to take
    /// back one command would be a punishment for using it.
    /// </summary>
    [Fact]
    public void ReplacingEveryOneIsStillOneUndoStep()
    {
        _find.SearchText = "stated";
        _find.ReplacementText = "regular";

        string before = Text();
        Assert.Equal(2, _find.ReplaceAll());
        Assert.DoesNotContain("stated", Text(), StringComparison.Ordinal);
        Assert.Equal(2, CountOf("regular"));
        Assert.Equal("Replace all", _session.UndoDescription);

        _session.Undo();
        Assert.Equal(before, Text());
    }

    /// <summary>Replacing with nothing at all takes the words out.</summary>
    [Fact]
    public void ReplacingWithNothingDeletesTheWords()
    {
        _find.SearchText = "Brethren, ";
        _find.ReplacementText = "";

        Assert.Equal(1, _find.ReplaceAll());
        Assert.StartsWith("the stated communication", Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void CapitalLettersOnlyMatterWhenAskedFor()
    {
        _find.SearchText = "BRETHREN";
        _find.MatchCase = true;
        Assert.False(_find.FindNext());

        _find.MatchCase = false;
        Assert.True(_find.FindNext());
    }

    [Fact]
    public void SearchingForNothingAsksForTheWordsRatherThanDoingNothing()
    {
        Assert.False(_find.FindNext());
        Assert.Equal("Type the words you are looking for first.", _find.StatusMessage);
    }

    private string Text() =>
        Core.Text.StoryNavigator.GetParagraphText(_session.Document.GetStory("story-1").Paragraphs[0]);

    private int CountOf(string needle) =>
        Core.Text.StoryFinder.All(_session.Document, needle, matchCase: true).Count;
}
