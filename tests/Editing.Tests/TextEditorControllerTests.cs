using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Editing.Tests;

public sealed class TextEditorControllerTests
{
    private const string Prose =
        "The Placeholder Lodge convenes on the appointed evening of every month. "
        + "Brothers gather early for fellowship and share a simple meal together.";

    [Fact]
    public void AcceptanceTypesAParagraphAroundAnExclusionAndUndoesInWordChunks()
    {
        using var h = new EditorTestHarness(initialText: null);
        Assert.True(h.ClickIntoFrame());

        // Type word by word: <1s inside words, >1s between words.
        string[] words = ["Brethren", " assemble", " early", " for", " supper"];
        foreach (string word in words)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(2));
            h.TypeBurst(word);
        }

        Assert.Equal("Brethren assemble early for supper", h.ParagraphText(0));

        // The caret has geometry and sits outside the exclusion.
        Assert.True(h.Controller.TryGetCaretGeometry(out CaretGeometry caret));
        Assert.True(caret.XPt < 260f - 8f || caret.XPt > 400f + 8f || caret.BottomPt <= 120f - 8f || caret.TopPt >= 230f + 8f);

        // Undo removes word-chunks, not characters.
        int undoCount = 0;
        while (h.Session.CanUndo)
        {
            h.Session.Undo();
            undoCount++;
        }

        Assert.Equal(words.Length, undoCount);
        Assert.Equal("", h.ParagraphText(0));
    }

    [Fact]
    public void ReplaceSelectionIsOneUndoStepAndFollowingKeystrokesCoalesce()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        for (int i = 0; i < 3; i++)
        {
            h.Controller.Move(CaretMotion.Right, extend: true);
        }

        h.Clock.Advance(TimeSpan.FromSeconds(2));
        h.Controller.InsertText("Our");                       // composite replace
        h.TypeBurst(" own");                                  // coalescing bare inserts

        Assert.StartsWith("Our own Placeholder Lodge", h.ParagraphText(0), StringComparison.Ordinal);

        h.Session.Undo();                                     // burst " own"
        Assert.StartsWith("Our Placeholder Lodge", h.ParagraphText(0), StringComparison.Ordinal);
        h.Session.Undo();                                     // the replace
        Assert.StartsWith("The Placeholder Lodge", h.ParagraphText(0), StringComparison.Ordinal);
    }

    [Fact]
    public void EnterSplitsAndBackspaceAtStartMerges()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        for (int i = 0; i < 3; i++)
        {
            h.Controller.Move(CaretMotion.WordRight, extend: false);
        }

        int splitOffset = h.Controller.Selection.Caret.Offset;
        h.Controller.InsertParagraphBreak();
        Assert.Equal(2, h.Story.Paragraphs.Count);
        Assert.Equal(splitOffset, h.ParagraphText(0).Length);
        Assert.Equal((1, 0), (h.Controller.Selection.Caret.ParagraphIndex, h.Controller.Selection.Caret.Offset));

        // Backspace at paragraph start merges back; caret at old end of first paragraph.
        h.Controller.Backspace();
        Assert.Single(h.Story.Paragraphs);
        Assert.Equal(Prose, h.ParagraphText(0));
        Assert.Equal(splitOffset, h.Controller.Selection.Caret.Offset);
        Assert.Equal(TextAffinity.Trailing, h.Controller.Selection.Caret.Affinity);

        // Undo restores the split; undo again restores one paragraph.
        h.Session.Undo();
        Assert.Equal(2, h.Story.Paragraphs.Count);
        h.Session.Undo();
        Assert.Single(h.Story.Paragraphs);
    }

    [Fact]
    public async Task MultiParagraphPasteIsOneUndoStep()
    {
        using var h = new EditorTestHarness("XYZ");
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        h.Controller.Move(CaretMotion.Right, extend: false);
        h.Controller.Move(CaretMotion.Right, extend: false);

        h.Clipboard.Text = "A\nB\nC";
        await h.Controller.PasteAsync();

        Assert.Equal(["XYA", "B", "CZ"], h.Story.Paragraphs.Select(p => Core.Text.StoryNavigator.GetParagraphText(p)));
        Assert.Equal((2, 1), (h.Controller.Selection.Caret.ParagraphIndex, h.Controller.Selection.Caret.Offset));

        h.Session.Undo();
        Assert.Equal("XYZ", h.AllText());
        Assert.False(h.Session.CanUndo);
    }

    [Fact]
    public async Task CutCopyPasteRoundTripWithNewlines()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        h.Controller.InsertParagraphBreak();
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        h.Controller.InsertText("First line");

        h.Controller.SelectAll();
        await h.Controller.CopyAsync();
        Assert.Equal("First line\n" + Prose, h.Clipboard.Text);

        await h.Controller.CutAsync();
        Assert.Equal("", h.AllText());
        Assert.Single(h.Story.Paragraphs);

        h.Session.Undo();
        Assert.Equal("First line\n" + Prose, h.AllText());
    }

    [Fact]
    public void BoldToggleRetargetsRunsAndUntogglesToOneRun()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        for (int i = 0; i < 5; i++)
        {
            h.Controller.Move(CaretMotion.Right, extend: false);
        }

        for (int i = 0; i < 6; i++)
        {
            h.Controller.Move(CaretMotion.Right, extend: true);
        }

        Assert.False(h.Controller.IsBoldActive);
        h.Controller.ToggleBold();
        Assert.True(h.Controller.IsBoldActive);
        Assert.Equal(3, h.Story.Paragraphs[0].Runs.Count);
        Assert.Equal("body-bold", h.Story.Paragraphs[0].Runs[1].CharacterStyleRef);

        h.Controller.ToggleBold();
        Assert.False(h.Controller.IsBoldActive);
        Assert.Single(h.Story.Paragraphs[0].Runs);
        Assert.Equal(Prose, h.ParagraphText(0));
    }

    [Fact]
    public void ItalicToggleDerivesMissingVariantAndUndoRemovesIt()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.SelectAll();

        int stylesBefore = h.Session.Document.StyleSheet.CharacterStyles.Count;
        h.Controller.ToggleItalic();
        Assert.True(h.Controller.IsItalicActive);
        Assert.Equal(stylesBefore + 1, h.Session.Document.StyleSheet.CharacterStyles.Count);
        Assert.Contains(h.Session.Document.StyleSheet.CharacterStyles, s => s.Name == "body-italic");

        h.Session.Undo();
        Assert.Equal(stylesBefore, h.Session.Document.StyleSheet.CharacterStyles.Count);
        Assert.Single(h.Story.Paragraphs[0].Runs);
    }

    [Fact]
    public void PendingBoldAppliesToNextTypedTextAndClearsOnMotion()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryEnd, extend: false);

        h.Controller.ToggleBold();
        Assert.True(h.Controller.IsBoldActive);
        h.Controller.InsertText(" Bold tail.");
        Assert.Equal("body-bold", h.Story.Paragraphs[0].Runs[^1].CharacterStyleRef);

        h.Controller.ToggleBold();
        h.Controller.Move(CaretMotion.Left, extend: false);
        Assert.True(h.Controller.IsBoldActive); // caret sits inside the bold tail now
    }

    [Fact]
    public void ApplyParagraphStyleOverSelectionIsOneUndoStep()
    {
        using var h = new EditorTestHarness("One");
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryEnd, extend: false);
        h.Controller.InsertParagraphBreak();
        h.Controller.InsertText("Two");
        h.Controller.InsertParagraphBreak();
        h.Controller.InsertText("Three");

        h.Controller.SelectAll();
        h.Controller.ApplyParagraphStyle("body-tight");
        Assert.All(h.Story.Paragraphs, p => Assert.Equal("body-tight", p.ParagraphStyleRef));

        h.Session.Undo();
        Assert.All(h.Story.Paragraphs, p => Assert.Equal("body", p.ParagraphStyleRef));

        Assert.Throws<KeyNotFoundException>(() => h.Controller.ApplyParagraphStyle("nope"));
    }

    [Fact]
    public void RelayoutIsLazyAcrossKeystrokeBursts()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryEnd, extend: false);

        // Settle any pending layout, then measure.
        Assert.True(h.Controller.TryGetCaretGeometry(out _));
        int before = h.Source.RelayoutCount;

        // 100 keystrokes with NO geometry queries in between (RequestReveal subscribes only
        // when a handler is attached; none is) → one relayout on the next query.
        for (int i = 0; i < 100; i++)
        {
            h.Clock.Advance(TimeSpan.FromMilliseconds(10));
            h.Controller.InsertText("y");
        }

        Assert.True(h.Controller.TryGetCaretGeometry(out _));
        Assert.Equal(before + 1, h.Source.RelayoutCount);
    }

    [Fact]
    public async Task SelectionClampsAfterUndoOfPasteThatRemovedParagraphs()
    {
        using var h = new EditorTestHarness("Short");
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryEnd, extend: false);
        h.Clipboard.Text = "A\nB\nC\nD";
        await h.Controller.PasteAsync();
        Assert.Equal(4, h.Story.Paragraphs.Count);
        Assert.Equal(3, h.Controller.Selection.Caret.ParagraphIndex);

        h.Session.Undo();
        Assert.Single(h.Story.Paragraphs);
        Assert.Equal(0, h.Controller.Selection.Caret.ParagraphIndex);
        Assert.InRange(h.Controller.Selection.Caret.Offset, 0, "Short".Length);
    }

    [Fact]
    public void EscEndsSessionAndClickOutsideFails()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());
        Assert.True(h.Controller.IsActive);
        h.Controller.End();
        Assert.False(h.Controller.IsActive);

        // Outside every frame → no session.
        Assert.False(h.Controller.TryBeginAt(0, 500f, 700f));
        Assert.False(h.Controller.IsActive);
    }

    [Fact]
    public void ArrowNavigationCrossesTheExclusionInOneLine()
    {
        using var h = new EditorTestHarness(Prose);
        Assert.True(h.ClickIntoFrame());

        // Walk right through the whole story; offsets must be strictly increasing (no traps
        // inside the exclusion) until the end.
        h.Controller.Move(CaretMotion.StoryStart, extend: false);
        int previous = -1;
        int steps = 0;
        while (h.Controller.Move(CaretMotion.Right, extend: false))
        {
            int offset = h.Controller.Selection.Caret.Offset;
            Assert.True(offset > previous || h.Controller.Selection.Caret.ParagraphIndex > 0);
            previous = offset;
            if (++steps > Prose.Length + 5)
            {
                Assert.Fail("Right-arrow walk did not terminate");
            }
        }

        Assert.Equal(Prose.Length, h.Controller.Selection.Caret.Offset);
    }

    [Fact]
    public void OversetCaretKeepsEditingWithoutGeometry()
    {
        using var h = new EditorTestHarness(Prose + " " + Prose + " " + Prose, frameHeightPt: 120, withExclusion: false);
        Assert.True(h.ClickIntoFrame());
        h.Controller.Move(CaretMotion.StoryEnd, extend: false);

        Assert.True(h.Controller.IsCaretOverset);
        Assert.False(h.Controller.TryGetCaretGeometry(out _));

        // Editing still works at the logical caret.
        h.Controller.InsertText("!");
        Assert.EndsWith("!", h.ParagraphText(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// Review §14.2: undo can take the story out from under an open text session, and the session
    /// has to end rather than throw.
    ///
    /// "Add a text frame" is one composite command over a story AND a block, so pressing Ctrl+Z
    /// straight after clicking into the new frame removes the story the caret is in. The change
    /// handler clamped the selection against <c>CurrentStory()</c>, which throws
    /// KeyNotFoundException for a story that is gone — out of an event handler, so it took the app
    /// down. FrameEditorController has handled the same case for its block since M5.
    /// </summary>
    [Fact]
    public void UndoingTheFrameTheCaretIsInEndsTheSessionInsteadOfThrowing()
    {
        using var h = new EditorTestHarness(Prose, frameHeightPt: 200, withExclusion: false);
        var frames = new FrameEditorController(h.Session, h.Source);

        string added = frames.AddTextFrame(0);
        var block = (Core.Model.TextBlock)h.Session.Document.FindBlock(added).Block;
        string newStoryId = block.StoryRef;

        // Click into the frame that was just added, the way a user reaches it.
        Assert.True(h.Controller.TryBeginAt(
            0,
            block.FrameRect.X + (block.FrameRect.Width / 2f),
            block.FrameRect.Y + 8f));
        Assert.True(h.Controller.IsActive);
        Assert.Equal(added, h.Controller.BlockId);

        // The whole point: this used to throw out of DocumentSession.Changed.
        h.Session.Undo();

        Assert.False(h.Controller.IsActive);
        Assert.False(h.Session.Document.TryGetStory(newStoryId, out _));

        // And the editor is still usable afterwards — the original frame takes the caret again.
        Assert.True(h.ClickIntoFrame());
        h.Controller.InsertText("!");
        Assert.Contains("!", h.ParagraphText(0), StringComparison.Ordinal);
    }
}
