namespace TrestleBoard.Editing.Actions;

/// <summary>
/// The single list of everything the app can do, and the single place that decides whether each one
/// is possible right now (PLAN.md §11 M11).
///
/// Before this existed the answer was spread across roughly thirty <c>IsEnabled =</c> assignments in
/// two methods of the window's code-behind, and none of them carried a reason. The user saw controls
/// go grey and had no way to find out why. Everything here is a pure function of an
/// <see cref="ActionContext"/>, so the panel, the menu bar and the right-click flyout are fed from
/// one evaluation and cannot contradict each other — and every sentence the user will read is
/// testable without starting Avalonia.
/// </summary>
public static class ActionCatalog
{
    private const string NoNewsletter =
        "There is no newsletter open yet. Start one from a template, or open one you saved.";

    private const string ChooseSomething =
        "Nothing on the page is chosen. Click something on the page, or press Tab to step through it.";

    private const string NeedsPicture =
        "This needs a picture. Choose one on the page first.";

    private const string EmptyPictureFrame =
        "There is no picture in this frame yet, so there is nothing to change. "
        + "Put one in first — double-clicking the frame on the page does the same thing.";

    private const string NeedsText =
        "Click into some writing first, then this changes the words you highlight.";

    private const string NeedsTextFrame =
        "This is about a frame of writing. Choose one on the page first.";

    private const string NeedsListItem =
        "This is about the lists TrestleBoard fills in for you, like the officers table. "
        + "Choose one on the page first.";

    /// <summary>M21: the two sentences that teach Shift+click, which is the only way to get here.</summary>
    private const string NeedsTwoThings =
        "This lines up two things or more. Choose one, then hold Shift and click the next one.";

    private const string NeedsThreeThings =
        "This shares the space out between three things or more. Choose one, then hold Shift and "
        + "click each of the others.";

    private const string NeedsBirthdayList =
        "This is about the birthday list. Choose one on the page first, or add one from the Insert menu.";

    private const string NeedsOfficersTable =
        "This is about the officers table. Choose one on the page first, or add one from the Insert menu.";

    /// <summary>
    /// The two widget type ids this project knows by name (M13, M19). <c>TrestleBoard.Widgets</c> is
    /// deliberately above this layer, so each is recognised by its stable id — the same string the
    /// document itself stores — rather than by a type reference that would invert the dependency.
    /// </summary>
    private const string BirthdayListTypeId = "birthdayList";

    private const string OfficersTableTypeId = "officersTable";

