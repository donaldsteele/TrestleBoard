using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M51 driven through the real shell: the offer before "Make the PDF", the review window itself,
/// and "Take me there" landing on the right page with the right thing chosen.
///
/// <para>The checklist's own judgement is <c>Editing.Tests/ReviewChecklistTests</c>'s business.
/// What is tested here is everything that needs a window: that the offer never stands in the way,
/// that "stop asking" is remembered, and that the screens read the way §6 requires.</para>
/// </summary>
public sealed class ReviewShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static MainWindow OpenLaidOut()
    {
        var window = new MainWindow();
        window.Show();

        // Immediately after Show(), never at the end — the M50 rule. A failing assertion below must
        // not leave Close() raising a dialog a headless run cannot answer.
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    public async Task TheReviewOpensOnTheNewsletterAndAsksAboutEveryPage()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ShowReview();

                ReviewWindow? review = window.ReviewWindowForTest;
                Assert.NotNull(review);
                Assert.Equal(0, review.ScreenForTest);
                Assert.Contains("look it over", review.HeadingForTest, StringComparison.OrdinalIgnoreCase);

                IReadOnlyList<ReviewFinding> findings = window.BuildReviewFindings();
                Assert.Equal(
                    5,
                    findings.Count(f => f.Kind == ReviewFindingKind.LookAtThePage));

                review.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md §11 M51: <i>"Make the PDF" is never blocked</i>. Answering "make it now" must leave
    /// the export doing exactly what it did before this milestone existed.
    /// </summary>
    [Fact]
    public async Task SayingMakeItNowLeavesTheExportAlone()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ReviewOfferAnswerForTest = MainWindow.ReviewOffer.MakeItNow;

                // Headlessly the file picker answers null, so this returns having done nothing —
                // which is the point: no review window was opened and nothing stood in the way.
                await window.ExportPdfAsync();

                Assert.Null(window.ReviewWindowForTest);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SayingLookItOverOpensTheReviewInsteadOfThePicker()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ReviewOfferAnswerForTest = MainWindow.ReviewOffer.LookItOver;

                await window.ExportPdfAsync();

                Assert.NotNull(window.ReviewWindowForTest);
                window.ReviewWindowForTest!.Close();
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The preference lives in the user's settings rather than the newsletter, so that saying "stop
    /// asking" cannot mark a newsletter as edited on the way to exporting it.
    /// </summary>
    [Fact]
    public async Task StopAskingIsRememberedAndTheExportStillGoesAhead()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                Assert.True(window.SettingsForTest.OfferTheReviewBeforeExport);
                window.ReviewOfferAnswerForTest = MainWindow.ReviewOffer.MakeItNowAndStopAsking;

                await window.ExportPdfAsync();

                Assert.False(window.SettingsForTest.OfferTheReviewBeforeExport);
                Assert.Null(window.ReviewWindowForTest);

                // And the second time it does not even ask: the answer is null now, so an offer
                // would hang waiting for a dialog nobody can answer.
                window.ReviewOfferAnswerForTest = null;
                await window.ExportPdfAsync();
                Assert.Null(window.ReviewWindowForTest);
            }
            finally
            {
                window.SetOfferTheReviewForTest(true);
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TakeMeThereTurnsToThePageAndChoosesTheThing()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                // The sample issue is a finished newsletter, so it has no defects to navigate to —
                // which is the right thing for it to be. What "Take me there" has to do is turn to
                // a page and choose a thing, so it is pointed at a real frame on a later page.
                string blockId = window.PackageForTest!.Document.Pages[2].Blocks[0].Id;
                var onALaterPage = new ReviewFinding(
                    ReviewFindingKind.PictureWithoutCaption,
                    PageNumber: 3,
                    blockId,
                    "The picture on page 3 has nothing printed under it",
                    "Would you like to write a caption?");

                var review = new ReviewWindow(
                    [onALaterPage],
                    window.TakeMeToTheFindingForTest,
                    _ => Task.CompletedTask);

                // The buttons are rebuilt for each screen, so ask for them on the screen that has
                // the one being pressed.
                review.GoToForTest(1);
                Button there = review.ButtonsForTest.First(b => (b.Content as string) == "Take me there");
                InvokeClick(there);

                Assert.Equal(onALaterPage.PageNumber - 1, window.PageIndexForTest);
                Assert.Equal(onALaterPage.BlockId, window.FramesForTest?.SelectedBlockId);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    // ---- the window's own manners (PLAN.md §6) -------------------------------------------------

    [Fact]
    public async Task EveryScreenIsWalkableForwardsAndBackwardsAndEndsByClosing()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var findings = new List<ReviewFinding>
            {
                new(ReviewFindingKind.UnwrittenWords, 1, "frame-1", "Page 1 still says a prompt",
                    "Would you like to write something here?"),
                new(ReviewFindingKind.LookAtThePage, 1, null, "Page 1 — have a look",
                    "Anything out of place?"),
            };
            var review = new ReviewWindow(findings, _ => { }, _ => Task.CompletedTask);
            bool closed = false;
            review.Closed += (_, _) => closed = true;

            Assert.Equal(0, review.ScreenForTest);
            review.GoToForTest(1);
            Assert.Equal("Screen 1 of 2", review.ProgressForTest);
            review.GoToForTest(2);
            Assert.Equal("Screen 2 of 2", review.ProgressForTest);
            review.GoToForTest(1);
            Assert.Equal(1, review.ScreenForTest);

            // Past the last screen there is nothing to finish, because nothing was ever changed.
            review.GoToForTest(3);
            Assert.True(closed);

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ANewsletterWithNothingWrongIsToldSoPlainly()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var review = new ReviewWindow(
                [new ReviewFinding(ReviewFindingKind.LookAtThePage, 1, null, "Page 1 — have a look", "Well?")],
                _ => { },
                _ => Task.CompletedTask);

            Assert.Contains("nothing jumped out", review.BodyForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("nothing here changes your newsletter", review.BodyForTest, StringComparison.OrdinalIgnoreCase);

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EveryButtonOnEveryScreenIsBigEnoughToHitAndBigEnoughToRead()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var review = new ReviewWindow(
                [
                    new ReviewFinding(ReviewFindingKind.WritingThatRanOut, 2, "frame-2",
                        "Page 2 has more writing than fits", "Shall I flow the rest?",
                        Editing.Actions.ActionId.AutoFlow),
                ],
                _ => { },
                _ => Task.CompletedTask);

            review.GoToForTest(1);

            Assert.All(review.ButtonsForTest, b =>
            {
                Assert.True(b.MinHeight >= 44, $"'{b.Content}' is {b.MinHeight} high; §6 asks for 44.");
                Assert.True(b.FontSize >= 18, $"'{b.Content}' is {b.FontSize}pt; §6 asks for 18.");
                Assert.False(
                    string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(b)),
                    $"'{b.Content}' has nothing to say to a screen reader.");
            });

            // The remedy is offered by its own name from the catalog, never as "fix it".
            Assert.Contains(review.ButtonsForTest, b => (b.Content as string) == "Make the rest fit");

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    private static void InvokeClick(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
}
