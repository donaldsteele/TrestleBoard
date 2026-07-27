using System.Text.Json;
using Avalonia.Headless;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §12 gate 10, driven through the real shell: a birthday re-sync is one undo step and
/// Ctrl+Z restores the prior payload byte-for-byte, hand-edited rows survive, and a brother the user
/// deliberately removed is not resurrected.
///
/// Every person here is fictional and lives in a book this test made (PLAN.md §0 rule 5) — the
/// window is pointed at it explicitly, so nothing here can read the real address book even by
/// accident.
/// </summary>
public sealed class BirthdaySyncShellTests
{
    private const string BirthdayBlockId = "w-birthdays";

    /// <summary>The sample issue is July 2026, so this is the month the projection works in.</summary>
    private const int July = 7;

    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task BringingInBirthdaysIsOneUndoStepAndCtrlZRestoresThePayloadByteForByte()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            string before = PayloadOf(window);
            int undoDepth = Depth(window);

            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            BirthdayListData after = Read(window);
            Assert.Equal(BirthdayListSource.Roster, after.Source);
            Assert.Equal(July, after.SourceMonth);
            Assert.Contains(after.Entries, e => e.Name == "Y. Newcomer" && e.MemberId == "m-new");

            Assert.Equal(undoDepth + 1, Depth(window));
            Assert.Equal(BirthdayRosterProjection.UndoLabel, window.SessionForTest!.UndoDescription);

            window.Undo();
            Assert.Equal(before, PayloadOf(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARowTheUserTypedHimselfSurvivesAReSyncUntouched()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            // The sample issue's six typed rows carry no member id, so every one of them is the
            // user's — which is exactly what the wizard's own rows would be.
            BirthdayListData afterFirst = Read(window);
            Assert.Equal(6, afterFirst.Entries.Count(e => e.MemberId is null));

            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            BirthdayListData afterSecond = Read(window);
            Assert.Equal(afterFirst.Entries.Count, afterSecond.Entries.Count);
            Assert.Equal(
                afterFirst.Entries.Select(e => e.Name),
                afterSecond.Entries.Select(e => e.Name));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunningItTwiceChangesNothingTheSecondTime()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            string once = PayloadOf(window);
            int depth = Depth(window);

            // The second run finds nothing to show, so it never asks — and the answer proves it.
            window.BirthdayConfirmForTest = _ =>
                throw new InvalidOperationException("the user was asked about a change that does not exist");
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            Assert.Equal(Entries(once), Entries(PayloadOf(window)));
            Assert.Equal(depth + 1, Depth(window)); // the provenance-only refresh, and nothing printed
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SomebodyTakenOffTheListIsNotPutBackByTheNextSync()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            // Removed the way the user would: through the list editor's Remove button, which is the
            // same step object both editors drive.
            BirthdayListData data = Read(window);
            int index = data.Entries.FindIndex(e => e.MemberId == "m-new");
            IWizardStep step = new BirthdayListDefinition().Wizard.Steps.First(s => s is IWizardListStep);
            Assert.True(step.RemoveRow(data, index));
            Assert.Contains("m-new", data.RemovedMemberIds);

            var definition = new BirthdayListDefinition();
            window.WidgetsForTest!.ApplyWidgetData(
                BirthdayBlockId, definition.WriteData(data), definition.CurrentDataVersion, "Edit birthdays");

            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            BirthdayListData after = Read(window);
            Assert.DoesNotContain(after.Entries, e => e.MemberId == "m-new");
            Assert.Contains("m-new", after.RemovedMemberIds);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SayingNoLeavesTheNewsletterExactlyAsItWas()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            string before = PayloadOf(window);
            int depth = Depth(window);

            window.BirthdayConfirmForTest = _ => false;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            Assert.Equal(before, PayloadOf(window));
            Assert.Equal(depth, Depth(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AStaleListIsNudgedButNeverQuietlyChangedByOpeningTheNewsletter()
    {
        await Session.Dispatch(() =>
        {
            // The hard rule (PLAN.md §11 M13): staleness never mutates the document. Opening a
            // newsletter only to look at it must not dirty it.
            MainWindow window = Windowed(Book());
            window.BirthdayConfirmForTest = _ => true;
            window.FramesForTest!.Select(BirthdayBlockId);
            window.SyncBirthdaysAsync().GetAwaiter().GetResult();

            string synced = PayloadOf(window);
            window.Roster.Save(
                new Member { Id = "m-later", DisplayName = "X. Latecomer", BirthMonth = July, BirthDay = 30 },
                "Add a person");

            window.FramesForTest.Select(null);
            window.RefreshActions();

            Assert.True(window.CurrentActionContext.BirthdayListIsStale);
            Assert.Equal(synced, PayloadOf(window));
            Assert.Contains(
                WhatsNext.Suggestions(window.CurrentActionContext),
                s => s.ActionId == ActionId.SyncBirthdays);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WithAnEmptyAddressBookTheActionExplainsItselfAndChangesNothing()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(NewRoster());
            string before = PayloadOf(window);

            window.FramesForTest!.Select(BirthdayBlockId);
            window.RefreshActions();

            ActionAvailability availability =
                ActionCatalog.Evaluate(ActionId.SyncBirthdays, window.CurrentActionContext);
            Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
            Assert.False(string.IsNullOrWhiteSpace(availability.Reason));

            window.ActionsForTest.RunAsync(ActionId.SyncBirthdays).GetAwaiter().GetResult();
            Assert.Equal(before, PayloadOf(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    private static MainWindow Windowed(RosterService roster)
    {
        // Not shown, for the same reason WidgetShellTests gives: a shown window leaves a queued
        // menu-measure that the session's teardown runs against a disposed font manager.
        var window = new MainWindow();
        window.OpenIssueSample();
        window.UseRosterForTest(roster);
        return window;
    }

    private static RosterService NewRoster(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "birthday-sync-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new RosterService(new RosterStore(path));
    }

    private static RosterService Book(
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        RosterService roster = NewRoster(name);
        roster.Save(
            new Member { Id = "m-new", DisplayName = "Y. Newcomer", BirthMonth = July, BirthDay = 2 },
            "Add a person");
        roster.Save(
            new Member { Id = "m-other", DisplayName = "Z. Elsewhere", BirthMonth = 11, BirthDay = 5 },
            "Add a person");
        return roster;
    }

    private static string PayloadOf(MainWindow window)
    {
        Assert.True(window.SessionForTest!.Document.TryFindBlock(BirthdayBlockId, out _, out Block? block));
        var widget = (WidgetBlock)block!;
        return widget.Data!.Value.GetRawText();
    }

    /// <summary>The printed half of the payload, with the provenance stamp left out.</summary>
    private static string Entries(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("entries").GetRawText();
    }

    private static BirthdayListData Read(MainWindow window)
    {
        var definition = new BirthdayListDefinition();
        using JsonDocument document = JsonDocument.Parse(PayloadOf(window));
        Assert.True(definition.TryReadData(
            document.RootElement.Clone(), definition.CurrentDataVersion, out object typed));
        return Assert.IsType<BirthdayListData>(typed);
    }

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
