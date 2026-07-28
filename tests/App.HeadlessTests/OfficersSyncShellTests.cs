using System.Text.Json;
using Avalonia.Headless;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// PLAN.md §11 M19, driven through the real shell: one command fills the twelve offices in behind
/// the diff dialog, sync twice equals sync once, a hand-edited row survives, an unrecognised office
/// never reaches the page, and the whole thing is ONE undo step that Ctrl+Z restores byte-for-byte.
///
/// Every person here is fictional and lives in a book this test made (PLAN.md §0 rule 5) — the
/// window is pointed at it explicitly, so nothing here can read the real address book even by
/// accident.
/// </summary>
public sealed class OfficersSyncShellTests
{
    private const string OfficersBlockId = "w-officers";

    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task FillingInTheOfficersIsOneUndoStepAndCtrlZRestoresThePayloadByteForByte()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            string before = PayloadOf(window);
            int undoDepth = Depth(window);

            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            OfficersTableData after = Read(window);
            Assert.Equal(OfficersTableSource.Roster, after.Source);
            Assert.Equal("A. Placeholder", Row(after, "Worshipful Master").Name);
            Assert.Equal("m-master", Row(after, "Worshipful Master").MemberId);
            Assert.Equal("B. Sample", Row(after, "Senior Warden").Name);

            Assert.Equal(undoDepth + 1, Depth(window));
            Assert.Equal(OfficersRosterProjection.UndoLabel, window.SessionForTest!.UndoDescription);

