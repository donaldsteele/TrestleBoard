using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Review;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M58 through the shell, on a machine with no voice — which is the machine every test runs on, and
/// deliberately the one the feature is designed around.
/// </summary>
public sealed class ReadAloudShellTests
{
    /// <summary>What a bare Linux box gets, and what every test here uses.</summary>
    private sealed class NoVoice : ISpeaker
    {
        internal int Asked { get; private set; }

        public bool Available => false;

        public bool Say(string text)
        {
            Asked++;
            return false;
        }

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

    [Fact]
    public async Task WithNoVoiceItWalksThroughInsteadAndSaysSo()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.UseSpeakerForTest(new NoVoice());

                window.ReadItBackToMe();

                ReadAloudWindow? aloud = window.ReadAloudWindowForTest;
                Assert.NotNull(aloud);
                Assert.Equal("Walk me through it", aloud.Title);
                Assert.Contains("no voice", aloud.SentenceForTest, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("no voice", window.StatusLabelTextForTest!, StringComparison.OrdinalIgnoreCase);

                aloud.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EachStepShowsTheNextSentence()
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
                string first = aloud.SentenceForTest;
                aloud.AdvanceForTest();
                string second = aloud.SentenceForTest;

                Assert.NotEqual(first, second);
                Assert.Equal("Sentence 2 of " + Sentences.In(window.PackageForTest!.Document).Count,
                    aloud.ProgressForTest);

                aloud.BackForTest();
                Assert.Equal(first, aloud.SentenceForTest);

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
    /// The highlight is chrome. It is set on the canvas, never on the document, and the exporter
    /// draws through a path that has never heard of it.
    /// </summary>
    [Fact]
    public async Task TheSentenceIsLitUpOnThePageAndGoesAwayWhenTheWindowCloses()
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
                Assert.NotEmpty(window.CanvasForTest.SpokenRects);

                aloud.Close();
                Assert.Empty(window.CanvasForTest.SpokenRects);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Nothing is said before the user asks. Opening the window and stopping must not have spoken.
    /// </summary>
    [Fact]
    public async Task OpeningItSaysNothingUntilAskedTo()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            var voice = new NoVoice();
            try
            {
                window.UseSpeakerForTest(voice);
                window.ReadItBackToMe();

                Assert.Equal(0, voice.Asked);

                window.ReadAloudWindowForTest!.AdvanceForTest();
                Assert.Equal(1, voice.Asked);

                window.ReadAloudWindowForTest!.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EveryButtonIsBigEnoughToHitAndSaysWhatItDoes()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.UseSpeakerForTest(new NoVoice());
                window.ReadItBackToMe();
                ReadAloudWindow aloud = window.ReadAloudWindowForTest!;

                Assert.All(aloud.ButtonsForTest, b =>
                {
                    Assert.True(b.MinHeight >= 44, $"'{b.Content}' is {b.MinHeight} high; §6 asks for 44.");
                    Assert.True(b.FontSize >= 18, $"'{b.Content}' is {b.FontSize}pt; §6 asks for 18.");
                    Assert.False(
                        string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(b)),
                        $"'{b.Content}' has nothing to say to a screen reader.");
                });

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
    /// M73 (g), the worst instance in the sweep: the walk captured the sentence text once, so
    /// correcting a paragraph while it was open had the app read the pre-fix wording back to
    /// somebody who was proofreading — non-destructive, silent, and actively misleading.
    ///
    /// <para>Editing mid-walk is the expected workflow here, not an edge case, so the walk re-reads
    /// rather than throwing itself away the way the find window throws its hit away.</para>
    /// </summary>
    [Fact]
    public async Task EditingTheSentenceBeingReadIsReadBackAsItNowStands()
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
                string wasReading = aloud.SentenceForTest;
                Sentence on = Sentences.In(window.PackageForTest!.Document)[0];
                Assert.Equal(on.Text, wasReading);

                // Somebody fixes a word on the page behind the window, which is exactly what this
                // window is for. One character in, so the sentence keeps its identity.
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    on.StoryId, on.ParagraphIndex, on.Offset, 1, "Zq", "Correct a letter"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                string nowInTheNewsletter = Sentences.In(window.PackageForTest!.Document)[0].Text;
                Assert.NotEqual(wasReading, nowInTheNewsletter);
                Assert.Equal(nowInTheNewsletter, aloud.SentenceForTest);

                // And the user is told, rather than left to notice: gate 27.
                Assert.Contains(
                    "changed while you were reading",
                    aloud.StatusForTest,
                    StringComparison.OrdinalIgnoreCase);

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
    /// The other half of the same defect: writing added ahead of where the reader is moves their
    /// place, and a walk that says "Sentence 4 of 112" while standing on the fifth is the same lie
    /// in a different sentence. The number is re-read and the move is said out loud.
    /// </summary>
    [Fact]
    public async Task WritingAddedAheadOfTheReaderMovesTheirPlaceAndSaysSo()
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
                Assert.Equal("Sentence 1 of " + Sentences.In(window.PackageForTest.Document).Count,
                    aloud.ProgressForTest);

                // A whole sentence typed in front of it: the reader is now on the second one.
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    on.StoryId, on.ParagraphIndex, on.Offset, 0, "Brethren all. ", "Type"));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(on.Text, aloud.SentenceForTest);
                Assert.Equal("Sentence 2 of " + Sentences.In(window.PackageForTest.Document).Count,
                    aloud.ProgressForTest);
                Assert.Contains("You are on sentence 2", aloud.StatusForTest, StringComparison.Ordinal);

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
    /// PLAN.md scheduled M51 first so that this could join its checklist rather than build a
    /// second review. It is the last station before the page-by-page look.
    /// </summary>
    [Fact]
    public async Task TheReviewOffersToReadItBackBeforeTheLookThrough()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var findings = window.BuildReviewFindings().ToList();

                ReviewFinding station = Assert.Single(
                    findings, f => f.Kind == ReviewFindingKind.ReadItBack);
                Assert.Equal(ActionId.ReadAloud, station.RemedyActionId);
                Assert.EndsWith("?", station.Heading, StringComparison.Ordinal);

                int readItBack = findings.IndexOf(station);
                int firstLook = findings.FindIndex(f => f.Kind == ReviewFindingKind.LookAtThePage);
                Assert.True(readItBack < firstLook, "The offer comes after the page-by-page look.");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }
}
