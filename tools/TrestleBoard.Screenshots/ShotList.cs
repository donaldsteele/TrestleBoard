using TrestleBoard.App;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Settings;
using TrestleBoard.Editing;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Screenshots;

/// <summary>
/// The eighteen committed images, as data (PLAN.md §11 M15).
///
/// Every one of them is taken against the five-page sample issue or a fictional fixture, at a
/// pinned window size, in the Light theme at 100% scale unless the shot is about something else.
/// Nothing here reads the maintainer's own settings, address book or recovery folder — see
/// <see cref="ScreenshotHarness"/> for why that is structural rather than a promise.
/// </summary>
internal static class ShotList
{
    /// <summary>The photograph on page 1 of the sample issue, with body text flowing round it.</summary>
    private const string CoverPhotoBlockId = "img-cover";

    /// <summary>The officers table on page 2 of the sample issue.</summary>
    private const string OfficersBlockId = "w-officers";

    public static IReadOnlyList<Shot> All { get; } =
    [
        // ---- The main window ----------------------------------------------------------------------
        new("hero-issue-page1", ShotKind.Window, null,
            "The cover of the example newsletter, open in the editor.",
            "The TrestleBoard editor showing the front page of a newsletter: a cover heading, an "
            + "article, and a photograph with the text flowing around it.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                return Task.FromResult(Stage.Shoot(window));
            }),

        // The app's whole thesis in one picture, so it is the one shot that must come through the
        // live canvas rather than a composite: text really is flowing around that photograph, drawn
        // by the same engine that will draw the PDF.
        new("text-wrap-around-photo", ShotKind.Window, null,
            "Body text flowing around a photograph, at actual size.",
            "A close view of a newsletter page where the paragraphs break around a photograph, "
            + "leaving a clean margin down the side of the picture.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.ZoomToActualSize();
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("action-panel-photo", ShotKind.Window, "M11",
            "Choose a photograph and the things you can do to it appear beside it.",
            "The editor with a photograph selected. A panel down the right-hand side is headed "
            + "\"A photo is selected\" and lists what can be done to it, each with a short "
            + "explanation.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.FramesForTest?.Select(CoverPhotoBlockId);
                window.RefreshActions();
                return Task.FromResult(Stage.Shoot(window));
            }),

