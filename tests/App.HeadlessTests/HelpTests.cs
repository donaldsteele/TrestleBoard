using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Help;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Help;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M63's "How do I…?" window and the first-run tour (PLAN.md §11 M63).
///
/// <para>The index itself is tested in <c>Editing.Tests/HelpIndexTests</c>, which needs no Avalonia.
/// What is left for here is the half that can only be true of a running app: that the menu paths are
/// read off the real menu bar, that an answer says where to find the thing, and that "Take me there"
/// tells the truth about whether it can.</para>
/// </summary>
public sealed class HelpTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    // ---- the menu paths, read off the menu bar rather than written down -------------------------

    /// <summary>
    /// The claim M63 rests on: the path in a help answer is derived from the menu bar, so it cannot
    /// describe a menu that has been reorganised since. This is the test that would fail if somebody
    /// moved a command and the help went on pointing at where it used to be.
    /// </summary>
    [Fact]
    public async Task EveryCommandInTheMenusCanBeToldWhereItLives()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            IReadOnlyDictionary<string, string> paths = MenuPaths.From(window);

            Assert.True(paths.Count > 90, $"only {paths.Count} paths were derived — the walk found nothing");

            // Every catalog action has a menu item (MenuIndexTests holds that), so every one of them
            // must come back with somewhere to be found.
            List<string> lost = [.. ActionCatalog.All.Select(a => a.Id).Where(id => !paths.ContainsKey(id))];
            Assert.True(lost.Count == 0, "no menu path could be derived for: " + string.Join(", ", lost));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task APathReadsTheWaySomebodyWouldSayItOutLoud()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            IReadOnlyDictionary<string, string> paths = MenuPaths.From(window);

            // The ellipsis is gone but the question mark is not: one is markup, the other is the name.
            Assert.Equal("Help " + MenuPaths.Arrow.Trim() + " How do I?", Tidy(paths[ActionId.HowDoI]));
            Assert.Equal("File " + MenuPaths.Arrow.Trim() + " Make the PDF", Tidy(paths[ActionId.ExportPdf]));

            window.Close();
        }, TestContext.Current.CancellationToken);

        static string Tidy(string path) => path.Replace("  ", " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// The access-key underscore is markup for the keyboard, not part of the name. "F_ormat" read
    /// aloud as "F underscore ormat" is worse than no path at all.
    /// </summary>
    [Fact]
    public async Task NoPathCarriesTheAccessKeyMarkup()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            List<string> shouty =
            [
                .. MenuPaths.From(window).Values.Where(p => p.Contains('_', StringComparison.Ordinal)),
            ];

            Assert.True(shouty.Count == 0, "these paths still have access-key markup in them: "
                + string.Join(", ", shouty));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>A command in a submenu is named through it, or the path sends somebody to the wrong menu.</summary>
    [Fact]
    public async Task ACommandInsideASubmenuIsNamedThroughIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string path = MenuPaths.From(window)[ActionId.FixPhoto];

            Assert.Equal(3, path.Split(MenuPaths.Arrow, StringSplitOptions.None).Length);
            Assert.StartsWith("Format", path, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- the window ------------------------------------------------------------------------------

    [Fact]
    public async Task TypingNarrowsTheListAndAnEmptyBoxShowsEverything()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            using HelpFixture help = HelpFixture.Open();

            int everything = help.Window.ShowingForTest.Count;
            Assert.Equal(ActionCatalog.All.Count, everything);

            help.Window.TypeForTest("picture");
            Assert.True(help.Window.ShowingForTest.Count < everything);
            Assert.True(help.Window.ShowingForTest.Count > 0);

            help.Window.TypeForTest("");
            Assert.Equal(everything, help.Window.ShowingForTest.Count);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A search that finds nothing must say so in words, and say what to do next. "0 results" leaves
    /// somebody who is already unsure believing they have broken it.
    /// </summary>
    [Fact]
    public async Task FindingNothingSaysSoAndSaysWhatToDo()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            using HelpFixture help = HelpFixture.Open();
            help.Window.TypeForTest("kubernetes");

            Assert.Empty(help.Window.ShowingForTest);
            Assert.Contains("Nothing matched", help.Window.CountTextForTest, StringComparison.Ordinal);
            Assert.Contains("empty the box", help.Window.CountTextForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("", help.Window.AnswerTextForTest);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md's acceptance: <i>each answer names the menu path and the shortcut</i>. Both are read
    /// from the app rather than written into the answer, which is the whole point.
    /// </summary>
    [Fact]
    public async Task AnAnswerNamesWhereToFindItAndHowToTypeIt()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            using HelpFixture help = HelpFixture.Open();
            help.Window.TypeForTest("bold");

            Assert.Equal(ActionId.Bold, help.Window.ShowingForTest[0].ActionId);
            Assert.Equal(ActionCatalog.Get(ActionId.Bold).ShortDescription, help.Window.AnswerTextForTest);
            Assert.Contains("Format", help.Window.WhereItIsTextForTest, StringComparison.Ordinal);
            Assert.Contains("Ctrl+B", help.Window.WhereItIsTextForTest, StringComparison.Ordinal);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The best answer is already open. This audience reads the first answer as the answer, so
    /// making them click it before they can read it is a step that earns nothing.
    /// </summary>
    [Fact]
    public async Task TheBestAnswerIsAlreadyOpenWithoutBeingClicked()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            using HelpFixture help = HelpFixture.Open();
            help.Window.TypeForTest("spelling");

            Assert.Equal(0, help.Window.ListForTest.SelectedIndex);
            Assert.False(string.IsNullOrWhiteSpace(help.Window.AnswerTextForTest));

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M11's rule reaches into the help too: a thing that cannot be done right now says why, in the
    /// same plain words the menu bar would use. Hiding the button and saying nothing would read as
    /// the help being broken.
    /// </summary>
    [Fact]
    public async Task SomethingThatCannotBeDoneRightNowSaysWhyInsteadOfOfferingIt()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            const string Reason = "There is no picture chosen, so there is nothing to fix.";

            using HelpFixture help = HelpFixture.Open(
                ask: id => id == ActionId.FixPhoto
                    ? ActionAvailability.Blocked(Reason)
                    : ActionAvailability.Available);

            help.Window.TypeForTest("sideways");

            Assert.Equal(ActionId.FixPhoto, help.Window.ShowingForTest[0].ActionId);
            Assert.False(help.Window.TakeMeThereForTest.IsVisible);
            Assert.Contains(Reason, help.Window.WhereItIsTextForTest, StringComparison.Ordinal);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TakeMeThereRunsTheThingTheyAskedAbout()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var ran = new List<string>();
            using HelpFixture help = HelpFixture.Open(run: id =>
            {
                ran.Add(id);
                return Task.CompletedTask;
            });

            help.Window.TypeForTest("bold");
            Assert.True(help.Window.TakeMeThereForTest.IsVisible);

            help.Window.TakeMeThereForTest.Command?.Execute(null);
            RaiseClick(help.Window.TakeMeThereForTest);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal([ActionId.Bold], ran);

            // It stays open: somebody who asked how to do one thing is often about to ask about the
            // next, and making them find this window again each time is what stops people using help.
            Assert.True(help.Window.IsVisible);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Help must never be unavailable — the moment it is refused is the moment it is needed.</summary>
    [Fact]
    public void AskingForHelpIsNeverRefused()
    {
        Assert.True(ActionCatalog.Evaluate(ActionId.HowDoI, new ActionContext()).IsAvailable);
        Assert.True(ActionCatalog.Evaluate(ActionId.ShowTheTour, new ActionContext()).IsAvailable);
    }

    [Fact]
    public async Task TheHelpWindowOpensAndOnlyOneOfItIsEverOpen()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();

            await window.ActionsForTest.RunAsync(ActionId.HowDoI);
            HelpWindow? first = window.HelpWindowForTest;
            Assert.NotNull(first);

            await window.ActionsForTest.RunAsync(ActionId.HowDoI);
            Assert.Same(first, window.HelpWindowForTest);

            first!.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(window.HelpWindowForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EscapeClosesTheHelpWindow()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            using HelpFixture help = HelpFixture.Open();

            help.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, keySymbol: null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(help.Window.IsVisible);

            await Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    // ---- the tour -----------------------------------------------------------------------------------

    /// <summary>PLAN.md says five screens, and five is a promise about how long this takes.</summary>
    [Fact]
    public void TheTourIsFiveScreensLong() => Assert.Equal(5, TourWindow.ScreenCount);

    /// <summary>
    /// The tour teaches the monthly cycle, not the buttons — so each of the five steps of that cycle
    /// has to actually appear in it. A tour that had drifted into a feature list would pass a count
    /// test and fail this one.
    /// </summary>
    [Theory]
    [InlineData("last month")]
    [InlineData("address book")]
    [InlineData("Look it over")]
    [InlineData("PDF")]
    [InlineData("email")]
    public void TheTourWalksTheMonthRatherThanListingFeatures(string mustMention)
    {
        string all = string.Concat(TourWindow.ScreensForTest.Select(s => s.Heading + " " + s.Body));

        Assert.Contains(mustMention, all, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The last thing the tour says is where to ask next. A tour that ends with "good luck" has
    /// taught somebody the shape of a month and left them with nowhere to go with a question.
    /// </summary>
    [Fact]
    public void TheLastScreenSaysWhereToAskEverythingElse() =>
        Assert.Contains("How do I", TourWindow.ScreensForTest[^1].Body, StringComparison.Ordinal);

    [Fact]
    public async Task TheTourCountsItsOwnScreensAndEndsWithDone()
    {
        await Session.Dispatch(() =>
        {
            var tour = new TourWindow();
            tour.Show();

            Assert.Equal("Screen 1 of 5", tour.ProgressForTest);
            Assert.False(tour.BackForTest.IsVisible);
            Assert.Equal("Next", tour.NextForTest.Content);

            tour.GoToForTest(4);
            Assert.Equal("Screen 5 of 5", tour.ProgressForTest);
            Assert.True(tour.BackForTest.IsVisible);
            Assert.Equal("Done", tour.NextForTest.Content);

            tour.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GoingPastTheLastScreenClosesTheTour()
    {
        await Session.Dispatch(() =>
        {
            var tour = new TourWindow();
            tour.Show();

            tour.GoToForTest(TourWindow.ScreenCount);

            Assert.False(tour.IsVisible);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md's acceptance: <i>never shown again unless asked</i>. Skipping counts as having seen
    /// it — the flag is claimed when the tour opens, not when it finishes, because somebody who
    /// closed it on screen two has decided.
    /// </summary>
    [Fact]
    public async Task TheTourIsOfferedOnceAndThenOnlyWhenAskedFor()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.SetHasSeenTheTourForTest(false);

            Assert.True(window.ClaimTheTour(becauseTheyAsked: false), "a new installation must be shown it");
            Assert.False(window.ClaimTheTour(becauseTheyAsked: false), "it must not come back on its own");
            Assert.False(window.ClaimTheTour(becauseTheyAsked: false));

            // …but "Show me round again" always works, or it would be a menu item that did nothing.
            Assert.True(window.ClaimTheTour(becauseTheyAsked: true));

            window.SetHasSeenTheTourForTest(false);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- plumbing --------------------------------------------------------------------------------------

    private static void RaiseClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>A help window on its own, with the two callbacks stubbed, closed on the way out.</summary>
    private sealed class HelpFixture : IDisposable
    {
        private HelpFixture(HelpWindow window) => Window = window;

        internal HelpWindow Window { get; }

        internal static HelpFixture Open(
            Func<string, ActionAvailability>? ask = null,
            Func<string, Task>? run = null)
        {
            var window = new HelpWindow(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ActionId.Bold] = "Format " + MenuPaths.Arrow.Trim() + " Bold",
                    [ActionId.FixPhoto] = "Format " + MenuPaths.Arrow.Trim() + " Picture",
                },
                ask ?? (_ => ActionAvailability.Available),
                run ?? (_ => Task.CompletedTask));
            window.Show();
            return new HelpFixture(window);
        }

        public void Dispose()
        {
            if (Window.IsVisible)
            {
                Window.Close();
            }
        }
    }
}
