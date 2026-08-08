using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Integration;
using TrestleBoard.Core.Phrases;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M54 through the shell: the words landing in the newsletter as one undo step, the shelf keeping
/// what the user saved, and the window asking one blank at a time.
/// </summary>
public sealed class PhraseShellTests
{
    private static MainWindow OpenTyping()
    {
        var window = new MainWindow();
        window.Show();
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Put the caret in the first frame, which is what "click into some writing" leaves behind.
        string storyId = window.PackageForTest!.Document.Stories[0].Id;
        window.EditorForTest!.SelectRange(storyId, 0, 0, 0);
        return window;
    }

    // ---- putting the words in ------------------------------------------------------------------

    [Fact]
    public async Task TheWordsGoInWhereTheCursorIs()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenTyping();
            try
            {
                window.PhraseAnswerForTest = "The lodge will miss him.";

                await window.InsertPhraseAsync();

                string text = Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest!.Document.Stories[0].Paragraphs[0]);
                Assert.StartsWith("The lodge will miss him.", text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The reason <c>InsertBlock</c> exists rather than <c>InsertText</c>: a bare insert coalesces
    /// with the typing either side of it, so one Ctrl+Z would take back a memorial the user did not
    /// mean to lose along with the sentence they did.
    /// </summary>
    [Fact]
    public async Task TakingBackWhatYouTypedAfterwardsDoesNotTakeBackTheWords()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenTyping();
            try
            {
                window.PhraseAnswerForTest = "The lodge will miss him.";
                await window.InsertPhraseAsync();
                window.EditorForTest!.InsertText("And so say all of us.");

                window.Undo();

                string text = Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest!.Document.Stories[0].Paragraphs[0]);
                Assert.DoesNotContain("And so say all of us.", text, StringComparison.Ordinal);
                Assert.Contains("The lodge will miss him.", text, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OneUndoTakesTheWholeParagraphBackOut()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenTyping();
            try
            {
                string before = Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest!.Document.Stories[0].Paragraphs[0]);
                window.PhraseAnswerForTest = "The lodge will miss him.";
                await window.InsertPhraseAsync();

                window.Undo();

                Assert.Equal(before, Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest!.Document.Stories[0].Paragraphs[0]));
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>M11: the refusal names the way out, in the user's own terms.</summary>
    [Fact]
    public void WithTheCursorNowhereBothCommandsSayWhatToDoFirst()
    {
        ActionAvailability insert = ActionCatalog.Evaluate(
            ActionId.InsertPhrase, ActionContext.Empty with { HasDocument = true });
        ActionAvailability save = ActionCatalog.Evaluate(
            ActionId.SavePhrase, ActionContext.Empty with { HasDocument = true });

        Assert.False(insert.IsAvailable);
        Assert.Contains("Click into some writing", insert.Reason, StringComparison.Ordinal);
        Assert.False(save.IsAvailable);
        Assert.Contains("Highlight the words", save.Reason, StringComparison.Ordinal);
    }

    // ---- the shelf -----------------------------------------------------------------------------

    [Fact]
    public void WhatTheUserKeepsIsThereNextTime()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"trestleboard-phrases-{Guid.NewGuid():N}.json");
        try
        {
            new PhraseShelf(path).Save("Our own memorial", "The lodge will miss him.");

            IReadOnlyList<Phrase> afterRestart = new PhraseShelf(path).All();

            Phrase mine = Assert.Single(afterRestart, p => p.IsMine);
            Assert.Equal("Our own memorial", mine.Title);
            Assert.Equal("The lodge will miss him.", mine.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The bundled paragraphs come first and stay put. A list that reorders itself as you use it is
    /// a list you cannot learn, which matters more to this audience than to most.
    /// </summary>
    [Fact]
    public void TheShippedParagraphsComeFirstAndTheUsersFollow()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"trestleboard-phrases-{Guid.NewGuid():N}.json");
        try
        {
            var shelf = new PhraseShelf(path);
            shelf.Save("Our own memorial", "The lodge will miss him.");

            IReadOnlyList<Phrase> all = shelf.All();

            Assert.Equal(PhraseLibrary.Bundled.Count + 1, all.Count);
            Assert.All(all.Take(PhraseLibrary.Bundled.Count), p => Assert.False(p.IsMine));
            Assert.True(all[^1].IsMine);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavingTwiceUnderOneNameReplacesRatherThanMakingATwin()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"trestleboard-phrases-{Guid.NewGuid():N}.json");
        try
        {
            var shelf = new PhraseShelf(path);
            shelf.Save("Our own memorial", "First wording.");
            shelf.Save("Our own memorial", "Second wording.");

            Phrase mine = Assert.Single(shelf.All(), p => p.IsMine);
            Assert.Equal("Second wording.", mine.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AShelfFileThatIsRubbishCostsTheSavedWordsRatherThanTheEvening()
    {
        string path = Path.Combine(
            Path.GetTempPath(), $"trestleboard-phrases-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "this is not json at all");

            IReadOnlyList<Phrase> all = new PhraseShelf(path).All();

            Assert.Equal(PhraseLibrary.Bundled.Count, all.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---- the window ----------------------------------------------------------------------------

    [Fact]
    public async Task TheWindowAsksOneBlankAtATimeAndReadsItBackBeforeAnythingGoesIn()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var window = new PhraseWindow(PhraseLibrary.Bundled);
            Phrase memorial = PhraseLibrary.Find("memorial")!;

            Assert.Equal(0, window.ScreenForTest);
            Assert.Contains("Which words", window.HeadingForTest, StringComparison.Ordinal);

            window.ChooseForTest(memorial);
            Assert.Equal(memorial.Blanks[0].Question, window.HeadingForTest);

            window.AnswerForTest("{name}", "A. Placeholder");
            window.AdvanceForTest();
            Assert.Equal(memorial.Blanks[1].Question, window.HeadingForTest);

            window.AnswerForTest("{date}", "14 September 2026");
            window.AdvanceForTest();

            // The read-back screen: the whole paragraph, and nothing in the newsletter yet.
            Assert.Contains("how it reads", window.HeadingForTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("A. Placeholder", window.BodyForTest, StringComparison.Ordinal);
            Assert.Contains("14 September 2026", window.BodyForTest, StringComparison.Ordinal);
            Assert.False(window.Confirmed);

            window.AdvanceForTest();
            Assert.True(window.Confirmed);
            Assert.Contains("A. Placeholder", window.Words!, StringComparison.Ordinal);

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EveryButtonIsBigEnoughToHitAndSaysWhatItDoes()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var window = new PhraseWindow(PhraseLibrary.Bundled);

            Assert.All(window.ButtonsForTest, b =>
            {
                Assert.True(b.MinHeight >= 44, $"'{b.Content}' is {b.MinHeight} high; §6 asks for 44.");
                Assert.True(b.FontSize >= 18, $"'{b.Content}' is {b.FontSize}pt; §6 asks for 18.");
                Assert.False(
                    string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(b)),
                    $"'{b.Content}' has nothing to say to a screen reader.");
            });

            // Every shipped paragraph is offered by name on the first screen.
            IReadOnlyList<string> labels =
                [.. window.ButtonsForTest.Select(b => (b.Content as string) ?? "")];
            Assert.All(PhraseLibrary.Bundled, p => Assert.Contains(p.Title, labels));

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }
}