        // Taken after a carry-forward on purpose: that is the state the card exists for, and an
        // empty checklist would document nothing.
        new("action-panel-whats-next", ShotKind.Window, "M11",
            "With nothing selected, the panel says what this issue still needs.",
            "The editor with nothing selected. The right-hand panel is headed \"What's next\" and "
            + "lists the things this month's newsletter still needs, each with a sentence saying "
            + "why.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.StartFromLastMonth();
                window.RefreshActions();
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("officers-table-widget", ShotKind.Window, null,
            "The officers table is a list TrestleBoard lays out for you.",
            "A newsletter page showing a table of lodge officers with their positions and "
            + "telephone numbers, selected on the page, with the article text flowing beside it.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.GoToPage(1);
                window.FramesForTest?.Select(OfficersBlockId);
                window.RefreshActions();
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("font-picker", ShotKind.Dialog, "M14",
            "Choosing a typeface, with the newsletter's own words in the preview.",
            "The fonts and text styles window. On the left are the kinds of writing in this "
            + "newsletter, such as Body text and Cover title; on the right a list of typefaces, "
            + "each shown in its own face, and a preview of the user's own words.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                // The sheet sizes itself to its owner in the app; offscreen it has none, so the
                // documentation pins the size it is designed around.
                TextStylesWindow sheet = stage.OpenDialog(
                    window.BuildTextStylesWindowForTest(), width: 1020, height: 820);
                return Task.FromResult(Stage.Shoot(sheet));
            }),

        // Deliberately the same page as the hero, or the comparison says nothing.
        new("high-contrast", ShotKind.Window, null,
            "The same page in the high-contrast theme.",
            "The same newsletter page shown in the high-contrast theme: white text on black "
            + "chrome, with the page itself still white paper.",
            stage =>
            {
                MainWindow window = stage.OpenEditor(
                    Stage.DocumentationSettings with { Theme = ThemeChoice.HighContrast });
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("scale-200", ShotKind.Window, null,
            "The same window with everything set to twice the size.",
            "The same newsletter page with the menus, buttons and panel text at twice their usual "
            + "size, and the page itself unchanged.",
            stage =>
            {
                MainWindow window = stage.OpenEditor(
                    Stage.DocumentationSettings with { UiScalePercent = 200 });
                return Task.FromResult(Stage.Shoot(window));
            }),

        // ---- Dialogs --------------------------------------------------------------------------------
        new("start-screen", ShotKind.Dialog, null,
            "The three ways to begin a month's newsletter.",
            "The start screen, offering three large buttons: start from last month, open a "
            + "newsletter you saved, or start from a ready-made layout.",
            stage =>
            {
                StartDialog dialog = stage.OpenDialog(
                    new StartDialog(canStartFromLastMonth: true), height: 640);
                return Task.FromResult(Stage.Shoot(dialog));
            }),

        new("wizard-officers-step", ShotKind.Dialog, null,
            "One question per screen, in large type.",
            "A wizard screen asking for one lodge officer's name and telephone number, in large "
            + "type, with big Back and Next buttons underneath.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.GoToPage(1);
                WizardSession session = window.CreateSession(OfficersBlockId)
                    ?? throw new InvalidOperationException("The officers wizard would not open.");
                session.TryGoNext();
                WizardWindow wizard = stage.OpenDialog(
                    new WizardWindow(session, window.PeopleForWizards()));
                wizard.RenderForTest();
                return Task.FromResult(Stage.Shoot(wizard));
            }),

        new("wizard-review", ShotKind.Dialog, null,
            "Every wizard ends by showing you what it is about to write.",
            "The last screen of a wizard, listing back everything that was entered so it can be "
            + "checked before anything is put on the page.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.GoToPage(1);
                WizardSession session = window.CreateSession(OfficersBlockId)
                    ?? throw new InvalidOperationException("The officers wizard would not open.");
                session.TryGoTo(session.ScreenCount - 1);
                WizardWindow wizard = stage.OpenDialog(
                    new WizardWindow(session, window.PeopleForWizards()));
                wizard.RenderForTest();
                return Task.FromResult(Stage.Shoot(wizard));
            }),

        // M19's headline: the officers table filled in from the address book, and the two things the
        // app refuses to decide on its own showing above the ordinary changes.
        new("officers-sync", ShotKind.Dialog, "M19",
            "The address book fills the officers table in — after showing you every change.",
            "The officers window, listing each office with the name it holds now and the name the "
            + "address book would put there, one tick box per row, an office two people claim with "
            + "neither chosen, and the offices it could not recognise listed above them.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.GoToPage(1);

                OfficersTableData current = window.ReadOfficersForTest(OfficersBlockId)
                    ?? throw new InvalidOperationException("The officers table would not open.");
                OfficersProjection plan = OfficersRosterProjection.Plan(
                    current, Fixtures.PeopleWithAwkwardOffices);

                OfficersSyncDialog dialog = stage.OpenDialog(
                    new OfficersSyncDialog(plan, inserting: false), height: 780);
                return Task.FromResult(Stage.Shoot(dialog));
            }),

