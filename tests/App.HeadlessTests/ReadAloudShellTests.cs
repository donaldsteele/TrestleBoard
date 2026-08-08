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
