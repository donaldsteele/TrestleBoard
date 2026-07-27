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

    private const string NeedsText =
        "Click into some writing first, then this changes the words you highlight.";

    private const string NeedsTextFrame =
        "This is about a frame of writing. Choose one on the page first.";

    private const string NeedsListItem =
        "This is about the lists TrestleBoard fills in for you, like the officers table. "
        + "Choose one on the page first.";

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

        // ---- Text -------------------------------------------------------------------------------
        new(ActionId.Bold, "Bold", "Makes the highlighted words heavier.", ActionGroup.Text, "Ctrl+B",
            IsPrimary: true),
        new(ActionId.Italic, "Italic", "Slants the highlighted words.", ActionGroup.Text, "Ctrl+I",
            IsPrimary: true),
        new(ActionId.ParagraphStyle, "Paragraph style ▸",
            "Chooses what kind of paragraph this is, such as a heading.", ActionGroup.Text),

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

        // ---- Pictures ---------------------------------------------------------------------------
        new(ActionId.FixPhoto, "Fix this picture", "Crops it to the frame and brightens it in one step.",
            ActionGroup.Picture, "Ctrl+Shift+F", IsPrimary: true),
        new(ActionId.AdjustPhoto, "Adjust the picture…", "Opens the sliders for brightness, contrast and colour.",
            ActionGroup.Picture, "Ctrl+Shift+A"),

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
            ActionId.Paste or ActionId.SelectAll => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

            // ---- Text ---------------------------------------------------------------------------
            ActionId.Bold or ActionId.Italic or ActionId.ParagraphStyle => context.IsEditingText
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsText),

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

            // ---- Pictures -------------------------------------------------------------------------
            ActionId.FixPhoto or ActionId.AdjustPhoto => context.Selection == SelectionKind.Photo
                ? ActionAvailability.Available
                : ActionAvailability.NotApplicable(NeedsPicture),

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
