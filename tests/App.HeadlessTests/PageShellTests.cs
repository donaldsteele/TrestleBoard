using Avalonia.Headless;
using TrestleBoard.Core.Model;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M8 through the real shell: pages can be added, removed and reordered, and an overflowing story
/// can be poured onward — all of it reachable without a mouse (PLAN.md §6, docs/M8-spec.md §2/§3).
/// </summary>
public sealed class PageShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static string LongProse => string.Concat(
        Enumerable.Repeat(
            "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ", 80));

    [Fact]
    public async Task PagesCanBeAddedRemovedAndReorderedThroughTheShell()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            int before = window.PagesForTest!.PageCount;
            string added = window.PagesForTest.AddPage(0);
            Assert.Equal(before + 1, window.PagesForTest.PageCount);
            Assert.Equal(added, window.SessionForTest!.Document.Pages[1].Id);

            Assert.True(window.PagesForTest.MovePage(1, 0));
            Assert.Equal(added, window.SessionForTest.Document.Pages[0].Id);

            // Every page operation is undoable, like everything else (PLAN.md §4).
            window.SessionForTest.Undo();
            Assert.Equal(added, window.SessionForTest.Document.Pages[1].Id);
            window.SessionForTest.Undo();
            Assert.Equal(before, window.PagesForTest.PageCount);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MakingTheRestFitIsReachableFromTheKeyboard()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            // Fill a frame past its capacity, then flow it onward.
            string blockId = window.FramesForTest!.AddTextFrame(0);
            var frame = (TextBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
            Story story = window.SessionForTest.Document.GetStory(frame.StoryRef);
            story.Paragraphs[0].Runs[0].Text = LongProse;
            window.SourceForTest!.Invalidate(new Core.Commands.ChangeScope(
                Core.Commands.ChangeKind.Text, StoryId: frame.StoryRef));

            Assert.True(window.SourceForTest.IsOverset);
            Assert.True(window.PagesForTest!.CanAutoFlow(blockId));

            int pagesBefore = window.PagesForTest.PageCount;
            Assert.True(window.PagesForTest.AutoFlow(blockId));

            Assert.False(window.SourceForTest.IsOverset);
            Assert.True(window.PagesForTest.PageCount > pagesBefore);
            Assert.Equal("Make the rest fit", window.SessionForTest.UndoDescription);

            // One Ctrl+Z takes the whole run back, however many pages it added.
            window.SessionForTest.Undo();
            Assert.Equal(pagesBefore, window.PagesForTest.PageCount);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M39: removing the page the caret is standing on does not take the app down.
    ///
    /// <para>It used to. The text session stayed open over a frame that had just been deleted with
    /// its page, so the next <c>RefreshActions</c> asked <c>DocumentRenderSource.IsTextBlock</c>
    /// about a block the document no longer held, and that method answered by throwing
    /// <c>KeyNotFoundException</c> — from inside <c>BuildContext</c>, outside any handler's
    /// try/catch. Two fixes, because either alone leaves the other half wrong: the caret is ended
    /// before the page goes, and asking whether a vanished block is a text frame now answers "no"
    /// instead of throwing.</para>
    ///
    /// <para>Found by accident, when fixing the swallowed-dispatch bug made an unrelated test start
    /// reporting what it had always been hiding (see DispatchDisciplineTests).</para>
    /// </summary>
    [Fact]
    public async Task RemovingThePageTheCaretIsOnDoesNotCrash()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            // The caret goes into a frame on page 1, and then page 1 goes.
            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            window.EditorForTest.InsertText("Brethren, ");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(window.CurrentActionContext.IsEditingText);

            int pagesBefore = window.PagesForTest!.PageCount;
            window.RemovePage();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(pagesBefore - 1, window.PagesForTest.PageCount);

            // The context still builds, and it no longer claims a caret in a deleted frame.
            Assert.False(window.CurrentActionContext.IsEditingText);

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheWholeIssueFixtureOpensAndFits()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();

            Assert.Equal(5, window.SourceForTest!.PageCount);
            Assert.False(window.SourceForTest.IsOverset);

            // The cover essay really does run onto the second page.
            Assert.True(window.SourceForTest.TryGetFrameLayout("frame-essay-2", out Layout.FrameLayout? second));
            Assert.NotEmpty(second!.Lines);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