        new("grid-editor", ShotKind.Dialog, null,
            "Or edit the whole list at once, in big rows.",
            "The list editor, showing every lodge officer at once in large rows, with Add and "
            + "Remove buttons beside them.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.GoToPage(1);
                WizardSession session = window.CreateSession(OfficersBlockId)
                    ?? throw new InvalidOperationException("The officers list editor would not open.");
                WidgetGridWindow grid = stage.OpenDialog(
                    new WidgetGridWindow(session, window.PeopleForWizards()));
                grid.RenderForTest();
                return Task.FromResult(Stage.Shoot(grid));
            }),

        new("fix-photo", ShotKind.Dialog, null,
            "Behind the one-click Fix photo button: large sliders that apply as you drag.",
            "The photograph adjustment window: large sliders for brighter or darker, more or "
            + "less contrast and more or less colour, a box to brighten and balance the picture "
            + "automatically, and four more sliders for trimming each edge, above buttons to turn "
            + "the picture a quarter turn or start over.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.FramesForTest?.Select(CoverPhotoBlockId);
                PhotoController photos = window.PhotosForTest
                    ?? throw new InvalidOperationException("No photo controller — is a document open?");
                PhotoAdjustWindow adjust = stage.OpenDialog(
                    new PhotoAdjustWindow(photos, CoverPhotoBlockId));
                return Task.FromResult(Stage.Shoot(adjust));
            }),

        // M22. FixPhoto auto-crops on resize; Position is the separate, revisitable step that only
        // recentres the already-committed crop, with the whole picture visible behind it so the
        // user can see what panning trims away.
        new("position-photo", ShotKind.Dialog, null,
            "Recentre the crop without resizing it — the whole picture is visible behind the frame.",
            "The position picture window: the whole decoded photograph behind a blue crop-window "
            + "overlay, zoom in and zoom out buttons with a plain-language percentage, and Apply "
            + "and Cancel buttons.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.FramesForTest?.Select(CoverPhotoBlockId);
                PhotoController photos = window.PhotosForTest
                    ?? throw new InvalidOperationException("No photo controller — is a document open?");
                PositionPhotoWindow position = stage.OpenDialog(
                    new PositionPhotoWindow(photos, CoverPhotoBlockId));
                return Task.FromResult(Stage.Shoot(position));
            }),

        // M18. The photo template shipped three of these frames from M9 and no command in the app
        // could put a picture in one; this is the shot of them asking to be filled.
        new("photo-template-placeholders", ShotKind.Window, "M18",
            "A photo page waiting for its photograph, and saying so.",
            "The editor showing a page of the six-page photo template. A large empty picture frame "
            + "carries the words \"Double-click to choose a picture\", and the panel beside it is "
            + "headed \"A photo is selected\" with \"Put a picture here…\" at the top of what can "
            + "be done to it.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                window.OpenTemplate("six-page-photos");
                if (window.PhotosForTest?.FirstPlaceholder is { } placeholder)
                {
                    window.GoToPage(placeholder.PageIndex);
                    window.FramesForTest?.Select(placeholder.BlockId);
                    window.RefreshActions();
                }

                return Task.FromResult(Stage.Shoot(window));
            }),

        new("settings", ShotKind.Dialog, null,
            "Theme and text size, and nothing else.",
            "The how-things-look window, offering a theme and a size for the app's own text, both "
            + "as large controls.",
            stage =>
            {
                SettingsDialog dialog = stage.OpenDialog(
                    new SettingsDialog(Stage.DocumentationSettings), height: 420);
                return Task.FromResult(Stage.Shoot(dialog));
            }),

        // The crash-recovery story is invisible without this one, and the thumbnail is the part
        // that makes it believable — you can see which newsletter is being offered back.
        new("restore-dialog", ShotKind.Dialog, null,
            "After a crash, the work is offered back with a picture of it.",
            "The restore window after an unexpected shutdown, offering the unsaved newsletter back "
            + "with a thumbnail of its first page and the time it was last saved.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                byte[] thumbnail = window.SourceForTest!.RenderPageToPng(0, 0.42f);
                var snapshot = new RecoverySnapshot(
                    "example",
                    [],
                    OriginalPath: null,
                    // Pinned: the dialog phrases the age of the snapshot, so a real clock would
                    // make this image change every time it was regenerated.
                    SavedAt: new DateTimeOffset(2026, 3, 3, 19, 12, 0, TimeSpan.Zero));
                RestoreDialog dialog = stage.OpenDialog(new RestoreDialog(
                    snapshot,
                    thumbnail,
                    new DateTimeOffset(2026, 3, 3, 19, 14, 0, TimeSpan.Zero)));
                return Task.FromResult(Stage.Shoot(dialog));
            }),

        new("people-window", ShotKind.Dialog, "M12",
            "The address book: type three letters and there they are.",
            "The people window, listing members of the lodge with their office and birthday, a "
            + "large search box above them and a form on the right for correcting a detail.",
            stage =>
            {
                PeopleWindow people = stage.OpenDialog(
                    new PeopleWindow(Fixtures.FictionalRoster(stage.StateRoot)),
                    width: 1100,
                    height: 760);
                // With nobody chosen the form on the right is seven empty boxes, which documents
                // the layout but not the point of it.
                people.SelectForTest("person-1");
                return Task.FromResult(Stage.Shoot(people));
            }),

        // The mapping step, because it is the one that decides whether the import is right, and
        // the one screen no other spreadsheet importer asks the way this one does.
        new("import-columns", ShotKind.Dialog, "M12",
            "Importing asks one question per lodge field, not one per column.",
            "The import window asking which column of the spreadsheet holds each piece of "
            + "information — name, birthday, telephone — with the guesses already filled in.",
            async stage =>
            {
                RosterImportWindow import = stage.OpenDialog(
                    new RosterImportWindow(RosterBook.Empty));
                import.ChooseFileForTest(Fixtures.MembersCsv());
                await import.NextForTest().ConfigureAwait(true);
                return Stage.Shoot(import);
            }),

        // ---- Rendered, not photographed ---------------------------------------------------------------
        // There is no export dialog worth showing: it is a file picker, and file pickers leak paths
        // (PLAN.md §0 rule 6). This is also the most deterministic image in the set — no Avalonia
        // is involved in it at all.
        new("pdf-page-spread", ShotKind.Render, null,
            "Three pages of the exported PDF.",
            "Three pages of the finished newsletter side by side as they appear in the exported "
            + "PDF: the cover, the officers page, and the birthdays and committees page.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                return Task.FromResult(PageSpread.ThreeUp(window.SourceForTest!));
            }),
    ];
}
