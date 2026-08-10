using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Editing.Review;
using TrestleBoard.Spelling;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M52 through the real shell: the wizard's screens, the correction going through the document as
/// one undo step, and the underline being chrome the PDF cannot see.
///
/// <para>The checker's own judgement is <c>Spelling.Tests</c>'s business, and the proof that a
/// squiggle cannot reach the PDF is <c>TheCheckerCannotReachThePageTests</c>'s — it is a fact about
/// the reference graph, not something a rendered page can demonstrate.</para>
/// </summary>
public sealed class SpellingShellTests
{
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
    public async Task TheWizardOpensAndCountsWhatItDoesNotKnow()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                window.ShowSpellingCheck();

                SpellingWindow? spelling = window.SpellingWindowForTest;
                Assert.NotNull(spelling);
                Assert.Equal(0, spelling.ScreenForTest);
                Assert.Contains("check the spelling", spelling.HeadingForTest, StringComparison.OrdinalIgnoreCase);

                spelling.Close();
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The correction goes through <c>IDocumentCommand</c> like everything else, so one Ctrl+Z puts
    /// the old word back — the same composite find-and-replace has used since M21.
    /// </summary>
    [Fact]
    public async Task ChangingAWordIsOneUndoStep()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var story = window.PackageForTest!.Document.Stories[0];
                string before = story.Paragraphs[0].Runs[0].Text;
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    story.Id, 0, 0, 0, "Chruch ", "Type"));

                var word = new Misspelling(story.Id, 0, 0, "Chruch", "Chruch supper on Tuesday.");
                Assert.True(window.ChangeTheWordForTest(word, "Church"));
                Assert.StartsWith("Church", story.Paragraphs[0].Runs[0].Text, StringComparison.Ordinal);

                window.Undo();
                Assert.StartsWith("Chruch", story.Paragraphs[0].Runs[0].Text, StringComparison.Ordinal);

                window.Undo();
                Assert.Equal(before, story.Paragraphs[0].Runs[0].Text);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Somebody typed while the wizard was open and the word moved. Changing the wrong six
    /// characters would be far worse than saying so.
    /// </summary>
    [Fact]
    public async Task AWordThatHasMovedIsNotChangedBlindly()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var story = window.PackageForTest!.Document.Stories[0];
                var stale = new Misspelling(story.Id, 0, 0, "Chruch", "Chruch supper.");

                Assert.False(window.ChangeTheWordForTest(stale, "Church"));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73 (g), and the only silent corruption the audit found. The guard asked "are the same
    /// characters still at the recorded offset?", which is not the same question as "is this the
    /// same word". An edit that shifts a paragraph by exactly the gap between two copies of the
    /// same word leaves an identical token sitting at the recorded offset: the guard passed, the
    /// wrong copy was corrected, and the window reported a success.
    ///
    /// <para>Rare — it needs the shift to be exactly right — and quiet, which is why it is treated
    /// as corruption rather than as a nuisance.</para>
    /// </summary>
    [Fact]
    public async Task AnIdenticalWordThatSlidIntoThePlaceIsNotCorrectedInstead()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var story = window.PackageForTest!.Document.Stories[0];

                // Two copies of the same unknown word, seven characters apart.
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    story.Id, 0, 0, 0, "Chruch Chruch ", "Type"));

                // The scan's own answer for the SECOND of them, offset 7, occurrence 2.
                Misspelling second = SpellCheckScan
                    .Run(window.PackageForTest.Document, window.Spelling.Checker)
                    .First(m => m.Word == "Chruch" && m.Occurrence == 2);
                Assert.Equal(7, second.Offset);

                // Now exactly seven characters go in ahead of both, so the FIRST "Chruch" comes to
                // rest on offset 7 — the recorded place, the recorded characters, the wrong word.
                window.SessionForTest.Execute(Editing.TextReplacement.Build(
                    story.Id, 0, 0, 0, "Brother", "Type"));
                string before = Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[0]);
                Assert.Equal("Chruch", before.Substring(7, 6));

                Assert.False(
                    window.ChangeTheWordForTest(second, "Church"),
                    "the correction landed on an identical word that had slid into the recorded "
                    + "place, and reported it as done");
                Assert.Equal(before, Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[0]));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheUnderlinesCanBeTurnedOffAndTheAppSaysTheyNeverPrint()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                Assert.True(window.SettingsForTest.ShowSpelling);

                window.ToggleShowSpelling();
                Assert.False(window.SettingsForTest.ShowSpelling);
                Assert.Contains("hidden again", window.StatusLabelTextForTest!, StringComparison.Ordinal);

                window.ToggleShowSpelling();
                Assert.True(window.SettingsForTest.ShowSpelling);
                Assert.Contains("never prints", window.StatusLabelTextForTest!, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M51 scheduled itself first so that this milestone could add a station rather than build a
    /// second wizard. The station appears only when there is something to say, and it carries the
    /// spell-check command as its remedy.
    /// </summary>
    [Fact]
    public async Task TheSpellingStationJoinsTheReviewOnlyWhenThereIsSomethingToAsk()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var story = window.PackageForTest!.Document.Stories[0];
                window.SessionForTest!.Execute(Editing.TextReplacement.Build(
                    story.Id, 0, 0, 0, "Chruch ", "Type"));

                ReviewFinding station = Assert.Single(
                    window.BuildReviewFindings(),
                    f => f.Kind == ReviewFindingKind.SpellingToCheck);

                Assert.Equal(Editing.Actions.ActionId.CheckSpelling, station.RemedyActionId);
                Assert.Contains("names are not mistakes", station.Question, StringComparison.Ordinal);
                Assert.EndsWith("?", station.Question, StringComparison.Ordinal);
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
    public async Task EveryButtonIsBigEnoughToHitAndSaysWhatItDoes()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var words = new List<Misspelling>
                {
                    new("story-1", 0, 0, "chruch", "The chruch supper is on Tuesday."),
                };
                var spelling = new SpellingWindow(
                    words, window.Spelling.Checker, _ => 1, (_, _) => true, _ => { });

                spelling.GoToForTest(1);

                Assert.Contains("chruch", spelling.HeadingForTest, StringComparison.Ordinal);
                Assert.Equal("The chruch supper is on Tuesday.", spelling.SentenceForTest);
                Assert.Equal("Word 1 of 1", spelling.ProgressForTest);

                Assert.All(spelling.ButtonsForTest, b =>
                {
                    Assert.True(b.MinHeight >= 44, $"'{b.Content}' is {b.MinHeight} high; §6 asks for 44.");
                    Assert.True(b.FontSize >= 18, $"'{b.Content}' is {b.FontSize}pt; §6 asks for 18.");
                    Assert.False(
                        string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(b)),
                        $"'{b.Content}' has nothing to say to a screen reader.");
                });

                // The three answers this audience needs, in their own words.
                IReadOnlyList<string> labels = [.. spelling.ButtonsForTest.Select(b => (b.Content as string) ?? "")];
                Assert.Contains("It's fine, leave it", labels);
                Assert.Contains("It's a name — never ask again", labels);
                Assert.Contains(labels, l => l.StartsWith("Change it to", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ANewsletterWithNoMisspellingsIsToldSoWithoutOverpromising()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenLaidOut();
            try
            {
                var spelling = new SpellingWindow(
                    [], window.Spelling.Checker, _ => 1, (_, _) => true, _ => { });

                // "Nothing is misspelled" is true; "everything is right" would not be, and the
                // difference matters to somebody about to send six pages to sixty people.
                Assert.Contains("knew all of them", spelling.SentenceForTest, StringComparison.Ordinal);
                Assert.Contains("form", spelling.SentenceForTest, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }
}
