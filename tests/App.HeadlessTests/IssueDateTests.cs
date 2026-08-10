using System.Text.Json;
using Avalonia.Headless;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §12 gate 28 (M75), driven through the real shell.
///
/// <para><b>The test gap this closes is the point of the milestone.</b> Every
/// <c>BirthdayRosterProjectionTests</c> case passes the month in as a literal <c>int</c> and never
/// mentions a <c>Document</c>; every headless birthday test loads <c>OpenIssueSample()</c>, which is
/// hard-coded to July 2026. <b>No test had ever started from a template</b> — the one path a real
/// user takes — so a newsletter that was permanently January 2000 passed everything for sixty
/// milestones.</para>
///
/// <para>Every person here is fictional and lives in a book this test made (PLAN.md §0 rule 5).</para>
/// </summary>
public sealed class IssueDateTests
{
    private const string ClassicTemplate = "classic-414";
    private const string BirthdayBlockId = "w-birthdays";
    private const string CoverBlockId = "w-cover";
    private const int July = 7;

    /// <summary>Fictional throughout (PLAN.md §0): a template ships with no lodge name on it, and
    /// the banner's wizard asks for one exactly as it always has.</summary>
    private const string Lodge = "Placeholder Lodge No. 000";

    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// <b>The owner's exact report, end to end.</b> Start from a shipped template, say it is July
    /// 2026, have a brother with a 3 July birthday in the address book — and the birthday list
    /// fills in.
    ///
    /// <para>Before M75 this failed at the third line from the end: the template's metadata was
    /// still January 2000, the projection filtered the book to January, the catalog refused the sync
    /// with "Nobody in your address book has a birthday in this issue's month", and the list stayed
    /// empty.</para>
    /// </summary>
    [Fact]
    public async Task StartingFromATemplateAndSayingJuly2026FillsInTheJulyBirthdays()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.UseRosterForTest(BookWithAJulyBirthday());
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);

            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            // The answer reached the document's own metadata, which is what everything else reads.
            DocumentMetadata metadata = window.SessionForTest!.Document.Metadata;
            Assert.Equal(July, metadata.IssueMonth);
            Assert.Equal(2026, metadata.IssueYear);
            Assert.True(metadata.HasIssueDate);

            window.FramesForTest!.Select(BirthdayBlockId);
            window.RefreshActions();

            // The catalog no longer refuses, and no longer blames the address book.
            Assert.True(ActionCatalog.Evaluate(ActionId.SyncBirthdays, window.CurrentActionContext).IsAvailable);

            window.BirthdayConfirmForTest = _ => true;
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            BirthdayListData list = ReadBirthdays(window);
            Assert.Equal(BirthdayListSource.Roster, list.Source);
            Assert.Equal(July, list.SourceMonth);
            Assert.Contains(list.Entries, e => e.Name == "A. Placeholder" && e.Day == 3);
            Assert.DoesNotContain(list.Entries, e => e.Name == "B. Elsewhere");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>The invariant whose absence let this ship.</b> Every route that produces an editable
    /// newsletter leaves the issue date answered.
    ///
    /// <para>It <i>enumerates</i> the routes rather than listing two by hand — the shipped templates
    /// come from <c>TemplateLibrary.All</c>, so adding a fourth template puts a fourth case in this
    /// test without anybody remembering to — and it carries gate 24's anti-vacuity check: a suite
    /// that quietly stopped covering anything would still pass a foreach over an empty list.</para>
    /// </summary>
    [Fact]
    public async Task EveryRouteThatProducesAnEditableNewsletterLeavesTheIssueDateAnswered()
    {
        await Session.Dispatch(() =>
        {
            var routes = new List<(string Name, Func<MainWindow, bool> Run)>();

            foreach (Core.Templates.TemplateInfo template in Core.Templates.TemplateLibrary.All)
            {
                string id = template.Id;
                routes.Add(($"shipped template “{id}”", w => w.OpenTemplateAsync(id).Result));
            }

            routes.Add(("a template of the user's own", w =>
            {
                w.OpenIssueSample();
                HashSet<string> before = [.. w.Templates.All().Select(t => t.Id)];
                w.TemplateNameAnswerForTest = "My layout";
                Assert.True(w.SaveAsTemplateAsync().Result);
                string mine = w.Templates.All().First(t => !before.Contains(t.Id)).Id;
                return w.OpenUserTemplateAsync(mine).Result;
            }));

            routes.Add(("start from last month", w =>
            {
                w.OpenIssueSample();
                return w.CarryForwardToNextIssueAsync().Result;
            }));

            // Anti-vacuity: three shipped templates plus the two hand-written routes. If the
            // enumeration ever returns nothing, the foreach below proves nothing and says so.
            Assert.True(routes.Count >= 5, $"only {routes.Count} start routes were enumerated");

            foreach ((string name, Func<MainWindow, bool> run) in routes)
            {
                var window = new MainWindow();
                window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
                window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);

                Assert.True(run(window), $"{name} did not produce a newsletter");
                Assert.True(
                    window.SessionForTest!.Document.Metadata.HasIssueDate,
                    $"{name} left the newsletter not knowing which issue it is");
                Assert.True(
                    window.CurrentActionContext.IssueDateChosen,
                    $"{name} left the action surface thinking the issue date is unanswered");

                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (b): carry-forward puts next month in the box as a suggestion and still asks. Cancelling
    /// means no new issue — and, the part that matters, last month's newsletter is untouched.
    /// </summary>
    [Fact]
    public async Task CarryForwardPreFillsNextMonthAndCancellingLeavesThisOneAlone()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.OpenIssueSample();

            string coverBefore = PayloadOf(window, CoverBlockId);
            DocumentSessionState before = StateOf(window);

            window.CancelTheWizardForTest = true;
            Assert.False(window.CarryForwardToNextIssueAsync().Result);

            Assert.Equal(before, StateOf(window));
            Assert.Equal(coverBefore, PayloadOf(window, CoverBlockId));
            Assert.Contains("exactly as it was", window.StatusLabelTextForTest!, StringComparison.Ordinal);

            // And the pre-fill: answering with no correction at all lands on August 2026, the month
            // after the July sample — the bump is still there, it just no longer decides on its own.
            // Nothing is corrected — both fields are left at whatever the wizard put in them.
            window.CancelTheWizardForTest = false;
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest();
            Assert.True(window.CarryForwardToNextIssueAsync().Result);
            Assert.Equal(8, window.SessionForTest!.Document.Metadata.IssueMonth);
            Assert.Equal(2026, window.SessionForTest.Document.Metadata.IssueYear);
            Assert.True(window.SessionForTest.Document.Metadata.HasIssueDate);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (b): a shipped template whose question goes unanswered starts nothing at all. The
    /// window is left exactly as it was — asking before the newsletter comes up is what makes that
    /// true rather than nearly true.
    /// </summary>
    [Fact]
    public async Task CancellingTheQuestionStartsNoNewsletter()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.CancelTheWizardForTest = true;

            Assert.False(window.OpenTemplateAsync(ClassicTemplate).Result);
            Assert.Null(window.SessionForTest);
            Assert.False(window.CurrentActionContext.HasDocument);
            Assert.Contains("No newsletter was started", window.StatusLabelTextForTest!, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (a): the answer and the widget edit land in ONE undo step, because they are one thing the
    /// user did. <c>SetMetadataCommand</c> rides the composite <c>WidgetController</c> already built
    /// for the payload and the height change.
    /// </summary>
    [Fact]
    public async Task AnsweringItLaterIsOneUndoStepThatTakesBackBoth()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, LodgeName: Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            string coverBefore = PayloadOf(window, CoverBlockId);
            int depth = Depth(window);

            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(12, 2027, LodgeName: Lodge);
            Assert.True(window.ActionsForTest.RunAsync(ActionId.SetIssueDate).Result == ActionOutcome.Did);

            Assert.Equal(12, window.SessionForTest!.Document.Metadata.IssueMonth);
            Assert.Equal(2027, window.SessionForTest.Document.Metadata.IssueYear);
            Assert.Equal(depth + 1, Depth(window));

            window.Undo();

            Assert.Equal(July, window.SessionForTest.Document.Metadata.IssueMonth);
            Assert.Equal(2026, window.SessionForTest.Document.Metadata.IssueYear);
            Assert.Equal(coverBefore, PayloadOf(window, CoverBlockId));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M75 (d): the identical write-back hole in <c>MeetingRule</c>. It was set only by the two
    /// sample documents, while the cover banner carried its own editable copy that never flowed
    /// back — so <c>CarryForward.RecomputeMeetingDates</c> always failed to parse and took the
    /// <c>ClearMeetingDates</c> branch, <b>blanking the cover date</b> for every newsletter not
    /// descended from a sample.
    /// </summary>
    [Fact]
    public async Task TheMeetingRuleTypedOnTheCoverReachesTheNewsletterAndTheDateFollowsTheMonth()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(July, 2026, "1st Tuesday", Lodge);
            Assert.True(window.OpenTemplateAsync(ClassicTemplate).Result);

            // (d) part one: the rule the user typed into the banner is on the newsletter now.
            Assert.Equal("1st Tuesday", window.SessionForTest!.Document.Metadata.MeetingRule);

            // (d) part two: the printed date was worked out from it, rather than left blank.
            Assert.Equal("July 7th", MeetingDateOn(window));

            // And next month's issue recomputes rather than blanking — the defect itself.
            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(8, 2026, LodgeName: Lodge);
            Assert.True(window.CarryForwardToNextIssueAsync().Result);
            Assert.Equal("August 4th", MeetingDateOn(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    private static RosterService BookWithAJulyBirthday(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "issue-date-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var roster = new RosterService(new RosterStore(path));
        roster.Save(
            new Member { Id = "m-a", DisplayName = "A. Placeholder", BirthMonth = July, BirthDay = 3 },
            "Add a person");
        roster.Save(
            new Member { Id = "m-b", DisplayName = "B. Elsewhere", BirthMonth = 11, BirthDay = 5 },
            "Add a person");
        return roster;
    }

    private static BirthdayListData ReadBirthdays(MainWindow window)
    {
        var definition = new BirthdayListDefinition();
        using JsonDocument document = JsonDocument.Parse(PayloadOf(window, BirthdayBlockId));
        Assert.True(definition.TryReadData(
            document.RootElement.Clone(), definition.CurrentDataVersion, out object typed));
        return Assert.IsType<BirthdayListData>(typed);
    }

    private static string PayloadOf(MainWindow window, string blockId)
    {
        Assert.True(window.SessionForTest!.Document.TryFindBlock(blockId, out _, out Block? block));
        return ((WidgetBlock)block!).Data!.Value.GetRawText();
    }

    private static string? MeetingDateOn(MainWindow window)
    {
        using JsonDocument document = JsonDocument.Parse(PayloadOf(window, CoverBlockId));
        return document.RootElement.GetProperty("meetingDateText").GetString();
    }

    /// <summary>Enough of the open newsletter to prove a cancelled command changed none of it.</summary>
    private readonly record struct DocumentSessionState(int Pages, int Month, int Year, bool Dirty);

    private static DocumentSessionState StateOf(MainWindow window) => new(
        window.SessionForTest!.Document.Pages.Count,
        window.SessionForTest.Document.Metadata.IssueMonth,
        window.SessionForTest.Document.Metadata.IssueYear,
        window.HasUnsavedChangesForTest);

    private static int Depth(MainWindow window)
    {
        int depth = 0;
        while (window.SessionForTest!.CanUndo && depth < 100)
        {
            window.SessionForTest.Undo();
            depth++;
        }

        for (int i = 0; i < depth; i++)
        {
            window.SessionForTest.Redo();
        }

        return depth;
    }
}
