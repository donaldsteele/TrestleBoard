using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Copying, cutting, pasting and moving things between pages (PLAN.md §11 M91).
///
/// <para>Before this, anything on a page was stuck there: "make another like this" always landed on
/// the same page, and nothing in the app could relocate a block at all. The committee's answer to
/// "this notice belongs on page 4" was to delete it and build it again.</para>
/// </summary>
public sealed class FrameClipboardTests
{
    private static PageFlowController Flow(EditorTestHarness harness) =>
        new(harness.Session, harness.Source);

    /// <summary>Gives the fixture a second page and returns its index.</summary>
    private static int AddPage(EditorTestHarness harness)
    {
        Flow(harness).AddPage(harness.Session.Document.Pages.Count - 1);
        return harness.Session.Document.Pages.Count - 1;
    }

    /// <summary>Makes the chosen frame continue into <paramref name="target"/>.</summary>
    private static void LinkOnward(FrameEditorController frames, string target)
    {
        Assert.True(frames.BeginLink());
        Assert.True(frames.CompleteLink(target));
    }

    private static Page PageOf(EditorTestHarness harness, string blockId)
    {
        Assert.True(harness.Session.Document.TryFindBlock(blockId, out Page? page, out _));
        return page!;
    }

    // ---- copy and paste ------------------------------------------------------------------------

    /// <summary>
    /// The whole point of the milestone: a thing copied on one page can be put down on another, and
    /// it lands in the SAME place — because "the same box in the same spot on page 4" is what the
    /// committee actually asks for.
    /// </summary>
    [Fact]
    public void SomethingCopiedIsPutDownOnTheOtherPageInTheSamePlace()
    {
        using var harness = new EditorTestHarness("Notice to the brethren.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        RectPt before = PageOf(harness, EditorTestHarness.BlockId)
            .GetBlock(EditorTestHarness.BlockId).FrameRect;

        Assert.True(frames.CopySelection());
        IReadOnlyList<string> pasted = frames.PasteOntoPage(second);

        string id = Assert.Single(pasted);
        Assert.Equal("page-2", PageOf(harness, id).Id);

        RectPt after = PageOf(harness, id).GetBlock(id).FrameRect;
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(before.Width, after.Width, 3);
        Assert.Equal(before.Height, after.Height, 3);
    }

    /// <summary>
    /// Onto the page it came from, a copy is offset instead — otherwise the user presses Ctrl+V and
    /// the page appears not to have changed, which is the one outcome a command must never produce.
    /// </summary>
    [Fact]
    public void PastedBackOntoItsOwnPageTheCopyLandsBelowTheOriginal()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        RectPt before = PageOf(harness, EditorTestHarness.BlockId)
            .GetBlock(EditorTestHarness.BlockId).FrameRect;

        Assert.True(frames.CopySelection());
        string id = Assert.Single(frames.PasteOntoPage(0));

        RectPt after = PageOf(harness, id).GetBlock(id).FrameRect;
        Assert.True(after.Y > before.Y, "A copy laid exactly on top of its original is invisible.");
    }

    /// <summary>
    /// A copied piece of writing arrives with a story of its own holding the same words. Never the
    /// same story: two blocks on one story is what a LINK is, and a copy is not a continuation.
    /// </summary>
    [Fact]
    public void ACopiedPieceOfWritingKeepsItsWordsAndGetsAStoryOfItsOwn()
    {
        using var harness = new EditorTestHarness("Notice to the brethren.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CopySelection());
        string id = Assert.Single(frames.PasteOntoPage(second));

        var copy = (TextBlock)PageOf(harness, id).GetBlock(id);
        Assert.NotEqual(EditorTestHarness.StoryId, copy.StoryRef);
        Assert.Null(copy.LinkNext);

        Story story = harness.Session.Document.GetStory(copy.StoryRef);
        Assert.Equal(
            "Notice to the brethren.",
            Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[0]));
    }

