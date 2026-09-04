using System;
using System.Collections.Generic;
using TrestleBoard.App;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Help;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Settings;
using TrestleBoard.Editing;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Editing.Actions;
using TrestleBoard.PdfPages;
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

    /// <summary>The essay on page 1, which M79's border and shading are shown on.</summary>
    private const string EssayBlockId = "frame-essay-1";

    /// <summary>Where the essay continues on page 2 — a full-width frame, so M83's columns show.</summary>
    private const string SecondEssayBlockId = "frame-essay-2";

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

                // M75: every start-an-issue path asks which issue it is. The harness answers with
                // the fictional issue the rest of these shots use, so no dialog stands in front of
                // the panel being photographed.
                window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(8, 2026);
                _ = window.CarryForwardToNextIssueAsync();
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
        // M63. The help window is shot with a word typed into it rather than empty, because an
        // empty one shows a list and the thing worth showing is that plain words find the answer.
        new("how-do-i", ShotKind.Dialog, "M63",
            "Ask in your own words; the answers come from the app itself.",
            "The How do I question window. The word \"picture\" has been typed into a large search "
            + "box, and underneath is a list of matching things the app can do, with the first one "
            + "open showing what it does, which menu it is in and its keyboard shortcut.",
            stage =>
            {
                HelpWindow window = stage.OpenDialog(
                    new HelpWindow(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [ActionId.InsertPhoto] = "Insert " + MenuPaths.Arrow.Trim() + " Insert a picture",
                        },
                        _ => ActionAvailability.Available,
                        _ => Task.FromResult(ActionOutcome.Did),
                        _ => { }),
                    height: 720);
                window.TypeForTest("picture");
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("first-run-tour", ShotKind.Dialog, "M63",
            "Five screens on how a month goes, shown once.",
            "The second screen of the welcome tour, headed \"Start from last month\", explaining "
            + "that a new newsletter begins as a copy of the last one with the news cleared out. "
            + "Underneath are large Back, Next and Skip this buttons.",
            stage =>
            {
                var tour = new TourWindow();
                TourWindow window = stage.OpenDialog(tour, height: 420);
                window.GoToForTest(1);
                return Task.FromResult(Stage.Shoot(window));
            }),

        // M64. Shot with the address book already here and the wordings not, because the whole
        // point of this window is the difference between the two rows.
        // M67. A hand-written two-page PDF, fictional by construction — the harness must never
        // shoot a real bulletin, and a PDF built out of nothing cannot contain one.
        new("pdf-page-picker", ShotKind.Dialog, "M67",
            "The pages of a PDF, as pictures of themselves.",
            "The A page from a PDF window. Two large tiles show small pictures of the pages of a "
            + "PDF, labelled Page 1 and Page 2, with a sentence above saying the chosen page comes "
            + "in as a picture.",
            stage =>
            {
                byte[] pdf = TinyPdf();
                var window = new PdfPageWindow(pdf, PdfPageRasterizer.ReadPages(pdf), "district-bulletin.pdf");
                return Task.FromResult(Stage.Shoot(stage.OpenDialog(window, height: 620)));
            }),

        // M65. Shot with the box empty, because the thing worth showing is how much is on the
        // shelf — a searched-down shelf would look like a small one.
        new("emblem-shelf", ShotKind.Dialog, "M65",
            "Nineteen emblems, bundled and drawn by the app itself.",
            "The Add an emblem window. A search box sits at the top, and under it the emblems are "
            + "laid out as large labelled tiles under headings: the working tools, the lodge, "
            + "lights and seasons, and ornaments.",
            stage => Task.FromResult(Stage.Shoot(stage.OpenDialog(new EmblemPickerWindow(), height: 760)))),

        new("bring-in-a-pack", ShotKind.Dialog, "M64",
            "What is in a predecessor's pack, and what taking it would replace.",
            "The Bring in a predecessor's pack window. Two things are listed with tick boxes: the "
            + "address book, which is not ticked because one is already on this computer and taking "
            + "it would replace it, and the saved wordings, which is ticked because nothing here "
            + "would be lost. Underneath, a line says what will happen.",
            stage =>
            {
                var window = new BringInPackWindow(
                    [
                        new PackPartChoice(
                            SuccessorPackParts.Roster,
                            SuccessorPackParts.TitleOf(SuccessorPackParts.Roster),
                            SuccessorPackParts.DescriptionOf(SuccessorPackParts.Roster),
                            "84 people",
                            "12 people",
                            true),
                        new PackPartChoice(
                            SuccessorPackParts.Phrases,
                            SuccessorPackParts.TitleOf(SuccessorPackParts.Phrases),
                            SuccessorPackParts.DescriptionOf(SuccessorPackParts.Phrases),
                            "6 saved wordings",
                            "",
                            false),
                    ],
                    new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
                    "TrestleBoard.tbpack");
                return Task.FromResult(Stage.Shoot(stage.OpenDialog(window, height: 560)));
            }),

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
                window.AnswerTheIssueWizardForTest = new MainWindow.IssueAnswerForTest(7, 2026, LodgeName: "Placeholder Lodge No. 000");
                _ = window.OpenTemplateAsync("six-page-photos");
                if (window.PhotosForTest?.FirstPlaceholder is { } placeholder)
                {
                    window.GoToPage(placeholder.PageIndex);
                    window.FramesForTest?.Select(placeholder.BlockId);
                    window.RefreshActions();
                }

                return Task.FromResult(Stage.Shoot(window));
            }),

        new("settings", ShotKind.Dialog, null,
            "Theme, text size, and who to speak to about sickness and distress.",
            "The how-things-look window, offering a theme and a size for the app's own text as "
            + "large controls, and asking in plain words which officer members should speak to "
            + "about sickness and distress.",
            stage =>
            {
                SettingsDialog dialog = stage.OpenDialog(
                    new SettingsDialog(Stage.DocumentationSettings), height: 560);
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
            + "large search box above them and, on the right, the first of two tabs holding the "
            + "details somebody looks up in a hurry — name, birthday, telephone, email and office. "
            + "The year he was born is stored beside the birthday and is never printed in the "
            + "newsletter.",
            stage =>
            {
                PeopleWindow people = stage.OpenDialog(
                    new PeopleWindow(Fixtures.FictionalRoster(stage.StateRoot)),
                    width: 1180,
                    height: 840);
                // With nobody chosen the form on the right is empty boxes, which documents the
                // layout but not the point of it.
                people.SelectForTest("person-1");
                return Task.FromResult(Stage.Shoot(people));
            }),

        // The search, mid-typing. The People shot above shows the window; this shows the thing the
        // window is FOR — three letters and the man is on screen — and, since M88, that the box
        // looks at more than the name: a street, a town, a lodge member number.
        new("people-search", ShotKind.Dialog, "M88",
            "Look somebody up by whatever you happen to have.",
            "The people window with a lodge member number typed into the search box, one brother "
            + "found by it, and his card open on the right — showing that the search looks at more "
            + "than the name: a member number, a telephone number, a street or a town all find him.",
            stage =>
            {
                PeopleWindow people = stage.OpenDialog(
                    new PeopleWindow(Fixtures.FictionalRoster(stage.StateRoot)),
                    width: 1180,
                    height: 840);

                // A number rather than a name, deliberately: a name is the search everybody expects
                // and this is the one M88 added. It also photographs unambiguously — a surname would
                // pull in the brethren who live on Sample Lane, which is right and reads as wrong.
                people.SearchBoxForTest.Text = "98515";
                people.SelectForTest("person-15");
                return Task.FromResult(Stage.Shoot(people));
            }),

        // M88's second tab. No existing shot can show it, and it is the whole of the milestone:
        // what the lodge's own member system holds, in the app rather than in another program.
        new("people-address", ShotKind.Dialog, "M88",
            "Everything the lodge already knows about a brother, in one place.",
            "The second tab of the person form, showing his lodge member number, the letters after "
            + "his name, his postal address, his home, mobile and work telephone numbers, his "
            + "wife's name and contact details, and a box for notes.",
            stage =>
            {
                PeopleWindow people = stage.OpenDialog(
                    new PeopleWindow(Fixtures.FictionalRoster(stage.StateRoot)),
                    width: 1180,
                    height: 840);
                people.SelectForTest("person-1");
                people.SelectedTabForTest = 1;
                return Task.FromResult(Stage.Shoot(people));
            }),

        // The mapping step, because it is the one that decides whether the import is right, and
        // the one screen no other spreadsheet importer asks the way this one does.
        new("import-columns", ShotKind.Dialog, "M12",
            "Importing asks one question per lodge field, not one per column.",
            "The import window asking which column of the spreadsheet holds each piece of "
            + "information — name, birthday, telephone, and under a second heading the postal "
            + "address and the rest — with the guesses already filled in.",
            async stage =>
            {
                RosterImportWindow import = stage.OpenDialog(
                    new RosterImportWindow(RosterBook.Empty));
                import.ChooseFileForTest(Fixtures.MembersCsv());
                await import.NextForTest().ConfigureAwait(true);
                return Stage.Shoot(import);
            }),

        // The screen before anything is written — the one that makes the import safe to press. It
        // is shot over the shape a lodge's own member system exports (M88), because that file is
        // where the counts stop being obvious: the same man appears on several rows, and wives are
        // in the sheet beside their husbands.
        new("import-review", ShotKind.Dialog, "M88",
            "Nothing is written until this screen says what it is about to do.",
            "The review screen of the import, listing in plain sentences how many people are new, "
            + "how many rows are spouses rather than members, and the first few people as they will "
            + "be stored — each with the telephone number and town read from their own row.",
            async stage =>
            {
                RosterImportWindow import = stage.OpenDialog(
                    new RosterImportWindow(RosterBook.Empty),
                    width: 1000,
                    height: 780);
                import.ChooseFileForTest(Fixtures.MembersFullCsv());
                await import.NextForTest().ConfigureAwait(true);
                await import.NextForTest().ConfigureAwait(true);
                return Stage.Shoot(import);
            }),

        // ---- Rendered, not photographed ---------------------------------------------------------------
        // There is no export dialog worth showing: it is a file picker, and file pickers leak paths
        // (PLAN.md §0 rule 6). This is also the most deterministic image in the set — no Avalonia
        // is involved in it at all.

        // ---- What M77–M85 added (2026-09-01) ------------------------------------------------------

        new("notice-border-and-rule", ShotKind.Window, "M79",
            "A shaded notice with a border, and a line right across the page.",
            "The editor showing a newsletter page. One block of writing has a pale background and a "
            + "thin line round its edge, marking it out as a notice, and a straight rule runs right "
            + "across the page under it. The panel down the right-hand side offers what can be done "
            + "to the chosen thing, including making another like it and keeping it where it is.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                FrameEditorController frames = window.FramesForTest!;

                frames.Select(EssayBlockId);
                frames.ToggleBorder();
                frames.ToggleShade();

                frames.AddRuleAcrossThePage(0);

                window.SourceForTest!.Invalidate(new ChangeScope(ChangeKind.PageStructure));
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("page-footer", ShotKind.Render, "M78",
            "Every page says which lodge, which issue, and which page it is.",
            "The bottom of a newsletter page. One grey line of small type reads \"Placeholder Lodge "
            + "No. 000\", then the month and year, then \"page 2 of 5\".",
            stage =>
            {
                // Page 2, whose frame runs nearly the full height: the bottom of page 1 is empty,
                // so the footer photographed there floats in white space and documents nothing.
                MainWindow window = stage.OpenEditor();
                window.TogglePageFooter();
                return Task.FromResult(PageSpread.BottomOfPage(window.SourceForTest!, 1));
            }),

        new("month-calendar", ShotKind.Render, "M84",
            "The month on a grid, with the stated meeting already marked.",
            "A calendar for one month laid out as a grid of seven columns, one for each day of the "
            + "week starting on Sunday. The day numbers run through the cells, and two of them "
            + "carry short lines of writing: the stated meeting, and a supper.",
            stage =>
            {
                MainWindow window = stage.OpenEditor();
                Stage.PutACalendarOnThePage(window);
                return Task.FromResult(PageSpread.OnePage(window.SourceForTest!, 2));
            }),

        new("two-columns", ShotKind.Render, "M83",
            "An article running down two columns, still breaking around the photograph.",
            "A newsletter page whose article runs down two columns of equal width with a gap "
            + "between them. The left column fills before the right one begins, and the right "
            + "column breaks around a photograph part way down.",
            stage =>
            {
                // Page 1's frame, not page 2's: page 2 holds the TAIL of the essay, which fits
                // inside a single column and so photographs as one narrow column beside the
                // officers table — a picture of the feature not working. Page 1 has the whole
                // article and a photograph in it, so both columns fill AND the writing still
                // breaks round the picture, which is the two features together.
                MainWindow window = stage.OpenEditor();
                window.FramesForTest!.Select(EssayBlockId);
                window.ToggleTwoColumns();
                return Task.FromResult(PageSpread.OnePage(window.SourceForTest!, 0));
            }),

        new("archive-search", ShotKind.Dialog, "M85",
            "Looking for words across every newsletter the committee has kept.",
            "The Find window with a search box at the top and, under it, a list of what was found "
            + "in earlier newsletters. Each row names the issue and the page, and shows the "
            + "sentence the words appear in. Newest first.",
            stage =>
            {
                // Find mode, not replace: the archive search is about looking, and a "Put this
                // instead" box in the picture would suggest it can rewrite eleven old newsletters.
                FindWindow window = stage.OpenDialog(
                    new FindWindow(stage.OpenEditor().FindForTest!), height: 520);
                window.SetMode(replacing: false);
                window.TypeForTest("fish fry");
                window.ShowArchiveResults(new ArchiveSearchResult(
                    [
                        new ArchiveHit("a", "March 2026", 2, "…tickets for the fish fry are on sale at the door…"),
                        new ArchiveHit("b", "September 2025", 1, "…the fish fry raised two hundred dollars for the fund…"),
                        new ArchiveHit("c", "June 2024", 3, "…thanks to everybody who cooked at the fish fry…"),
                    ],
                    11,
                    null));
                return Task.FromResult(Stage.Shoot(window));
            }),

        new("recent-pictures", ShotKind.Dialog, "M80",
            "The pictures used before, offered ahead of the file picker.",
            "The Put a picture in window. Three large buttons list pictures used in earlier "
            + "newsletters by their file names, and below them a button reads \"Choose a file…\".",
            stage => Task.FromResult(Stage.Shoot(stage.OpenDialog(
                new RecentPicturesDialog(
                    ["lodge-front.jpg", "worshipful-master.jpg", "summer-picnic-2025.jpg"]),
                height: 420)))),

        new("something-went-wrong", ShotKind.Dialog, "M77",
            "When something goes wrong, the work is already kept.",
            "A card headed \"Something went wrong\". The first line says the newsletter has been "
            + "kept and nothing written is lost. Under it, two buttons: no thank you, and save a "
            + "report.",
            stage => Task.FromResult(Stage.Shoot(stage.OpenDialog(
                new ProblemCard(workWasKept: true, appWillClose: false), height: 380)))),

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

    /// <summary>
    /// A two-page PDF written byte by byte, for M67's picker. Fictional by construction: there is
    /// no source file, so there is nothing real in it to leak (§0 rule 2).
    /// </summary>
    private static byte[] TinyPdf()
    {
        string[] contents =
        [
            "BT /F1 28 Tf 60 700 Td (Indian Land Lodge 414) Tj ET",
            "BT /F1 28 Tf 60 500 Td (District calendar - placeholder) Tj ET",
        ];

        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 7 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {contents[0].Length} >>\nstream\n{contents[0]}\nendstream",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 7 0 R >> >> /Contents 6 0 R >>",
            $"<< /Length {contents[1].Length} >>\nstream\n{contents[1]}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");

        return System.Text.Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
