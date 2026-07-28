using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Editing;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M21's gate (PLAN.md §12 item 18), driven through the real window: the everyday conveniences whose
/// absence a user notices without being able to name them.
///
/// <para>Four of the five claims in the acceptance criteria are here — Ctrl+wheel holds its point,
/// aligning is one undo step, the text session survives a click on the chrome, and every new command
/// is menu-indexed and keyboard-reachable (which <c>MenuIndexTests</c> and <c>KeyboardAuditTests</c>
/// now cover automatically, because they walk the catalog). The fifth, find reaching the second frame
/// of a linked chain, is <c>FindControllerTests</c>, where a chain can be built exactly.</para>
/// </summary>
public sealed class ConveniencesShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>Shows the window and gives it a real layout, which zoom arithmetic needs.</summary>
    private static MainWindow OpenLaidOut()
    {
        var window = new MainWindow();
        window.Show();
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>
    /// <b>The never-cut deliverable.</b> Ctrl+wheel zooms about the pointer: the point of the page
    /// under the pointer is still under it afterwards. Zooming about the centre — which is what the
    /// toolbar buttons do — means hunting for what you were reading after every step.
    /// </summary>
    [Fact]
    public async Task ControlWheelZoomKeepsThePointUnderThePointerStill()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();

            // Far enough in that the page overflows the viewport both ways; otherwise the canvas is
            // centred in the scroller, there is nowhere to scroll to, and the question is moot.
            window.ZoomToActualSize();
            for (int i = 0; i < 4; i++)
            {
                window.StepZoom(+1);
            }

            ScrollViewer scroller = window.CanvasScrollerForTest;
            scroller.UpdateLayout();

            // Scrolled away from the corner, because the page must have room to move in BOTH
            // directions: nothing can hold a point still at an edge the view cannot scroll past,
            // and clamping there is the right answer rather than the interesting one.
            scroller.Offset = new Vector(200, 200);
            scroller.UpdateLayout();

            var pointerInScroller = new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2);
            Point pointerInCanvas = Assert.IsType<Point>(
                scroller.TranslatePoint(pointerInScroller, window.CanvasForTest));

            double before = window.CanvasForTest.Zoom;
            double pageX = (pointerInCanvas.X - Canvas.PageCanvasControl.PagePaddingPx) / before;
            double pageY = (pointerInCanvas.Y - Canvas.PageCanvasControl.PagePaddingPx) / before;

            window.ZoomAtPointer(pointerInCanvas, +1);
            window.CanvasScrollerForTest.UpdateLayout();

            double after = window.CanvasForTest.Zoom;
            Assert.NotEqual(before, after);

            var samePageSpot = new Point(
                (pageX * after) + Canvas.PageCanvasControl.PagePaddingPx,
                (pageY * after) + Canvas.PageCanvasControl.PagePaddingPx);
            Point landedAt = Assert.IsType<Point>(
                window.CanvasForTest.TranslatePoint(samePageSpot, scroller));

            // Within a pixel: the scroller snaps its offset to whole pixels, so "still" means still
            // to the nearest one — which is the finest anything on a screen can be.
            Assert.True(
                Math.Abs(pointerInScroller.X - landedAt.X) < 1.0
                && Math.Abs(pointerInScroller.Y - landedAt.Y) < 1.0,
                $"the point under the pointer moved: zoom {before} → {after}, "
                + $"pointer at {pointerInScroller}, the same page spot landed at {landedAt}");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Space arms panning, and a pan moves the scroller rather than the page.</summary>
    [Fact]
    public async Task SpaceArmsPanningAndPanningScrollsTheView()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();
            window.ZoomToActualSize();
            window.CanvasForTest.Focus();

            Assert.False(window.CanvasForTest.PanArmedForTest);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(window.CanvasForTest.PanArmedForTest);

            window.CanvasScrollerForTest.UpdateLayout();
            Vector before = window.CanvasScrollerForTest.Offset;

            // Dragging the sheet DOWN scrolls the view up, which is why the delta is negated.
            window.PanBy(new Vector(0, -60));
            Assert.True(window.CanvasScrollerForTest.Offset.Y > before.Y);

            // And the page itself never moved: panning is a view gesture, not an edit.
            Assert.False(window.SessionForTest!.CanUndo);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The M17 deferral, closed. Until M21 losing focus ENDED the text session, so every click on a
    /// menu, a panel button or a dialog silently threw the caret and the highlight away — which is
    /// also why a non-modal find window was impossible before this.
    /// </summary>
    [Fact]
    public async Task TheTextSessionSurvivesAClickOnTheChrome()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();
            window.CanvasForTest.Focus();

            window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control | RawInputModifiers.Shift);
            window.KeyTextInput("Brethren");
            Assert.True(window.EditorForTest!.IsActive);
            string blockId = Assert.IsType<string>(window.EditorForTest.BlockId);

            Button somewhereElse = window.PanelForTest.ButtonsForTest[0];
            somewhereElse.Focus();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.EditorForTest.IsActive);
            Assert.Equal(blockId, window.EditorForTest.BlockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Three frames, chosen with Shift, lined up in one undo step through the real action runner —
    /// availability rules, status line and all.
    /// </summary>
    [Fact]
    public async Task LiningUpThreeChosenFramesIsOneUndoStepThroughTheRunner()
    {
        await Session.Dispatch(async () =>
        {
            MainWindow window = OpenLaidOut();
            FrameEditorController frames = window.FramesForTest!;

            string a = frames.AddTextFrame(0);
            string b = frames.AddTextFrame(0);
            string c = frames.AddTextFrame(0);
            window.EditorForTest!.End();

            frames.Select(a);
            Assert.True(frames.AddToSelection(b));
            Assert.True(frames.AddToSelection(c));
            window.RefreshActions();

            Assert.Equal(3, window.CurrentActionContext.SelectionCount);
            Assert.Equal("3 things are selected", window.PanelForTest.HeadingForTest);
            Assert.Contains(
                ActionCatalog.ForSelection(window.CurrentActionContext),
                offer => offer.Action.Id == ActionId.AlignLeft);

            await window.ActionsForTest.RunAsync(ActionId.AlignLeft);

            float leftmost = new[] { a, b, c }.Min(id => RectOf(window, id).X);
            Assert.All([a, b, c], id => Assert.Equal(leftmost, RectOf(window, id).X, 3));
            Assert.Equal("Line up the left edges", window.SessionForTest!.UndoDescription);

            window.SessionForTest.Undo();
            Assert.NotEqual(
                RectOf(window, b).X,
                RectOf(window, c).X);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// With ONE thing chosen the lining-up commands are absent from the panel rather than greyed in
    /// it — the M11 rule, and the reason M21 re-bakes no screenshot: the panel for a single
    /// selection looks exactly as it did before the milestone.
    /// </summary>
    [Fact]
    public async Task WithOneThingChosenNothingAboutLiningUpIsOffered()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();
            string photo = window.SessionForTest!.Document.Pages
                .SelectMany(p => p.Blocks)
                .OfType<Core.Model.ImageFrame>()
                .First().Id;

            window.FramesForTest!.Select(photo);
            window.RefreshActions();

            Assert.Equal(1, window.CurrentActionContext.SelectionCount);
            Assert.DoesNotContain(
                ActionCatalog.ForSelection(window.CurrentActionContext),
                offer => offer.Action.Id.StartsWith("arrange.align", StringComparison.Ordinal)
                    || offer.Action.Id.StartsWith("arrange.distribute", StringComparison.Ordinal));

            // And the menu says why rather than greying in silence.
            ActionAvailability availability =
                ActionCatalog.Evaluate(ActionId.AlignLeft, window.CurrentActionContext);
            Assert.False(availability.IsAvailable);
            Assert.Contains("hold Shift", availability.Reason, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>A marquee drag over the whole page chooses everything on it.</summary>
    [Fact]
    public async Task DraggingABoxOverThePageChoosesEverythingItTouches()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();
            int blocksOnPage = window.SessionForTest!.Document.Pages[0].Blocks.Count;
            Assert.True(blocksOnPage > 1, "the sample's first page should hold several blocks");

            window.CanvasForTest.MarqueeForTest(new Point(0, 0), new Point(10_000, 10_000));

            Assert.Equal(blocksOnPage, window.FramesForTest!.SelectionCount);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>A box too small to have been meant chooses nothing — it was a click that missed.</summary>
    [Fact]
    public async Task ABoxTooSmallToHaveBeenMeantChoosesNothing()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();
            window.CanvasForTest.MarqueeForTest(new Point(30, 30), new Point(31, 31));

            Assert.Equal(0, window.FramesForTest!.SelectionCount);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Ctrl+F opens the find window, and it is the same window Ctrl+H puts in replace mode.</summary>
    [Fact]
    public async Task ControlFOpensTheFindWindowAndControlHIsTheSameWindowInReplaceMode()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();

            window.ShowFind(replacing: false);
            FindWindow first = Assert.IsType<FindWindow>(window.FindWindowForTest);
            Assert.False(first.IsReplacing);

            window.ShowFind(replacing: true);
            Assert.Same(first, window.FindWindowForTest);
            Assert.True(first.IsReplacing);

            first.Close();
            Assert.Null(window.FindWindowForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Find, driven through the window the user actually presses: type the words, press the button,
    /// and the newsletter's own text is highlighted with the caret in the frame it was found in.
    /// </summary>
    [Fact]
    public async Task TheFindWindowHighlightsWhatItFindsAndSaysWhatItDid()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();

            FindWindow find = window.BuildFindWindowForTest(replacing: false);
            find.TypeForTest("lodge");
            find.FindNextForTest();

            Assert.True(window.EditorForTest!.IsActive);
            Assert.Equal("lodge", window.EditorForTest.SelectedText);
            Assert.Contains("Found", find.MessageForTest, StringComparison.Ordinal);

            // The status bar heard it too: it is a polite live region, so a screen-reader user is
            // told what was found without having to go looking for the message.
            Assert.Contains("Found", window.StatusLabelTextForTest ?? "", StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Words that are nowhere say so, and say what was not searched.</summary>
    [Fact]
    public async Task WordsThatAreNowhereSayWhatWasNotSearched()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();

            FindWindow find = window.BuildFindWindowForTest(replacing: false);
            find.TypeForTest("Zamboni");
            find.FindNextForTest();

            Assert.Contains(
                FindController.WidgetsNotSearched, find.MessageForTest, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Replace all through the window: one undo step, and the words really change.</summary>
    [Fact]
    public async Task ReplacingEveryOneThroughTheWindowIsOneUndoStep()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenLaidOut();

            FindWindow find = window.BuildFindWindowForTest(replacing: true);
            find.TypeForTest("lodge", "chapter");
            find.ReplaceAllForTest();

            Assert.Equal("Replace all", window.SessionForTest!.UndoDescription);
            Assert.Equal(0, window.FindForTest!.CountAll());

            window.SessionForTest.Undo();
            Assert.True(window.FindForTest.CountAll() > 0);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static Core.Model.RectPt RectOf(MainWindow window, string blockId) =>
        window.SessionForTest!.Document.FindBlock(blockId).Block.FrameRect;
}
