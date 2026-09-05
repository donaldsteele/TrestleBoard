using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M91: cut, copy and paste stopped being only about words.
///
/// <para>The risk this milestone carries is not the new half — it is the old one. Ctrl+C, Ctrl+X
/// and Ctrl+V have meant "the highlighted words" since M4, and their keyboard rows were widened out
/// of <c>WhileTyping</c> so that they could reach the page as well. A user who never selects a
/// frame in their life must not be able to tell that anything changed, and these are the tests of
/// that.</para>
/// </summary>
public sealed class ClipboardShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// Puts the caret in a frame with words in it and highlights them all, and hands back the frame
    /// the caret is ACTUALLY in.
    ///
    /// <para>The first version of this returned the frame it had just added and clicked at that
    /// frame's corner — where the click landed in a different, higher block, so the words went
    /// somewhere else and the story being asserted on was empty from the start. The test passed
    /// against deliberately broken code because it was asserting nothing. Asking the editor which
    /// block it is in, and checking there are words to lose before trying to lose them, is what
    /// makes the assertion mean something.</para>
    /// </summary>
    private static string TypeAndHighlight(MainWindow window)
    {
        string added = window.FramesForTest!.AddTextFrame(0);
        RectPt rect = window.SourceForTest!.GetEffectiveRect(added);

        window.EditorForTest!.TryBeginAt(0, rect.X + 2f, rect.Y + 2f);
        window.EditorForTest.InsertText("Notice of the stated communication");
        window.EditorForTest.SelectAll();

        Assert.True(window.EditorForTest.IsActive);
        string blockId = Assert.IsType<string>(window.EditorForTest.BlockId);
        Assert.NotEqual(string.Empty, TextIn(window, blockId));
        return blockId;
    }

    private static string TextIn(MainWindow window, string blockId)
    {
        var frame = (TextBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
        Story story = window.SessionForTest.Document.GetStory(frame.StoryRef);
        return string.Concat(story.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text));
    }

    /// <summary>
    /// Ctrl+X inside a piece of writing still takes the WORDS. If the frame half had taken over,
    /// the whole box would be gone off the page instead — which is the failure this milestone could
    /// most easily have shipped.
    /// </summary>
    [Fact]
    public async Task CuttingWhileTypingStillTakesTheWordsAndNotTheBox()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            string blockId = TypeAndHighlight(window);
            await window.CutAsync();

            Assert.Contains(
                window.SessionForTest!.Document.Pages[0].Blocks,
                b => b.Id == blockId);

            Assert.Equal(string.Empty, TextIn(window, blockId));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Ctrl+C outside a piece of writing now takes the chosen thing, and Ctrl+V puts it on whatever
    /// page is being looked at — the whole point of the milestone, driven the way a user drives it.
    /// </summary>
    [Fact]
    public async Task CopyingAThingAndPastingItPutsItOnThePageBeingLookedAt()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.FramesForTest.Select(blockId);
            window.EditorForTest!.End();

            await window.CopyAsync();
            Assert.True(window.FramesForTest.HasHeldFrames);

            int before = window.SessionForTest!.Document.Pages[1].Blocks.Count;
            window.GoToNextPageForTest();
            await window.PasteAsync();

            Assert.Equal(before + 1, window.SessionForTest.Document.Pages[1].Blocks.Count);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Ctrl+Shift+Page Down takes the chosen thing to the next page and shows that page — the
    /// keyboard route, since the canvas draws one page and there is nothing to drag across.
    /// </summary>
    [Fact]
    public async Task TheKeyboardMovesAChosenThingToTheNextPageAndFollowsIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.FramesForTest.Select(blockId);

            window.KeyPressQwerty(
                PhysicalKey.PageDown, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.Contains(window.SessionForTest!.Document.Pages[1].Blocks, b => b.Id == blockId);
            Assert.DoesNotContain(window.SessionForTest.Document.Pages[0].Blocks, b => b.Id == blockId);
            Assert.Equal(1, window.PageIndexForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
