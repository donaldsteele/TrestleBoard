using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Roster;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M55 through the shell: recording a brother as passed keeps his record, takes him out of the
/// birthday list behind the diff dialog, and offers a memorial exactly once — and, from M73(a),
/// the app then does what it offered.
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

    private static RosterService NewRoster(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "passed-brother-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new RosterService(new RosterStore(path));
    }

    private static int TextBlocksOnPageOne(MainWindow window) =>
        window.PackageForTest!.Document.Pages[0].Blocks.OfType<Core.Model.TextBlock>().Count();

    private static bool MemorialIsSomewhereInThe(MainWindow window) =>
        window.PackageForTest!.Document.Stories.Any(story =>
            story.Paragraphs.Any(p =>
                Core.Text.StoryNavigator.GetParagraphText(p)
                    .Contains("Celestial Lodge", StringComparison.Ordinal)));

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
                var people = new PeopleWindow(window.Roster) { MemorialAnswerForTest = _ => false };

                Assert.Empty(people.MemorialsRequestedFor);
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
    /// M73(a), the central defect. People is reached from a MENU, so there is almost never a caret
    /// when the offer is accepted — and the app used to answer its own offer with an instruction.
    /// It writes the notice into a new frame of its own instead, in one undo step, and says where
    /// it put it (M66's <c>AddTextFrameWith</c> precedent).
    ///
    /// <para>This replaces <c>WithNowhereToWriteItTheAppSaysWhereToStart</c>, which asserted the
    /// refusal and was green.</para>
    /// </summary>
    [Fact]
    public async Task WithNoCaretTheMemorialGetsAFrameOfItsOwnInOneUndoStep()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                window.EditorForTest!.End();
                int before = TextBlocksOnPageOne(window);
                Assert.False(MemorialIsSomewhereInThe(window));

                await window.OfferTheMemorialForTest("A. Placeholder");

                Assert.True(
                    MemorialIsSomewhereInThe(window),
                    "the app offered to write a memorial and then did not write one");
                Assert.Equal(before + 1, TextBlocksOnPageOne(window));

                // It says where it went, so the user can find it.
                string said = window.StatusLabelTextForTest ?? string.Empty;
                Assert.Contains("page 1", said, StringComparison.Ordinal);

                // One undo step, all of it.
                window.Undo();
                Assert.Equal(before, TextBlocksOnPageOne(window));
                Assert.False(MemorialIsSomewhereInThe(window));
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(a): with no newsletter open at all there is genuinely nowhere for the words to go, so a
    /// refusal is honest — but "click into some writing" is not something anybody can do on an
    /// empty desk. What is said has to be followable from where the user is standing.
    /// </summary>
    [Fact]
    public async Task WithNoNewsletterAtAllTheRefusalCanBeFollowed()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            try
            {
                Assert.Null(window.PackageForTest);

                await window.OfferTheMemorialForTest("A. Placeholder");

                string said = window.StatusLabelTextForTest ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(said), "nothing was said at all");
                Assert.DoesNotContain("click into some writing", said, StringComparison.Ordinal);
                Assert.Contains("A. Placeholder", said, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(a): the phrase shelf not holding the memorial is a state nobody can do anything about,
    /// but returning in total silence from an offer the user accepted is not an option. Gate 27.
    /// </summary>
    [Fact]
    public async Task AMissingMemorialPhraseIsSaidOutLoud()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            MainWindow window = OpenWithSomebody(out string id);
            try
            {
                string storyId = window.PackageForTest!.Document.Stories[0].Id;
                window.EditorForTest!.SelectRange(storyId, 0, 0, 0);
                window.MemorialPhraseIdForTest = "no-such-phrase-is-on-the-shelf";
                window.Announce(string.Empty);

                await window.OfferTheMemorialForTest("A. Placeholder");

                Assert.False(
                    string.IsNullOrWhiteSpace(window.StatusLabelTextForTest),
                    "the memorial could not be written and the app said nothing at all");
            }
            finally
            {
                window.Roster.Delete(id, "Tidy up after the test");
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(a): tick "passed", press the X, choose "Save". The card used to be raised as a child of
    /// a window that was already closing, and the answer arrived after the shell had stopped
    /// listening — the request vanished without a word. What matters is that the request exists by
    /// the time the window is gone, because that is the moment the shell reads it.
    /// </summary>
    [Fact]
    public async Task ClosingTheWindowStillAsksAboutTheMemorial()
    {
        await HeadlessSession.DispatchAsync(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "A. Placeholder" }, "Add");

            var people = new PeopleWindow(roster)
            {
                MemorialAnswerForTest = _ => true,
                PendingEditAnswerForTest = PeopleWindow.PendingEdit.Save,
            };
            people.Show();
            people.SelectForTest("person-1");
            people.TickPassedForTest();

            int askedByTheTimeItClosed = -1;
            people.Closed += (_, _) => askedByTheTimeItClosed = people.MemorialsRequestedFor.Count;

            people.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, askedByTheTimeItClosed);

            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(a): two brothers recorded in one visit. "Not now" on the second used to write null over
    /// the first accepted yes, because the request was a single slot.
    /// </summary>
    [Fact]
    public async Task ASecondBrotherDoesNotEraseTheFirstRequest()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "A. Placeholder" }, "Add");
            roster.Save(new Member { Id = "person-2", DisplayName = "B. Placeholder" }, "Add");

            var people = new PeopleWindow(roster)
            {
                // Yes for the first brother, "Not now" for the second.
                MemorialAnswerForTest = name => name == "A. Placeholder",
                PendingEditAnswerForTest = PeopleWindow.PendingEdit.Save,
            };
            people.Show();

            people.SelectForTest("person-1");
            people.TickPassedForTest();
            await people.SaveAndOfferForTest();

            people.SelectForTest("person-2");
            people.TickPassedForTest();
            await people.SaveAndOfferForTest();

            Assert.Equal<string[]>(["A. Placeholder"], [.. people.MemorialsRequestedFor]);

            people.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(a), gate 26: with no newsletter open the shell cannot write a memorial anywhere, so the
    /// card must not offer to. It still says what happened to his record, which is the half of that
    /// card that matters most.
    /// </summary>
    [Fact]
    public async Task WithNoNewsletterOpenTheCardDoesNotOfferAMemorial()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "A. Placeholder" }, "Add");

            // The control: with a newsletter to write in, the card offers and the yes is kept.
            var withANewsletter = new PeopleWindow(roster, canWriteAMemorial: () => true)
            {
                MemorialAnswerForTest = _ => true,
                PendingEditAnswerForTest = PeopleWindow.PendingEdit.Save,
            };
            withANewsletter.Show();
            withANewsletter.SelectForTest("person-1");
            withANewsletter.TickPassedForTest();
            await withANewsletter.SaveAndOfferForTest();
            Assert.NotEmpty(withANewsletter.MemorialsRequestedFor);
            withANewsletter.Close();

            // And with none, the same tick offers nothing, because nothing could be kept.
            roster.Save(new Member { Id = "person-2", DisplayName = "B. Placeholder" }, "Add");
            var withNone = new PeopleWindow(roster, canWriteAMemorial: () => false)
            {
                MemorialAnswerForTest = _ => true,
                PendingEditAnswerForTest = PeopleWindow.PendingEdit.Save,
            };
            withNone.Show();
            withNone.SelectForTest("person-2");
            withNone.TickPassedForTest();
            await withNone.SaveAndOfferForTest();
            Assert.Empty(withNone.MemorialsRequestedFor);
            withNone.Close();
        }, TestContext.Current.CancellationToken);
    }
}
