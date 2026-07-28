using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M17: the canvas answers the first click (PLAN.md §11 M17).
///
/// Every mechanism these tests exercise already worked before M17 — inline editing since M4, the
/// widget wizard since M7, the overset badge since M5. What was missing was any affordance telling
/// the user so, which is why the owner's first hands-on session read a working editor as a broken
/// one. These are tests of the TELLING.
/// </summary>
public sealed class CanvasAffordanceTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// The central M17 promise: making a text frame means you wanted to type, so the caret is
    /// already in it. Before this the gesture left a selected empty rectangle and the user had to
    /// know to press Enter or F2 — the discoverability defect in miniature.
    /// </summary>
    [Fact]
    public async Task AddingATextFrameLeavesTheCaretInItAndTypingGoesStraightIn()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);

            Assert.True(window.EditorForTest!.IsActive);
            string blockId = Assert.IsType<string>(window.EditorForTest.BlockId);

            // No key in between: the very next thing typed is text in the new frame.
            window.KeyTextInput("Brethren");

            var block = (Core.Model.TextBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
            Story story = window.SessionForTest.Document.GetStory(block.StoryRef);
            Assert.Equal(
                "Brethren",
                string.Concat(story.Paragraphs.SelectMany(p => p.Runs).Select(r => r.Text)));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An empty frame must be able to say what it is for. The renderer answers WHICH frames are
    /// empty; the hint itself is painted by the canvas with Avalonia primitives, which is what
    /// keeps it off the PDF.
    /// </summary>
    [Fact]
    public async Task AnEmptyTextFrameIsOfferedForTheHintAndStopsBeingOnceItHasWords()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.FramesForTest!.AddTextFrame(0);
            Assert.Contains(
                window.SourceForTest!.GetEmptyTextFrameRects(0),
                f => f.BlockId == blockId);

            window.EditorForTest!.TryBeginAt(
                0,
                window.SourceForTest.GetEffectiveRect(blockId).X + 2f,
                window.SourceForTest.GetEffectiveRect(blockId).Y + 2f);
            window.EditorForTest.InsertText("Notice of the stated communication");

            Assert.DoesNotContain(
                window.SourceForTest.GetEmptyTextFrameRects(0),
                f => f.BlockId == blockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Double-clicking one of the filled-in lists opens its editor — through the action runner, so
    /// the availability rules apply exactly as they do from the menu.
    /// </summary>
    [Fact]
    public async Task DoubleClickingAWidgetSelectsItAndAsksForItsEditor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            RectPt rect = window.SourceForTest!.GetEffectiveRect(blockId);

            // Record and stop short: the real handler opens the wizard window.
            var invoked = new List<string>();
            window.ActionsForTest.InterceptorForTest = id =>
            {
                invoked.Add(id);
                return true;
            };

            Assert.True(window.CanvasForTest.TryActivateWidgetAt(
                rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));

            Assert.Equal(blockId, window.FramesForTest!.SelectedBlockId);
            Assert.Equal([ActionId.EditWidget], invoked);

            window.ActionsForTest.InterceptorForTest = null;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>A double-click on ordinary text is still a double-click on ordinary text.</summary>
    [Fact]
    public async Task DoubleClickingSomethingThatIsNotAWidgetDoesNotClaimTheGesture()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            RectPt rect = window.SourceForTest!.GetEffectiveRect("block-officers");
            Assert.False(window.CanvasForTest.TryActivateWidgetAt(
                rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A drag whose pointer runs off the sheet clamps at the page edge. It used to stop updating
    /// entirely, which while it was happening was indistinguishable from a hang.
    /// </summary>
    [Fact]
    public async Task ADragPastThePageEdgeClampsInsteadOfFreezing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            SizePt page = window.SourceForTest!.GetPageSize(0);

            // Far off the bottom-right of the control, well past the sheet.
            (float x, float y) = window.CanvasForTest.ToClampedPagePoint(new Point(20_000, 20_000));
            Assert.Equal(page.Width, x, 3);
            Assert.Equal(page.Height, y, 3);

            // And off the top-left, where the page point goes negative.
            (float nx, float ny) = window.CanvasForTest.ToClampedPagePoint(new Point(-500, -500));
            Assert.Equal(0f, nx, 3);
            Assert.Equal(0f, ny, 3);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One native cursor handle per shape. <c>UpdateCursor</c> runs on every pointer move, and
    /// until M17 it allocated a fresh <see cref="Cursor"/> each time.
    /// </summary>
    [Fact]
    public async Task TheCursorForAShapeIsMadeOnceAndReused()
    {
        await Session.Dispatch(
            () => Assert.Same(
                Canvas.PageCanvasControl.CursorFor(StandardCursorType.Ibeam),
                Canvas.PageCanvasControl.CursorFor(StandardCursorType.Ibeam)),
            TestContext.Current.CancellationToken);
    }
}