    private static readonly EditorAction[] AllActions =
    [
        // ---- The newsletter itself ------------------------------------------------------------
        new(ActionId.Open, "Open a newsletter…", "Opens a newsletter you saved earlier.",
            ActionGroup.Newsletter, "Ctrl+O"),
        new(ActionId.OpenSample, "Open the sample newsletter", "Shows an example issue to look around in.",
            ActionGroup.Newsletter),
        new(ActionId.NewFromTemplate, "Start from a template…", "Begins a new newsletter from a ready-made layout.",
            ActionGroup.Newsletter),
        new(ActionId.StartFromLastMonth, "Start from last month",
            "Copies this newsletter forward to next month and clears the articles.", ActionGroup.Newsletter),
        new(ActionId.ExportPdf, "Export as PDF…", "Makes the file you email to the lodge.",
            ActionGroup.Newsletter, "Ctrl+E", IsPrimary: true),
        new(ActionId.Exit, "Exit", "Closes TrestleBoard.", ActionGroup.Newsletter),

        // ---- Edit -------------------------------------------------------------------------------
        new(ActionId.Undo, "Undo", "Takes back the last thing you did.", ActionGroup.Edit, "Ctrl+Z"),
        new(ActionId.Redo, "Redo", "Does again the thing you just took back.", ActionGroup.Edit, "Ctrl+Y"),
        new(ActionId.Cut, "Cut", "Removes the highlighted words and keeps a copy.", ActionGroup.Edit, "Ctrl+X"),
        new(ActionId.Copy, "Copy", "Keeps a copy of the highlighted words.", ActionGroup.Edit, "Ctrl+C"),
        new(ActionId.Paste, "Paste", "Puts the copied words where the cursor is.", ActionGroup.Edit, "Ctrl+V"),
        new(ActionId.SelectAll, "Select all", "Highlights everything in this piece of writing.",
            ActionGroup.Edit, "Ctrl+A"),
        new(ActionId.Find, "Find…", "Looks for words anywhere in this newsletter.",
            ActionGroup.Edit, "Ctrl+F"),
        new(ActionId.Replace, "Find and replace…",
            "Looks for words and puts different ones in their place.", ActionGroup.Edit, "Ctrl+H"),

        // ---- Text -------------------------------------------------------------------------------
        new(ActionId.Bold, "Bold", "Makes the highlighted words heavier.", ActionGroup.Text, "Ctrl+B",
            IsPrimary: true),
        new(ActionId.Italic, "Italic", "Slants the highlighted words.", ActionGroup.Text, "Ctrl+I",
            IsPrimary: true),
        new(ActionId.ParagraphStyle, "Paragraph style ▸",
            "Chooses what kind of paragraph this is, such as a heading.", ActionGroup.Text),
        new(ActionId.FontsAndStyles, "Change the font for this style ▸",
            "Chooses the typeface and size for every piece of writing of this kind.",
            ActionGroup.Text, "Ctrl+Shift+D", IsPrimary: true),
        new(ActionId.BiggerText, "+ Bigger", "Makes this kind of writing one step larger everywhere.",
            ActionGroup.Text, "Ctrl+Shift+."),
        new(ActionId.SmallerText, "− Smaller", "Makes this kind of writing one step smaller everywhere.",
            ActionGroup.Text, "Ctrl+Shift+,"),
        new(ActionId.FontJustHere, "Use a different font just here…",
            "Changes the typeface of the highlighted words only, leaving the rest alone.",
            ActionGroup.Text),
        new(ActionId.ClearFontOverride, "Put it back to the usual font",
            "Returns the highlighted words to the font the rest of this kind of writing uses.",
            ActionGroup.Text),

        // ---- Putting things on the page ---------------------------------------------------------
        new(ActionId.AddTextFrame, "Add a text frame", "Puts an empty box on the page for you to write in.",
            ActionGroup.Insert, "Ctrl+Shift+T", IsPrimary: true),
        new(ActionId.InsertPhoto, "Insert a picture…", "Puts a photograph on the page.",
            ActionGroup.Insert, "Ctrl+Shift+P", IsPrimary: true),
        new(ActionId.InsertOfficers, "Lodge officers", "Adds the officers table and asks who they are.",
            ActionGroup.Insert),
        new(ActionId.InsertBirthdays, "Birthdays", "Adds the birthday list.", ActionGroup.Insert),
        new(ActionId.InsertCommittees, "Committees", "Adds the committee list.", ActionGroup.Insert),
        new(ActionId.InsertDistrictCalendar, "District calendar", "Adds the 22nd District meeting table.",
            ActionGroup.Insert),
        new(ActionId.InsertEventCard, "Announcement box", "Adds a box for one announcement.", ActionGroup.Insert),
        new(ActionId.InsertCoverBanner, "Cover heading", "Adds the lodge name and meeting details for page one.",
            ActionGroup.Insert),

        // ---- The selected thing -----------------------------------------------------------------
        new(ActionId.DeleteFrame, "Delete this", "Takes it off the page. You can undo this.",
            ActionGroup.Item, "Delete"),
        new(ActionId.EditWidget, "Change what this says…", "Asks the questions again, already filled in.",
            ActionGroup.Item, "Ctrl+Shift+E", IsPrimary: true),
        new(ActionId.EditWidgetList, "Edit the list…", "Shows the whole list at once, with big rows.",
            ActionGroup.Item, "Ctrl+Shift+G"),
        new(ActionId.FitToContents, "Fit to contents", "Makes the box exactly as tall as what is in it.",
            ActionGroup.Item, "Ctrl+Shift+Y"),
        new(ActionId.SyncBirthdays, "Bring in birthdays from the address book",
            "Fills the birthday list in from your address book, showing you the changes first.",
            ActionGroup.Item, "Ctrl+Shift+U"),
        new(ActionId.SyncOfficers, "Fill in the officers from the address book",
            "Fills the officers table in from your address book, showing you the changes first.",
            ActionGroup.Item, "Ctrl+Shift+B"),

        // ---- Pictures ---------------------------------------------------------------------------
        // M18 puts the replace command FIRST: an empty frame is the state every photo template
        // ships in, and until M18 no command in the app could fill one.
        new(ActionId.ReplacePicture, "Put a picture here…", "Chooses a picture file and puts it in this frame.",
            ActionGroup.Picture, "Ctrl+Shift+O", IsPrimary: true),
        new(ActionId.FixPhoto, "Fix this picture", "Crops it to the frame and brightens it in one step.",
            ActionGroup.Picture, "Ctrl+Shift+F", IsPrimary: true),
        new(ActionId.AdjustPhoto, "Adjust the picture…", "Opens the sliders for brightness, contrast and colour.",
            ActionGroup.Picture, "Ctrl+Shift+A"),
        new(ActionId.TrimPicture, "Trim the edges…", "Takes the edges off without changing the original file.",
            ActionGroup.Picture),
        new(ActionId.CaptionPicture, "Write a caption…", "Writes the words printed under the picture.",
            ActionGroup.Picture),
        new(ActionId.DescribePicture, "Describe this picture…",
            "Writes what somebody who cannot see it should be told.", ActionGroup.Picture),

        // ---- How text flows ---------------------------------------------------------------------
        new(ActionId.ToggleWrap, "Wrap text around this", "Makes the writing on the page flow around it.",
            ActionGroup.TextFlow, "Ctrl+Shift+W", IsPrimary: true),
        new(ActionId.LinkFrames, "Continue this text in another frame…",
            "Lets a long article carry on in a second box.", ActionGroup.TextFlow, "Ctrl+Shift+L"),
        new(ActionId.UnlinkFrames, "Stop continuing into the next frame",
            "Ends the link so this box stands on its own.", ActionGroup.TextFlow, "Ctrl+Shift+K"),
        new(ActionId.AutoFlow, "Make the rest fit",
            "Adds pages and boxes until all of this writing has somewhere to go.",
            ActionGroup.TextFlow, "Ctrl+Shift+M"),

        // ---- Arranging --------------------------------------------------------------------------
        new(ActionId.BringForward, "Bring forward", "Moves it one step towards the front.",
            ActionGroup.Arrange, "Ctrl+]"),
        new(ActionId.SendBackward, "Send backward", "Moves it one step towards the back.",
            ActionGroup.Arrange, "Ctrl+["),
        new(ActionId.BringToFront, "Bring to front", "Puts it in front of everything else.",
            ActionGroup.Arrange, "Ctrl+Shift+]"),
        new(ActionId.SendToBack, "Send to back", "Puts it behind everything else.",
            ActionGroup.Arrange, "Ctrl+Shift+["),

        // ---- Lining things up (M21) -------------------------------------------------------------
        // Each one says "the things you have chosen", because that is the whole difference: these
        // are the first commands in the app that act on more than one thing at a time.
        new(ActionId.AlignLeft, "Line up the left edges",
            "Moves the things you have chosen so their left edges are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignCentres, "Line up the centres, side to side",
            "Moves them sideways until their middles are one above the other.", ActionGroup.Arrange),
        new(ActionId.AlignRight, "Line up the right edges",
            "Moves the things you have chosen so their right edges are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignTop, "Line up the top edges",
            "Moves the things you have chosen so their tops are in a line.", ActionGroup.Arrange),
        new(ActionId.AlignMiddles, "Line up the middles, top to bottom",
            "Moves them up and down until their middles are side by side.", ActionGroup.Arrange),
        new(ActionId.AlignBottom, "Line up the bottom edges",
            "Moves the things you have chosen so their bottoms are in a line.", ActionGroup.Arrange),
        new(ActionId.DistributeHorizontally, "Space them out evenly, side to side",
            "Leaves the same gap between each one, without moving the two on the ends.",
            ActionGroup.Arrange),
        new(ActionId.DistributeVertically, "Space them out evenly, top to bottom",
            "Leaves the same gap above and below each one, without moving the top and bottom ones.",
            ActionGroup.Arrange),

        // ---- Pages ------------------------------------------------------------------------------
        new(ActionId.NextPage, "Next page", "Shows the following page.", ActionGroup.Page, "Ctrl+PageDown"),
        new(ActionId.PreviousPage, "Previous page", "Shows the page before this one.",
            ActionGroup.Page, "Ctrl+PageUp"),
        new(ActionId.AddPage, "Add a page after this one", "Puts a new empty page in.", ActionGroup.Page),
        new(ActionId.RemovePage, "Delete this page", "Takes this page out. You can undo this.", ActionGroup.Page),
        new(ActionId.MovePageEarlier, "Move this page earlier", "Swaps it with the page before it.",
            ActionGroup.Page),
        new(ActionId.MovePageLater, "Move this page later", "Swaps it with the page after it.", ActionGroup.Page),

        // ---- Looking at it ----------------------------------------------------------------------
        new(ActionId.ZoomIn, "Zoom in", "Makes the page on screen bigger.", ActionGroup.View, "Ctrl+="),
        new(ActionId.ZoomOut, "Zoom out", "Makes the page on screen smaller.", ActionGroup.View, "Ctrl+-"),
        new(ActionId.ActualSize, "Actual size", "Shows the page at its printed size.", ActionGroup.View, "Ctrl+0"),
        new(ActionId.FitPage, "Fit page", "Shows the whole page in the window.", ActionGroup.View, "Ctrl+1"),
        new(ActionId.Settings, "How things look…", "Changes the theme and how big everything is.",
            ActionGroup.View),
        new(ActionId.NextRegion, "Move to the next part of the window",
            "Moves between the page, the panel, the toolbar and the menus.", ActionGroup.View, "F6"),
        new(ActionId.PreviousRegion, "Move to the previous part of the window",
            "Moves the other way round the window.", ActionGroup.View, "Shift+F6"),
        new(ActionId.ToggleActionPanel, "Show what I can do",
            "Shows or hides the panel of things you can do to what you have chosen.", ActionGroup.View),
        new(ActionId.ShowFontChanges, "Show where fonts were changed",
            "Underlines, on screen only, any writing whose font was changed by hand.",
            ActionGroup.View),

        // ---- The address book (M12) ---------------------------------------------------------------
        new(ActionId.ShowPeople, "People…", "Opens your lodge address book.",
            ActionGroup.People, "Ctrl+Shift+R", IsPrimary: true),
        new(ActionId.ImportPeople, "Import from a file…",
            "Reads a list of members from a spreadsheet you already have.", ActionGroup.People),
        new(ActionId.ExportPeople, "Save as a spreadsheet…",
            "Writes your address book out so you can open it in Excel.", ActionGroup.People),
        new(ActionId.UndoPeopleChange, "Undo the last change",
            "Takes back the last change to your address book.", ActionGroup.People),
        new(ActionId.RestorePeople, "Restore an earlier version…",
            "Puts your address book back as it was on an earlier day.", ActionGroup.People),

        // ---- Help -------------------------------------------------------------------------------
        new(ActionId.CheckForUpdates, "Check for an update", "Asks whether a newer TrestleBoard exists.",
            ActionGroup.Help),
        new(ActionId.About, "About TrestleBoard", "Shows which version this is.", ActionGroup.Help),
        new(ActionId.FontLicences, "Fonts and licences",
            "Lists the typefaces that came with TrestleBoard and the licence each one is used under.",
            ActionGroup.Help),
        new(ActionId.ShowExampleIssue, "Show me an example newsletter",
            "Opens a finished five-page newsletter you can look around in and change without saving.",
            ActionGroup.Help),
    ];