    /// <summary>
    /// What is held is a copy taken at the moment of copying. If it were a reference to the block on
    /// the page, editing the original after copying would silently change what came out of the
    /// clipboard — and after a CUT there would be nothing to reference at all.
    /// </summary>
    [Fact]
    public void ChangingTheOriginalAfterCopyingDoesNotChangeWhatIsPutDown()
    {
        using var harness = new EditorTestHarness("First wording.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CopySelection());

        harness.ClickIntoFrame();
        harness.Controller.SelectAll();
        harness.Controller.InsertText("Second wording, typed after the copy was taken.");

        string id = Assert.Single(frames.PasteOntoPage(second));
        var copy = (TextBlock)PageOf(harness, id).GetBlock(id);
        Story story = harness.Session.Document.GetStory(copy.StoryRef);

        Assert.Equal(
            "First wording.",
            Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[0]));
    }

    /// <summary>
    /// Three things pasted at once must get three different ids.
    ///
    /// <para>The id minter answers "what is free" by looking at the document, and the document is
    /// not changed until the whole paste runs as one command — so without a running tally of what
    /// this batch has already claimed, every block in it is handed the same id. Blocks are found by
    /// id document-wide and the FIRST match wins, so the wrong one would be edited from then on.</para>
    /// </summary>
    [Fact]
    public void EveryThingPastedAtOnceGetsAnIdOfItsOwn()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        string b = frames.AddTextFrame(0);
        frames.SelectAll([EditorTestHarness.BlockId, a, b]);

        Assert.True(frames.CopySelection());
        IReadOnlyList<string> pasted = frames.PasteOntoPage(second);

        Assert.Equal(3, pasted.Count);
        Assert.Equal(3, pasted.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, harness.Session.Document.Pages[second].Blocks.Count);

        // Stories too — the same minter, the same trap.
        List<string> stories = [.. harness.Session.Document.Pages[second].Blocks
            .OfType<TextBlock>().Select(t => t.StoryRef)];
        Assert.Equal(stories.Count, stories.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A picture shares the bytes already in the package. A second copy of a three-megabyte
    /// photograph would double the file for no reason a reader could see.
    /// </summary>
    [Fact]
    public void APastedPictureSharesTheBytesAlreadyInTheFile()
    {
        using var harness = new EditorTestHarness("Words.");
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select("img-1");
        Assert.True(frames.CopySelection());
        string id = Assert.Single(frames.PasteOntoPage(second));

        var copy = (ImageFrame)PageOf(harness, id).GetBlock(id);
        Assert.Equal("missing.png", copy.AssetRef);
    }

    /// <summary>Several things pasted together are one thing to take back, not three.</summary>
    [Fact]
    public void PastingSeveralThingsIsOneUndoStep()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        frames.SelectAll([EditorTestHarness.BlockId, a]);
        Assert.True(frames.CopySelection());
        frames.PasteOntoPage(second);

        Assert.Equal(2, harness.Session.Document.Pages[second].Blocks.Count);
        harness.Session.Undo();
        Assert.Empty(harness.Session.Document.Pages[second].Blocks);
    }

    // ---- cut -----------------------------------------------------------------------------------

