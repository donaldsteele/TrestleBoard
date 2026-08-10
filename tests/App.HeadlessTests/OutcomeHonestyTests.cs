using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using TrestleBoard.App.Settings;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Spelling;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M73(e): announcements that were not a function of what happened. Every one of these
/// discarded a return value — or had no return value to discard — and then said something about an
/// outcome nobody had checked.
/// </summary>
public sealed class OutcomeHonestyTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static string LongProse => string.Concat(
        Enumerable.Repeat(
            "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ", 80));

    private static void Click(Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// "Delete this" took the frame off the page and said nothing at all. Somebody following the
    /// status bar with a screen reader had no way to know the key had done anything.
    /// </summary>
    [Fact]
    public async Task TakingSomethingOffThePageSaysSo()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.FramesForTest.Select(blockId);
                window.RefreshActions();

                await window.ActionsForTest.RunAsync(ActionId.DeleteFrame);

                Assert.Contains(
                    "Taken off the page",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Wrap is the one command whose whole effect is a reflow a few lines further down the page,
    /// and the app would not say which of the two things it had just done — or that it had done
    /// anything.
    /// </summary>
    [Fact]
    public async Task MakingTheWritingFlowAroundSomethingSaysWhichWayItWent()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.FramesForTest.Select(blockId);
                window.RefreshActions();

                await window.ActionsForTest.RunAsync(ActionId.ToggleWrap);
                Assert.Contains(
                    "now flows around",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);

                await window.ActionsForTest.RunAsync(ActionId.ToggleWrap);
                Assert.Contains(
                    "no longer flows around",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M71's own command. It said something only when it had chosen the frame for the user, so
    /// "Make the rest fit" on a frame the user chose themselves poured the writing onto new pages
    /// in silence — and <c>PageFlowController</c> nulls its own <c>StatusMessage</c> on the way.
    /// </summary>
    [Fact]
    public async Task MakingTheRestFitSaysWhatItDid()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                string blockId = window.FramesForTest!.AddTextFrame(0);
                var frame = (Core.Model.TextBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
                Story story = window.SessionForTest.Document.GetStory(frame.StoryRef);
                story.Paragraphs[0].Runs[0].Text = LongProse;
                window.SourceForTest!.Invalidate(new Core.Commands.ChangeScope(
                    Core.Commands.ChangeKind.Text, StoryId: frame.StoryRef));

                window.FramesForTest.Select(blockId);
                window.RefreshActions();
                Assert.True(
                    ActionCatalog.Evaluate(ActionId.AutoFlow, window.CurrentActionContext).IsAvailable);

                await window.ActionsForTest.RunAsync(ActionId.AutoFlow);

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "Make the rest fit poured the writing onto new pages and said nothing.");
                Assert.Contains(
                    "writing",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// "Dismiss this note" discarded its bool. On the path where it is false the note stays on
    /// screen and the button that was meant to hide it looks broken.
    /// </summary>
    [Fact]
    public async Task DismissingANoteThatCannotBeDismissedSaysSo()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                string blockId = window.FramesForTest!.AddTextFrame(0);
                window.FramesForTest.Select(blockId);
                window.RefreshActions();

                window.DismissCropNotice();
                window.RefreshActions();

                Assert.Contains(
                    "no note to hide",
                    window.StatusLabelTextForTest ?? "",
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The spelling window's "Show me where it is" — the sibling of the defect M70 fixed in
    /// <see cref="ReviewWindow"/>. The page is editable behind this window, so the word may not be
    /// where the scan said it was, and either way this said nothing.
    /// </summary>
    [Fact]
    public async Task ShowMeWhereItIsSaysWhereItWentAndSaysWhenItIsGone()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenSample();
            try
            {
                var words = new List<Misspelling>
                {
                    new("story-1", 0, 0, "chruch", "The chruch supper is on Tuesday."),
                };

                var found = new SpellingWindow(
                    words, window.Spelling.Checker, _ => 3, (_, _) => true, _ => { });
                found.GoToForTest(1);
                Click(found.ButtonsForTest.First(b => (b.Content as string) == "Show me where it is"));
                Assert.Contains("page 3", found.StatusForTest, StringComparison.Ordinal);

                var gone = new SpellingWindow(
                    words, window.Spelling.Checker, _ => null, (_, _) => true, _ => { });
                gone.GoToForTest(1);
                Click(gone.ButtonsForTest.First(b => (b.Content as string) == "Show me where it is"));
                Assert.Contains("not where it was", gone.StatusForTest, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Teach it eleven surnames on a folder it cannot write to, be told eleven times that it
    /// worked, and be asked about all eleven again next month. The flag has been on
    /// <see cref="PersonalDictionary"/> since M52 and nobody read it.
    /// </summary>
    [Fact]
    public async Task NeverAskAgainSaysSoWhenItCouldNotBeRemembered()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            // A directory where a file should be: every write throws, and none of it escapes.
            string path = Path.Combine(
                Path.GetTempPath(), "trestleboard-m73e-" + Guid.NewGuid().ToString("N"), "not-a-file");
            Directory.CreateDirectory(path);

            var checker = new SpellChecker(new PersonalDictionary(path));
            var said = new List<string>();
            var spelling = new SpellingWindow(
                [new Misspelling("story-1", 0, 0, "Fauntleroy", "Brother Fauntleroy is well.")],
                checker,
                _ => 1,
                (_, _) => true,
                said.Add);

            spelling.GoToForTest(1);
            Click(spelling.ButtonsForTest.First(b =>
                (b.Content as string) == "It's a name — never ask again"));

            // The walk moves to the next screen afterwards and says its own thing, so it is the
            // answer to the button that is asserted, not merely the last sentence said.
            Assert.NotEmpty(said);
            Assert.Contains(said, s => s.Contains("could not", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(said, s => s.Contains("has been added", StringComparison.OrdinalIgnoreCase));

            spelling.Close();
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <see cref="AppSettings.Save"/> was <c>void</c> with an empty catch and was announced as
    /// "Saved." whatever happened. This is the return value that had to exist before the sentence
    /// could be a function of anything.
    /// </summary>
    [Fact]
    public void HowThingsLookSaysWhetherItReachedTheDisk()
    {
        string folder = Path.Combine(
            Path.GetTempPath(), "trestleboard-m73e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var settings = new AppSettings { UiScalePercent = 150 };
        Assert.True(settings.Save(Path.Combine(folder, "settings.json")));

        // A directory where the file should be: the write throws and is swallowed, exactly as it
        // is on an AppData folder the user cannot write to.
        string blocked = Path.Combine(folder, "blocked");
        Directory.CreateDirectory(blocked);
        Assert.False(settings.Save(blocked));
    }
}
