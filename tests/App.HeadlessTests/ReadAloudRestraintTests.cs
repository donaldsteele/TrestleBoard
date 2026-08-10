using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M74 (e). M73 (g) made "Read it back to me" follow the newsletter as it is edited,
/// which is right — editing what you hear is the workflow, not an edge case. What it did not do was
/// separate the two reasons the window redraws. Every keystroke on the page behind raises
/// <c>Changed</c>, and the redraw that followed did everything a button press does: it turned the
/// page back to the sentence being read, it took the focus, and it said the voice-failure sentence
/// again. The feature fought the workflow it was built for.
/// </summary>
public sealed class ReadAloudRestraintTests
{
    /// <summary>A machine that reports a voice and then does not speak — the M70 case.</summary>
    private sealed class BrokenVoice : ISpeaker
    {
        public bool Available => true;

        public bool Say(string text) => false;

        public void Hush()
        {
        }
    }

    /// <summary>What a bare Linux box gets.</summary>
    private sealed class NoVoice : ISpeaker
    {
        public bool Available => false;

        public bool Say(string text) => false;

        public void Hush()
        {
        }
    }

    private static MainWindow OpenLaidOut()
    {
        var window = new MainWindow();
        window.Show();
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Types one harmless character into the LAST sentence of the newsletter, which is
    /// nowhere near the sentence being read: the walk must notice nothing about it.</summary>
    private static void TypeSomewhereElse(MainWindow window)
    {
        Sentence last = Sentences.In(window.PackageForTest!.Document)[^1];
        window.SessionForTest!.Execute(Editing.TextReplacement.Build(
            last.StoryId, last.ParagraphIndex, last.Offset, 0, "Zz ", "Type"));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The stated workflow: listen to a sentence on one page, go and fix something on another. The
    /// canvas snapped back to the sentence on every character typed, so the caret the user was
    /// working at went off the screen and the page they had turned to was taken away from them.
    /// Turning a page is a navigation, and navigations belong to the user.
    /// </summary>
    [Fact]
    public async Task TypingOnAnotherPageDoesNotSnapTheCanvasBackToTheSentence()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.UseSpeakerForTest(new NoVoice());
                window.ReadItBackToMe();
                ReadAloudWindow aloud = window.ReadAloudWindowForTest!;

                aloud.AdvanceForTest();
                Sentence on = Sentences.In(window.PackageForTest!.Document)[0];
                Assert.Equal(0, window.PageIndexForTest);

                // The user turns to another page to fix something there.
                window.GoToNextPageForTest();
                Assert.Equal(1, window.PageIndexForTest);

                // And types. The sentence being read is back on page 1; the user is not.
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    on.StoryId, on.ParagraphIndex, on.Offset, 1, "Zq", "Correct a letter"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, window.PageIndexForTest);

                aloud.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The heading is focused after a button press so that a screen reader reads the new sentence —
    /// the WizardWindow technique. Doing it on every keystroke instead takes the focus off whatever
    /// the user was on and, under a real screen reader, starts the announcement again mid-word.
    /// (How bad that is on NVDA is on the owner's hands-on list; the mechanism is wrong either way.)
    /// </summary>
    [Fact]
    public async Task TypingDoesNotTakeTheFocusBackToTheHeading()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.UseSpeakerForTest(new NoVoice());
                window.ReadItBackToMe();
                ReadAloudWindow aloud = window.ReadAloudWindowForTest!;
                aloud.AdvanceForTest();

                Button stop = aloud.ButtonsForTest[^1];
                stop.Focus();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(stop.IsFocused, "The test needs the focus to start on a button.");

                TypeSomewhereElse(window);

                Assert.True(stop.IsFocused, "A keystroke on the page behind moved the focus.");

                aloud.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A machine that reports a voice and cannot use it gets one honest sentence about it. It used
    /// to get one per keystroke, into the window AND into the main window's status bar, describing
    /// a failure that had not happened again — the status bar being a live region, that is a screen
    /// reader interrupting every letter typed to repeat the same paragraph.
    /// </summary>
    [Fact]
    public async Task TheVoiceFailureIsNotRepeatedOnEveryKeystroke()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.UseSpeakerForTest(new BrokenVoice());
                window.ReadItBackToMe();
                ReadAloudWindow aloud = window.ReadAloudWindowForTest!;

                aloud.AdvanceForTest();
                Assert.Contains("could not say that sentence out loud", aloud.StatusForTest, StringComparison.Ordinal);
                Assert.Contains(
                    "could not say that sentence out loud",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);

                // The status bar is a live region shared with the whole app. If the redraw repeats
                // the failure, a screen reader reads this paragraph out again on every letter typed.
                window.Announce("Page 2 of 6.");
                TypeSomewhereElse(window);

                Assert.DoesNotContain(
                    "could not say that sentence out loud",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);

                // The message is still on the window, where it is still true — it was not repeated,
                // it was simply left standing.
                Assert.Contains("could not say that sentence out loud", aloud.StatusForTest, StringComparison.Ordinal);

                aloud.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// "Take me there" said "That is it, picked out on page 4" while the highlight sat on page 3.
    /// The review is a snapshot and the page behind it stays editable, so a block reflows; the page
    /// number was repeated from the scan instead of re-derived from where the block is now, and the
    /// page number is the one thing the user checks against the screen before deciding whether the
    /// worry being described is really there.
    /// </summary>
    [Fact]
    public async Task TakeMeThereNamesThePageTheBlockIsOnNow()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                Core.Model.Block onPageTwo = window.SessionForTest!.Document.Pages[1].Blocks[0];

                // A finding written when that block was on page 1 — which is what a scan taken
                // before the writing above it grew says.
                var stale = new ReviewFinding(
                    ReviewFindingKind.LookAtThePage,
                    PageNumber: 1,
                    BlockId: onPageTwo.Id,
                    "Have a look at this",
                    "A placeholder worry.",
                    RemedyActionId: null);

                ReviewLanding landing = window.TakeMeToTheFindingForTest(stale);

                Assert.True(landing.FoundIt);
                Assert.Equal(2, landing.PageNumber);
                Assert.Equal(1, window.PageIndexForTest);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }
}