    private static readonly Dictionary<string, EditorAction> ById =
        AllActions.ToDictionary(a => a.Id, StringComparer.Ordinal);

    /// <summary>Every action, in declaration order.</summary>
    public static IReadOnlyList<EditorAction> All => AllActions;

    /// <summary>The declaration for one id. Throws for an unknown id — that is a programming error.</summary>
    public static EditorAction Get(string actionId) => ById[actionId];

    public static bool TryGet(string actionId, out EditorAction action) =>
        ById.TryGetValue(actionId, out action!);

    /// <summary>
    /// What this action is called RIGHT NOW (PLAN.md §11 M18). Almost every action has one name; two
    /// picture commands do not, because the same command means two different things depending on
    /// whether the frame already holds a photograph, and "Swap this picture…" offered over an empty
    /// grey rectangle would be describing something that is not there.
    ///
    /// <para>The catalog's own <see cref="EditorAction.Title"/> stays the empty-frame wording, so a
    /// surface that has not been taught about this — a menu header written in XAML — still reads
    /// correctly rather than reading wrongly.</para>
    /// </summary>
    public static string TitleFor(string actionId, ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return actionId switch
        {
            ActionId.ReplacePicture when !context.SelectedPictureIsEmpty => "Swap this picture…",
            ActionId.CaptionPicture when context.SelectedPictureHasCaption => "Change the caption…",
            _ => Get(actionId).Title,
        };
    }

