using Avalonia.Headless;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Startup;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M108: fit the width.
///
/// <para>Fit page is what you use to see the SHAPE of a page; fit width is what you use to READ it,
/// because the writing is as large as it can be while a whole line still fits across. For the
/// audience PLAN.md §6 is about, that is the difference between editing a paragraph and squinting at
/// one — and the ladder has had Ctrl+0 and Ctrl+1 since M2 with no way at all to say "as big as will
/// fit sideways".</para>
/// </summary>
public sealed class FitWidthTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// <b>It fills the width, which means it is bigger than fitting the whole page.</b> On paper
    /// taller than it is wide — which every sheet this application prints on is — fitting the page
    /// is limited by the height, so fitting the width must magnify more. A "fit width" that came out
    /// the same size as fit page would be a menu item that does nothing.
    /// </summary>
    [Fact]
    public async Task FittingTheWidthMakesThePageBiggerThanFittingTheWholePage()
    {
        await Session.Dispatch(
            () =>
            {
                var window = new MainWindow { StartupOptions = new StartupOptions(null, SkipUpdateCheck: true) };
                window.Show();
                window.OpenSample();
                window.Width = 900;
                window.Height = 700;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.FitPage();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                double whole = window.CanvasZoomForTest;

                window.FitWidth();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                double wide = window.CanvasZoomForTest;

                Assert.True(
                    wide > whole,
                    $"fitting the width gave {wide:0.###} and fitting the page gave {whole:0.###}");

                window.Close();
                return true;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// It is a MODE, like fit page: the window can be resized and it goes on fitting. A one-off
    /// magnification would come undone the first time somebody widened the window, which for this
    /// audience reads as the setting not having taken.
    /// </summary>
    [Fact]
    public async Task ItKeepsFittingWhenTheWindowIsResized()
    {
        await Session.Dispatch(
            () =>
            {
                var window = new MainWindow { StartupOptions = new StartupOptions(null, SkipUpdateCheck: true) };
                window.Show();
                window.OpenSample();
                window.Width = 800;
                window.Height = 700;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.FitWidth();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                double narrow = window.CanvasZoomForTest;

                window.Width = 1200;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.True(window.FittingTheWidthForTest);
                Assert.True(
                    window.CanvasZoomForTest > narrow,
                    "a wider window must give a bigger page while the width is being fitted");

                window.Close();
                return true;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Choosing a size by hand ends the mode. Otherwise the next window resize would silently undo
    /// what the user had just chosen — and they would have no idea why.
    /// </summary>
    [Fact]
    public async Task ChoosingASizeByHandEndsIt()
    {
        await Session.Dispatch(
            () =>
            {
                var window = new MainWindow { StartupOptions = new StartupOptions(null, SkipUpdateCheck: true) };
                window.Show();
                window.OpenSample();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                window.FitWidth();
                Assert.True(window.FittingTheWidthForTest);

                window.ZoomToActualSize();
                Assert.False(window.FittingTheWidthForTest);

                window.Close();
                return true;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Ctrl+2, beside Ctrl+1 — the same question asked a second way.</summary>
    [Fact]
    public void ItHasItsOwnKeyBesideFitPage()
    {
        Assert.Equal(
            ActionId.FitWidth,
            KeyboardMap.Resolve(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.Control, isTyping: false));
    }

    /// <summary>And it needs a newsletter, like every other rung on the ladder.</summary>
    [Fact]
    public void WithNoNewsletterThereIsNothingToFit()
    {
        Assert.False(
            ActionCatalog.Evaluate(ActionId.FitWidth, new ActionContext()).IsAvailable);
    }
}