    /// <summary>
    /// Cut takes it off this page and Ctrl+Z puts it back where it was — on its own page, at the
    /// place it held in the stack, not merely somewhere on the page.
    /// </summary>
    [Fact]
    public void CutThenTakeItBackRestoresItWhereItWas()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: true);
        FrameEditorController frames = harness.Frames;

        List<string> before = [.. harness.Session.Document.Pages[0].Blocks.Select(b => b.Id)];
        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CutSelection());
        Assert.DoesNotContain(
            harness.Session.Document.Pages[0].Blocks, b => b.Id == EditorTestHarness.BlockId);

        harness.Session.Undo();
        Assert.Equal(before, [.. harness.Session.Document.Pages[0].Blocks.Select(b => b.Id)]);
    }

    /// <summary>Cut keeps a copy — that is the whole difference between it and Delete.</summary>
    [Fact]
    public void WhatWasCutCanBePutDownOnAnotherPage()
    {
        using var harness = new EditorTestHarness("Notice to the brethren.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CutSelection());
        Assert.True(frames.HasHeldFrames);

        string id = Assert.Single(frames.PasteOntoPage(second));
        var copy = (TextBlock)PageOf(harness, id).GetBlock(id);
        Story story = harness.Session.Document.GetStory(copy.StoryRef);
        Assert.Equal(
            "Notice to the brethren.",
            Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[0]));
    }

    /// <summary>
    /// Cutting two frames out of the middle of one article must leave the frame before them
    /// pointing at the frame that is STAYING.
    ///
    /// <para>Healing one block at a time would point it at a frame that is about to stop existing.
    /// Nulling it instead breaks the invariant the whole flow model rests on — that a story has
    /// exactly one head — and the article is then drawn twice, from its first paragraph, in two
    /// places on the page.</para>
    /// </summary>
    [Fact]
    public void CuttingTwoFramesOutOfOneArticleJoinsUpWhatIsLeft()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        FrameEditorController frames = harness.Frames;

        string b = frames.AddTextFrame(0);
        string c = frames.AddTextFrame(0);
        string d = frames.AddTextFrame(0);

        // A → B → C → D, all on one page.
        frames.Select(EditorTestHarness.BlockId);
        LinkOnward(frames, b);
        frames.Select(b);
        LinkOnward(frames, c);
        frames.Select(c);
        LinkOnward(frames, d);

        frames.SelectAll([b, c]);
        Assert.True(frames.CutSelection());

        var a = (TextBlock)PageOf(harness, EditorTestHarness.BlockId)
            .GetBlock(EditorTestHarness.BlockId);
        Assert.Equal(d, a.LinkNext);
    }

    // ---- move --------------------------------------------------------------------------------

    /// <summary>
    /// Moving is not copying: the very same thing arrives on the other page, keeping its id, its
    /// writing and the article it was continuing.
    /// </summary>
    [Fact]
    public void MovingSomethingKeepsItsIdItsWritingAndItsPlaceInTheArticle()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        string tail = frames.AddTextFrame(0);
        frames.Select(EditorTestHarness.BlockId);
        LinkOnward(frames, tail);

        frames.Select(EditorTestHarness.BlockId);
        string moved = Assert.Single(frames.MoveSelectionToPage(second));

        Assert.Equal(EditorTestHarness.BlockId, moved);
        Assert.Equal("page-2", PageOf(harness, moved).Id);

        var block = (TextBlock)PageOf(harness, moved).GetBlock(moved);
        Assert.Equal(EditorTestHarness.StoryId, block.StoryRef);
        Assert.Equal(tail, block.LinkNext);
    }

    /// <summary>One Ctrl+Z puts a moved thing back on the page it came from, at its old place.</summary>
    [Fact]
    public void TakingBackAMoveReturnsItToThePageItCameFrom()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: true);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        List<string> before = [.. harness.Session.Document.Pages[0].Blocks.Select(b => b.Id)];
        frames.Select(EditorTestHarness.BlockId);
        Assert.Single(frames.MoveSelectionToPage(second));

        harness.Session.Undo();
        Assert.Equal(before, [.. harness.Session.Document.Pages[0].Blocks.Select(b => b.Id)]);
        Assert.Empty(harness.Session.Document.Pages[second].Blocks);
    }

    /// <summary>Several things move together, and come back together.</summary>
    [Fact]
    public void MovingSeveralThingsIsOneUndoStep()
    {
        using var harness = new EditorTestHarness("Words.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        string a = frames.AddTextFrame(0);
        frames.SelectAll([EditorTestHarness.BlockId, a]);

        Assert.Equal(2, frames.MoveSelectionToPage(second).Count);
        Assert.Equal(2, harness.Session.Document.Pages[second].Blocks.Count);
        Assert.Empty(harness.Session.Document.Pages[0].Blocks);

        harness.Session.Undo();
        Assert.Empty(harness.Session.Document.Pages[second].Blocks);
        Assert.Equal(2, harness.Session.Document.Pages[0].Blocks.Count);
    }

    /// <summary>
    /// A thing moved onto smaller paper is pushed onto the sheet rather than left hanging off it —
    /// a block the user cannot see is a move that appears not to have happened.
    /// </summary>
    [Fact]
    public void SomethingMovedOntoSmallerPaperStaysOnTheSheet()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        int second = AddPage(harness);
        Document document = harness.Session.Document;

        document.PageMasters.Add(new PageMaster { Id = "master-small", Size = new SizePt(300f, 300f) });
        document.Pages[second].MasterRef = "master-small";

        harness.Frames.Select(EditorTestHarness.BlockId);
        string moved = Assert.Single(harness.Frames.MoveSelectionToPage(second));

        RectPt rect = PageOf(harness, moved).GetBlock(moved).FrameRect;
        Assert.True(rect.X >= 0f && rect.X + rect.Width <= 300f, $"X off the sheet: {rect.X}");
        Assert.True(rect.Y >= 0f && rect.Y + rect.Height <= 300f, $"Y off the sheet: {rect.Y}");
    }

    /// <summary>Nothing to move, nothing happens — and the caller can tell.</summary>
    [Fact]
    public void MovingToThePageItIsAlreadyOnDoesNothing()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);

        Assert.Empty(harness.Frames.MoveSelectionToPage(0));
    }

    // ---- what is held --------------------------------------------------------------------------

    /// <summary>
    /// What is held came out of one newsletter — a picture in it names bytes in that newsletter's
    /// file — so it must not outlive the newsletter it came from. The shell builds a controller per
    /// newsletter, which is what makes that structural; this holds the guarantee in place.
    /// </summary>
    [Fact]
    public void AFreshNewsletterIsHoldingNothing()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        harness.Frames.Select(EditorTestHarness.BlockId);
        Assert.True(harness.Frames.CopySelection());

        var reopened = new FrameEditorController(harness.Session, harness.Source);
        Assert.False(reopened.HasHeldFrames);
        Assert.Empty(reopened.PasteOntoPage(0));
    }

    /// <summary>Copying with nothing chosen is refused rather than quietly emptying the clipboard.</summary>
    [Fact]
    public void CopyingNothingIsRefused()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        harness.Frames.ClearSelection();

        Assert.False(harness.Frames.CopySelection());
        Assert.False(harness.Frames.HasHeldFrames);
    }

    /// <summary>
    /// A copy of a frame that was continuing an article cannot continue it, and the shell has to be
    /// able to say so — the same sentence M81 says for "make another like this".
    /// </summary>
    [Fact]
    public void CopyingALinkedFrameSaysThatTheCopyContinuesNothing()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        FrameEditorController frames = harness.Frames;

        string tail = frames.AddTextFrame(0);
        frames.Select(EditorTestHarness.BlockId);
        LinkOnward(frames, tail);

        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CopySelection());
        Assert.True(frames.HeldFrameWasLinked);
    }

    /// <summary>Whatever was just put down is what is chosen, so the next keystroke moves it.</summary>
    [Fact]
    public void WhatWasJustPutDownIsWhatIsChosen()
    {
        using var harness = new EditorTestHarness("Notice.", withExclusion: false);
        int second = AddPage(harness);
        FrameEditorController frames = harness.Frames;

        frames.Select(EditorTestHarness.BlockId);
        Assert.True(frames.CopySelection());
        string id = Assert.Single(frames.PasteOntoPage(second));

        Assert.Equal(id, frames.SelectedBlockId);
    }
}