            window.Undo();
            Assert.Equal(before, PayloadOf(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheTwelveOfficesSurviveTheSyncInTheirPrintedOrder()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            OfficersTableData after = Read(window);
            Assert.Equal(
                OfficersTableData.StandardPositions,
                after.Officers.Where(o => OfficersTableData.StandardPositions.Contains(o.Position))
                    .Select(o => o.Position));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunningItTwiceChangesNothingTheSecondTime()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            string once = PayloadOf(window);
            int depth = Depth(window);

            // The second run finds nothing to show, so it never asks — and the answer proves it.
            window.OfficersConfirmForTest = _ =>
                throw new InvalidOperationException("the user was asked about a change that does not exist");
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            Assert.Equal(Officers(once), Officers(PayloadOf(window)));
            Assert.Equal(depth + 1, Depth(window)); // the provenance-only refresh, and nothing printed
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AHandEditedTreasurerSurvivesAReSync()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            // Edited the way the user would: through the payload both editors commit, with the row
            // marked as his own exactly as the wizard's setters mark it.
            OfficersTableData data = Read(window);
            OfficerEntry treasurer = Row(data, "Treasurer");
            treasurer.Name = "H. Handtyped";
            treasurer.IsManual = true;

            var definition = new OfficersTableDefinition();
            window.WidgetsForTest!.ApplyWidgetData(
                OfficersBlockId, definition.WriteData(data), definition.CurrentDataVersion, "Edit officers");

            // The address book now disagrees about the Treasurer, and must lose.
            window.Roster.Save(
                new Member { Id = "m-treasurer", DisplayName = "C. Example", Office = "Treasurer" },
                "Change a person");
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            Assert.Equal("H. Handtyped", Row(Read(window), "Treasurer").Name);
            Assert.True(Row(Read(window), "Treasurer").IsManual);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnUnrecognisedOfficeIsShownToTheUserAndNeverReachesThePage()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.Roster.Save(
                new Member { Id = "m-hist", DisplayName = "E. Historian", Office = "Historian" },
                "Add a person");

            OfficersProjection? seen = null;
            window.OfficersConfirmForTest = plan =>
            {
                seen = plan;
                return new MainWindow.OfficersSyncAnswer(true, plan.DefaultDecisions, []);
            };

            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            Assert.NotNull(seen);
            Assert.Contains(seen!.Unrecognised, u => u.Name == "E. Historian" && u.Office == "Historian");
            Assert.DoesNotContain(Read(window).Officers, o => o.Name == "E. Historian");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoClaimantsForOneOfficeApplyNothingUntilTheUserChooses()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.Roster.Save(
                new Member { Id = "m-rival", DisplayName = "R. Rival", Office = "Senior Warden" },
                "Add a person");

            // The user says yes to everything the dialog offers by default — and the contest is
            // deliberately not among it.
            OfficerEntry before = Row(Read(window), "Senior Warden");
            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            OfficersTableData after = Read(window);
            OfficerEntry warden = Row(after, "Senior Warden");
            Assert.Equal(before.Name, warden.Name);
            Assert.Null(warden.MemberId);

            // The uncontested offices are filled in all the same: one man's contested office does
            // not hold up the other eleven.
            Assert.Equal("A. Placeholder", Row(after, "Worshipful Master").Name);
            Assert.Equal("m-master", Row(after, "Worshipful Master").MemberId);

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

            window.OfficersConfirmForTest = _ => new MainWindow.OfficersSyncAnswer(false, [], []);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            Assert.Equal(before, PayloadOf(window));
            Assert.Equal(depth, Depth(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AStaleTableIsNudgedButNeverQuietlyChangedByOpeningTheNewsletter()
    {
        await Session.Dispatch(() =>
        {
            // The hard rule (PLAN.md §11 M13, kept by M19): staleness never mutates the document.
            MainWindow window = Windowed(Book());
            AcceptEverything(window);
            window.FramesForTest!.Select(OfficersBlockId);
            window.SyncOfficersAsync().GetAwaiter().GetResult();

            string synced = PayloadOf(window);
            window.Roster.Save(
                new Member { Id = "m-master", DisplayName = "A. Placeholder", Office = "WM", Phone = "555-0199" },
                "Change a person");

            window.FramesForTest.Select(null);
            window.RefreshActions();

            Assert.True(window.CurrentActionContext.OfficersTableIsStale);
            Assert.Equal(synced, PayloadOf(window));
            Assert.Contains(
                WhatsNext.Suggestions(window.CurrentActionContext),
                s => s.ActionId == ActionId.SyncOfficers);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AGeneratedTableSaysSoOnThePanel()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = Windowed(Book());
            window.FramesForTest!.Select(OfficersBlockId);
            window.RefreshActions();
            Assert.Null(window.CurrentActionContext.SelectionFilledInFromRoster);

            AcceptEverything(window);
            window.SyncOfficersAsync().GetAwaiter().GetResult();
            window.RefreshActions();

            Assert.NotNull(window.CurrentActionContext.SelectionFilledInFromRoster);
            Assert.Contains(
                "Filled in from your address book",
                ActionCatalog.DescribeSelectionHint(window.CurrentActionContext)!,
                StringComparison.Ordinal);

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

            window.FramesForTest!.Select(OfficersBlockId);
            window.RefreshActions();

            ActionAvailability availability =
                ActionCatalog.Evaluate(ActionId.SyncOfficers, window.CurrentActionContext);
            Assert.Equal(ActionAvailabilityKind.Blocked, availability.Kind);
            Assert.False(string.IsNullOrWhiteSpace(availability.Reason));

            window.ActionsForTest.RunAsync(ActionId.SyncOfficers).GetAwaiter().GetResult();
            Assert.Equal(before, PayloadOf(window));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- fictional scaffolding -------------------------------------------------------------------

    /// <summary>The user ticking every box the dialog offers by default. A contested office is never
    /// among them, which is the point of <see cref="OfficersProjection.DefaultDecisions"/>.</summary>
    private static void AcceptEverything(MainWindow window) =>
        window.OfficersConfirmForTest =
            plan => new MainWindow.OfficersSyncAnswer(true, plan.DefaultDecisions, []);

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
        string folder = Path.Combine(AppPaths.Root, "officers-sync-tests", name);
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
            new Member { Id = "m-master", DisplayName = "A. Placeholder", Office = "WM", Phone = "555-0100" },
            "Add a person");
        roster.Save(
            new Member { Id = "m-warden", DisplayName = "B. Sample", Office = "Sr. Warden" },
            "Add a person");
        roster.Save(
            new Member { Id = "m-treasurer", DisplayName = "C. Example", Office = "Treas." },
            "Add a person");
        return roster;
    }

    private static OfficerEntry Row(OfficersTableData data, string position) =>
        data.Officers.Single(o => o.Position == position);

    private static string PayloadOf(MainWindow window)
    {
        Assert.True(window.SessionForTest!.Document.TryFindBlock(OfficersBlockId, out _, out Block? block));
        var widget = (WidgetBlock)block!;
        return widget.Data!.Value.GetRawText();
    }

    /// <summary>The printed half of the payload, with the provenance stamp left out.</summary>
    private static string Officers(string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("officers").GetRawText();
    }

    private static OfficersTableData Read(MainWindow window)
    {
        var definition = new OfficersTableDefinition();
        using JsonDocument document = JsonDocument.Parse(PayloadOf(window));
        Assert.True(definition.TryReadData(
            document.RootElement.Clone(), definition.CurrentDataVersion, out object typed));
        return Assert.IsType<OfficersTableData>(typed);
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
