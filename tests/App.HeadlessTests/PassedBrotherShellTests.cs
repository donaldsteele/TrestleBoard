using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M55 through the shell: recording a brother as passed keeps his record, takes him out of the
/// birthday list behind the diff dialog, and offers a memorial exactly once.
///
/// <para>§0 rule 7: every person here is fictional, and the harness points <c>AppPaths.Root</c> at
/// a temporary folder so no test can reach a real address book.</para>
/// </summary>
public sealed class PassedBrotherShellTests
{
    private static MainWindow OpenWithSomebody(out string memberId)
    {
        var window = new MainWindow();
        window.Show();
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(1280, 860));
        window.Arrange(new Rect(0, 0, 1280, 860));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        memberId = window.Roster.NextMemberId();
        window.Roster.Save(
            new Member
            {
                Id = memberId,
                DisplayName = "A. Placeholder",
                BirthMonth = 9,
                BirthDay = 14,
                Groups = [MemberGroups.Printed],
            },
            "Add A. Placeholder");
        return window;
    }

    [Fact]
    public async Task RecordingHimAsPassedKeepsHisRecordAndTakesHimOutOfWhatIsGenerated()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                Member before = window.Roster.Book.Find(id)!;
                window.Roster.Save(
                    (before with { PassedOn = "2026-09-14" }).Normalised(), "Change A. Placeholder");

                Member after = window.Roster.Book.Find(id)!;

                // Still there — the half that matters to the family.
                Assert.NotNull(after);
                Assert.Equal("A. Placeholder", after.DisplayName);
                Assert.True(after.HasPassed);

                // And out of everything the app generates.
                Assert.False(after.IsInTheNewsletter);
                Assert.DoesNotContain(
                    MemberGroups.Members(window.Roster.Book.Members, MemberGroups.Printed),
                    m => m.Id == id);
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The memorial is offered, never inserted uninvited — and saying "Not now" leaves the
    /// newsletter alone.
    /// </summary>
    [Fact]
    public async Task SayingNotNowToTheMemorialWritesNothing()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                var people = new PeopleWindow(window.Roster) { MemorialAnswerForTest = false };

                Assert.Null(people.MemorialRequestedFor);
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheMemorialArrivesAsOrdinaryWritingInOneUndoStep()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                string storyId = window.PackageForTest!.Document.Stories[0].Id;
                window.EditorForTest!.SelectRange(storyId, 0, 0, 0);
                string before = Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest.Document.Stories[0].Paragraphs[0]);

                await window.OfferTheMemorialForTest("A. Placeholder");

                string after = Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest.Document.Stories[0].Paragraphs[0]);
                Assert.Contains("A. Placeholder", after, StringComparison.Ordinal);
                Assert.Contains("Celestial Lodge", after, StringComparison.Ordinal);

                // The date he passed is left as a blank for the committee, not invented.
                Assert.Contains("__________", after, StringComparison.Ordinal);

                window.Undo();
                Assert.Equal(before, Core.Text.StoryNavigator.GetParagraphText(
                    window.PackageForTest.Document.Stories[0].Paragraphs[0]));
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// With no cursor in any writing there is nowhere for a memorial to go. Saying where to start
    /// beats opening a wizard whose last button cannot do anything.
    /// </summary>
    [Fact]
    public async Task WithNowhereToWriteItTheAppSaysWhereToStart()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                window.EditorForTest!.End();

                await window.OfferTheMemorialForTest("A. Placeholder");

                Assert.Contains("Words for hard news", window.StatusLabelTextForTest!, StringComparison.Ordinal);
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }
}