    /// <summary>
    /// Can the user do this right now, and if not, why not? The one decision point; everything the
    /// user sees about availability comes from here.
    /// </summary>
    public static ActionAvailability Evaluate(string actionId, ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return actionId switch
        {
            // Always available: these are how you get a newsletter in the first place. The address
            // book's own two doors are here too — it is app state, so it does not need a newsletter
            // open, and an empty book is exactly when importing matters most.
            ActionId.Open or ActionId.OpenSample or ActionId.NewFromTemplate or ActionId.Exit
                or ActionId.Settings or ActionId.NextRegion or ActionId.PreviousRegion
                or ActionId.ToggleActionPanel or ActionId.CheckForUpdates or ActionId.About
                or ActionId.FontLicences or ActionId.ShowExampleIssue
                or ActionId.ShowPeople or ActionId.ImportPeople =>
                ActionAvailability.Available,

            // ---- The address book (M12) -----------------------------------------------------------
            ActionId.ExportPeople => context.RosterCount > 0
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "Your address book is empty, so there is nothing to save yet. Import a list, or "
                    + "add somebody in the People window.",
                    ActionId.ImportPeople),
            ActionId.UndoPeopleChange => context.RosterCanUndo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "Nothing has changed in your address book since TrestleBoard was opened."),
            ActionId.RestorePeople => context.RosterHasEarlierVersions
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "There are no earlier versions of your address book yet. TrestleBoard keeps one "
                    + "every time you change it."),

            ActionId.StartFromLastMonth => context.CanStartFromLastMonth
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "There is no newsletter open to carry forward. Open last month's newsletter first.",
                    ActionId.Open),

            ActionId.ExportPdf => RequiresDocument(context),

            // ---- Edit ---------------------------------------------------------------------------
            ActionId.Undo => context.CanUndo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked("There is nothing to take back yet."),
            ActionId.Redo => context.CanRedo
                ? ActionAvailability.Available
                : ActionAvailability.Blocked(
                    "There is nothing to do again. This becomes possible after you undo something.",
                    ActionId.Undo),
            ActionId.Cut or ActionId.Copy => !context.IsEditingText
                ? ActionAvailability.NotApplicable(NeedsText)
                : context.HasTextSelection
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "No words are highlighted. Drag across some words first, or press Ctrl+A to take them all.",
                        ActionId.SelectAll),
            // M18: paste stopped being only about words. Outside a piece of writing it puts a
            // picture from the clipboard on the page, so it needs a newsletter rather than a caret.
            // What is actually on the clipboard is the shell's to read — asking here would mean
            // reading the clipboard on every refresh — and it says so out loud when there is
            // nothing to paste.
            ActionId.Paste => context.IsEditingText || context.HasDocument
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),
            ActionId.SelectAll => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

            // M21. Neither needs a caret: looking for words is something you do TO the newsletter,
            // and requiring the user to click into a frame first would mean requiring them to guess
            // which frame the words are in — the exact thing they opened this to find out.
            ActionId.Find or ActionId.Replace => RequiresDocument(context),

            // ---- Text ---------------------------------------------------------------------------
            ActionId.Bold or ActionId.Italic or ActionId.ParagraphStyle => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

            // ---- Fonts and sizes (M14) ------------------------------------------------------------
            // Style-first: these three act on the kind of writing the caret is in, so they need a
            // caret but not a highlight. The font picker itself only needs a newsletter, because it
            // is a whole-document sheet the user can open to look at.
            ActionId.FontsAndStyles => RequiresDocument(context),
            ActionId.BiggerText or ActionId.SmallerText or ActionId.FontJustHere =>
                context.IsEditingText
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsText),
            ActionId.ClearFontOverride => !context.IsEditingText
                ? ActionAvailability.NotApplicable(NeedsText)
                : context.SelectionUsesFontOverride
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(
                        "This writing already uses the font its kind of writing normally uses."),
            ActionId.ShowFontChanges => RequiresDocument(context),

            // ---- Putting things on the page -----------------------------------------------------
            ActionId.AddTextFrame or ActionId.InsertPhoto or ActionId.InsertOfficers
                or ActionId.InsertBirthdays or ActionId.InsertCommittees
                or ActionId.InsertDistrictCalendar or ActionId.InsertEventCard
                or ActionId.InsertCoverBanner => RequiresDocument(context),

            // ---- The selected thing -------------------------------------------------------------
            ActionId.DeleteFrame => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),

            ActionId.EditWidget => EvaluateWidget(context, ActionAvailability.Available),
            ActionId.EditWidgetList => EvaluateWidget(
                context,
                context.WidgetHasListEditor
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable("There is no list in this item to edit.")),
            ActionId.FitToContents => context.Selection == SelectionKind.Widget
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsListItem),

            // Reachable two ways (M13): with the list selected, and — because that is where the
            // user is actually standing when they are told the list has gone stale — from the
            // "what's next" card with nothing selected at all. The shell finds the stale list.
            ActionId.SyncBirthdays =>
                context.WidgetTypeId == BirthdayListTypeId
                    ? EvaluateWidget(context, EvaluateBirthdaySync(context))
                    : context.BirthdayListIsStale
                        ? EvaluateBirthdaySync(context)
                        : ActionAvailability.NotApplicable(NeedsBirthdayList),

            // M19, reachable the same two ways and for the same reason.
            ActionId.SyncOfficers =>
                context.WidgetTypeId == OfficersTableTypeId
                    ? EvaluateWidget(context, EvaluateOfficersSync(context))
                    : context.OfficersTableIsStale
                        ? EvaluateOfficersSync(context)
                        : ActionAvailability.NotApplicable(NeedsOfficersTable),

            // ---- Pictures -------------------------------------------------------------------------
            // Putting one in and describing one are the two things an EMPTY frame can still do;
            // everything else needs a picture to work on, and says so by name rather than greying.
            // Replace is reachable two ways, like M13's birthday sync: with a picture frame chosen,
            // and — because that is where the user is standing when the "what's next" card tells
            // them the photo pages are still empty — with nothing chosen at all. The shell finds the
            // first empty frame in that case.
            ActionId.ReplacePicture =>
                context.Selection == SelectionKind.Photo
                    ? ActionAvailability.Available
                    : context.HasPicturePlaceholder && !context.HasFrameSelection && !context.IsEditingText
                        ? ActionAvailability.Available
                        : ActionAvailability.NotApplicable(NeedsPicture),
            ActionId.DescribePicture =>
                context.Selection == SelectionKind.Photo
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsPicture),
            ActionId.FixPhoto or ActionId.AdjustPhoto or ActionId.TrimPicture
                or ActionId.CaptionPicture => context.Selection != SelectionKind.Photo
                ? ActionAvailability.NotApplicable(NeedsPicture)
                : context.SelectedPictureIsEmpty
                    ? ActionAvailability.Blocked(EmptyPictureFrame, ActionId.ReplacePicture)
                    : ActionAvailability.Available,

            // ---- How text flows -------------------------------------------------------------------
            ActionId.ToggleWrap => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),
            ActionId.LinkFrames => context.SelectionIsTextFrame
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsTextFrame),
            ActionId.UnlinkFrames => !context.SelectionIsTextFrame
                ? ActionAvailability.NotApplicable(NeedsTextFrame)
                : context.SelectionIsLinked
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked(
                        "This frame does not continue into another one yet.", ActionId.LinkFrames),
            ActionId.AutoFlow => !context.SelectionIsTextFrame
                ? ActionAvailability.NotApplicable(NeedsTextFrame)
                : context.CanAutoFlow
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("All of this writing already fits, so there is nothing to move."),

            // ---- Arranging ------------------------------------------------------------------------
            ActionId.BringForward or ActionId.SendBackward
                or ActionId.BringToFront or ActionId.SendToBack => context.HasFrameSelection
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(ChooseSomething),

            // M21. Lining things up is meaningless with one thing, so with one thing chosen these
            // are ABSENT from the panel rather than greyed in it — the M11 rule, which is also why
            // a single selection's panel looks exactly as it did before this milestone.
            ActionId.AlignLeft or ActionId.AlignCentres or ActionId.AlignRight
                or ActionId.AlignTop or ActionId.AlignMiddles or ActionId.AlignBottom =>
                context.SelectionCount >= 2
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsTwoThings),
            ActionId.DistributeHorizontally or ActionId.DistributeVertically =>
                context.SelectionCount >= 3
                    ? ActionAvailability.Available
                    : ActionAvailability.NotApplicable(NeedsThreeThings),

            // ---- Pages ----------------------------------------------------------------------------
            ActionId.NextPage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex < context.PageCount - 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This is the last page."),
            ActionId.PreviousPage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex > 0
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This is the first page."),
            ActionId.AddPage => RequiresDocument(context),
            ActionId.RemovePage => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageCount > 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("A newsletter needs at least one page."),
            ActionId.MovePageEarlier => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex > 0
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This page is already first."),
            ActionId.MovePageLater => !context.HasDocument
                ? RequiresDocument(context)
                : context.PageIndex < context.PageCount - 1
                    ? ActionAvailability.Available
                    : ActionAvailability.Blocked("This page is already last."),

            // ---- Looking at it --------------------------------------------------------------------
            ActionId.ZoomIn or ActionId.ZoomOut or ActionId.ActualSize or ActionId.FitPage =>
                RequiresDocument(context),

            _ => throw new ArgumentOutOfRangeException(
                nameof(actionId), actionId, "No availability rule is written for this action."),
        };
    }

    /// <summary>
    /// What the panel shows for the current selection: the actions that are about this thing, with
    /// the ones that do not apply left out entirely rather than greyed (PLAN.md §6).
    /// </summary>
    public static IReadOnlyList<ActionOffer> ForSelection(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var offers = new List<ActionOffer>();
        foreach (ActionGroup group in PanelGroups(context))
        {
            foreach (EditorAction action in AllActions.Where(a => a.Group == group))
            {
                ActionAvailability availability = Evaluate(action.Id, context);
                if (availability.Kind != ActionAvailabilityKind.NotApplicable)
                {
                    offers.Add(new ActionOffer(action, availability));
                }
            }
        }

        return offers;
    }

    /// <summary>The panel's heading, and the sentence a screen reader hears when the selection changes.</summary>
    public static string DescribeSelection(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // M21: more than one thing chosen is its own answer. Naming the kind of the first one
        // ("A photo is selected") while three things are highlighted would be a lie about two of
        // them, and the panel heading is a live region a screen reader reads out.
        if (context.SelectionCount > 1)
        {
            return $"{context.SelectionCount} things are selected";
        }

        return context.Selection switch
        {
            SelectionKind.Text => "You are typing",
            SelectionKind.TextFrame => "A text frame is selected",
            SelectionKind.Photo => "A photo is selected",
            SelectionKind.Widget => $"{context.WidgetDisplayName ?? "An item"} is selected",
            SelectionKind.Shape => "A shape is selected",
            _ => context.HasDocument ? "Nothing is selected" : "No newsletter is open",
        };
    }

    /// <summary>
    /// A sentence under the panel's heading saying how this thing is edited, or null when the
    /// heading already says everything there is to say (PLAN.md §11 M17).
    ///
    /// <para>It exists for the widgets. Their text is drawn from a payload rather than from a
    /// story, so a click inside one lands on no paragraph at all and nothing happens — correct by
    /// the M7 design, and indistinguishable from a broken program. The panel is where the user is
    /// already looking after a click, so it is where the answer goes.</para>
    /// </summary>
    public static string? DescribeSelectionHint(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // M21: with several things chosen, the panel says what having chosen them is FOR, and how
        // to change what is in the set. Shift+click is the only way into this state and nothing
        // else in the app teaches it.
        if (context.SelectionCount > 1)
        {
            return "Hold Shift and click to add another one, or to take one out. "
                + "The lining-up commands below act on all of them at once.";
        }

        // M18: an empty picture frame has the same problem the widgets have — clicking it does
        // something invisible — with the extra sting that the photo template ships three of them and
        // nothing in the app could fill one until M18.
        if (context.Selection == SelectionKind.Photo)
        {
            return context.SelectedPictureIsEmpty
                ? "There is no picture in this frame yet. Use 'Put a picture here…' — "
                  + "or just double-click it on the page."
                : null;
        }

        if (context.Selection != SelectionKind.Widget)
        {
            return null;
        }

        if (!context.CanEditWidget)
        {
            return null;
        }

        // M19: the owner's complaint was that the widgets are not driven by the address book. Half
        // of that was false, and the half that was true was that nothing ANYWHERE said so. A list
        // that came out of the address book now says it, on the panel, in the widget editor, and in
        // the "what's next" card.
        string hint = "This is a filled-in list. Use 'Change what this says…' to edit it — "
            + "or just double-click it on the page.";
        return context.SelectionFilledInFromRoster is { } filledIn
            ? DescribeFilledIn(filledIn) + " " + hint
            : hint;
    }

    /// <summary>
    /// "Filled in from your address book on 14 July 2026." — or without the date when the stamp is
    /// missing or unreadable, because a sentence with an empty gap in it reads worse than no date.
    /// One place, so the panel and the widget editor say the same words (M19).
    /// </summary>
    public static string DescribeFilledIn(string? whenText) =>
        string.IsNullOrWhiteSpace(whenText)
            ? "Filled in from your address book."
            : $"Filled in from your address book on {whenText}.";

    /// <summary>
    /// Which groups the panel shows for this selection. Ordered: the thing you most likely want
    /// first. With nothing selected the panel shows the "what's next" card and the ways of putting
    /// something new on the page.
    /// </summary>
    private static ActionGroup[] PanelGroups(ActionContext context) => context.Selection switch
    {
        SelectionKind.Text => [ActionGroup.Text, ActionGroup.Edit],
        SelectionKind.TextFrame => [ActionGroup.TextFlow, ActionGroup.Item, ActionGroup.Arrange],
        SelectionKind.Photo =>
            [ActionGroup.Picture, ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        SelectionKind.Widget => [ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        SelectionKind.Shape => [ActionGroup.Item, ActionGroup.TextFlow, ActionGroup.Arrange],
        _ => context.HasDocument ? [ActionGroup.Insert] : [],
    };

    /// <summary>The heading each group gets in the panel — plain language, never a class name.</summary>
    public static string DescribeGroup(ActionGroup group) => group switch
    {
        ActionGroup.Newsletter => "This newsletter",
        ActionGroup.Edit => "Editing",
        ActionGroup.Text => "The words",
        ActionGroup.Insert => "Add to the page",
        ActionGroup.Item => "This item",
        ActionGroup.Picture => "This picture",
        ActionGroup.TextFlow => "How text flows",
        ActionGroup.Arrange => "Front and back",
        ActionGroup.Page => "Pages",
        ActionGroup.View => "Looking at it",
        ActionGroup.People => "Your address book",
        _ => "Help",
    };

    /// <summary>
    /// Whether the address book has anything to contribute to this month (M13). Both refusals name
    /// the door out: an empty book wants importing, and a book with nobody born this month is not
    /// broken — it is simply a quiet month, and saying so is better than a grey button.
    /// </summary>
    private static ActionAvailability EvaluateBirthdaySync(ActionContext context)
    {
        if (context.RosterCount == 0)
        {
            return ActionAvailability.Blocked(
                "Your address book is empty, so there are no birthdays to bring in. Import your "
                + "member list first, or type the birthdays in yourself.",
                ActionId.ImportPeople);
        }

        if (context.RosterBirthdaysThisMonth == 0)
        {
            return ActionAvailability.Blocked(
                "Nobody in your address book has a birthday in this issue's month. You can still "
                + "type a birthday in yourself, or add the missing dates in the People window.",
                ActionId.ShowPeople);
        }

        return ActionAvailability.Available;
    }

    /// <summary>
    /// Whether the address book has anything to say about who holds office (M19). Both refusals name
    /// the door out, exactly as the birthday rule does: an empty book wants importing, and a book
    /// where nobody's office field is filled in is not broken — it is simply a book nobody has
    /// recorded offices in, and saying so is better than a grey button.
    /// </summary>
    private static ActionAvailability EvaluateOfficersSync(ActionContext context)
    {
        if (context.RosterCount == 0)
        {
            return ActionAvailability.Blocked(
                "Your address book is empty, so there are no officers to fill in. Import your member "
                + "list first, or type the officers in yourself.",
                ActionId.ImportPeople);
        }

        if (context.RosterOfficesFilledIn == 0)
        {
            return ActionAvailability.Blocked(
                "Nobody in your address book has an office written against his name, so there is "
                + "nothing to fill in. Open the People window and write in who holds each office.",
                ActionId.ShowPeople);
        }

        return ActionAvailability.Available;
    }

    private static ActionAvailability RequiresDocument(ActionContext context) =>
        context.HasDocument
            ? ActionAvailability.Available
            : ActionAvailability.Blocked(NoNewsletter, ActionId.NewFromTemplate);

    /// <summary>
    /// The widget rules all share one shape: is a widget selected at all, was it made by a newer
    /// TrestleBoard, and only then the action's own question.
    /// </summary>
    private static ActionAvailability EvaluateWidget(ActionContext context, ActionAvailability whenEditable)
    {
        if (context.Selection != SelectionKind.Widget)
        {
            return ActionAvailability.NotApplicable(NeedsListItem);
        }

        return context.CanEditWidget
            ? whenEditable
            : ActionAvailability.Blocked(
                "This item was made by a newer TrestleBoard than this one, so its questions are not "
                + "known here. You can still move, resize or delete it.",
                ActionId.CheckForUpdates);
    }
}
