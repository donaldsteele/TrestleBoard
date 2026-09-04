using Avalonia.Automation;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.App.Theme;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Settings;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Roster;
using TrestleBoard.Roster.Import;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The address book as the user meets it (PLAN.md §11 M12, §12 gate 9). Every person here is
/// fictional, and the whole suite runs against a temporary app-state root — see
/// <see cref="HeadlessSession"/>, where that is set — so nothing here can read a real roster.
/// </summary>
public sealed class PeopleShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static RosterService NewRoster([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        string folder = Path.Combine(AppPaths.Root, "people-tests", name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "roster.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return new RosterService(new RosterStore(path));
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>The acceptance criterion: find a person by typing three letters.</summary>
    [Fact]
    public async Task TypingThreeLettersFindsThePersonAndSaysHowMany()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "Aaron Placeholder" }, "Add");
            roster.Save(new Member { Id = "person-2", DisplayName = "Bertram Sample" }, "Add");
            roster.Save(new Member { Id = "person-3", DisplayName = "Cyrus Fictitious" }, "Add");

            var window = new PeopleWindow(roster);
            window.Show();

            Assert.Equal("3 people.", window.CountTextForTest);

            window.SearchBoxForTest.Text = "sam";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Member found = Assert.Single(window.ShownForTest);
            Assert.Equal("Bertram Sample", found.DisplayName);
            Assert.Equal("1 person matches \"sam\".", window.CountTextForTest);

            // A search that finds nobody says so rather than showing an empty box.
            window.SearchBoxForTest.Text = "zzz";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Empty(window.ShownForTest);
            Assert.Contains("Nobody matches", window.CountTextForTest, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddingAPersonWritesThemStraightAwayAndSaysSo()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            var window = new PeopleWindow(roster);
            window.Show();

            window.AddForTest("Aaron Placeholder", "7/4", "555-0100");

            Member added = Assert.Single(roster.Book.Members);
            Assert.Equal("Aaron Placeholder", added.DisplayName);
            Assert.Equal("7/4", added.BirthdayText);
            Assert.Equal("555-0100", added.Phone);
            Assert.Contains("was added", window.StatusTextForTest, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>A birthday that cannot be read is refused in words, with an example (PLAN.md §6).</summary>
    [Fact]
    public async Task ABirthdayThatCannotBeReadIsExplainedRatherThanRejectedSilently()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            var window = new PeopleWindow(roster);
            window.Show();

            window.AddForTest("Aaron Placeholder", "sometime in July", "555-0100");

            Assert.Empty(roster.Book.Members);
            Assert.Contains("like 7/4", window.StatusTextForTest, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M40, review §14.4: an edit in the form is not thrown away without a word.
    ///
    /// <para>The People window saves one person at a time, deliberately — but clicking a different
    /// name in the list overwrote the form with no question asked, so a corrected phone number that
    /// had been typed but not saved simply vanished. The person who lost it had no way to know it
    /// had happened; the list had done exactly what they clicked on.</para>
    ///
    /// <para>This is the roster, so what is being lost is a real member's real telephone number
    /// (PLAN.md §0 rule 5) — and it is the one place in the app where losing it is silent.</para>
    /// </summary>
    [Fact]
    public async Task SwitchingPeopleWithAnUnsavedEditAsksFirst()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "Aaron Placeholder", Phone = "555-0100" }, "Add");
            roster.Save(new Member { Id = "person-2", DisplayName = "Bertram Sample" }, "Add");

            var window = new PeopleWindow(roster);
            window.Show();

            window.SelectForTest("person-1");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(window.FormHasUnsavedEditsForTest);

            // The correction is typed and NOT saved.
            window.TypePhoneForTest("555-0102");
            Assert.True(window.FormHasUnsavedEditsForTest);

            // "Go back" leaves the form and the list exactly as they were.
            window.PendingEditAnswerForTest = PeopleWindow.PendingEdit.Stay;
            window.SelectForTest("person-2");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("person-1", window.SelectedIdForTest);
            Assert.Equal("555-0102", window.PhoneTextForTest);
            Assert.True(window.FormHasUnsavedEditsForTest);

            // "Save it" writes the correction and then switches.
            window.PendingEditAnswerForTest = PeopleWindow.PendingEdit.Save;
            window.SelectForTest("person-2");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("555-0102", roster.Book.Find("person-1")!.Phone);
            Assert.Equal("person-2", window.SelectedIdForTest);
            Assert.False(window.FormHasUnsavedEditsForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DiscardingAnUnsavedEditLeavesTheAddressBookAlone()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "Aaron Placeholder", Phone = "555-0100" }, "Add");
            roster.Save(new Member { Id = "person-2", DisplayName = "Bertram Sample" }, "Add");

            var window = new PeopleWindow(roster);
            window.Show();
            window.SelectForTest("person-1");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            window.TypePhoneForTest("555-0199");
            window.PendingEditAnswerForTest = PeopleWindow.PendingEdit.Discard;
            window.SelectForTest("person-2");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("person-2", window.SelectedIdForTest);
            Assert.Equal("555-0100", roster.Book.Find("person-1")!.Phone);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M40, review §14.4: the button that removes somebody does not look like the button that saves
    /// them. Colour is not the only signal — the outline is heavier too, and the label ends in an
    /// ellipsis that promises a question (PLAN.md §6).
    /// </summary>
    [Fact]
    public async Task TheRemoveButtonDoesNotLookLikeTheSaveButton()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            var window = new PeopleWindow(roster);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Button remove = window.GetLogicalDescendants().OfType<Button>()
                .Single(b => (b.Content as string) == "Remove this person…");
            Button save = window.GetLogicalDescendants().OfType<Button>()
                .Single(b => (b.Content as string) == "Save this person");

            // Still an app-made button, so ActionSurfaceTests' rule still covers it...
            Assert.Contains("action", remove.Classes);
            Assert.Contains("action", save.Classes);

            // ...and distinguished on top of that.
            Assert.Contains("destructive", remove.Classes);
            Assert.DoesNotContain("destructive", save.Classes);
            Assert.NotEqual(save.Theme, remove.Theme);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }


    /// <summary>
    /// M42, review §14.4: the import wizard's two footer buttons said the wrong things about
    /// themselves. "Stop" did not say stop WHAT — the step, the import, or the program — and "Next",
    /// the button the user is meant to press, looked exactly like the one that abandons the work.
    /// </summary>
    [Fact]
    public async Task TheImportWizardSaysWhatStopStopsAndMarksTheWayOnward()
    {
        await Session.Dispatch(() =>
        {
            var window = new RosterImportWindow(RosterBook.Empty);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IReadOnlyList<Button> buttons = window.GetLogicalDescendants().OfType<Button>()
                .Where(b => b.TemplatedParent is null)
                .ToList();

            Button stop = Assert.Single(buttons, b => b.IsCancel);
            Assert.Equal("Stop the import", stop.Content as string);

            // The way onward is the default, which is both how the primary treatment is chosen
            // (M16) and what makes Enter mean "carry on".
            Button onward = Assert.Single(buttons, b => b.IsDefault);
            Assert.Equal("Find the file…", onward.Content as string);

            // And it actually WEARS the primary treatment. M37's action style was declared after
            // M16's default style, so any button that was both — this one, and the officers-sync
            // dialog's "Update the table" — quietly came out looking ordinary (M42).
            Assert.Equal(
                window.TryFindResource(Tokens.PrimaryButtonTheme, out object? primary) ? primary : null,
                onward.Theme);
            Assert.NotEqual(onward.Theme, stop.Theme);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

/// <summary>
    /// M42, review §14.3: the officers-sync dialog held the one control in the app that was
    /// unavailable and said nothing about why. The M11 rule — nothing becomes unavailable without
    /// saying why, in plain English — stopped at the action panel's edge and never reached dialogs.
    /// </summary>
    [Fact]
    public async Task TheOfficersSyncCheckboxSaysWhyItCannotBeTickedYet()
    {
        await Session.Dispatch(() =>
        {
            // Two brothers claiming one office is what the app refuses to decide (M19), and is
            // therefore the row whose tick box starts unavailable.
            List<Member> book =
            [
                new() { Id = "p1", DisplayName = "Aaron Placeholder", Office = "Tyler" },
                new() { Id = "p2", DisplayName = "Bertram Placeholder", Office = "Tyler" },
            ];

            OfficersProjection plan = OfficersRosterProjection.Plan(
                new OfficersTableDefinition().CreateEmpty(
                    new WidgetSeed("Placeholder Lodge No. 000", 7, 2026, "first Tuesday")),
                book);
            Assert.Contains(plan.Proposals, p => p.IsAmbiguous);

            var dialog = new OfficersSyncDialog(plan, inserting: false);
            dialog.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            CheckBox blocked = Assert.Single(
                dialog.GetLogicalDescendants().OfType<CheckBox>(),
                c => !c.IsEnabled);

            Assert.Equal("Choose one of the names above first", blocked.Content as string);
            Assert.Equal(
                "Choose one of the names above first",
                AutomationProperties.GetHelpText(blocked));

            dialog.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RemovingSomebodySaysHowToPutThemBack()
    {
        await Session.Dispatch(() =>
        {
            RosterService roster = NewRoster();
            roster.Save(new Member { Id = "person-1", DisplayName = "Aaron Placeholder" }, "Add");

            var window = new PeopleWindow(roster);
            window.Show();
            window.SelectForTest("person-1");
            window.DeleteSelectedForTest();

            Assert.Empty(roster.Book.Members);
            Assert.Contains("Undo the last change", window.StatusTextForTest, StringComparison.Ordinal);

            Assert.True(roster.Undo());
            Assert.Single(roster.Book.Members);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>The whole import, driven through the real window rather than the session alone.</summary>
    [Fact]
    public async Task TheImportWindowWalksTheSixScreensAndAddsThePeople()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new RosterImportWindow(RosterBook.Empty);
            window.Show();

            Assert.Equal(ImportStep.ChooseFile, window.SessionForTest.Step);

            window.ChooseFileForTest(Fixture("members-100.csv"));
            Assert.Equal(ImportStep.ChooseHeaderRow, window.SessionForTest.Step);

            await window.NextForTest();
            Assert.Equal(ImportStep.MapColumns, window.SessionForTest.Step);

            await window.NextForTest();
            Assert.Equal(ImportStep.Review, window.SessionForTest.Step);
            Assert.Equal(100, window.SessionForTest.Plan().NewCount);

            await window.NextForTest();
            Assert.Equal(ImportStep.Done, window.SessionForTest.Step);
            Assert.Equal(100, window.Result!.Count);
            Assert.Equal("Your address book now has 100 people.", window.StatusTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The same walk over the shape the lodge's own member system exports (M88): repeated rows,
    /// spouses in the same sheet as members, and three telephone columns with no plain "Phone".
    ///
    /// <para>The window is what is driven, not the session, because the mapping screen is where this
    /// milestone can fail invisibly — twenty-six questions, all guessed, and a wrong guess looks
    /// exactly like a right one until somebody reads a card.</para>
    /// </summary>
    [Fact]
    public async Task TheImportWindowHandlesTheLodgesOwnFile()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new RosterImportWindow(RosterBook.Empty);
            window.Show();

            window.ChooseFileForTest(Fixture("members-full.csv"));
            await window.NextForTest();
            Assert.Equal(ImportStep.MapColumns, window.SessionForTest.Step);

            await window.NextForTest();
            Assert.Equal(ImportStep.Review, window.SessionForTest.Step);

            MergePlan plan = window.SessionForTest.Plan();
            Assert.Equal(10, plan.NewCount);
            Assert.Equal(2, plan.SpouseCount);
            Assert.Contains(
                plan.Summary(),
                line => line.Contains("spouses, not members", StringComparison.Ordinal));

            await window.NextForTest();
            Assert.Equal(ImportStep.Done, window.SessionForTest.Step);
            Assert.Equal(10, window.Result!.Count);

            // The father and the son are two cards, not one (M88's member-number guard).
            Assert.Equal(
                2,
                window.Result!.Members.Count(m => m.DisplayName.StartsWith("Gideon", StringComparison.Ordinal)));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M73(b1): "Stop the import" — automation name "Stop importing and change nothing" — was still
    /// on the screen after the import had been committed, and the shell reads <c>Result</c> however
    /// the window closed. Pressing it, or pressing Escape, imported everybody into the real address
    /// book while promising it would change nothing.
    ///
    /// <para>Before the review step the promise is true and the button stays. On the Done step it
    /// cannot be true, so the button is gone and Escape means the same as the one button left.</para>
    /// </summary>
    [Fact]
    public async Task StoppingTheImportBeforeItRunsChangesNothingAndAfterwardsIsNotOffered()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            static IReadOnlyList<Button> Footer(RosterImportWindow w) =>
                w.GetLogicalDescendants().OfType<Button>()
                    .Where(b => b.TemplatedParent is null)
                    .ToList();

            var stopped = new RosterImportWindow(RosterBook.Empty);
            stopped.Show();
            stopped.ChooseFileForTest(Fixture("members-100.csv"));
            await stopped.NextForTest();
            await stopped.NextForTest();
            Assert.Equal(ImportStep.Review, stopped.SessionForTest.Step);

            // Up to here the button tells the truth: Escape abandons the import, and the shell is
            // handed nothing to write.
            stopped.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(stopped.Result);

            var done = new RosterImportWindow(RosterBook.Empty);
            done.Show();
            done.ChooseFileForTest(Fixture("members-100.csv"));
            await done.NextForTest();
            await done.NextForTest();
            await done.NextForTest();
            Assert.Equal(ImportStep.Done, done.SessionForTest.Step);

            // The import happened when "Add these people" was pressed. Nothing on this step may
            // offer to unsay it, so the button that promised to is not here at all.
            Assert.DoesNotContain(
                Footer(done),
                b => b.IsVisible && (b.Content as string) == "Stop the import");
            Assert.DoesNotContain(
                Footer(done),
                b => b.IsVisible
                    && AutomationProperties.GetName(b) == "Stop importing and change nothing");

            // And Escape does what the one remaining button does — closes, keeping the import.
            Button close = Assert.Single(Footer(done), b => b.IsVisible && b.IsCancel);
            Assert.Equal("Close", close.Content as string);

            done.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, done.Result!.Count);
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AFileTheAppCannotReadIsExplainedInTheImportWindow()
    {
        await Session.Dispatch(() =>
        {
            var window = new RosterImportWindow(RosterBook.Empty);
            window.Show();

            window.ChooseFileForTest(Fixture("old-format.xls"));

            Assert.Equal(ImportStep.ChooseFile, window.SessionForTest.Step);
            Assert.Contains("Save As", window.StatusTextForTest, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The menu's two roster actions that depend on state: with an empty address book there is
    /// nothing to save, and the item says why rather than merely going grey.
    /// </summary>
    [Fact]
    public async Task WithAnEmptyAddressBookSavingItSaysWhyItCannot()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            Assert.Equal(0, window.CurrentActionContext.RosterCount);

            ActionAvailability availability =
                ActionCatalog.Evaluate(ActionId.ExportPeople, window.CurrentActionContext);
            Assert.False(availability.IsAvailable);
            Assert.Equal(ActionId.ImportPeople, availability.RemedyId);

            await window.ActionsForTest.RunAsync(ActionId.ExportPeople);
            Assert.Equal(availability.Reason, window.StatusLabelTextForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The People menu is the complete index PLAN.md §6 requires, and its undo item names what it
    /// will take back — separately from the newsletter's, because Ctrl+Z never crosses that line.
    /// </summary>
    [Fact]
    public async Task ThePeopleMenuNamesWhatItsUndoWillTakeBack()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();

            MenuItem undo = window.GetLogicalDescendants().OfType<MenuItem>()
                .First(m => m is { Tag: ActionId.UndoPeopleChange });
            Assert.Equal("_Undo the last change", undo.Header);
            Assert.False(undo.IsEnabled);
            Assert.False(string.IsNullOrWhiteSpace(
                Avalonia.Automation.AutomationProperties.GetHelpText(undo)));

            window.Roster.Save(new Member { Id = "person-1", DisplayName = "Aaron Placeholder" }, "Add A. Placeholder");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("_Undo Add A. Placeholder", undo.Header);
            Assert.True(undo.IsEnabled);

            // Undoing the address book must not touch the newsletter's own undo stack.
            window.UndoPeopleChange();
            Assert.Empty(window.Roster.Book.Members);
            Assert.False(window.CurrentActionContext.CanUndo);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN §11 M12's "what's next" source: a list of people on the page with an empty address
    /// book is the one moment the app should mention the address book on its own.
    /// </summary>
    [Fact]
    public async Task AnEmptyAddressBookUnderAPeopleWidgetBecomesAWhatsNextRow()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            window.WidgetsForTest!.InsertWidget(0, "officersTable");
            window.FramesForTest!.ClearSelection();
            window.RefreshActions();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(window.CurrentActionContext.RosterEmptyButNeeded);
            Assert.Contains(
                WhatsNext.Suggestions(window.CurrentActionContext),
                s => s.Title == "Fill in your address book");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
