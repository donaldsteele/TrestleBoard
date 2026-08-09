using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Spelling;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Correcting one word must not strand the next one (M52, reported 2026-08-09).
///
/// <para><b>The bug.</b> The walk scanned the document once, when it opened, and held the resulting
/// <see cref="Misspelling"/> list — each carrying a paragraph offset. Correcting a word with a
/// replacement of a different length moves every later word in that paragraph, so the second
/// correction's offset pointed at the wrong characters. <c>ChangeTheWord</c>'s guard — does the text
/// at this offset still read as this word? — then refused, correctly, and the click did nothing.
/// The user reported "no error, just nothing happens", and the app had in fact said what happened
/// in the main window's status bar, behind the window they were looking at.</para>
/// </summary>
public sealed class SpellingWalkRegressionTests
{
    /// <summary>
    /// Two misspellings in one paragraph, the first replaced by something LONGER. Before the fix the
    /// second change silently refused.
    /// </summary>
    [Fact]
    public async Task FixingOneWordLeavesTheNextOneFixable()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string storyId = SeedParagraph(window, "The brethren wil gather on Tuesdy evening.");

            List<Misspelling> found = Scan(window, storyId);
            Assert.True(found.Count >= 2, $"expected two unknown words, found {found.Count}");

            Misspelling first = found[0];
            Misspelling second = found[1];

            // "wil" -> "will" is one character longer, which is what shifts everything after it.
            Assert.True(window.ChangeTheWordForTest(first, "will"), "the first change failed");

            // The list the walk opened with is now stale. The fix is that the walk re-reads the
            // document; this asserts the state that made the second click do nothing.
            Assert.False(
                window.ChangeTheWordForTest(second, "Tuesday"),
                "the stale offset was expected to be refused — if this passes, the premise is wrong");

            // Re-read, exactly as the window now does after every change, and the same correction
            // goes through.
            Misspelling refreshed = Scan(window, storyId).Single(m => m.Word == second.Word);
            Assert.True(window.ChangeTheWordForTest(refreshed, "Tuesday"), "the refreshed change failed");

            Assert.Equal(
                "The brethren will gather on Tuesday evening.",
                ParagraphText(window, storyId));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A shorter replacement shifts the other way, and strands the next word just as thoroughly.
    /// </summary>
    [Fact]
    public async Task AShorterReplacementStrandsTheNextWordToo()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string storyId = SeedParagraph(window, "The lodgge meets and the brethrenn gather.");
            List<Misspelling> found = Scan(window, storyId);
            Assert.True(found.Count >= 2);

            Assert.True(window.ChangeTheWordForTest(found[0], "lodge"));
            Assert.False(window.ChangeTheWordForTest(found[1], "brethren"));

            Misspelling refreshed = Scan(window, storyId).Single(m => m.Word == found[1].Word);
            Assert.True(window.ChangeTheWordForTest(refreshed, "brethren"));
            Assert.Equal("The lodge meets and the brethren gather.", ParagraphText(window, storyId));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A replacement of the SAME length leaves later offsets alone — which is why the fault looked
    /// intermittent rather than constant, and why it was reported as "sometimes".
    /// </summary>
    [Fact]
    public async Task ASameLengthReplacementNeverStrandedAnything()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string storyId = SeedParagraph(window, "The brethrer gather and teh lodge opens.");
            List<Misspelling> found = Scan(window, storyId);
            Assert.True(found.Count >= 2);

            // "brethrer" -> "brethren": same length, so nothing after it moves.
            Assert.True(window.ChangeTheWordForTest(found[0], "brethren"));
            Assert.True(
                window.ChangeTheWordForTest(found[1], "the"),
                "a same-length replacement should never have stranded the next word");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- plumbing ------------------------------------------------------------------------------

    private static string SeedParagraph(MainWindow window, string text)
    {
        string blockId = window.FramesForTest!.AddTextFrame(0);
        window.EditorForTest!.TryBeginAt(
            0,
            window.SourceForTest!.GetEffectiveRect(blockId).X + 2f,
            window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
        window.EditorForTest.InsertText(text);
        window.EditorForTest.End();

        var block = (Core.Model.TextBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
        return block.StoryRef;
    }

    private static List<Misspelling> Scan(MainWindow window, string storyId) =>
        [.. window.Spelling
            .ScanDocument(window.PackageForTest!.Document)
            .Where(m => m.StoryId == storyId)
            .OrderBy(m => m.Offset)];

    private static string ParagraphText(MainWindow window, string storyId) =>
        Core.Text.StoryNavigator.GetParagraphText(
            window.SessionForTest!.Document.GetStory(storyId).Paragraphs[0]);
}
