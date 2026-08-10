using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Help;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M73(f): <c>ActionRunner.RunAsync</c> used to hand back <c>null</c> for "ran and
/// nothing threw", and two windows read that as "it happened". A cancelled picker, a cancelled
/// wizard, a handler that early-returned and an action id with no handler at all were all
/// indistinguishable from success, so the app told the user it had done work it had not done.
/// </summary>
public sealed class ActionOutcomeTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static HelpWindow OpenHelp(Func<string, Task<ActionOutcome>> run) =>
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ActionId.Bold] = "Format " + MenuPaths.Arrow.Trim() + " Bold",
            },
            _ => ActionAvailability.Available,
            run,
            _ => { });

    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Help → "Do it for me" → the picker → Cancel. The window used to say
    /// <i>"Done: Put a picture here…"</i>, and help is exactly where somebody uncertain goes.
    /// </summary>
    [Fact]
    public async Task HelpDoesNotSayDoneWhenNothingHappened()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            HelpWindow help = OpenHelp(_ => Task.FromResult(ActionOutcome.Nothing));
            help.Show();
            try
            {
                help.TypeForTest("bold");
                Assert.True(help.TakeMeThereForTest.IsVisible);
                Click(help.TakeMeThereForTest);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain("Done", help.StatusTextForTest, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Nothing has changed", help.StatusTextForTest, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                help.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every remedy button in the review said "Done: …" on anything that did not hand back a
    /// sentence — including the wizard the user had just cancelled, which ticks a real defect off
    /// the walk-through the window exists to conduct.
    /// </summary>
    [Fact]
    public async Task AReviewRemedyThatChangedNothingDoesNotSayDone()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var review = new ReviewWindow(
                [
                    new ReviewFinding(ReviewFindingKind.WritingThatRanOut, 2, "frame-2",
                        "Page 2 has more writing than fits", "Shall I flow the rest?",
                        ActionId.AutoFlow),
                ],
                f => new ReviewLanding(true, f.PageNumber),
                _ => Task.FromResult(ActionOutcome.Nothing),
                _ => { });
            review.Show();
            try
            {
                review.GoToForTest(1);
                Button fix = review.ButtonsForTest.First(b =>
                    (b.Content as string) == ActionCatalog.Get(ActionId.AutoFlow).Title);
                Click(fix);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain("Done", review.StatusForTest, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Nothing has changed", review.StatusForTest, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                review.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An action id nobody implemented used to run nothing, say nothing, and be reported to the
    /// caller exactly as a command that had worked — <c>TryGetValue</c> in an <c>if</c> with no
    /// <c>else</c>. Nothing in the app may claim it did a thing no code exists to do.
    /// </summary>
    [Fact]
    public async Task AnActionIdWithNoHandlerIsLoudRatherThanSilentlyDone()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                window.ActionsForTest.ForgetHandlerForTest(ActionId.AddPage);
                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.AddPage);

                Assert.Equal(ActionResult.Refused, outcome.Result);
                Assert.False(string.IsNullOrWhiteSpace(outcome.Message));
                Assert.Contains("nothing has happened", outcome.Message!, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("nothing has happened", window.StatusLabelTextForTest ?? "", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// End to end, through the real runner: the headless picker answers with no file, which is what
    /// pressing Cancel does, and the command must report that nothing happened.
    /// </summary>
    [Fact]
    public async Task ACancelledPicturePickerReportsThatNothingHappened()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                ActionOutcome outcome = await window.ActionsForTest.RunAsync(ActionId.InsertPhoto);
                Assert.Equal(ActionResult.NothingHappened, outcome.Result);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
