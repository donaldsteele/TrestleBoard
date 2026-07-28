using Avalonia.Controls;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.App.Actions;

/// <summary>
/// The other half of M11's split: <see cref="ActionCatalog"/> owns "can I, and if not, why not",
/// and this owns "how" — one map from an action id to the code that performs it (PLAN.md §11 M11).
///
/// Everything the user can press goes through <see cref="RunAsync"/>: the menu bar, the panel, the
/// right-click flyout and the keyboard table. That is what makes explained refusal a property of the
/// app rather than of each surface — an action that cannot run says why, once, here.
/// </summary>
internal sealed class ActionRunner
{
    /// <summary>Which widget each Insert action puts on the page.</summary>
    private static readonly (string ActionId, string WidgetTypeId)[] WidgetInserts =
    [
        (ActionId.InsertOfficers, "officersTable"),
        (ActionId.InsertBirthdays, "birthdayList"),
        (ActionId.InsertCommittees, "committeeList"),
        (ActionId.InsertDistrictCalendar, "districtCalendar"),
        (ActionId.InsertEventCard, "eventCard"),
        (ActionId.InsertCoverBanner, "coverBanner"),
    ];

    private readonly MainWindow _window;
    private readonly Dictionary<string, Func<Control?, Task>> _handlers;

    internal ActionRunner(MainWindow window)
    {
        _window = window;
        _handlers = new Dictionary<string, Func<Control?, Task>>(StringComparer.Ordinal)
        {
            // ---- The newsletter itself ----------------------------------------------------------
            [ActionId.Open] = _ => window.OpenNewsletterAsync(),
            [ActionId.OpenSample] = Sync(window.OpenSample),
            [ActionId.NewFromTemplate] = _ => window.NewFromTemplateAsync(),
            [ActionId.StartFromLastMonth] = Sync(() => window.StartFromLastMonth()),
            [ActionId.ExportPdf] = _ => window.ExportPdfAsync(),
            [ActionId.Exit] = Sync(window.Close),

            // ---- Edit -----------------------------------------------------------------------------
            [ActionId.Undo] = Sync(window.Undo),
            [ActionId.Redo] = Sync(window.Redo),
            [ActionId.Cut] = _ => window.CutAsync(),
            [ActionId.Copy] = _ => window.CopyAsync(),
            [ActionId.Paste] = _ => window.PasteAsync(),
            [ActionId.SelectAll] = Sync(window.SelectAllText),

            // ---- Text -----------------------------------------------------------------------------
            [ActionId.Bold] = Sync(window.ToggleBold),
            [ActionId.Italic] = Sync(window.ToggleItalic),
            [ActionId.ParagraphStyle] = source => { window.ShowParagraphStyles(source); return Task.CompletedTask; },

            // ---- Fonts and sizes (M14) --------------------------------------------------------------
            [ActionId.FontsAndStyles] = _ => window.ShowTextStylesAsync(),
            [ActionId.BiggerText] = Sync(() => window.StepTextSize(+1)),
            [ActionId.SmallerText] = Sync(() => window.StepTextSize(-1)),
            [ActionId.FontJustHere] = _ => window.UseFontJustHereAsync(),
            [ActionId.ClearFontOverride] = Sync(window.ClearFontOverrideHere),

            // ---- Putting things on the page --------------------------------------------------------
            [ActionId.AddTextFrame] = Sync(window.AddTextFrame),
            [ActionId.InsertPhoto] = _ => window.InsertPhotoAsync(),

            // ---- The selected thing ----------------------------------------------------------------
            [ActionId.DeleteFrame] = Sync(window.DeleteSelectedFrame),
            [ActionId.EditWidget] = _ => window.EditWidgetAsync(grid: false),
            [ActionId.EditWidgetList] = _ => window.EditWidgetAsync(grid: true),
            [ActionId.FitToContents] = Sync(window.FitWidgetToContents),
            [ActionId.SyncBirthdays] = _ => window.SyncBirthdaysAsync(),

            // ---- Pictures --------------------------------------------------------------------------
            [ActionId.FixPhoto] = Sync(window.FixPhoto),
            [ActionId.AdjustPhoto] = _ => window.AdjustPhotoAsync(),
            [ActionId.ReplacePicture] = _ => window.ReplacePictureAsync(),
            [ActionId.TrimPicture] = _ => window.TrimPictureAsync(),
            [ActionId.CaptionPicture] = _ => window.CaptionPictureAsync(),
            [ActionId.DescribePicture] = _ => window.DescribePictureAsync(),

            // ---- How text flows --------------------------------------------------------------------
            [ActionId.ToggleWrap] = Sync(window.ToggleWrap),
            [ActionId.LinkFrames] = Sync(window.BeginFrameLink),
            [ActionId.UnlinkFrames] = Sync(window.UnlinkFrames),
            [ActionId.AutoFlow] = Sync(window.AutoFlow),

            // ---- Arranging -------------------------------------------------------------------------
            [ActionId.BringForward] = Sync(() => window.Restack(f => f.BringForward())),
            [ActionId.SendBackward] = Sync(() => window.Restack(f => f.SendBackward())),
            [ActionId.BringToFront] = Sync(() => window.Restack(f => f.BringToFront())),
            [ActionId.SendToBack] = Sync(() => window.Restack(f => f.SendToBack())),

            // ---- Pages -----------------------------------------------------------------------------
            [ActionId.NextPage] = Sync(() => window.GoToRelativePage(+1)),
            [ActionId.PreviousPage] = Sync(() => window.GoToRelativePage(-1)),
            [ActionId.AddPage] = Sync(window.AddPage),
            [ActionId.RemovePage] = Sync(window.RemovePage),
            [ActionId.MovePageEarlier] = Sync(() => window.MovePage(-1)),
            [ActionId.MovePageLater] = Sync(() => window.MovePage(+1)),

            // ---- Looking at it ---------------------------------------------------------------------
            [ActionId.ZoomIn] = Sync(() => window.StepZoom(+1)),
            [ActionId.ZoomOut] = Sync(() => window.StepZoom(-1)),
            [ActionId.ActualSize] = Sync(window.ZoomToActualSize),
            [ActionId.FitPage] = Sync(window.FitPage),
            [ActionId.Settings] = _ => window.ShowSettingsAsync(),
            [ActionId.NextRegion] = Sync(() => window.CycleRegion(forward: true)),
            [ActionId.PreviousRegion] = Sync(() => window.CycleRegion(forward: false)),
            [ActionId.ToggleActionPanel] = Sync(window.ToggleActionPanel),
            [ActionId.ShowFontChanges] = Sync(window.ToggleShowFontChanges),

            // ---- The address book (M12) --------------------------------------------------------------
            [ActionId.ShowPeople] = _ => window.ShowPeopleAsync(),
            [ActionId.ImportPeople] = _ => window.ImportPeopleAsync(),
            [ActionId.ExportPeople] = _ => window.ExportPeopleAsync(),
            [ActionId.UndoPeopleChange] = Sync(window.UndoPeopleChange),
            [ActionId.RestorePeople] = _ => window.RestorePeopleAsync(),

            // ---- Help ------------------------------------------------------------------------------
            [ActionId.CheckForUpdates] = _ => window.CheckForUpdatesForTest(userAsked: true),
            [ActionId.About] = _ => window.ShowAboutAsync(),
            [ActionId.FontLicences] = _ => window.ShowFontLicencesAsync(),
            [ActionId.ShowExampleIssue] = Sync(window.OpenIssueSample),
        };

        foreach ((string actionId, string widgetTypeId) in WidgetInserts)
        {
            string typeId = widgetTypeId;
            _handlers[actionId] = _ => window.InsertWidgetAsync(typeId);
        }
    }

