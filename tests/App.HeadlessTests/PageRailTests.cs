using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The page rail (M76 (g), docs/M76-spec.md §6 and §9).
///
/// <para>Until this milestone "Page 1 of 5" sat between two buttons and pressing Next three times
/// was the only route to page four. These tests hold the four promises the spec made about what
/// replaced it: it refuses in words rather than by greying, it says which page each tile is in a
/// sentence a screen reader can read, it is reachable and drivable with the keyboard alone, and it
/// folds away and comes back.</para>
///
/// <para>And one the review added: a miniature is a picture of a PAGE, so it has to follow that page
/// when the rail's own buttons move it.</para>
/// </summary>
public sealed class PageRailTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// **The refusal, word for word.**
    ///
    /// <para><b>Asserting that a reason is non-empty proves nothing here</b> and the spec says so:
    /// <c>ActionAvailability.Blocked</c> has refused an empty reason at construction since M11, so a
    /// non-emptiness check is a test of a constructor that already has its own test. What can
    /// actually be wrong is that the sentence is <i>false</i> — a refusal that names the wrong
    /// obstacle sends an elderly user hunting for a thing that is not in their way. So the exact
    /// text is compared, and each of the three says something that is true of a one-page
    /// newsletter.</para>
    /// </summary>
    [Fact]
    public async Task WithOnePageAllThreePageCommandsRefuseAndEachSentenceIsTrue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;

            // The sample opens with two pages; one goes, and what is left is the state this test is
            // about — reached through the real command, so the context is the real one.
            window.OpenSample();
            window.RemovePage();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, window.PagesForTest!.PageCount);

            ActionAvailability goTo =
                ActionCatalog.Evaluate(ActionId.GoToPage, window.CurrentActionContext);
            ActionAvailability earlier =
                ActionCatalog.Evaluate(ActionId.MovePageEarlier, window.CurrentActionContext);
            ActionAvailability later =
                ActionCatalog.Evaluate(ActionId.MovePageLater, window.CurrentActionContext);

            Assert.False(goTo.IsAvailable);
            Assert.False(earlier.IsAvailable);
            Assert.False(later.IsAvailable);

            Assert.Equal("There is only one page, so there is nowhere else to go.", goTo.Reason);
            Assert.Equal("This page is already first.", earlier.Reason);
            Assert.Equal("This page is already last.", later.Reason);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// **And refusing is not greying.** The rail keeps M11's rule: the tile and the two move buttons
    /// stay pressable with one page open, and each carries the catalog's own sentence where a screen
    /// reader will find it without having to press anything.
    /// </summary>
    [Fact]
    public async Task NothingInTheRailIsEverGreyed()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            window.RemovePage();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            PageRail rail = window.RailForTest;
            List<Button> everything = [.. rail.TilesForTest, .. rail.MoveButtonsForTest];

            Assert.NotEmpty(everything);
            foreach (Button button in everything)
            {
                Assert.True(
                    button.IsEnabled,
                    $"{AutomationProperties.GetName(button)} was greyed instead of explaining itself");
                Assert.False(
                    string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)),
                    $"{AutomationProperties.GetName(button)} says nothing about why");
            }

            // The move buttons carry the very sentence the catalog gives the menu bar, so the two
            // surfaces cannot drift into telling the user different things about the same page.
            Assert.Equal(
                "This page is already first.",
                AutomationProperties.GetHelpText(rail.MoveButtonsForTest[0]));
            Assert.Equal(
                "This page is already last.",
                AutomationProperties.GetHelpText(rail.MoveButtonsForTest[1]));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// One tile per page, each named the way the spec asked — "Page 3 of 5" — and the current one
    /// saying so in the name as well as in colour, because colour is never the only signal
    /// (PLAN.md §6).
    /// </summary>
    [Fact]
    public async Task TheRailShowsOneTilePerPageAndNamesEachOne()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            int pages = window.SourceForTest!.PageCount;
            Assert.True(pages >= 4, $"the issue fixture has only {pages} pages");

            IReadOnlyList<Button> tiles = window.RailForTest.TilesForTest;
            Assert.Equal(pages, tiles.Count);

            for (int index = 0; index < tiles.Count; index++)
            {
                Assert.StartsWith(
                    $"Page {index + 1} of {pages}",
                    AutomationProperties.GetName(tiles[index]),
                    StringComparison.Ordinal);
            }

            // The page being shown says so in words, not only in accent and gold.
            Assert.Equal($"Page 1 of {pages}, showing now", AutomationProperties.GetName(tiles[0]));
            Assert.Equal($"Page 2 of {pages}", AutomationProperties.GetName(tiles[1]));

            window.GoToPage(2);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal($"Page 1 of {pages}", AutomationProperties.GetName(tiles[0]));
            Assert.Equal($"Page 3 of {pages}, showing now", AutomationProperties.GetName(tiles[2]));

            // Updated in place, not rebuilt: a keyboard user standing on a tile must still be
            // standing on it after the page turned under them.
            Assert.Same(tiles[0], window.RailForTest.TilesForTest[0]);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// **Keyboard-complete** (docs/M76-spec.md §9): the arrows walk the tiles and Enter goes to the
    /// page you are standing on. Driven through real key presses on the real window, because the
    /// thing being tested is where a key press lands — which is exactly what a method call would
    /// assume rather than prove.
    /// </summary>
    [Fact]
    public async Task TheArrowsWalkTheTilesAndEnterGoesToThatPage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, window.PageIndexForTest);
            Assert.True(window.RailForTest.FocusTile(0), "the rail would not take the keyboard");
            Assert.Equal(0, window.RailForTest.FocusedTileForTest);

            Press(window, Key.Down);
            Assert.Equal(1, window.RailForTest.FocusedTileForTest);

            Press(window, Key.Down);
            Assert.Equal(2, window.RailForTest.FocusedTileForTest);

            Press(window, Key.Up);
            Assert.Equal(1, window.RailForTest.FocusedTileForTest);

            // Nothing has moved yet: walking the rail looks around, and going somewhere is a
            // separate press. That is what makes the rail safe to explore.
            Assert.Equal(0, window.PageIndexForTest);

            Press(window, Key.Enter);
            Assert.Equal(1, window.PageIndexForTest);
            Assert.Contains(
                "Showing page 2 of",
                window.StatusLabelTextForTest ?? string.Empty,
                StringComparison.Ordinal);

            // End jumps to the last page's tile, which is the whole point of a rail: page four
            // without pressing Next three times.
            Press(window, Key.End);
            Assert.Equal(window.SourceForTest!.PageCount - 1, window.RailForTest.FocusedTileForTest);
            Press(window, Key.Enter);
            Assert.Equal(window.SourceForTest.PageCount - 1, window.PageIndexForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every tile is in the tab order and can take focus, which is what "Tab reaches the rail"
    /// means once F6 has put the user in it.
    /// </summary>
    [Fact]
    public async Task EveryTileIsInTheTabOrder()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.All(window.RailForTest.TilesForTest, tile =>
            {
                Assert.True(tile.Focusable, "a tile cannot take focus");
                Assert.True(tile.IsTabStop, "a tile is not in the tab order");
            });

            // F6 walks the window's parts, and the rail is one of them now.
            window.CanvasForTest.Focus();
            window.CycleRegion(forward: true);
            Assert.Contains(
                "Moved to", window.StatusLabelTextForTest ?? string.Empty, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// It folds away to a labelled button and comes back, exactly as the action panel does — and the
    /// button that takes its place carries a word, not only an arrow (PLAN.md §6: no icon-only
    /// controls).
    ///
    /// <para>The setting is put back before this test ends. It is persisted, and the whole suite
    /// shares one settings file, so a test that left the rail hidden would hide it from every test
    /// that ran afterwards.</para>
    /// </summary>
    [Fact]
    public async Task TheRailFoldsAwayToALabelledButtonAndComesBack()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.RailIsShowingForTest, "the rail did not start on screen");

            try
            {
                window.TogglePageRail();
                Assert.False(window.RailIsShowingForTest);

                // The HOST, not the button. ApplyRailVisibility toggles the Border the button
                // stands in and never the button's own IsVisible, which is the default true at all
                // times — so asserting on the button proved nothing, and the one thing this test
                // exists to show is that something labelled takes the rail's place on screen.
                Assert.True(
                    window.CollapsedRailHostForTest.IsVisible,
                    "the rail folded away and nothing took its place");
                Assert.Contains(
                    "Pages",
                    window.ShowRailButtonForTest.Content as string ?? string.Empty,
                    StringComparison.Ordinal);
                Assert.Contains(
                    "hidden", window.StatusLabelTextForTest ?? string.Empty, StringComparison.Ordinal);
            }
            finally
            {
                window.TogglePageRail();
            }

            Assert.True(window.RailIsShowingForTest, "the rail did not come back");
            Assert.False(
                window.CollapsedRailHostForTest.IsVisible,
                "the rail came back and the button that stood in for it is still there");
            Assert.Contains(
                "showing", window.StatusLabelTextForTest ?? string.Empty, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The menu's half of <c>page.goTo</c>. A menu item cannot know which page somebody wants — that
    /// is the whole reason the parameter rides in the pressed control's <c>Tag</c> — so what it does
    /// instead is open the chooser and stand the keyboard on it, and say so.
    /// </summary>
    [Fact]
    public async Task GoToAPageFromTheMenuOpensTheRailAndStandsOnTheCurrentPage()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.Show();
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                window.OpenIssueSample();
                window.GoToPage(2);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                // No source control at all: this is the menu item's call, which carries an action id
                // and nothing else.
                await window.ActionsForTest.RunAsync(ActionId.GoToPage);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(2, window.RailForTest.FocusedTileForTest);
                Assert.Contains(
                    "arrow keys",
                    window.StatusLabelTextForTest ?? string.Empty,
                    StringComparison.Ordinal);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    private static void Press(MainWindow window, Key key) =>
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, keySymbol: null);

    // ---- the miniature and the page it is a picture of -------------------------------------------

    /// <summary>
    /// <b>Reordering is the rail's headline feature, and it used to break the rail's own pictures.</b>
    ///
    /// <para>The cache was keyed by the page's POSITION. Moving a page leaves the page count alone,
    /// so the tiles are not rebuilt and every tile is handed back the bitmap filed under its index —
    /// the picture of whatever page used to be standing there. Deleting a page shifted every
    /// miniature after it by one. The label and the accessible name were recomputed correctly on the
    /// same pass, so the picture and the name actively disagreed, and the miniature is the only
    /// channel a low-vision user has for recognising a page by its shape.</para>
    ///
    /// <para>Driven against the rail directly rather than through a newsletter, because what has to
    /// be proved is that a specific bitmap follows a specific page — and object identity is the only
    /// unambiguous way to say "the same picture". The renderer here is keyed by index exactly as the
    /// shell's is, which is the whole trap: it will happily draw the wrong page if it is asked.</para>
    /// </summary>
    [Fact]
    public async Task AMiniatureFollowsItsPageWhenThePagesAreMovedOrDeleted()
    {
        await Session.Dispatch(() =>
        {
            var pages = new List<string> { "page-a", "page-b", "page-c" };
            var rail = new PageRail(
                index => index >= 0 && index < pages.Count ? PngFor(pages[index]) : null,
                _ => { });

            var host = new Window { Content = rail, Width = 400, Height = 700 };
            host.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Refresh(rail, pages);
            List<Bitmap?> drawn = MiniaturesOf(rail);
            Assert.Equal(3, drawn.Count);
            Assert.All(drawn, picture => Assert.NotNull(picture));
            Assert.Equal(3, drawn.Distinct().Count());

            // "Move this page later" on the first page: the two swap and the page COUNT does not
            // change, which is precisely why nothing rebuilt the tiles and nothing noticed.
            (pages[0], pages[1]) = (pages[1], pages[0]);
            Refresh(rail, pages);

            List<Bitmap?> moved = MiniaturesOf(rail);
            Assert.Same(drawn[1], moved[0]);
            Assert.Same(drawn[0], moved[1]);
            Assert.Same(drawn[2], moved[2]);

            // Delete what is now the first page. Everything after it shifts down one, and the two
            // that remain must still be showing themselves.
            pages.RemoveAt(0);
            Refresh(rail, pages);

            List<Bitmap?> afterDelete = MiniaturesOf(rail);
            Assert.Equal(2, afterDelete.Count);
            Assert.Same(drawn[0], afterDelete[0]);
            Assert.Same(drawn[2], afterDelete[1]);

            host.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>One refresh of the rail, exactly as <c>RefreshActions</c> makes it.</summary>
    private static void Refresh(PageRail rail, IReadOnlyList<string> pages)
    {
        rail.Update(
            pages,
            currentPage: 0,
            ActionAvailability.Available,
            ActionAvailability.Available,
            ActionAvailability.Available);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The picture inside each tile, in tile order.</summary>
    private static List<Bitmap?> MiniaturesOf(PageRail rail) =>
        [.. rail.TilesForTest.Select(tile => tile.GetLogicalDescendants()
            .OfType<Image>()
            .Single()
            .Source as Bitmap)];

    /// <summary>A tiny valid PNG. Only its identity matters, so the colour merely makes it real.</summary>
    private static byte[] PngFor(string pageId)
    {
        using var bitmap = new SKBitmap(8, 8);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor((byte)(pageId.Length * 31 % 256), 0x22, 0x33));
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// **At 200% the page keeps a page's worth of window, and it was a screenshot that asked.**
    ///
    /// <para>The rail and the panel are both wrapped in <c>LayoutTransformControl</c>s, so at the
    /// 200% UI scale PLAN.md §6 offers this audience they ask for twice their width — 368 and 720.
    /// Both were tested against the SAME constant threshold, against the RAW window width, so each
    /// asked "is there room for one of me", both answered yes, and in a 1280 window they took 1088
    /// between them and left the newsletter about 190 pixels. <c>scale-200.png</c> showed it plainly
    /// and no test did, because every fold test here resizes the window and none of them changes the
    /// scale — the same shape as M69, where the guard was there and invisible.</para>
    ///
    /// <para>The rail is what gives way, and that is a decision rather than an accident: §6 puts the
    /// commands in the panel ("actions belong next to the object"), while the rail is a faster route
    /// to a page Previous and Next still reach. So this asserts BOTH halves — the rail folds, and it
    /// folds to something labelled that brings it back.</para>
    /// </summary>
    [Fact]
    public async Task AtTwiceTheSizeTheRailGivesWayRatherThanSqueezingThePageOut()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            TrestleBoard.App.Settings.AppSettings before = window.SettingsForTest;
            try
            {
                window.ApplySettings(before with { UiScalePercent = 200 });
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.False(
                    window.RailIsShowingForTest,
                    "at 200% the rail is 368px and the panel 720px, which in a 1280 window leaves "
                        + "the newsletter a sliver — the rail must fold away instead");
                Assert.True(
                    window.CollapsedRailHostForTest.IsVisible,
                    "the rail folded away at 200% and nothing labelled took its place");

                // The half that says the fix was a fold and not a deletion: the page still has a
                // page's worth of window to be drawn in.
                Assert.True(
                    window.CanvasScrollerWidthForTest >= 400,
                    $"the newsletter was left {window.CanvasScrollerWidthForTest:F0}px wide at 200%");
            }
            finally
            {
                window.ApplySettings(before);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            Assert.True(window.RailIsShowingForTest, "the rail did not come back at 100%");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