    /// <summary>
    /// Set by the headless keyboard audit: records which action a key press reached and stops it
    /// short of running, so the test can press Ctrl+O without a file dialog opening on the runner.
    /// </summary>
    internal Func<string, bool>? InterceptorForTest { get; set; }

    /// <summary>The last action id this runner was asked for, whether or not it could run.</summary>
    internal string? LastActionForTest { get; private set; }

    /// <summary>Every action id in the catalog that this runner knows how to perform.</summary>
    internal IEnumerable<string> HandledIds => _handlers.Keys;

    internal bool Handles(string actionId) => _handlers.ContainsKey(actionId);

    /// <summary>
    /// Runs an action, or explains why it cannot run. The refusal goes to the status bar, which is
    /// already a polite live region, so a screen-reader user hears the reason rather than nothing
    /// (PLAN.md §6).
    /// </summary>
    /// <param name="actionId">One of the <see cref="ActionId"/> constants.</param>
    /// <param name="source">The control the user pressed, for actions that open a flyout beside it.</param>
    internal async Task RunAsync(string actionId, Control? source = null)
    {
        LastActionForTest = actionId;
        if (InterceptorForTest is { } intercept && intercept(actionId))
        {
            return;
        }

        ActionAvailability availability = ActionCatalog.Evaluate(actionId, _window.CurrentActionContext);
        if (!availability.IsAvailable)
        {
            _window.Announce(availability.Reason);
            return;
        }

        if (_handlers.TryGetValue(actionId, out Func<Control?, Task>? handler))
        {
            await handler(source);
        }

        _window.RefreshActions();
    }

    private static Func<Control?, Task> Sync(Action action) => _ =>
    {
        action();
        return Task.CompletedTask;
    };
}
