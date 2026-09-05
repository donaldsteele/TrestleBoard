using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Canvas;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Import;
using TrestleBoard.Emblems;
using TrestleBoard.PdfPages;
using TrestleBoard.App.Help;
using TrestleBoard.App.Settings;
using TrestleBoard.App.Startup;
using TrestleBoard.App.Theme;
using TrestleBoard.App.Updates;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Text;
using TrestleBoard.Core.Workflow;
using TrestleBoard.Editing;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Review;
using TrestleBoard.App.Integration;
using TrestleBoard.Spelling;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using TrestleBoard.Roster;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Builtins.CoverBanner;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.App;

/// <summary>
/// The editor shell: open a .tboard (or the built-in sample), click into a text frame, type with
/// full undo/redo, page through, zoom/fit, export the PDF. Every mouse action has a keyboard path
/// (PLAN.md §6).
///
/// From M11 every command in the app is declared once in <see cref="ActionCatalog"/> and performed
/// once in <see cref="ActionRunner"/>. The menu bar, the right-docked panel, the right-click flyout
/// and the keyboard table are four views of that one list, refreshed together by
/// <see cref="RefreshActions"/> — which is what replaced the thirty scattered <c>IsEnabled =</c>
/// assignments that used to grey controls out without ever saying why.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime is its Closed event, not IDisposable; the recovery service, "
        + "render source and font store are all released there.")]
public partial class MainWindow : Window
{
    private static readonly double[] ZoomSteps = [0.5, 0.65, 0.8, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0];

    /// <summary>
    /// How much window the page keeps for itself before a strip of chrome is allowed to stand
    /// beside it (M76 (g)).
    ///
    /// <para><b>Why a second number was needed, and it was a screenshot that said so.</b> Since M16
    /// the fold compared a CONSTANT — <c>ActionPanel.PanelWidth * 2.5</c>, or 900 — against the raw
    /// window width, which was exactly right while there was one strip and it never changed size.
    /// M76 broke both halves of
    /// that at once: the rail is a second strip, and both of them are inside
    /// <c>LayoutTransformControl</c>s that DOUBLE their width at the 200% UI scale §6 offers this
    /// audience. At 200% in a 1280 window the two together asked for 368 + 720 = 1088 pixels and
    /// the page was left about 190 — a sliver, in the one window this application exists to show.
    /// <c>scale-200.png</c> showed it; no test did, because every fold test resizes the window and
    /// none of them changes the scale.</para>
    ///
    /// <para>So the question is no longer "is the window wide enough" but "what would be left of the
    /// page", and the answer has to be asked in the same units the chrome is drawn in. This is the
    /// floor: below it the page is not a page, it is a column.</para>
    ///
    /// <para><b>540 is not a fresh opinion, it is the old number restated.</b> M16's 900 was the
    /// panel's 360 plus 540 for the page. Keeping that 540 means the panel's behaviour at 100% is
    /// exactly what it has been since M16, and §11.9 of <c>docs/accessibility-test-script.md</c>
    /// still describes what a tester sees. What changes is only that the sum is computed at the
    /// scale the chrome is actually drawn at, and that a second strip must fit in what is left.</para>
    /// </summary>
    private const double MinimumRoomForThePage = 540d;

    /// <summary>
    /// The app is the one place where "the newsletter must still open" outranks "fail loudly", so
    /// unlike the engine default this store substitutes rather than throwing when a document names
    /// a family this build does not bundle. The user is told once, at open, by DocumentFontAudit —
    /// never by a crash in the paint pass. Still never a system font (PLAN.md §1).
    /// </summary>
    private readonly FontStore _fonts = CreateAppFontStore();
    private readonly ActionPanel _panel = new();

    /// <summary>
    /// M76 (g): the row of miniature pages down the left. Built here rather than in the XAML because
    /// it has one tile per page of whatever newsletter is open — see <see cref="PageRail"/>.
    /// </summary>
    private readonly PageRail _rail;
    private readonly ActionRunner _actions;
    private TboardPackage? _package;
    private DocumentRenderSource? _source;
    private DocumentSession? _session;
    private TextEditorController? _editor;
    private FrameEditorController? _frames;
    private PhotoController? _photos;
    private WidgetController? _widgets;
    private PageFlowController? _pages;

    /// <summary>M93: how spaced out the writing is. Built beside the others, per newsletter.</summary>
    private WritingLookController? _writingLook;
    private RecoveryService? _recovery;
    private IRecoveryStore? _recoveryStore;
    private DispatcherTimer? _recoveryTimer;
    private string? _documentPathValue;

    /// <summary>M39: the .bak ring beside <see cref="DocumentPath"/> holds at least one copy.</summary>
    private bool _documentHasEarlierVersions;

    /// <summary>M48: an update check is in flight, so a second one waits rather than duplicating it.</summary>
    private bool _updateCheckRunning;

    /// <summary>
    /// The file this newsletter lives in, or null if it has never been saved.
    ///
    /// <para>A property rather than a field because M39 hung a second fact off it — whether the
    /// rotating <c>.bak</c> ring beside it holds anything — and there are seven places that set the
    /// path. Six of them would have been right and one would have been forgotten.</para>
    /// </summary>
    private string? DocumentPath
    {
        get => _documentPathValue;
        set
        {
            _documentPathValue = value;
            _documentHasEarlierVersions =
                value is { } path && FileRecoveryStore.FindBackups(path).Count > 0;
        }
    }
    private UpdateCoordinator? _updates;

    /// <summary>
    /// M77: the handler that stands between an unhandled exception and a window that vanishes.
    /// Held so that the shell can be asked, in a test, whether it caught anything.
    /// </summary>
    private Diagnostics.CrashGuard? _crashGuard;

    /// <summary>M77: whether the last crash managed to write the snapshot, for the card's promise.</summary>
    private bool _lastCrashKeptTheWork;
    private AppSettings _settings = AppSettings.Load();
    private RosterService? _roster;
    private readonly WidgetLayoutProvider _widgetProvider = WidgetLayoutProvider.CreateDefault();
    private ActionContext _context = ActionContext.Empty;
    private string? _announcement;

    private int _pageIndex;
    private bool _fitToWindow = true;
    private bool _exportedThisSession;

    /// <summary>
    /// M24: the newsletter has been edited since it was last written to <see cref="DocumentPath"/>.
    /// Set by the document session's own change event and cleared only by a successful save, so it
    /// tracks the file rather than the undo stack — undoing back to where you started still leaves
    /// the file out of date if something was written in between.
    /// </summary>
    private bool _unsavedChanges;

    /// <summary>
    /// M24: the close has already been through <see cref="OnWindowClosing"/> and been agreed to.
    /// Without this the second <see cref="Window.Close"/> would ask the same question again.
    /// </summary>
    private bool _closeAgreed;
    private int _regionIndex;
    private TextStylesWindow? _textStylesForTest;
    private Dialogs.RestoreDialog? _restoreDialogForTest;

    /// <summary>M21: one find controller per open newsletter, one window at a time over it.</summary>
    private FindController? _find;
    private FindWindow? _findWindow;

    private static FontStore CreateAppFontStore()
    {
        FontStore store = BundledFonts.CreateDefaultStore();
        store.SubstituteFamily = BundledFonts.BodyFamily;
        return store;
    }

    public MainWindow()
    {
        InitializeComponent();

        // M77: before anything else can throw. The size the window opens at is settled here, from
        // what the user left it at last time, and the screen check happens on Opened — by which
        // point Avalonia knows what screens there are.
        RestoreRememberedSize();
        DressToolbar();
        _actions = new ActionRunner(this);
        ActionPanelHost.Content = _panel;

        // M76 (g). The rail is handed a way to draw a page and a way to run a command, and nothing
        // else: it never reaches for the open document, because the document it would reach for is
        // replaced every time somebody opens a newsletter and this control outlives all of them.
        _rail = new PageRail(RenderPageThumbnail, RunActionFor);
        PageRailHost.Content = _rail;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        PageCanvas.ContextRequested += OnCanvasContextRequested;
        PageCanvas.PeerAskedForContextMenu += (_, _) => ShowContextActions();

        // M17: double-clicking a filled-in list opens its editor. The canvas reports the gesture;
        // the decision goes through the runner like every other command, so a widget this build
        // does not recognise refuses in plain language instead of opening a wrong dialog.
        PageCanvas.WidgetActivated += (_, _) =>
        {
            RefreshActions();
            _ = _actions.RunAsync(ActionId.EditWidget);
        };

        // M18: and double-clicking a picture frame fills it in, or swaps what is in it. Same route
        // through the runner, for the same reason.
        PageCanvas.PictureActivated += (_, _) =>
        {
            RefreshActions();
            _ = _actions.RunAsync(ActionId.ReplacePicture);
        };

        // M21: the two gestures every other publishing program has. Both are reported by the canvas
        // and answered by the shell, because both are really questions about the scroller.
        PageCanvas.ZoomAtPointerRequested += (_, request) =>
            ZoomAtPointer(request.Position, request.Direction);
        PageCanvas.PanRequested += (_, delta) => PanBy(delta);
        ApplySettings(_settings);
        RefreshActions();

        // M77: three doors, one room. Installed here rather than in Program so that the handler has
        // a window to hang a card off, and so that the two callbacks are the shell's own methods
        // rather than statics reaching back for it.
        _crashGuard = new Diagnostics.CrashGuard(KeepTheWorkAfterACrash, ShowTheProblemCard);
        if (!SuppressStartupForTest)
        {
            // Never in the headless tests: a process-wide exception handler installed by one test
            // outlives it and changes how every other test in the assembly fails. What the tests
            // drive is CrashGuard.Handle, which is where the behaviour worth proving lives.
            _crashGuard.Install();
        }

        // The start screen and the recovery offer are the app's front door; without this they exist
        // but nobody ever sees them.
        Opened += async (_, _) =>
        {
            // M77: the position, once there are screens to check it against. Runs even under the
            // test suppression, because where the window sits is not part of the startup flow the
            // tests are suppressing.
            RestoreRememberedPosition();

            if (SuppressStartupForTest)
            {
                return;
            }

            try
            {
                await RunStartupAsync();
            }
            catch (Exception ex)
            {
                // The startup flow reads the recovery directory, parses a snapshot and opens
                // whatever the command line named — all disk work, all before there is a newsletter
                // to lose. An exception here used to take the app down at launch, leaving the user
                // with a window that flashed and vanished and no way to find out why. Landing on
                // the start screen with a sentence is strictly better (review §14.2).
                Announce($"TrestleBoard could not finish starting up. ({ex.Message})");
            }
        };
        Closing += OnWindowClosing;
        Closed += (_, _) =>
        {
            // A clean close removes the recovery file, so a file surviving startup MEANS the app did
            // not close cleanly (docs/M9-spec.md §1.4).
            //
            // M24 removed a SaveNow() that stood here. It read as "any unsaved work is written
            // first", but Complete() deletes the snapshot SaveNow() had just written, under the same
            // id — so the pair wrote a file and immediately destroyed it, and the work survived only
            // when the app CRASHED. What makes deleting correct now is that nothing reaches this
            // point with unsaved work any more: OnWindowClosing has already offered to save it, and
            // the user either did, or said not to.
            // M77: where the window was, so it opens there next time. Before the teardown below
            // rather than after it: this is the cheap step and the one the user notices, and a
            // throw from any of the disposals would otherwise skip it silently.
            RememberWherePlaced();
            _findWindow?.Close();
            _rail.ForgetEveryThumbnail();
            _recoveryTimer?.Stop();
            _recovery?.Complete();
            _recovery?.Dispose();
            _source?.Dispose();
            _fonts.Dispose();

            // An update that was downloaded during the session installs itself now, with the work
            // already safely written above (docs/M10-spec.md §2).
            _updates?.ApplyIfReady();
        };
    }

    /// <summary>What the command line asked for; set by <see cref="App"/> on a real launch.</summary>
    public StartupOptions StartupOptions { get; init; } = StartupOptions.Empty;

    // ---- The action surface (PLAN.md §11 M11) -------------------------------------------------

    /// <summary>The snapshot every availability decision in the app is made against.</summary>
    internal ActionContext CurrentActionContext => _context;

    internal ActionRunner ActionsForTest => _actions;

    /// <summary>
    /// The toolbar, as a list. Extracted from <see cref="RefreshActions"/> at M27 so the wording
    /// test walks exactly the buttons the availability pass walks — a button reachable by one and
    /// not the other is how a surface drifts out of the catalog's reach unnoticed.
    /// </summary>
    internal Button[] ToolbarButtons =>
    [
        OpenButton, SaveButton, UndoButton, RedoButton, PrevPageButton, NextPageButton,
        ExportPdfButton,
    ];

    /// <summary>
    /// The canvas footer, as a list (M76(d)). These four were on the toolbar until this milestone
    /// and are the reason it did not fit the window's own default width; they act on the VIEW of
    /// the page rather than on the newsletter, so they now live under the canvas.
    ///
    /// <para>They are a SEPARATE list from <see cref="ToolbarButtons"/> rather than folded into it,
    /// because the name of that property is what tells the next reader where a button is. What the
    /// two lists must SHARE is every pass made over a button: availability, icons, wording and
    /// tooltips all walk them concatenated. That was not true when this list was introduced — the
    /// availability pass took the footer and the wording and tooltip tests did not, so three
    /// commands lost the guarantee that their label is the catalog's own word for them, which is
    /// exactly the drift the note over ToolbarButtons was written to prevent.</para>
    ///
    /// <para>The zoom-percentage button is not here: it carries no <c>ActionId</c> — it opens a
    /// chooser rather than performing a command — and everything these lists are for is keyed on
    /// one. It is never greyed, which is correct: pressing it always has something to say.</para>
    /// </summary>
    internal Button[] CanvasFooterButtons => [ZoomOutButton, ZoomInButton, FitButton];

    /// <summary>The zoom-percentage button and the label inside it, for M76's footer test.</summary>
    internal Button ZoomLadderButtonForTest => ZoomLadderButton;

    /// <summary>The scrolling part of the toolbar, so M76's overflow test can measure it.</summary>
    internal ScrollViewer ToolbarStripForTest => ToolbarStrip;

    /// <summary>The controls in the toolbar's scrolling strip, whose total width must fit it.</summary>
    internal Control ToolbarStackForTest => ToolbarStack;

    /// <summary>The canvas footer strip, so a test can prove the moved controls are really in it.</summary>
    internal Control CanvasFooterStackForTest => CanvasFooterStack;

    /// <summary>What the toolbar is saying about the save state, for the tests.</summary>
    internal string SaveStateTextForTest => SaveStateLabel.Text ?? string.Empty;

    internal ActionPanel PanelForTest => _panel;

    /// <summary>
    /// Says something in the status bar, which is a polite live region (PLAN.md §6) — this is where
    /// the reason an action could not run is spoken. It survives the next refresh and is cleared by
    /// the one after, so a refusal is readable but does not sit there for the rest of the session.
    /// </summary>
    internal void Announce(string message)
    {
        // M70(g): a document switch closes the windows that were showing the newsletter being
        // replaced, and every switch path ends with a sentence of its own — "Carried forward…",
        // "Your work is back…". The status bar holds one sentence at a time, so the reason a window
        // vanished is added to that sentence rather than written over by it a moment later.
        if (_switchNote is { Length: > 0 } note)
        {
            _switchNote = null;
            message = string.IsNullOrEmpty(message) ? note : message + " " + note;
        }

        _announcement = message;

        // M70: an identical string is not a property change, so Avalonia raises nothing and the
        // polite live region stays silent. Pressing a blocked command twice used to answer once.
        // Clearing first makes the second press a real change, and a screen reader hears it again.
        if (string.Equals(StatusLabel.Text, message, StringComparison.Ordinal))
        {
            StatusLabel.Text = string.Empty;
        }

        StatusLabel.Text = message;
    }

    /// <summary>
    /// M70(g): the sentence naming whatever a document switch has just closed, waiting for the next
    /// announcement to carry it. Null when the switch closed nothing.
    /// </summary>
    private string? _switchNote;

    /// <summary>
    /// For the document-switch paths that have no sentence of their own — opening a newsletter from
    /// a file, starting from a template. A window that shut itself still has to say why.
    /// </summary>
    private void SayWhatTheSwitchClosed()
    {
        if (_switchNote is not null)
        {
            Announce(string.Empty);
        }
    }

    /// <summary>
    /// Every menu item, toolbar button and panel control carries its action id in Tag and shares
    /// this one handler. Whether it can run, and what to say if it cannot, is decided in one place.
    /// </summary>
    private void OnActionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control)
        {
            RunActionFor(control);
        }
    }

    /// <summary>
    /// Runs whatever command a control's <c>Tag</c> names, and hands the runner the control itself
    /// so a command with a parameter can read it back off the thing the user pressed.
    ///
    /// <para>M76 (g) split this out of <see cref="OnActionClicked"/> because the page rail invokes
    /// from two places — a click and the Enter key on a tile — and both must arrive at the runner by
    /// exactly the same route. <see cref="ActionTarget.IdOf"/> is what lets a menu item's bare string
    /// and a tile's id-plus-page-number be answered by one line: see ActionTarget for why the page
    /// number rides beside the id rather than becoming an ActionId of its own.</para>
    /// </summary>
    internal void RunActionFor(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (ActionTarget.IdOf(control.Tag) is { } actionId)
        {
            _ = _actions.RunAsync(actionId, control);
        }
    }

    /// <summary>
    /// Takes one snapshot of the editing state and feeds every surface from it. This is the whole
    /// of M11's replacement for <c>UpdateEditChrome</c>/<c>UpdateFrameChrome</c>: the panel, the
    /// menus and the flyout cannot disagree, because they are all reading the same answer.
    /// </summary>
    internal void RefreshActions()
    {
        _context = BuildContext();

        // The menu bar keeps conventional greying — "dimmed" is a convention screen readers
        // announce — but every unavailable item now carries the plain-language reason in its
        // HelpText, and pressing its shortcut says the reason out loud (PLAN.md §6).
        foreach (MenuItem item in this.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (item is not { Tag: string actionId } || !ActionCatalog.TryGet(actionId, out _))
            {
                continue;
            }

            ActionAvailability availability = ActionCatalog.Evaluate(actionId, _context);
            item.IsEnabled = availability.IsAvailable;
            Avalonia.Automation.AutomationProperties.SetHelpText(item, availability.Reason);
        }

        // M76(d): the footer is walked with the toolbar, not after it. Zoom out, Zoom in and Fit
        // page changed which strip of chrome they live on and nothing else — they must still be
        // told when they cannot run, and still carry the catalog's reason where a screen reader
        // finds it. A second loop here would be a second thing to forget.
        foreach (Button button in ToolbarButtons.Concat(CanvasFooterButtons))
        {
            if (button.Tag is string actionId && ActionCatalog.TryGet(actionId, out _))
            {
                ActionAvailability availability = ActionCatalog.Evaluate(actionId, _context);
                button.IsEnabled = availability.IsAvailable;
                Avalonia.Automation.AutomationProperties.SetHelpText(button, availability.Reason);
            }
        }

        // M76 (g): the rail is fed from the same one snapshot as the menus, the toolbar and the
        // panel, so it cannot disagree with them about which page is showing or about why a page
        // cannot be moved. It is updated IN PLACE — see PageRail.Update — because this method runs
        // after every command and every selection change, and rebuilding the tiles each time would
        // take the focus out from under a keyboard user in the middle of walking them.
        _rail.Update(
            PageIdsForRail(),
            _pageIndex,
            ActionCatalog.Evaluate(ActionId.GoToPage, _context),
            ActionCatalog.Evaluate(ActionId.MovePageEarlier, _context),
            ActionCatalog.Evaluate(ActionId.MovePageLater, _context));

        // Plain-language labels straight from the command descriptions (PLAN.md §4).
        UndoMenuItem.Header = _context.CanUndo ? $"_Undo {_context.UndoDescription}" : "_Undo";
        RedoMenuItem.Header = _context.CanRedo ? $"_Redo {_context.RedoDescription}" : "_Redo";

        // The address book's own undo says what it will take back too — and stays a separate
        // sentence from the newsletter's, because Ctrl+Z never crosses that boundary (M12).
        UndoPeopleMenuItem.Header = _context.RosterCanUndo
            ? $"_Undo {_context.RosterUndoDescription}"
            : "_Undo the last change";

        // M18: the two picture commands whose name depends on what is in the frame. The catalog
        // owns both wordings — the menu must not invent a third.
        // M24: "Save this newsletter" grows an ellipsis when there is no file yet, because then it
        // asks where to put it. Same rule, same owner — the catalog. The mnemonic is on the "v"
        // because "Open the sample newsletter" already holds S in this menu.
        SaveMenuItem.Header = ActionCatalog
            .TitleFor(ActionId.Save, _context)
            .Replace("Save", "Sa_ve", StringComparison.Ordinal);

        ReplacePictureMenuItem.Header =
            "_" + ActionCatalog.TitleFor(ActionId.ReplacePicture, _context);
        CaptionPictureMenuItem.Header = ActionCatalog
            .TitleFor(ActionId.CaptionPicture, _context)
            .Replace("caption", "_caption", StringComparison.Ordinal);

        RebuildParagraphStyleMenu();

        // M70(e): the panel is cleared and built again from scratch here, so a keyboard or
        // screen-reader user who tabbed to an offer and pressed it has that button torn out from
        // under them a moment later and is left standing on nothing. Asked before the rebuild,
        // answered after it — and only when they were in the panel to begin with, because this runs
        // on every keystroke and must never pull focus out of the page.
        string? standingOn = FocusedPanelActionId();

        _panel.Update(
            _context,
            ActionCatalog.ForSelection(_context),
            WhatsNext.Suggestions(_context),
            (id, source) => _ = _actions.RunAsync(id, source));

        if (standingOn is not null)
        {
            if (PanelButtonFor(standingOn) is { } sameOffer)
            {
                sameOffer.Focus();
            }
            else
            {
                // The command is no longer offered — the selection it belonged to has gone. The
                // heading says what the panel is about now, which is the honest answer.
                _panel.FocusHeading();
            }
        }

        // M73(g): "How do I…?" is not modal, so the answer it is showing was drawn against a caret,
        // a selection and a newsletter that have all moved on since. This is where every other
        // surface in the app is re-asked whether a command can run; the help answer is one of them.
        _helpWindow?.Recheck();

        UpdatePageChrome();
        UpdateStatus();
    }

    private ActionContext BuildContext()
    {
        string? blockId = _frames?.SelectedBlockId;
        bool isWidget = blockId is not null && _widgets?.IsWidget(blockId) == true;
        string? widgetType = isWidget ? _widgets!.GetWidgetType(blockId) : null;

        string? displayName = null;
        if (widgetType is not null && _widgetProvider.Registry.TryGet(widgetType, out IWidgetDefinition? definition))
        {
            displayName = definition.DisplayName;
        }

        bool hasListEditor = isWidget
            && _widgets!.CanEdit(blockId)
            && CreateSession(blockId!)?.HasListSteps == true;

        return ActionContextFactory.Create(
            _session,
            _source,
            _editor,
            _frames,
            _photos,
            _widgets,
            _pages,
            _pageIndex,
            new ShellFacts(
                ExportedPdfThisSession: _exportedThisSession,

                // M67: asked once and remembered by the rasterizer, so this costs nothing per
                // refresh — and it is the only way the catalog can know that a native library did
                // not load without the Editing layer learning that native libraries exist.
                CanReadPdfs: PdfPageRasterizer.IsAvailable,
                SelectedWidgetHasListEditor: hasListEditor,
                SelectedWidgetDisplayName: displayName,
                CoverDateMissing: CoverHeadingNeedsADate(),
                RosterEmptyButNeeded: RosterEmptyButNeeded(),
                BirthdayListIsStale: BirthdayListNeedsUpdating(),
                BirthdayListIsEmpty: BirthdayListCouldBeFilledIn(),
                RosterBirthdaysThisMonth: _session is null
                    ? 0
                    : BirthdayRosterProjection.CountFor(
                        Roster.Book.Members, _session.Document.Metadata.IssueMonth),

                // M19: the same three facts for the officers table. Reading the page's widgets on
                // every refresh is what M13 already does for birthdays, and the cost is a dozen
                // JSON reads against a document that is at most six pages long.
                OfficersTableIsStale: OfficersTableNeedsUpdating(),
                RosterOfficesFilledIn: OfficersRosterProjection.CountFor(Roster.Book.Members),
                SelectionFilledInFromRoster: FilledInFromRoster(blockId),

                // Reading the book here means the first refresh loads it, which is near enough to
                // eager — and that is the right trade: "Save as a spreadsheet…" greyed with the
                // wrong reason until somebody happens to open the People window would be exactly
                // the silent wrongness M11 exists to remove. HasEarlierVersions is tracked rather
                // than re-scanned, so no refresh lists a directory.
                RosterCount: Roster.Book.Count,

                // M75 (f). RosterService has known this since M24 and nothing above it ever asked,
                // so an address book another program had locked was reported to the user as empty.
                RosterCouldNotBeRead: Roster.CouldNotBeRead,
                RosterCanUndo: Roster.CanUndo,
                RosterUndoDescription: Roster.UndoDescription,
                RosterHasEarlierVersions: Roster.HasEarlierVersions,

                // M24. The file name only, never the folder — it is printed in the status bar and
                // read out by the screen reader, and a full path is neither what the user needs nor
                // something PLAN.md §0 wants on screen when a screenshot is taken.
                HasUnsavedChanges: _unsavedChanges,
                DocumentFileName: DocumentPath is { } saved ? Path.GetFileName(saved) : null,

                // M39. Tracked, not re-scanned, for the same reason RosterHasEarlierVersions is:
                // RefreshActions runs on every keystroke, and listing a directory on each one to
                // answer a menu item's enabled state is a directory listing per keystroke.
                DocumentHasEarlierVersions: _documentHasEarlierVersions,
                PageHasFrames: FramesOnThisPage().Any()));
    }

    /// <summary>
    /// The one "what's next" source that needs to read widget data: a cover heading on the page with
    /// no meeting date typed into it. Read here rather than in Editing, which knows nothing about
    /// what is inside a widget's payload.
    /// </summary>
    private bool CoverHeadingNeedsADate() => DatelessCoverHeading() is not null;

    /// <summary>
    /// The cover heading with no meeting date in it, and the page it is on. M71: the fact and the
    /// thing the fact is about come from one walk of the document, so "Fill in the meeting date on
    /// the cover" opens the very heading that caused the card to say so.
    /// </summary>
    private (string BlockId, int PageIndex)? DatelessCoverHeading()
    {
        if (_session is null)
        {
            return null;
        }

        for (int i = 0; i < _session.Document.Pages.Count; i++)
        {
            foreach (Core.Model.Block block in _session.Document.Pages[i].Blocks)
            {
                if (block is not Core.Model.WidgetBlock { WidgetType: "coverBanner" } cover)
                {
                    continue;
                }

                if (cover.Data is not { } data
                    || !data.TryGetProperty("meetingDateText", out System.Text.Json.JsonElement date)
                    || string.IsNullOrWhiteSpace(date.GetString()))
                {
                    return (cover.Id, i);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Format → Paragraph style. This was a toolbar combo box and nothing else — the only command in
    /// the app with no menu path at all, which is a hole in PLAN.md §6's guarantee (M11).
    /// </summary>
    private void RebuildParagraphStyleMenu()
    {
        ParagraphStyleMenu.IsEnabled = _context.IsEditingText;
        Avalonia.Automation.AutomationProperties.SetHelpText(
            ParagraphStyleMenu, ActionCatalog.Evaluate(ActionId.ParagraphStyle, _context).Reason);

        var items = new List<MenuItem>();
        foreach (string style in _editor?.AvailableParagraphStyles ?? [])
        {
            string styleRef = style;

            // M27: what the user reads is the role's plain-language name — "Body text", not `body`;
            // "Tables", not `lodge-table`. StyleLabels exists precisely to keep raw style ids off
            // the screen and was not being called here, so this menu and the panel flyout under it
            // were the one place in the app that still showed them (review §14.3).
            string label = Core.Text.StyleLabels.Describe(styleRef);
            var item = new MenuItem { Header = label, FontSize = 16 };
            Avalonia.Automation.AutomationProperties.SetName(item, label);
            item.Click += (_, _) =>
            {
                _editor?.ApplyParagraphStyle(styleRef);
                RefreshActions();
            };
            items.Add(item);
        }

        ParagraphStyleMenu.ItemsSource = items;
    }

    /// <summary>
    /// Right-click, Shift+F10 and the Applications key all land here, built from the same catalog as
    /// the panel. Until M11 a screen-reader user pressing the Applications key over the canvas got
    /// nothing at all.
    /// </summary>
    private void OnCanvasContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ShowContextActions();
        e.Handled = true;
    }

    private void ShowContextActions()
    {
        IReadOnlyList<ActionOffer> offers = ActionCatalog.ForSelection(_context);
        if (offers.Count == 0)
        {
            Announce("Choose something on the page first, and its actions appear here.");
            return;
        }

        var flyout = new MenuFlyout();
        foreach (ActionOffer offer in offers)
        {
            // TitleFor, not Title. Three commands are named for the state they are in — the two
            // picture ones since M18 and Save since M24 — and the panel has always used the
            // context-aware name while this flyout used the static one. Right-clicking a frame that
            // already holds a photograph offered "Put a picture here…" (review §14.3).
            string title = ActionCatalog.TitleFor(offer.Action.Id, _context);
            var item = new MenuItem
            {
                Header = title,
                FontSize = 16,
                IsEnabled = offer.IsAvailable,
                Tag = offer.Action.Id,
            };
            Avalonia.Automation.AutomationProperties.SetName(item, title);
            Avalonia.Automation.AutomationProperties.SetHelpText(
                item, offer.IsAvailable ? offer.Action.ShortDescription : offer.Availability.Reason);
            item.Click += OnActionClicked;
            flyout.Items.Add(item);
        }

        flyout.ShowAt(PageCanvas, showAtPointer: true);
    }

    /// <summary>
    /// F6 walks the window's parts in a fixed order so a keyboard-only user can get from the page to
    /// the panel without tabbing through every block on the page (PLAN.md §11 M11).
    /// </summary>
    internal void CycleRegion(bool forward)
    {
        (string Name, Control Root)[] regions =
        [
            ("the page", PageCanvas),

            // M76 (g): the rail is a part of the window in its own right, so F6 has to stop at it.
            // It is listed beside the page because that is where it stands, and a region that
            // cannot take focus — the rail folded away on a narrow window — is stepped over by the
            // loop below rather than announced as somewhere the user has been sent (M70(c)).
            ("the pages down the side", PageRailHost),
            ("the panel of things you can do", ActionPanelHost),
            ("the toolbar", OpenButton),
            ("the menus", MenuScale),
        ];

        for (int step = 1; step <= regions.Length; step++)
        {
            int next = ((_regionIndex + (forward ? step : -step)) % regions.Length + regions.Length)
                % regions.Length;
            (string name, Control root) = regions[next];
            if (TryFocusRegion(root))
            {
                _regionIndex = next;
                Announce($"Moved to {name}.");
                return;
            }
        }

        // M70(c): every region refused focus — the panel folded away on a narrow window, no
        // newsletter open. F6 is a keyboard-only user's way around the window, so it has to answer
        // even when it cannot go anywhere.
        Announce("There is nowhere else to move to just now.");
    }

    private static bool TryFocusRegion(Control root)
    {
        if (root is { IsEffectivelyVisible: true, Focusable: true } && root.Focus())
        {
            return true;
        }

        foreach (Control candidate in root.GetVisualDescendants().OfType<Control>())
        {
            if (candidate is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true, Focusable: true }
                && candidate.Focus())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M74 (b): the sentence a toggle appends when the choice did not reach the disk. Toggling a
    /// preference is two operations — changing it now, and remembering it for next time — and the
    /// second used to fail in silence (docs/M73-spec.md recorded it as an open question). The M73
    /// (e) rule settles it: the announcement branches on what <see cref="AppSettings.Save"/>
    /// returned, exactly as <see cref="ShowSettingsAsync"/> does.
    /// </summary>
    private const string ButTheChoiceCouldNotBeRemembered =
        " But the choice could not be written down, so it will be back to how it was the next "
        + "time you open TrestleBoard.";

    /// <summary>
    /// M70(d): this said the panel was showing whether it was or not. On a narrow window
    /// <see cref="ApplyPanelVisibility"/> keeps the panel folded away however the setting is set,
    /// so the user was told a thing had happened that had not — a false report, which is worse than
    /// saying nothing. It now says what is actually on screen.
    /// </summary>
    internal void ToggleActionPanel()
    {
        _settings = _settings with { ShowActionPanel = !_settings.ShowActionPanel };
        bool remembered = _settings.Save();
        bool showing = ApplyPanelVisibility();
        Announce((showing
            ? "The panel of things you can do is showing."
            : _settings.ShowActionPanel
                ? "This window is too narrow for the panel, so it stays folded away. "
                    + "Make the window wider and it will come back."
                : "The panel is hidden. Bring it back from View, Show what I can do.")
            + (remembered ? "" : ButTheChoiceCouldNotBeRemembered));
    }

    /// <summary>
    /// Whether a strip <paramref name="stripWidth"/> wide at 100% can stand beside the page without
    /// squeezing it under <see cref="MinimumRoomForThePage"/>.
    ///
    /// <para><b>Derived, never written beside the thing it governs</b> — M16's rule, now covering
    /// the scale as well as the width. The strips live in <c>LayoutTransformControl</c>s, so one at
    /// 200% asks for twice the room, and a threshold that does not multiply is wrong in exactly the
    /// place PLAN.md §6 cares about most. A window that has not been laid out yet
    /// (<c>Bounds.Width</c> 0) counts as roomy, so nothing folds itself away before it is measured.
    /// </para>
    /// </summary>
    private bool RoomBeside(double stripWidth) =>
        Bounds.Width <= 0
        || Bounds.Width >= (stripWidth * _settings.UiScale) + MinimumRoomForThePage;

    /// <summary>
    /// The chrome budget (PLAN.md §11 M11): the panel folds itself away on a narrow window rather
    /// than leaving the page a strip down the middle.
    /// </summary>
    /// <returns>Whether the panel is now on screen — which is not the same as the setting.</returns>
    private bool ApplyPanelVisibility()
    {
        bool roomForIt = RoomBeside(ActionPanel.PanelWidth);
        bool showPanel = _settings.ShowActionPanel && roomForIt;
        PanelScale.IsVisible = showPanel;
        CollapsedPanelHost.IsVisible = !showPanel;
        ShowPanelButton.Content = _settings.ShowActionPanel && !roomForIt
            ? "▸"
            : "What can I do? ▸";
        return showPanel;
    }

    /// <summary>
    /// M76 (g): the rail's half of the same switch, worded the same way and for the same reason.
    /// M70(d)'s rule holds here too — the sentence says what is actually on screen, not what the
    /// setting says, because on a narrow window the setting and the screen disagree on purpose.
    /// </summary>
    internal void TogglePageRail()
    {
        _settings = _settings with { ShowPageRail = !_settings.ShowPageRail };
        bool remembered = _settings.Save();
        bool showing = ApplyRailVisibility();
        Announce((showing
            ? "The pages are showing down the left-hand side."
            : _settings.ShowPageRail
                ? "This window is too narrow for the row of pages, so it stays folded away. "
                    + "Make the window wider and it will come back."
                : "The row of pages is hidden. Bring it back from View, Show the pages down the "
                    + "side.")
            + (remembered ? "" : ButTheChoiceCouldNotBeRemembered));
    }

    /// <summary>
    /// The chrome budget again (PLAN.md §11 M11), from the other side of the window: the rail folds
    /// itself away on a narrow window rather than taking a second strip out of the page.
    ///
    /// <para><b>The rail asks for the room the panel has already taken, and it folds first.</b> The
    /// first version of this used the panel's own threshold on the grounds that "the reason either
    /// folds is the page in the middle, which does not care which side the room was taken from" —
    /// which is true of the page and false of the arithmetic. Two strips were each asked whether
    /// ONE of them would fit, both answered yes, and at 200% they took 1088 of a 1280 window
    /// between them. So the rail's question includes the panel: is there room for the panel, the
    /// rail AND a page.
    ///
    /// <para>When only one can stand, it is the panel that stands. The panel is where §6 puts the
    /// commands — "actions belong next to the object, not only in the menu bar" — while the rail is
    /// a faster route to a page that Previous and Next still reach. Neither disappears: each folds
    /// to a labelled button that brings it straight back.</para></para>
    /// </summary>
    /// <returns>Whether the rail is now on screen — which is not the same as the setting.</returns>
    private bool ApplyRailVisibility()
    {
        bool panelStanding = _settings.ShowActionPanel && RoomBeside(ActionPanel.PanelWidth);
        bool roomForIt = RoomBeside(
            panelStanding ? PageRail.RailWidth + ActionPanel.PanelWidth : PageRail.RailWidth);
        bool showRail = _settings.ShowPageRail && roomForIt;
        RailScale.IsVisible = showRail;
        CollapsedRailHost.IsVisible = !showRail;

        // The label stays whole at every width. The panel's own button shortens to a bare "▸" when
        // the window is too narrow for it, and this one deliberately does not follow it there: "no
        // icon-only controls, ever" (PLAN.md §6) and "Pages" is five characters, so there is nothing
        // to buy by dropping it.
        return showRail;
    }

    // ---- Autosave and recovery ----------------------------------------------------------------

    /// <summary>
    /// Wires autosave to the open document. One tick a second; the service decides whether a rule
    /// says it is time to write (docs/M9-spec.md §1.1).
    /// </summary>
    private void StartRecovery(TboardPackage package)
    {
        // The snapshot belongs to the newsletter being replaced, and by the time anything reaches
        // here that newsletter has been saved or deliberately discarded (M24) — every path that
        // swaps documents goes through ConfirmSaveFirstAsync first. Dropping it is therefore
        // correct. It used to be preceded by a SaveNow() whose file this very line deleted.
        _recovery?.Complete();
        _recovery?.Dispose();
        _recoveryStore ??= new FileRecoveryStore(AppPaths.RecoveryDirectory);

        _recovery = new RecoveryService(
            _session!,
            _recoveryStore,
            () => new RecoveryService.RecoveryPayload(SnapshotBytes(package), DocumentPath));

        _recoveryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recoveryTimer.Tick -= OnRecoveryTick;
        _recoveryTimer.Tick += OnRecoveryTick;
        _recoveryTimer.Start();
    }

    private void OnRecoveryTick(object? sender, EventArgs e)
    {
        try
        {
            _recovery?.Poll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // An unhandled exception on a timer tick takes the whole app down — the autosave feature
            // causing the very crash it exists to protect against. A skipped tick costs at most one
            // interval; the next one tries again.
            Announce(
                "Could not save a backup copy just now. TrestleBoard will keep trying. "
                + $"({ex.Message})");
        }
    }

    /// <summary>
    /// The whole document plus a page-1 thumbnail — "is this the work I lost?" is answered by
    /// looking, not by reading a filename (docs/M9-spec.md §1.3).
    /// </summary>
    private byte[] SnapshotBytes(TboardPackage package)
    {
        RefreshThumbnail(package);
        using var buffer = new MemoryStream();
        TboardContainer.Save(package, buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Puts a current page-1 thumbnail in the package. Shared by the crash snapshot and (from M24)
    /// by saving to the user's own file, so a saved `.tboard` carries the same picture the restore
    /// dialog and the file picker use to tell one newsletter from another (PLAN.md §2).
    /// </summary>
    private void RefreshThumbnail(TboardPackage package)
    {
        if (_source is not { PageCount: > 0 })
        {
            return;
        }

        try
        {
            package.Thumbnails["page-1.png"] = _source.RenderPageToPng(0, scale: 0.35f);
        }
        catch (InvalidOperationException)
        {
            // A thumbnail is a nicety; the document bytes are the point.
        }
    }

    /// <summary>
    /// The real startup path (PLAN.md §7, docs/M9-spec.md §1.5/§2): offer back anything that
    /// survived a crash, and otherwise ask what the user wants to do. Called once, from Opened.
    /// </summary>
    private async Task RunStartupAsync()
    {
        // M63: the tour comes before everything, because everything else here assumes the person
        // already knows what this app is for. It shows once and then never again, and skipping it
        // counts as having seen it.
        await ShowTheTourAsync(becauseTheyAsked: false);

        // Double-clicking a .tboard file is an instruction, not a suggestion: it skips both the
        // recovery offer and the start screen (docs/M10-spec.md §3).
        if (StartupOptions.DocumentPath is { } path && OpenDocumentFromPath(path))
        {
            StartUpdateCheck();
            return;
        }

        if (await OfferRecoveryAsync())
        {
            StartUpdateCheck();
            return;
        }

        StartUpdateCheck();

        var start = new StartDialog(canStartFromLastMonth: false, Templates.All());
        await start.ShowDialog(this);
        await ActOnWhatTheStartScreenSaidAsync(start);
    }

    /// <summary>
    /// Does the thing the start screen was pressed for — the ONE place that answer is acted on.
    ///
    /// <para><b>Why it is one place.</b> There were two switches over <c>StartChoice</c>, one for
    /// first run and one for File &gt; New, and M76 (h) added a choice to the window and to neither
    /// of them: pressing a recent newsletter closed the start screen and opened nothing, in silence,
    /// while the suite stayed green because the only test asked the dialog what it had decided
    /// rather than asking the shell what it had done. A second switch is a second thing to forget,
    /// so there is now one, and every route through it answers whether a newsletter actually came
    /// up.</para>
    /// </summary>
    /// <returns>
    /// M74 (f): false where no new newsletter came up — a window closed without a choice, or the
    /// chosen route itself coming to nothing.
    /// </returns>
    internal async Task<bool> ActOnWhatTheStartScreenSaidAsync(StartDialog start) =>
        start.Choice switch
        {
            StartChoice.MyTemplate when start.SelectedUserTemplateId is { } mine =>
                await OpenUserTemplateAsync(mine),
            StartChoice.Template => await OpenTemplateAsync(start.SelectedTemplateId),
            StartChoice.LastMonth => await CarryForwardToNextIssueAsync(),
            StartChoice.OpenFile => await OpenNewsletterAsync(),

            // M76 (h): the shortcut past the file dialog. Deliberately the ORDINARY open path and
            // not a second way of loading a newsletter — so a file that has been moved or damaged
            // since it was listed refuses in the M25 sentence, exactly as it would have done if the
            // user had walked the dialog to it.
            StartChoice.RecentFile when start.SelectedRecentPath is { } recent =>
                OpenDocumentFromPath(recent),

            // Closed without an answer, or a choice whose companion value is missing — which the
            // window does not offer, but a switch has to say something about.
            _ => false,
        };

    /// <summary>Offers back anything that survived a previous run (docs/M9-spec.md §1.5).</summary>
    internal async Task<bool> OfferRecoveryAsync()
    {
        _recoveryStore ??= new FileRecoveryStore(AppPaths.RecoveryDirectory);
        IReadOnlyList<RecoverySnapshot> survivors = _recoveryStore.FindRecoverable();
        if (survivors.Count == 0)
        {
            return false;
        }

        RecoverySnapshot snapshot = survivors[0];
        TboardPackage? package = null;
        try
        {
            using var buffer = new MemoryStream(snapshot.Bytes);
            package = TboardContainer.Load(buffer);
        }
        catch (Exception ex) when (ex is InvalidDataException or Core.Migrations.UnsupportedFormatException)
        {
            _recoveryStore.Delete(snapshot.Id);
            return false;
        }

        package.Thumbnails.TryGetValue("page-1.png", out byte[]? thumbnail);
        var dialog = new RestoreDialog(snapshot, thumbnail, DateTimeOffset.UtcNow);
        _restoreDialogForTest = dialog;
        await dialog.ShowDialog(this);

        // M73(b3), gate 27: three answers, three sentences, and the deleting one is only reached by
        // the user saying so. "Not restored" used to be enough to delete the snapshot, so the
        // title-bar X on a card that had just said "Nothing has been lost" lost it.
        switch (dialog.Choice)
        {
            case RestoreChoice.StartFresh:
                // M73(e): Delete was `void` over a swallowed IOException, so this sentence was said
                // whether or not the snapshot went. Being offered work back that you asked to be
                // rid of is harmless; being told it is gone when it is not is a promise about a
                // file that is still on the disk.
                Announce(_recoveryStore.Delete(snapshot.Id)
                    ? "You chose to start fresh, so the work TrestleBoard had kept was thrown away."
                    : "You chose to start fresh. TrestleBoard could not throw the work it had kept "
                      + "away, so it may offer it to you again next time it starts.");
                return false;

            case RestoreChoice.Closed:
                Announce(
                    "That window was closed without an answer, so your work is still kept safe. "
                    + "TrestleBoard will offer it to you again next time it starts.");
                return false;
        }

        DocumentPath = snapshot.OriginalPath;

        // Restored work is by definition work no file holds — that is why it was in the recovery
        // store. M24 makes the app agree: the title says "not saved yet", Ctrl+S is live, and the
        // sentence below finally points at a command that exists.
        ShowPackage(package, startsDirty: true);
        Announce("Your work is back. Press Ctrl+S to save it now.");
        return true;
    }

    // ---- The newsletter -----------------------------------------------------------------------

    /// <returns>
    /// M74 (f): false where nothing on screen was replaced — "Go back" to the save question, an
    /// empty picker (Cancel), or a file that would not load. Help's "Do it for me" says "Done."
    /// off this value, and a cancelled Open is not a done thing.
    /// </returns>
    internal async Task<bool> OpenNewsletterAsync()
    {
        if (await ConfirmSaveFirstAsync("and open another newsletter") == SaveFirst.Stay)
        {
            return false;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a newsletter",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("TrestleBoard newsletter") { Patterns = ["*.tboard"] },
            ],
        });
        if (files.Count == 0)
        {
            return false;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            // Load before committing the path: if the chosen file is damaged, the newsletter that
            // is already open must keep its own path — the same rule OpenDocumentFromPath follows,
            // and the same defect (M74 (d)2) if the order flips. Recovery offers to put the work
            // back where it came from, so the path is set once there is really something to put.
            TboardPackage package = TboardContainer.Load(buffer);
            DocumentPath = files[0].TryGetLocalPath();
            ShowPackage(package);
            SayWhatTheSwitchClosed();
            return true;
        }
        catch (Exception ex) when (ex is Core.Migrations.UnsupportedFormatException or System.IO.InvalidDataException)
        {
            await ShowErrorAsync("Could not open that file", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// M24: the same thing, for the one caller that can arrive while a newsletter is already open —
    /// a second `.tboard` dropped on the running app, or double-clicked in the file manager. It used
    /// to replace whatever was on screen without a word.
    /// </summary>
    internal async Task OpenDocumentFromPathAsync(string path)
    {
        if (await ConfirmSaveFirstAsync("and open that newsletter") == SaveFirst.Stay)
        {
            return;
        }

        OpenDocumentFromPath(path);
    }

    /// <summary>
    /// Opens a newsletter by path — the file association's landing point (docs/M10-spec.md §3).
    /// Returns false when the file is missing or is not a newsletter, having already told the user
    /// so in the status bar; the caller then falls through to the normal start flow rather than
    /// leaving them looking at an empty window.
    /// </summary>
    internal bool OpenDocumentFromPath(string path)
    {
        try
        {
            using var buffer = new MemoryStream(File.ReadAllBytes(path));
            TboardPackage package = TboardContainer.Load(buffer);

            // M74 (d): the path changes hands only once the file has proven readable. It used to be
            // set before the load and nulled on failure, so a damaged file left the still-open
            // newsletter with no path — Ctrl+S became a surprise Save-As, and the autosave
            // sidecar's OriginalPath went null, so crash recovery would have offered real work as
            // "never saved".
            DocumentPath = path;
            ShowPackage(package);
            SayWhatTheSwitchClosed();
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidDataException
            or Core.Migrations.UnsupportedFormatException)
        {
            // Everything about the currently open document — its path included — is exactly as it
            // was before this open was tried.
            Announce($"TrestleBoard could not open {Path.GetFileName(path)}. {ex.Message}");
            return false;
        }
    }

    /// <returns>
    /// M74 (f): false where no new newsletter came up — "Go back" to the save question, a start
    /// window closed without a choice, or the chosen route itself coming to nothing. Each route
    /// answers for itself rather than being assumed to have worked: a user template that has been
    /// moved since it was listed does not open, and that is not a done thing either.
    /// </returns>
    internal async Task<bool> NewFromTemplateAsync()
    {
        if (await ConfirmSaveFirstAsync("and start another newsletter") == SaveFirst.Stay)
        {
            return false;
        }

        var start = new StartDialog(canStartFromLastMonth: _package is not null, Templates.All());
        await start.ShowDialog(this);

        return await ActOnWhatTheStartScreenSaidAsync(start);
    }

    internal Task<bool> ExportPdfAsync() => ExportPdfAsync(draft: false);

    /// <summary>
    /// M53: "Make a draft copy" — the same renderer, the same bytes, plus a diagonal saying what it
    /// is. The review copy and the final copy differed only in the sender's memory before this.
    /// </summary>
    internal Task<bool> ExportDraftPdfAsync() => ExportPdfAsync(draft: true);

    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> ExportPdfAsync(bool draft)
    {
        if (_source is null || _package is null)
        {
            return false;
        }

        // M51. The offer, and only an offer: whatever the answer, the export goes ahead. The one
        // way this can stop an export is the user choosing to look it over, which is them changing
        // their mind, not the app refusing. PLAN.md's acceptance is explicit that "Make the PDF" is
        // never blocked. A draft is not offered the review — the whole point of a draft is that it
        // is going to somebody who will read it.
        if (!draft && await OfferTheReviewAsync())
        {
            return false;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        string stem = Integration.IssueNaming.FileStem(meta);
        string? path = ExportPathForTest;
        if (path is null && !ExportPickerHasNoLocalPathForTest)
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = draft ? "Save the draft copy" : "Export as PDF",
                DefaultExtension = "pdf",
                SuggestedFileName = draft ? $"{stem} DRAFT.pdf" : $"{stem}.pdf",
                FileTypeChoices = [new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] }],
            });
            if (file is null)
            {
                return false;
            }

            path = file.TryGetLocalPath();
        }

        // M74 (a): the PDF is written by path, temp-then-rename, because that is the only way a
        // failed export can leave last month's good PDF untouched. A picked place with no path on
        // this computer's disk — a cloud location a storage provider will not name — cannot be
        // written that way, so it is refused with the truth rather than written dangerously.
        if (path is null)
        {
            await ShowErrorAsync(
                draft ? "Could not make the draft copy" : "Could not export the PDF",
                "TrestleBoard cannot tell where that place is on this computer, so it cannot save "
                + "the PDF there safely. Nothing was written. Choose a folder on this computer and "
                + "try again.");
            return false;
        }

        try
        {
            AtomicFileWrite.Write(path, stream => DocumentPdfExporter.Export(
                stream,
                _source,
                new PdfMetadata(
                    Integration.IssueNaming.Title(meta),
                    meta.LodgeName,
                    Integration.IssueNaming.PdfSubject(meta)),
                draft ? WatermarkRenderer.DraftText : null));

            _exportedThisSession = true;

            // The file is complete and renamed into place before anything is offered: handing a
            // half-written file to a printer would be a worse bug than not offering to print at all.
            LastExportedPdf = path;
            RefreshActions();
            await OfferToPrintAsync(draft);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(
                draft ? "Could not make the draft copy" : "Could not export the PDF",
                "The PDF could not be saved. If a PDF with this name was already there, it has not "
                + "been touched. Make sure the file is not open in another program and try again. "
                + $"({ex.Message})");
            return false;
        }
    }

    // ---- Print it (PLAN.md §11 M53) ------------------------------------------------------------

    /// <summary>Set by tests in place of the save dialog.</summary>
    internal string? ExportPathForTest { get; set; }

    /// <summary>
    /// Set by tests to stand in for a picker that returned a place with no path on the local disk —
    /// a cloud location. The real picker cannot be driven headlessly, so this is how the refusal
    /// sentence for that case is kept honest.
    /// </summary>
    internal bool ExportPickerHasNoLocalPathForTest { get; set; }

    /// <summary>
    /// The PDF this session last wrote, or null. It is what "Print it" prints, and M56 will hand
    /// the same path to the mail program.
    /// </summary>
    internal string? LastExportedPdf { get; private set; }

    /// <summary>Set by tests in place of the card, which cannot be answered headlessly.</summary>
    internal bool? PrintAnswerForTest { get; set; }

    /// <summary>
    /// Offers to print, once, straight after a successful export. The workflow used to end with a
    /// file somewhere on the disk and a user who had to go and find it.
    /// </summary>
    private async Task OfferToPrintAsync(bool draft)
    {
        if (LastExportedPdf is null)
        {
            Announce(draft
                ? "The draft copy is made."
                : "The PDF is made. It is ready to send to the lodge.");
            return;
        }

        bool print = PrintAnswerForTest
            ?? (SuppressStartupForTest ? false : await AskAboutPrintingAsync(draft));
        if (!print)
        {
            Announce(draft
                ? "The draft copy is made."
                : "The PDF is made. When you are ready, choose “Now send it” from the File menu.");
            return;
        }

        await PrintTheLastPdfAsync();
    }

    /// <summary>Help the user get the finished PDF onto paper, honestly (M53).</summary>
    internal async Task PrintTheLastPdfAsync()
    {
        if (LastExportedPdf is not { } path)
        {
            // The picker handed back a file with no path on the local disk — a cloud location, say.
            // Nothing can be printed from here, and pretending otherwise would be worse than this.
            Announce(
                "TrestleBoard cannot tell where that PDF was saved, so it cannot print it. Open it "
                + "the way you normally open a PDF and print it from there.");
            return;
        }

        PrintOutcome outcome = PrintService.Print(path);
        LastPrintOutcomeForTest = outcome;

        if (outcome == PrintOutcome.HandedOver)
        {
            Announce(
                "The newsletter has been sent to your printer. If nothing comes out, your printer "
                + "may be off or out of paper.");
            return;
        }

        // Never "printed successfully" when nothing was printed. The card says what happened and
        // what to do about it.
        await ShowErrorAsync(
            "TrestleBoard could not print it for you",
            PrintService.FallbackMessage(path, outcome));
    }

    internal PrintOutcome? LastPrintOutcomeForTest { get; private set; }

    private async Task<bool> AskAboutPrintingAsync(bool draft)
    {
        bool print = false;
        var dialog = new Window
        {
            Title = draft ? "The draft copy is made" : "The PDF is made",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var printIt = new Button
        {
            Content = "Print it",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        printIt.Action();
        var later = new Button
        {
            Content = "Not now",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsCancel = true,
        };
        later.Action();

        printIt.Click += (_, _) => { print = true; dialog.Close(); };
        later.Click += (_, _) => dialog.Close();
        Avalonia.Automation.AutomationProperties.SetName(printIt, "Print it");
        Avalonia.Automation.AutomationProperties.SetName(later, "Not now");

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = draft
                        ? "The draft copy is saved, with DRAFT written across every page."
                        : "The PDF is saved and ready to send to the lodge.",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new Avalonia.Controls.TextBlock
                {
                    Text = $"It is saved as {Path.GetFileName(LastExportedPdf)}. Would you like to "
                        + "print it now?",
                    FontSize = 18,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Vertical,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Children = { printIt, later },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(
            dialog, draft ? "The draft copy is made" : "The PDF is made");
        await dialog.ShowDialog(this);
        return print;
    }

    // ---- Look it over with me (PLAN.md §11 M51) -----------------------------------------------

    private ReviewWindow? _reviewWindow;

    /// <summary>What the user said when asked whether to look the newsletter over first (M51).</summary>
    internal enum ReviewOffer
    {
        /// <summary>Open the checklist. The export stops here; the user will come back to it.</summary>
        LookItOver,

        /// <summary>Get on with the PDF.</summary>
        MakeItNow,

        /// <summary>Get on with the PDF, and stop offering.</summary>
        MakeItNowAndStopAsking,
    }

    /// <summary>
    /// Set by tests in place of the three-button offer, which cannot be answered headlessly — the
    /// same seam <see cref="SaveFirstAnswerForTest"/> opens for the unsaved-changes dialog. Null
    /// means "ask the user".
    /// </summary>
    internal ReviewOffer? ReviewOfferAnswerForTest { get; set; }

    /// <summary>The review window while it is open, so a test can read the screen it is showing.</summary>
    internal ReviewWindow? ReviewWindowForTest => _reviewWindow;

    /// <summary>"Take me there", so a test can press it without building the whole window.</summary>
    internal Func<ReviewFinding, ReviewLanding> TakeMeToTheFindingForTest => TakeMeToTheFinding;

    /// <summary>
    /// Puts the offer back after a test has turned it off. The setting is shared app state — the
    /// headless session redirects <c>AppPaths.Root</c>, but not between tests in one run.
    /// </summary>
    internal void SetOfferTheReviewForTest(bool offer)
    {
        _settings = _settings with { OfferTheReviewBeforeExport = offer };
        _settings.Save();
    }

    /// <summary>
    /// Asks whether to look it over first, and returns true only when the user chose to. True means
    /// "the export is not happening right now" — never "the export is refused".
    /// </summary>
    private async Task<bool> OfferTheReviewAsync()
    {
        if (!_settings.OfferTheReviewBeforeExport)
        {
            return false;
        }

        ReviewOffer answer = ReviewOfferAnswerForTest
            ?? (SuppressStartupForTest ? ReviewOffer.MakeItNow : await AskAboutTheReviewAsync());

        if (answer == ReviewOffer.MakeItNowAndStopAsking)
        {
            _settings = _settings with { OfferTheReviewBeforeExport = false };
            _settings.Save();
            return false;
        }

        if (answer != ReviewOffer.LookItOver)
        {
            return false;
        }

        ShowReview();
        return true;
    }

    private async Task<ReviewOffer> AskAboutTheReviewAsync()
    {
        ReviewOffer answer = ReviewOffer.MakeItNow;
        var dialog = new Window
        {
            Title = "Before you make the PDF",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var review = new Button
        {
            Content = "Look it over with me",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        review.Action();
        var now = new Button
        {
            Content = "Make the PDF now",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsCancel = true,
        };
        now.Action();
        var never = new Button
        {
            Content = "Make it now, and stop asking",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
        };
        never.Action();

        review.Click += (_, _) => { answer = ReviewOffer.LookItOver; dialog.Close(); };
        now.Click += (_, _) => { answer = ReviewOffer.MakeItNow; dialog.Close(); };
        never.Click += (_, _) => { answer = ReviewOffer.MakeItNowAndStopAsking; dialog.Close(); };

        Avalonia.Automation.AutomationProperties.SetName(review, "Look it over with me");
        Avalonia.Automation.AutomationProperties.SetName(now, "Make the PDF now");
        Avalonia.Automation.AutomationProperties.SetName(never, "Make it now, and stop asking");

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = "Would you like to look it over first?",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new Avalonia.Controls.TextBlock
                {
                    Text = "TrestleBoard can go through the newsletter with you, one question at a "
                        + "time, before you make the PDF. It never changes anything, and you can "
                        + "stop at any point.",
                    FontSize = 18,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Vertical,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Children = { review, now, never },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, "Would you like to look it over first?");
        await dialog.ShowDialog(this);
        return answer;
    }

    /// <summary>
    /// Opens the review (M51). Not modal — every screen points at something on the page behind it.
    /// </summary>
    internal void ShowReview()
    {
        if (_source is null || _package is null)
        {
            return;
        }

        if (_reviewWindow is not null)
        {
            _reviewWindow.Activate();
            return;
        }

        IReadOnlyList<ReviewFinding> findings = BuildReviewFindings();
        _reviewWindow = new ReviewWindow(
            findings, TakeMeToTheFinding, id => _actions.RunAsync(id), Announce);
        _reviewWindow.Closed += (_, _) => _reviewWindow = null;
        _reviewWindow.Show(this);
        _reviewWindow.Activate();

        int questions = findings.Count(f => f.Kind != ReviewFindingKind.LookAtThePage);
        Announce(questions == 0
            ? "Nothing jumped out at me. We will look at each page together."
            : $"There {(questions == 1 ? "is 1 thing" : $"are {questions} things")} worth asking you about.");
    }

    /// <summary>
    /// M82: the last look before it goes out. Returns false only when the user chose to stop and
    /// deal with something — never because the newsletter had findings.
    /// </summary>
    private async Task<bool> LookItOverBeforeSendingAsync()
    {
        if (_source is null || _package is null || !_settings.OfferTheReviewBeforeExport)
        {
            return true;
        }

        int questions = BuildReviewFindings().Count(f => f.Kind != ReviewFindingKind.LookAtThePage);
        if (questions == 0)
        {
            return true;
        }

        LastSendReviewCountForTest = questions;
        if (SuppressStartupForTest || SwallowErrorsForTest)
        {
            // Headless: the decision is recorded and the send goes on, which is the answer the
            // "never a gate" rule demands when nobody is there to be asked.
            return true;
        }

        var card = new Dialogs.SendReviewCard(questions);
        await card.ShowDialog(this);
        switch (card.Choice)
        {
            case Dialogs.SendReviewChoice.LookFirst:
                ShowReview();
                return false;
            default:
                return true;
        }
    }

    /// <summary>How many things the review had to say before the last send, for the tests.</summary>
    internal int? LastSendReviewCountForTest { get; private set; }

    private HelpWindow? _helpWindow;

    internal HelpWindow? HelpWindowForTest => _helpWindow;

    internal TourWindow? TourWindowForTest { get; private set; }

    /// <summary>
    /// Opens "How do I…?" (M63). Not modal, for <see cref="ShowReview"/>'s reason: "Take me there"
    /// would be a lie if the user were not allowed to touch what it took them to.
    ///
    /// <para>The menu paths are read off this window's own menu bar at the moment it opens, so the
    /// help describes the app that is running rather than the app somebody documented once.</para>
    /// </summary>
    internal void ShowHowDoI()
    {
        if (_helpWindow is not null)
        {
            _helpWindow.Activate();
            return;
        }

        _helpWindow = new HelpWindow(
            MenuPaths.From(this),
            id => ActionCatalog.Evaluate(id, CurrentActionContext),
            id => _actions.RunAsync(id),
            Announce);
        _helpWindow.Closed += (_, _) => _helpWindow = null;
        _helpWindow.Show(this);
        _helpWindow.Activate();
    }

    /// <summary>
    /// The five-screen tour (M63). Shown once on a new installation, and afterwards only when asked
    /// for from the Help menu.
    /// </summary>
    /// <param name="becauseTheyAsked">
    /// True from the menu item, false from start-up. The start-up call is the one that checks
    /// whether it has been seen; asking for it again always works, or "Show me round again" would
    /// be a menu item that silently did nothing.
    /// </param>
    internal async Task ShowTheTourAsync(bool becauseTheyAsked)
    {
        if (!ClaimTheTour(becauseTheyAsked))
        {
            return;
        }

        // Modal, unlike the help window. Nothing behind it matters yet on a first run, and at
        // start-up it has to finish before the start screen appears or it would open underneath it.
        var tour = new TourWindow();
        TourWindowForTest = tour;
        tour.Closed += (_, _) => TourWindowForTest = null;
        await tour.ShowDialog(this);
    }

    /// <summary>
    /// Whether to open the tour, and — as one step — the record that it has now been offered.
    ///
    /// <para>Separate from opening it so the "once, and only once" rule can be tested without a
    /// modal window: the decision is the part worth holding, and a test that has to drive a dialog
    /// to check it would be testing the dialog.</para>
    ///
    /// <para>Marked seen when it <b>opens</b>, not when it finishes. Somebody who closes it on
    /// screen two has decided; showing it again next Tuesday would be overriding that decision.
    /// </para>
    /// </summary>
    internal bool ClaimTheTour(bool becauseTheyAsked)
    {
        bool seen = _settings.HasSeenTheTour;
        if (!seen)
        {
            _settings = _settings with { HasSeenTheTour = true };
            _settings.Save();
        }

        return becauseTheyAsked || !seen;
    }

    /// <summary>Puts the flag back after a test has tripped it — the settings file is shared state.</summary>
    internal void SetHasSeenTheTourForTest(bool seen)
    {
        _settings = _settings with { HasSeenTheTour = seen };
        _settings.Save();
    }

    /// <summary>
    /// The checklist reads; this gathers what only the laid-out document knows and hands it over.
    /// </summary>
    internal IReadOnlyList<ReviewFinding> BuildReviewFindings()
    {
        if (_source is null || _package is null)
        {
            return [];
        }

        var emptyPictures = new List<string>();
        for (int page = 0; page < _source.PageCount; page++)
        {
            foreach ((string blockId, _) in _source.GetPlaceholderPictureRects(page))
            {
                emptyPictures.Add(blockId);
            }
        }

        return ReviewChecklist.Build(
            _package.Document,
            _source.GetOversetTailBlockIds(),
            emptyPictures,
            [.. SpellingStation(), .. ReadAloudStation()]);
    }

    /// <summary>
    /// M58 joins M51's checklist as its last station before the page-by-page look, which is where
    /// PLAN.md put it: the eye has done what it can by then, and the ear is the other sense.
    /// </summary>
    private IEnumerable<ReviewFinding> ReadAloudStation()
    {
        if (_package is null)
        {
            yield break;
        }

        int sentences = Core.Text.Sentences.In(_package.Document).Count;
        if (sentences == 0)
        {
            yield break;
        }

        yield return new ReviewFinding(
            ReviewFindingKind.ReadItBack,
            PageNumber: 1,
            BlockId: null,
            "Would you like to hear it read back?",
            "Errors the eye slides over, the ear catches. TrestleBoard can go through all "
            + $"{sentences} sentences one at a time — out loud if this computer has a voice, and "
            + "one at a time on the page if it has not.",
            ActionId.ReadAloud);
    }

    /// <summary>
    /// M52 joins M51's checklist as a station, which is what PLAN.md scheduled M51 first for. One
    /// screen — "there are eleven words I do not know" — with the wizard behind a button, because
    /// eleven words inside the review would bury the six questions the review is actually for.
    ///
    /// <para>It is built here rather than in <c>ReviewChecklist</c> because <c>TrestleBoard.Editing</c>
    /// does not reference <c>TrestleBoard.Spelling</c> and must not: that reference is what keeps the
    /// checker out of everything downstream of it.</para>
    /// </summary>
    private IEnumerable<ReviewFinding> SpellingStation()
    {
        if (_package is null)
        {
            yield break;
        }

        int words = Spelling.ScanDocument(_package.Document).Count;
        if (words == 0)
        {
            yield break;
        }

        yield return new ReviewFinding(
            ReviewFindingKind.SpellingToCheck,
            PageNumber: 1,
            BlockId: null,
            words == 1
                ? "There is 1 word I do not know"
                : $"There are {words} words I do not know",
            "Some of them will be names, and names are not mistakes. Would you like to go through "
            + "them one at a time?",
            ActionId.CheckSpelling);
    }

    /// <summary>
    /// "Take me there". Page first, THEN select — <see cref="GoToPage"/> clears the selection, so
    /// the other order destroys the very selection this exists to make (the M21 lesson, review
    /// §14.2).
    ///
    /// <para>M70: returns false when the block the finding is about is not in the newsletter any
    /// more. The review is a snapshot and the page behind it stays editable, so a finding can
    /// outlive the thing it is about; before this the page turned, nothing was selected and nothing
    /// was said. <c>FrameEditorController.Select</c> takes any id it is handed, so the document has
    /// to be asked rather than the selection read back afterwards.</para>
    /// </summary>
    private ReviewLanding TakeMeToTheFinding(ReviewFinding finding)
    {
        // M74 (e): where the finding is about a block, the page is the one that block is on NOW,
        // not the one the scan wrote down. The review is a snapshot and the page behind it stays
        // editable, so writing added above a frame can reflow it from page 4 to page 3 — and the
        // window then said "That is it, picked out on page 4" with the highlight sitting on page 3.
        // The page number is the one thing the user checks against the screen.
        int wanted = finding.BlockId is { } live
            && _session?.Document.TryFindBlock(live, out _, out _) == true
                ? PageOf(live)
                : finding.PageNumber - 1;
        GoToPage(Math.Clamp(wanted, 0, Math.Max(0, (_source?.PageCount ?? 1) - 1)));

        bool found = true;
        if (finding.BlockId is { } blockId)
        {
            found = _session?.Document.TryFindBlock(blockId, out _, out _) == true;
            _editor?.End();
            if (found)
            {
                _frames?.Select(blockId);
            }
            else
            {
                // Leaving the last selection standing would point at the wrong thing, which is
                // worse than pointing at nothing.
                _frames?.ClearSelection();
            }
        }

        RefreshActions();

        // M73(g): the page actually landed on, read back after the clamp rather than repeated from
        // the finding. A page can be deleted between the scan and the button.
        return new ReviewLanding(found, _pageIndex + 1);
    }

    // ---- Show me last year's (PLAN.md §11 M59) -------------------------------------------------

    private LastYearWindow? _lastYearWindow;

    internal LastYearWindow? LastYearWindowForTest => _lastYearWindow;

    /// <summary>Set by tests in place of the folder picker.</summary>
    internal string? OldIssuesFolderAnswerForTest { get; set; }

    /// <summary>
    /// M59: opens the same month of last year beside this one, to look at.
    /// </summary>
    /// <returns>
    /// M74 (f): false where last year's issue did not come up — no newsletter open, the folder
    /// question closed without a folder, or the issue itself refusing to open. Bringing the window
    /// that is already open back to the front is true: that is what the press asked for.
    /// </returns>
    internal async Task<bool> ShowLastYearAsync()
    {
        if (_package is null)
        {
            return false;
        }

        if (_lastYearWindow is not null)
        {
            _lastYearWindow.Activate();
            return true;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        PastIssue issue = PastIssues.Find(_settings.OldIssuesFolder, meta.IssueMonth, meta.IssueYear);

        bool folderRemembered = true;
        if (issue.Problem == PastIssueProblem.NoFolderYet)
        {
            string? folder = OldIssuesFolderAnswerForTest ?? await AskForTheOldIssuesFolderAsync();
            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            _settings = _settings with { OldIssuesFolder = folder };
            // M74 (b): "it will remember" is the promise the NoFolderYet sentence makes, so the
            // announcement below has to read whether remembering actually worked.
            folderRemembered = _settings.Save();
            issue = PastIssues.Find(folder, meta.IssueMonth, meta.IssueYear);
        }

        if (!issue.Opened)
        {
            await ShowErrorAsync("Last year's newsletter", issue.Message ?? "It could not be opened.");
            return false;
        }

        _lastYearWindow = new LastYearWindow(
            issue,
            meta.IssueMonth,
            meta.IssueYear,
            () => issue.Package!.Thumbnails.TryGetValue("page-1.png", out byte[]? png) ? png : null,
            CopyLastYearsArticle,
            Announce);
        _lastYearWindow.Closed += (_, _) => _lastYearWindow = null;
        _lastYearWindow.Show(this);
        _lastYearWindow.Activate();
        Announce("Last year's issue is open beside this one, to look at. It cannot be changed."
            + (folderRemembered
                ? ""
                : " But the folder could not be written down, so you may be asked where the old "
                    + "newsletters are again next time."));
        return true;
    }

    /// <summary>
    /// Brings one of last year's articles across as ordinary editable writing — through the same
    /// one-undo-step composite M54's phrases use.
    /// </summary>
    /// <returns>
    /// What came of it, in a sentence. M70: both of these used to go straight to the status bar,
    /// which is behind last year's window and at the far bottom of the screen — the refusal in
    /// particular, which is the one somebody needs to read. The window shows it and passes it on to
    /// the status bar, so it lands in both places.
    /// </returns>
    private string CopyLastYearsArticle(PastArticle article)
    {
        if (_editor is not { IsActive: true })
        {
            return "Click into some writing in this month's newsletter first, and the article will "
                + "go in where the cursor is.";
        }

        _editor.InsertBlock(article.Text, "Copy last year's article");
        RefreshSpellingMarks();
        RefreshActions();
        return "That article is in this month's newsletter. It is ordinary writing now — change "
            + "any of it you like.";
    }

    private async Task<string?> AskForTheOldIssuesFolderAsync()
    {
        IReadOnlyList<Avalonia.Platform.Storage.IStorageFolder> chosen =
            await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Where do you keep your old newsletters?",
                AllowMultiple = false,
            });

        return chosen.Count > 0 ? chosen[0].TryGetLocalPath() : null;
    }

    // ---- Read it back to me (PLAN.md §11 M58) --------------------------------------------------

    private ReadAloudWindow? _readAloudWindow;
    private ISpeaker? _speaker;

    internal ISpeaker Speaker => _speaker ??= new SystemSpeaker();

    /// <summary>Lets a test run the whole walk with a machine that has no voice.</summary>
    internal void UseSpeakerForTest(ISpeaker speaker) => _speaker = speaker;

    internal ReadAloudWindow? ReadAloudWindowForTest => _readAloudWindow;

    /// <summary>
    /// M58: reads the newsletter back one sentence at a time, or walks through it silently where
    /// this machine has no voice.
    /// </summary>
    internal void ReadItBackToMe()
    {
        if (_source is null || _package is null)
        {
            return;
        }

        if (_readAloudWindow is not null)
        {
            _readAloudWindow.Activate();
            return;
        }

        // M73(g): the live session, not a copy of the document. The page stays editable behind this
        // window — that is the whole point of it not being modal — so the walk has to be able to
        // re-read what it is about to read out. The window disposes it when it closes.
        ReadAloudSession session = _session is { } live
            ? ReadAloudSession.For(live)
            : ReadAloudSession.For(_package.Document);
        _readAloudWindow = new ReadAloudWindow(session, Speaker, ShowTheSentence, Announce);
        _readAloudWindow.Closed += (_, _) =>
        {
            _readAloudWindow = null;
            PageCanvas.SpokenRects = [];
        };
        _readAloudWindow.Show(this);
        _readAloudWindow.Activate();

        Announce(Speaker.Available
            ? $"Reading the newsletter back to you, one sentence at a time. {session.Count} to go."
            : "This computer has no voice TrestleBoard can use, so it will show you one sentence at "
              + $"a time instead. {session.Count} to go.");
    }

    /// <summary>
    /// Turns the page to the sentence being read and lights it up. Page first, then the highlight —
    /// the M21 ordering lesson, the same as M51's and M55's.
    ///
    /// <para>M74 (e): only where the user asked for this sentence. The walk follows the newsletter
    /// as it is edited, and editing on a different page from the one being read is what the window
    /// is for — so a redraw caused by a keystroke turned the canvas back to the sentence and left
    /// the caret off the screen, on every letter typed. Where the page is not turned the highlight
    /// is still worked out for the page on screen, and comes back empty if the sentence is not on
    /// it, which is the truth.</para>
    /// </summary>
    private void ShowTheSentence(Core.Text.Sentence? sentence, bool mayTurnThePage)
    {
        if (_source is null || _package is null || sentence is null)
        {
            PageCanvas.SpokenRects = [];
            return;
        }

        if (mayTurnThePage && PageOfStory(sentence.StoryId) is { } page && page != _pageIndex)
        {
            GoToPage(page);
        }

        PageCanvas.SpokenRects = TextGeometry.RectsFor(
            _package.Document, _source, _pageIndex, sentence.StoryId, sentence.ParagraphIndex,
            sentence.Offset, sentence.Length);
    }

    // ---- My templates (PLAN.md §11 M57) --------------------------------------------------------

    private UserTemplateStore? _templates;

    internal UserTemplateStore Templates => _templates ??= new UserTemplateStore();

    internal void UseTemplateStoreForTest(UserTemplateStore store) => _templates = store;

    /// <summary>Set by tests in place of the naming dialog.</summary>
    internal string? TemplateNameAnswerForTest { get; set; }

    /// <summary>
    /// M57: writes this newsletter as a template — layout, styles, widgets and pictures kept, the
    /// writing reset to the same prompts carry-forward uses, the issue date cleared.
    /// </summary>
    internal async Task<bool> SaveAsTemplateAsync()
    {
        if (_package is null)
        {
            return false;
        }

        // The thumbnail is refreshed first, so the tile shows the layout as it is now rather than
        // as it was when the newsletter was last saved.
        RefreshThumbnail(_package);

        string suggested = string.IsNullOrWhiteSpace(_package.Document.Metadata.Title)
            ? "My layout"
            : _package.Document.Metadata.Title.Trim();
        string? name = TemplateNameAnswerForTest
            ?? (SuppressStartupForTest
                ? null
                : await AskForTextAsync(
                    "Save this as one of my templates",
                    "What would you like to call it? You will pick it by this name when you start a "
                        + "newsletter.",
                    suggested));
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        Core.Container.TboardPackage template = Core.Workflow.NewsletterTemplate.From(_package);
        UserTemplate? saved = Templates.Save(template, name, DateTimeOffset.Now);

        Announce(saved is null
            ? $"TrestleBoard could not write “{name.Trim()}” to your templates."
            : $"“{saved.Name}” is one of your templates now. You will find it on the screen that "
              + "asks what you would like to do, and under File, “My templates”.");
        RefreshActions();
        return saved is not null;
    }

    /// <summary>M57: rename, remove or hand on a template.</summary>
    internal async Task ShowMyTemplatesAsync()
    {
        var window = new MyTemplatesWindow(Templates);
        await window.ShowDialog(this);

        if (window.ExportRequestedFor is { } id)
        {
            await HandOnTemplateAsync(id);
        }

        RefreshActions();
    }

    /// <summary>
    /// Writes a template out to a file the user chose, so it can be handed to a successor.
    ///
    /// <para>§0 rule 7: a template carries the officers table and the cover, so it carries real
    /// names. It goes only where the user browsed to — there is no default location beside the
    /// repository or the newsletter, exactly as roster export does it.</para>
    /// </summary>
    private async Task HandOnTemplateAsync(string id)
    {
        if (Templates.Open(id) is not { } package)
        {
            await ShowErrorAsync(
                "That template could not be opened",
                "TrestleBoard could not read that template. It may have been moved or removed.");
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Hand this template on",
            DefaultExtension = "tboard",
            SuggestedFileName = $"{id}.tboard",
            FileTypeChoices = [new FilePickerFileType("TrestleBoard newsletter") { Patterns = ["*.tboard"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            Core.Container.TboardContainer.SaveToFile(package, path);
            Announce($"That template is saved as {Path.GetFileName(path)}. Send that file to whoever "
                + "needs it, and they can open it or keep it as one of their own templates.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not write that file",
                $"The template could not be saved there. ({ex.Message})");
        }
    }

    /// <summary>Opens one of the user's own templates as a new, unsaved newsletter.</summary>
    /// <returns>M74 (f): false when the template file has gone since it was listed, so the caller
    /// does not report a newsletter that never came up.</returns>
    internal async Task<bool> OpenUserTemplateAsync(string id)
    {
        if (Templates.Open(id) is not { } package)
        {
            Announce("That template could not be opened. It may have been moved or removed.");
            return false;
        }

        // M75 (b): a template of the user's own is a start-an-issue path like any other, and
        // NewsletterTemplate.From strips the issue date out of it in so many words.
        if (!await AskWhichIssueBeforeStartingAsync(package))
        {
            SayNoIssueWasStarted();
            return false;
        }

        DocumentPath = null;
        ShowPackage(package, startsDirty: true);
        Announce("Started from one of your templates. It has no file yet, so Save it when you are ready.");
        return true;
    }

    // ---- A page from a PDF (PLAN.md §11 M67) ---------------------------------------------------

    /// <summary>Set by tests in place of the open dialog.</summary>
    internal string? PdfPathForTest { get; set; }

    /// <summary>Set by tests in place of the page picker. 1-based.</summary>
    internal int? PdfPageAnswerForTest { get; set; }

    /// <summary>
    /// Shows the pages of a chosen PDF and puts the one picked on the page as a picture (M67).
    ///
    /// <para><b>The conversion happens once, here.</b> The page is rendered to a raster and stored
    /// as an ordinary image asset, so the layout engine, the PDF export and the snapshot suite
    /// never see a PDF and determinism is untouched — M65's reasoning for the emblems, applied to a
    /// second source of pictures.</para>
    ///
    /// <para><b>The original PDF is kept in the container beside it</b> (gate 7's discipline). It
    /// costs a few hundred kilobytes and it buys the thing the raster cannot: the page can be
    /// re-rendered sharper by a later version without the committee having to find the file again,
    /// years after whoever emailed it has left the committee.</para>
    /// </summary>
    /// <returns>
    /// M74 (f): false wherever no page reached the newsletter — PDFs unreadable on this computer,
    /// an empty picker, a file that would not open, a page picker closed without a page, a page
    /// that would not draw, or no room on the page for the picture.
    /// </returns>
    internal async Task<bool> BringInPdfPageAsync()
    {
        if (_photos is null || _package is null)
        {
            return false;
        }

        if (!PdfPageRasterizer.IsAvailable)
        {
            await ShowErrorAsync("PDFs cannot be read on this computer", PdfPageRasterizer.NotAvailableReason);
            return false;
        }

        string? path = PdfPathForTest;
        if (path is null)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Bring in a page from a PDF",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("PDF files") { Patterns = ["*.pdf"] }],
            });

            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        if (path is null)
        {
            return false;
        }

        byte[] pdf;
        IReadOnlyList<PdfPageInfo> pages;
        try
        {
            pdf = await File.ReadAllBytesAsync(path);
            pages = PdfPageRasterizer.ReadPages(pdf);
        }
        catch (PdfPageException e)
        {
            await ShowErrorAsync("That PDF could not be opened", e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "That file could not be opened",
                $"TrestleBoard could not read that file. ({e.Message})");
            return false;
        }

        int? chosen = PdfPageAnswerForTest;
        if (chosen is null)
        {
            var picker = new PdfPageWindow(pdf, pages, Path.GetFileName(path));
            await picker.ShowDialog(this);
            chosen = picker.ChosenPage;
        }

        // Nothing chosen, or a number that is not a page: the window was closed. Guarded rather
        // than trusted, because "which page" arrives from a dialog and dialogs get cancelled.
        if (chosen is not { } pageNumber || pageNumber < 1)
        {
            return false;
        }

        byte[] png;
        try
        {
            png = PdfPageRasterizer.RenderPage(pdf, pageNumber);
        }
        catch (PdfPageException e)
        {
            await ShowErrorAsync("That page could not be brought in", e.Message);
            return false;
        }

        // The name is settled before the command runs, because the frame records it — but the bytes
        // go in only once the picture is actually on the page. Nothing ever paints the PDF, so
        // there is no reason to register it early, and registering early is how a cancelled or
        // failed insert leaves a megabyte of orphan in somebody's newsletter for ever.
        string pdfAsset = NextPdfAssetRef(path);

        _editor?.End();
        string? blockId = _photos.InsertPhoto(
            _pageIndex,
            png,
            $"Page {pageNumber} of {Path.GetFileName(path)}.",
            caption: null,
            centre: null,
            fromPdf: (pdfAsset, pageNumber),
            fit: Core.Model.ImageFit.Contain);

        if (blockId is null)
        {
            await ShowErrorAsync(
                "That page could not be brought in",
                "TrestleBoard drew the page but could not put it on the newsletter. Your newsletter "
                + "is unchanged.");
            return false;
        }

        _package.Assets[pdfAsset] = pdf;
        _frames?.Select(blockId);
        Announce($"Page {pageNumber} is on the newsletter as a picture. Use \"Describe this "
            + "picture\" to say what is on it — a screen reader cannot read a picture of writing, "
            + "and right now all it knows is which page this was.");
        RefreshActions();
        return true;
    }

    /// <summary>
    /// A container entry name for a source PDF that nothing else is using. Counted rather than
    /// stamped with the clock, so two pages of the same file brought in one after another get
    /// different names without the document depending on what time it was.
    /// </summary>
    private string NextPdfAssetRef(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        for (int i = 1; ; i++)
        {
            string candidate = $"source-{stem}-{i}.pdf";
            if (!_package!.Assets.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    // ---- Bringing writing in from a file (PLAN.md §11 M66) -------------------------------------

    /// <summary>Set by tests in place of the open dialog.</summary>
    internal string? WritingPathForTest { get; set; }

    /// <summary>Set by tests in place of the "bring it in?" card. True to go ahead.</summary>
    internal bool? BringItInAnswerForTest { get; set; }

    /// <summary>Set by tests in place of the picture-by-picture asks. True to take each one.</summary>
    internal bool? UseEachPictureForTest { get; set; }

    /// <summary>How many pictures the last import actually put on the page.</summary>
    internal int PicturesTakenForTest { get; private set; }

    /// <summary>
    /// Reads a Word document or a text file and puts the writing on the page (M66).
    ///
    /// <para><b>What arrives is words in this newsletter's own lettering.</b> Not Word's fonts, not
    /// its margins, not its tables — the writing, mapped onto the app's own five paragraph styles.
    /// That line is drawn in <c>WritingImport</c> and the reason is written there; what matters
    /// here is that the user is told what came in, in a count they can check against the article
    /// they were sent.</para>
    ///
    /// <para><b>The writing is one undo step</b>, because the frame and everything in it arrive as
    /// a single command. Each picture is its own step afterwards — they are separate decisions and
    /// undoing one should not undo the article.</para>
    /// </summary>
    /// <returns>
    /// M74 (f): false where nothing went into the newsletter — an empty picker, a file that would
    /// not read, a file with nothing in it, or "no" to the "bring this in?" question. True once
    /// paragraphs have gone in, or once a picture the user said yes to has been placed.
    /// </returns>
    internal async Task<bool> BringInWritingAsync()
    {
        if (_photos is null || _frames is null)
        {
            return false;
        }

        PicturesTakenForTest = 0;

        string? path = WritingPathForTest;
        if (path is null)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Bring in writing from a file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Word documents and text files")
                    {
                        Patterns = ["*.docx", "*.txt"],
                    },
                ],
            });

            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        if (path is null)
        {
            return false;
        }

        ImportedWriting writing;
        try
        {
            writing = WritingImport.Read(path);
        }
        catch (Core.Migrations.UnsupportedFormatException e)
        {
            await ShowErrorAsync("That file could not be brought in", e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "That file could not be opened",
                $"TrestleBoard could not read that file. ({e.Message})");
            return false;
        }

        if (writing.IsEmpty)
        {
            await ShowErrorAsync(
                "There was nothing in that file",
                "TrestleBoard found no writing and no pictures in it. If the words are inside a "
                + "table or a text box, they will not come through — copy them into an ordinary "
                + "paragraph first.");
            return false;
        }

        if (!(BringItInAnswerForTest ?? await ConfirmTheWritingAsync(writing, Path.GetFileName(path))))
        {
            return false;
        }

        if (writing.Paragraphs.Count > 0)
        {
            _editor?.End();
            string blockId = _frames.AddTextFrameWith(_pageIndex, [.. writing.Paragraphs.Select(ToStoryParagraph)]);
            _frames.Select(blockId);

            Announce($"{Describe(writing.Paragraphs.Count, "paragraph")} came in, on this page. "
                + "It is an ordinary frame of writing — drag it where you want it, and one Ctrl+Z "
                + "takes the whole lot back out.");
        }

        bool aPictureWentIn = await OfferThePicturesAsync(writing.Pictures);
        RefreshActions();

        // A file of pictures only, every one of them declined, has changed nothing — the same
        // "no" as the confirm, arriving one question later.
        return writing.Paragraphs.Count > 0 || aPictureWentIn;
    }

    /// <summary>
    /// Each picture from the document, one at a time, through M18's single picture ingest path.
    ///
    /// <para>Never all of them unasked: a Word document's media folder holds the author's
    /// letterhead and their signature scan as readily as the photograph they meant to send.</para>
    /// </summary>
    /// <returns>M74 (f): whether the user said yes to at least one, so the caller can tell a file
    /// whose pictures were all declined from one that put something on the page.</returns>
    private async Task<bool> OfferThePicturesAsync(IReadOnlyList<ImportedPicture> pictures)
    {
        bool any = false;
        foreach (ImportedPicture picture in pictures)
        {
            bool take = UseEachPictureForTest
                ?? await AskAboutAPictureAsync(picture, pictures.Count);
            if (!take)
            {
                continue;
            }

            // The same ingest path a dropped or pasted picture takes, description and caption asks
            // and all — so the bytes land in the container untouched (gate 7) and the picture is a
            // picture, with nothing about where it came from written anywhere.
            await PlacePictureAsync(picture.Bytes, picture.Name, centre: null);
            PicturesTakenForTest++;
            any = true;
        }

        return any;
    }

    private async Task<bool> AskAboutAPictureAsync(ImportedPicture picture, int outOf)
    {
        bool use = false;
        var dialog = new Window
        {
            Title = "A picture came with the writing",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        Avalonia.Controls.Image preview = new()
        {
            Width = 260,
            Height = 200,
            Stretch = Avalonia.Media.Stretch.Uniform,
        };

        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        try
        {
            bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(picture.Bytes));
            preview.Source = bitmap;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            // A picture the app cannot show is one it cannot place either; say so rather than
            // offering an empty box and then failing at the last moment.
            return false;
        }

        var yes = new Button { Content = "Use it", FontSize = 18, MinHeight = 44, MinWidth = 180, IsDefault = true };
        yes.Action();
        var no = new Button { Content = "Leave it out", FontSize = 18, MinHeight = 44, MinWidth = 180, IsCancel = true };
        no.Action();
        yes.Click += (_, _) => { use = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        Avalonia.Automation.AutomationProperties.SetName(yes, "Use this picture");
        Avalonia.Automation.AutomationProperties.SetName(no, "Leave this picture out");

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = outOf == 1
                        ? "This picture came with the writing — use it?"
                        : $"This is one of {outOf} pictures that came with the writing — use it?",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 460,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                preview,
                new Avalonia.Controls.TextBlock
                {
                    Text = "If you use it, TrestleBoard will ask you to describe it, the same as any "
                        + "other picture. Letterheads and signatures often travel inside a Word "
                        + "document without anybody meaning to send them.",
                    FontSize = 17,
                    MaxWidth = 460,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    Children = { yes, no },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, "A picture came with the writing");
        await dialog.ShowDialog(this);
        bitmap?.Dispose();
        return use;
    }

    /// <summary>
    /// What was found, before anything happens to the newsletter. The counts are the point: somebody
    /// who was sent a three-page article and is told "2 paragraphs" has learned that the words were
    /// in a table before they printed sixty copies.
    /// </summary>
    private async Task<bool> ConfirmTheWritingAsync(ImportedWriting writing, string fileName)
    {
        bool go = false;
        var dialog = new Window
        {
            Title = "Bring in writing from a file",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var yes = new Button { Content = "Bring it in", FontSize = 18, MinHeight = 44, MinWidth = 200, IsDefault = true };
        yes.Action();
        var no = new Button { Content = "Cancel", FontSize = 18, MinHeight = 44, MinWidth = 200, IsCancel = true };
        no.Action();
        yes.Click += (_, _) => { go = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        Avalonia.Automation.AutomationProperties.SetName(yes, "Bring it in");
        Avalonia.Automation.AutomationProperties.SetName(no, "Cancel");

        string found = writing.Pictures.Count == 0
            ? $"{Describe(writing.Paragraphs.Count, "paragraph")}, about {writing.WordCount} words."
            : $"{Describe(writing.Paragraphs.Count, "paragraph")}, about {writing.WordCount} words, "
              + $"and {Describe(writing.Pictures.Count, "picture")}.";

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = $"In {fileName}: {found}",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new Avalonia.Controls.TextBlock
                {
                    Text = "The words come in wearing this newsletter's own lettering, not the ones "
                        + "they were written with. Anything in a table or a text box does not come "
                        + "through — if the count above looks short, that is usually why.",
                    FontSize = 17,
                    MaxWidth = 480,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    Children = { yes, no },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, "Bring in writing from a file");
        await dialog.ShowDialog(this);
        return go;
    }

    private static Core.Model.StoryParagraph ToStoryParagraph(ImportedParagraph paragraph) =>
        new()
        {
            ParagraphStyleRef = paragraph.StyleRef,
            ListKind = paragraph.ListKind,
            Runs = [new Core.Model.StoryRun { Text = paragraph.Text }],
        };

    private static string Describe(int n, string thing) =>
        n == 1 ? $"1 {thing}" : $"{n} {thing}s";

    // ---- The emblem shelf (PLAN.md §11 M65) ----------------------------------------------------

    /// <summary>Set by tests in place of the picker.</summary>
    internal string? EmblemAnswerForTest { get; set; }

    /// <summary>
    /// Opens the shelf and puts the chosen emblem on the page.
    ///
    /// <para><b>It stays a drawing (M72).</b> Until M72 the emblem was rasterised here at 2048px and
    /// handed to <c>InsertPhoto</c>, so what a <c>.tboard</c> stored was a PNG. Antialiased coverage
    /// is floating-point arithmetic and processor architectures disagree about the last bit, so a
    /// macOS member's newsletter genuinely carried different bytes from a Windows member's. The path
    /// data goes into the document instead: no asset, nothing to differ, and the PDF receives the
    /// curve rather than a ~680dpi picture of it. docs/M65-spec.md §3 and §9 argued the other way
    /// and are corrected in place.</para>
    ///
    /// <para>Everything the ingest path gave it for free is kept deliberately rather than by
    /// accident: <c>InsertVector</c> reuses the same default rectangle, z-order and single composite
    /// command, so one Ctrl+Z still takes it back off, and M69's corner aspect-lock still holds it
    /// in shape.</para>
    ///
    /// <para>No description dialog, unlike a photograph: the app knows what this picture is, and
    /// asking somebody to describe the square and compasses to the app that just drew it would be
    /// the software pretending not to know something. M23's "Describe this picture" changes it.</para>
    /// </summary>
    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> InsertEmblemAsync()
    {
        if (_photos is null)
        {
            return false;
        }

        Emblem? chosen;
        if (EmblemAnswerForTest is { } id)
        {
            chosen = EmblemLibrary.Find(id);
        }
        else
        {
            var picker = new EmblemPickerWindow();
            await picker.ShowDialog(this);
            chosen = picker.Chosen;
        }

        if (chosen is null)
        {
            return false;
        }

        _editor?.End();
        string? blockId = _photos.InsertVector(
            _pageIndex,
            Emblems.EmblemGeometry.PartsOf(chosen),
            chosen.Width,
            chosen.Height,
            chosen.Description,
            caption: null,
            emblem: (chosen.Id, EmblemFingerprint.Of(chosen)));

        if (blockId is null)
        {
            await ShowErrorAsync(
                "That emblem could not be added",
                "TrestleBoard could not put that emblem on the page. Your newsletter is unchanged.");
            return false;
        }

        _frames?.Select(blockId);
        Announce($"{chosen.Name} is on the page. Drag its corners to size it, and it can be moved, "
            + "captioned and wrapped like any other picture.");
        RefreshActions();
        return true;
    }

    // ---- Pack it up for my successor (PLAN.md §11 M64) -----------------------------------------

    /// <summary>Set by tests in place of the save dialog.</summary>
    internal string? PackPathForTest { get; set; }

    /// <summary>Set by tests in place of the "here is what is in it" confirmation.</summary>
    internal bool? PackConfirmForTest { get; set; }

    /// <summary>Set by tests in place of the open dialog.</summary>
    internal string? BringInPackPathForTest { get; set; }

    /// <summary>Set by tests in place of the item-by-item window.</summary>
    internal IReadOnlyList<string>? PackPartsAnswerForTest { get; set; }

    /// <summary>The last restore, for tests and for what the app says afterwards.</summary>
    internal RestoreOutcome? LastRestoreForTest { get; private set; }

    /// <summary>
    /// Writes everything this committee has accumulated into one file for whoever comes next.
    ///
    /// <para>§0 rule 7: this is the most concentrated personal data the app produces — the address
    /// book, its backups, the templates with the officers in them, and the phrase shelf, in one
    /// attachment. It goes only where the user browsed to, there is no default location, and the
    /// confirmation says in as many words what is about to be in the file, because somebody who
    /// emails this to the wrong person has emailed the lodge's membership to the wrong person.</para>
    /// </summary>
    /// <returns>
    /// M74 (f): false where no pack was written — nothing gathered to pack, the confirm declined,
    /// an empty save picker, or a write that failed.
    /// </returns>
    internal async Task<bool> PackUpForSuccessorAsync()
    {
        SuccessorPackage pack = SuccessorPackService.Gather(DateTimeOffset.Now, AppVersion());
        if (pack.Manifest.Parts.Count == 0)
        {
            await ShowErrorAsync(
                "There is nothing to pack up yet",
                "TrestleBoard has not gathered an address book, any templates or any saved wordings "
                + "on this computer yet, so there is nothing a successor would need. Come back when "
                + "there is.");
            return false;
        }

        if (!(PackConfirmForTest ?? await ConfirmThePackAsync(pack)))
        {
            return false;
        }

        string? path = PackPathForTest;
        if (path is null)
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Pack everything up for my successor",
                DefaultExtension = SuccessorPackContainer.Extension.TrimStart('.'),
                SuggestedFileName = "TrestleBoard" + SuccessorPackContainer.Extension,
                FileTypeChoices =
                [
                    new FilePickerFileType("TrestleBoard pack")
                    {
                        Patterns = ["*" + SuccessorPackContainer.Extension],
                    },
                ],
            });

            path = file?.TryGetLocalPath();
        }

        if (path is null)
        {
            return false;
        }

        try
        {
            SuccessorPackContainer.SaveToFile(pack, path);
            Announce($"Everything is packed up in {Path.GetFileName(path)}. Give that one file to "
                + "whoever takes over, and they can bring it in on their own computer. Keep it "
                + "somewhere safe — it has the lodge's address book in it.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not write that file",
                $"The pack could not be saved there. ({ex.Message})");
            return false;
        }
    }

    /// <summary>
    /// Reads a pack and puts back whichever parts of it the user asks for.
    ///
    /// <para>Every refusal here is one sentence about what to do next. Somebody bringing in a pack
    /// is on a new computer, on their first day of a job they did not ask for, holding a file they
    /// cannot look inside.</para>
    /// </summary>
    /// <returns>
    /// M74 (f): false where nothing on this computer changed — an empty picker, a pack that would
    /// not read, a pack holding nothing this version understands, nothing ticked, or every ticked
    /// part failing to be written.
    /// </returns>
    internal async Task<bool> BringInAPackAsync()
    {
        string? path = BringInPackPathForTest;
        if (path is null)
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Bring in a predecessor's pack",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("TrestleBoard pack")
                    {
                        Patterns = ["*" + SuccessorPackContainer.Extension],
                    },
                ],
            });

            path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        if (path is null)
        {
            return false;
        }

        SuccessorPackage pack;
        try
        {
            pack = SuccessorPackContainer.LoadFromFile(path);
        }
        catch (Core.Migrations.UnsupportedFormatException e)
        {
            await ShowErrorAsync("That pack could not be brought in", e.Message);
            return false;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or NotSupportedException)
        {
            await ShowErrorAsync(
                "That pack could not be brought in",
                "TrestleBoard could not read that file. It may be damaged, or it may not be a "
                + "TrestleBoard pack.");
            return false;
        }

        IReadOnlyList<PackPartChoice> choices = SuccessorPackService.Choices(pack);
        if (choices.Count == 0)
        {
            await ShowErrorAsync(
                "There is nothing in that pack",
                "That file is a TrestleBoard pack, but there is nothing inside it that this version "
                + "of TrestleBoard knows what to do with.");
            return false;
        }

        IReadOnlyList<string> chosen = PackPartsAnswerForTest ?? await AskWhatToTakeAsync(pack, choices, path);
        if (chosen.Count == 0)
        {
            Announce("Nothing was brought in, so nothing on this computer has changed.");
            return false;
        }

        RestoreOutcome outcome = SuccessorPackService.Restore(pack, chosen);
        LastRestoreForTest = outcome;
        ReloadStoresAfterRestore(outcome.Restored);

        if (outcome.Failed.Count > 0)
        {
            await ShowErrorAsync(
                "Some of that pack could not be brought in",
                string.Join(
                    Environment.NewLine + Environment.NewLine,
                    outcome.Failed.Select(f =>
                        $"{SuccessorPackParts.TitleOf(f.PartId)} could not be written. ({f.Reason})")));
        }

        if (outcome.Restored.Count > 0)
        {
            Announce(
                "Brought in: "
                + string.Join(", ", outcome.Restored.Select(id => SuccessorPackParts.TitleOf(id).ToLowerInvariant()))
                + ". It is all here now, exactly as it was on the other computer.");
        }

        RefreshActions();

        // Some parts written and some failed is still a change to this computer, so it is true —
        // the failures are named in their own card above, which the outcome sentence cannot say.
        return outcome.Restored.Count > 0;
    }

    private async Task<IReadOnlyList<string>> AskWhatToTakeAsync(
        SuccessorPackage pack, IReadOnlyList<PackPartChoice> choices, string path)
    {
        var window = new BringInPackWindow(choices, pack.Manifest.WrittenOn, Path.GetFileName(path));
        await window.ShowDialog(this);
        return window.Chosen;
    }

    /// <summary>
    /// Says what is about to be written into the file, by name, before it is written. The privacy
    /// sentence is not a footnote: this is the one artifact the app makes that would matter if it
    /// went to the wrong address.
    /// </summary>
    private async Task<bool> ConfirmThePackAsync(SuccessorPackage pack)
    {
        bool go = false;
        var dialog = new Window
        {
            Title = "Pack everything up for my successor",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var list = new StackPanel { Spacing = 6 };
        foreach (SuccessorPackPart part in pack.Manifest.Parts)
        {
            list.Children.Add(new Avalonia.Controls.TextBlock
            {
                Text = $"• {SuccessorPackParts.TitleOf(part.Id)} — {part.Summary}",
                FontSize = 18,
                MaxWidth = 520,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
        }

        var pack_ = new Button { Content = "Pack it up", FontSize = 18, MinHeight = 44, MinWidth = 200, IsDefault = true };
        pack_.Action();
        var never = new Button { Content = "Cancel", FontSize = 18, MinHeight = 44, MinWidth = 200, IsCancel = true };
        never.Action();
        pack_.Click += (_, _) => { go = true; dialog.Close(); };
        never.Click += (_, _) => dialog.Close();
        Avalonia.Automation.AutomationProperties.SetName(pack_, "Pack it up");
        Avalonia.Automation.AutomationProperties.SetName(never, "Cancel");

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = "One file, with everything the next committee needs:",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 520,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                list,
                new Avalonia.Controls.TextBlock
                {
                    Text = "This file will have the lodge's address book in it — members' names, "
                        + "birthdays, telephone numbers and email addresses. Give it only to the "
                        + "person taking over, and keep it off anything shared.",
                    FontSize = 18,
                    MaxWidth = 520,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    Children = { pack_, never },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, "Pack everything up for my successor");
        await dialog.ShowDialog(this);
        return go;
    }

    /// <summary>
    /// Picks up what was just written underneath the running app. The stores were replaced on disk
    /// behind their own objects' backs, so anything holding one in memory is now describing a file
    /// that no longer exists — and the first thing a successor does after restoring is open the
    /// address book to check it worked.
    /// </summary>
    private void ReloadStoresAfterRestore(IReadOnlyList<string> restored)
    {
        if (restored.Contains(SuccessorPackParts.Roster))
        {
            Roster.Reload();
        }

        if (restored.Contains(SuccessorPackParts.Settings))
        {
            _settings = AppSettings.Load();
            ApplySettings(_settings);
        }
    }

    // ---- Now send it (PLAN.md §11 M56) ---------------------------------------------------------

    /// <summary>Set by tests in place of the card.</summary>
    internal bool? SendAnswerForTest { get; set; }

    internal MailOutcome? LastMailOutcomeForTest { get; private set; }

    /// <summary>
    /// M56: hands the finished newsletter to the user's own mail program, with the email group in
    /// BCC and the subject already written.
    /// </summary>
    /// <returns>
    /// M74 (f): false only where the hand-off never got started — no newsletter open, or nobody on
    /// either list, which is a refusal with a card of its own. The clipboard fallback returns
    /// <b>true</b>: the mail program not answering is not the user backing out, and the addresses
    /// really are on the clipboard by then, with a card saying what to do with them.
    /// </returns>
    internal async Task<bool> SendItAsync()
    {
        if (_package is null)
        {
            return false;
        }

        // M82: the review belongs to sending rather than beside it. M51 built the checklist and
        // hung it on a command of its own; the moment it is actually wanted is the moment before
        // sixty people get the newsletter.
        //
        // OFFERED, never a gate. "Send it anyway" is always there and is never disabled: a refusal
        // to send is a telephone call, and the app has no business deciding that a newsletter with
        // an unfilled picture frame must not go out. The user is told and then obeyed.
        if (!await LookItOverBeforeSendingAsync())
        {
            return false;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        IReadOnlyList<string> addresses =
            MailHandoff.AddressesIn(Roster.Book.Members, MemberGroups.ByEmail);
        int printed = MemberGroups.Members(Roster.Book.Members, MemberGroups.Printed).Count;

        if (addresses.Count == 0 && printed == 0)
        {
            await ShowErrorAsync(
                "Nobody is on a list yet",
                "TrestleBoard does not know who gets the newsletter. Open People, choose a brother, "
                + $"and tick “{MemberGroups.ByEmail}” or “{MemberGroups.Printed}”. You only have to "
                + "do it once.");
            return false;
        }

        string subject = MailHandoff.Subject(meta.LodgeName, meta.Title, meta.IssueYear, meta.IssueMonth);
        string fileName = LastExportedPdf is { } path ? Path.GetFileName(path) : "the PDF";
        string? uri = MailHandoff.BuildUri(addresses, subject, MailHandoff.Body(fileName));

        if (uri is not null && PrintService.Open(uri))
        {
            LastMailOutcomeForTest = MailOutcome.Opened;
            Announce(
                $"Your mail program is open, with {Count(addresses.Count, "address", "addresses")} "
                + $"in the blind copy line. Attach {fileName} before you send it."
                + (printed > 0 ? $" {Count(printed, "brother", "brethren")} still need a printed copy." : ""));
            return true;
        }

        // Either the link was too long for a mail program to be trusted with, or nothing answered.
        // Both end the same way: the addresses go on the clipboard, and the card says what to do.
        LastMailOutcomeForTest = uri is null ? MailOutcome.TooManyForOneLink : MailOutcome.NothingAnswered;
        await CopyTheAddressesAsync(addresses, subject, fileName, printed, LastMailOutcomeForTest.Value);
        return true;
    }

    private async Task CopyTheAddressesAsync(
        IReadOnlyList<string> addresses,
        string subject,
        string fileName,
        int printed,
        MailOutcome why)
    {
        await new Canvas.AvaloniaTextClipboard(this).SetTextAsync(MailHandoff.ClipboardBatches(addresses));

        string reason = why == MailOutcome.TooManyForOneLink
            ? $"There are {addresses.Count} addresses, which is more than a mail program will take "
              + "in one go."
            : "TrestleBoard could not find a mail program on this computer.";

        await ShowErrorAsync(
            "The addresses are copied, ready to paste",
            $"{reason}\n\n"
            + "They are on the clipboard now. Open your email the way you normally do, start a new "
            + "message, and paste them into the BLIND COPY line — Bcc — so that nobody sees "
            + "everybody else's address.\n\n"
            + $"Subject: {subject}\n"
            + $"Attach: {fileName}"
            + (printed > 0
                ? $"\n\nAnd {Count(printed, "brother", "brethren")} still need a printed copy."
                : string.Empty));
    }

    private static string Count(int n, string one, string many) =>
        n == 1 ? $"1 {one}" : $"{n} {many}";

    // ---- Words for hard news (PLAN.md §11 M54) ------------------------------------------------

    private PhraseShelf? _phrases;

    /// <summary>Built on first use, like the spell checker: most sittings never open it.</summary>
    internal PhraseShelf Phrases => _phrases ??= new PhraseShelf();

    /// <summary>Lets a test point the shelf at a temporary file instead of the real AppData one.</summary>
    internal void UsePhraseShelfForTest(PhraseShelf shelf) => _phrases = shelf;

    /// <summary>Set by tests in place of the window, which cannot be driven headlessly.</summary>
    internal string? PhraseAnswerForTest { get; set; }

    /// <summary>
    /// The blanks the app can answer without asking (M54, the owner's ruling of 2026-08-09): the
    /// office the sickness paragraph names comes from the user's settings, pre-filled rather than
    /// asked. <c>TrestleBoard.Core</c> knows nothing of <see cref="AppSettings"/> — the value
    /// arrives through the same <c>Fill</c> seam as <c>{name}</c> and <c>{date}</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> WhatTheAppAlreadyKnows(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{office}"] = settings.SicknessContactOffice,
        };
    }

    /// <summary>
    /// M54: choose a paragraph, fill its blanks, put it in as ordinary editable writing.
    /// </summary>
    internal async Task<bool> InsertPhraseAsync()
    {
        if (_editor is not { IsActive: true })
        {
            return false;
        }

        string? words = PhraseAnswerForTest;
        if (words is null)
        {
            if (SuppressStartupForTest)
            {
                return false;
            }

            var window = new PhraseWindow(Phrases.All(), WhatTheAppAlreadyKnows(_settings));
            await window.ShowDialog(this);
            if (!window.Confirmed)
            {
                // M74 (c). Cancelling the shelf answered exactly as inserting from it did, so Help
                // said "Done." to somebody who had just closed "Words for hard news" without
                // choosing anything — at the worst possible moment to be told a thing happened.
                return false;
            }

            words = window.Words;
        }

        if (string.IsNullOrWhiteSpace(words))
        {
            return false;
        }

        // InsertBlock, not InsertText: a bare insert coalesces with the typing either side of it,
        // so somebody who added a memorial, typed a sentence after it and pressed Ctrl+Z would lose
        // both. This is one undo step, named after the thing they chose.
        _editor.InsertBlock(words, "Add words for hard news");
        Announce(
            "The words are in your newsletter. They are ordinary writing now — change any of them "
            + "you like.");
        RefreshSpellingMarks();
        RefreshActions();
        return true;
    }

    /// <summary>
    /// M54: keep the highlighted words on the shelf. The committee's own wording for a hard moment
    /// is usually better than ours, and next year they will want it again.
    /// </summary>
    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> SavePhraseAsync()
    {
        if (_editor is not { IsActive: true } editor || _session is null)
        {
            return false;
        }

        string words = Core.Text.StoryNavigator.GetRangeText(
            _session.Document.Stories.Find(s => s.Id == editor.Selection.Range.StoryId)
                ?? throw new InvalidOperationException("The highlighted writing is not in a story."),
            editor.Selection.Range);
        if (string.IsNullOrWhiteSpace(words))
        {
            Announce("Highlight the words you would like to keep first.");
            return false;
        }

        string? title = PhraseTitleAnswerForTest;
        if (title is null)
        {
            if (SuppressStartupForTest)
            {
                return false;
            }

            title = await AskForTextAsync(
                "Keep these words for next time",
                "What would you like to call them? You will pick them from a list by this name.",
                FirstWordsOf(words));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        Phrases.Save(title, words);
        Announce(Phrases.CouldNotBeSaved
            ? $"“{title}” is on your shelf for now, but TrestleBoard could not write it down, so "
              + "it will be gone when you close the program."
            : $"“{title}” is on your shelf. You will find it under Insert, “Words for hard news”.");
        RefreshActions();
        return true;
    }

    /// <summary>Set by tests in place of the naming dialog.</summary>
    internal string? PhraseTitleAnswerForTest { get; set; }

    private static string FirstWordsOf(string text)
    {
        string flat = string.Join(' ', text.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 40 ? flat : flat[..40].TrimEnd() + "…";
    }

    /// <summary>
    /// A one-question dialog: a heading, a sentence, a box and two buttons. Returns null when the
    /// user changed their mind.
    /// </summary>
    private async Task<string?> AskForTextAsync(string title, string question, string suggested)
    {
        string? answer = null;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var box = new TextBox
        {
            Text = suggested,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 460,
            MaxWidth = 520,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, question);

        var keep = new Button
        {
            Content = "Keep them",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 160,
            IsDefault = true,
        };
        keep.Action();
        var never = new Button
        {
            Content = "Cancel",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 160,
            IsCancel = true,
        };
        never.Action();
        Avalonia.Automation.AutomationProperties.SetName(keep, "Keep them");
        Avalonia.Automation.AutomationProperties.SetName(never, "Cancel");

        keep.Click += (_, _) => { answer = box.Text; dialog.Close(); };
        never.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new Avalonia.Controls.TextBlock
                {
                    Text = question,
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 520,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                    Children = { keep, never },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, title);
        await dialog.ShowDialog(this);
        return answer;
    }

    /// <summary>
    /// M55 → M54: the memorial the People window offered. It opens the phrase shelf on the memorial
    /// with his name already answered, so the user meets a paragraph rather than a blank page.
    /// </summary>
    internal Task OfferTheMemorialForTest(string name) => OfferTheMemorialAsync(name);

    /// <summary>
    /// Which phrase the memorial is written from. Only a test changes it, and only to point it at a
    /// name that is not on the shelf — which is the one branch of this method that cannot be reached
    /// any other way, and the one that used to return in silence (M73(a)).
    /// </summary>
    internal string MemorialPhraseIdForTest { get; set; } = "memorial";

    /// <summary>
    /// Whether there is anywhere at all for a memorial notice to go. M73(a), gate 26: the People
    /// window is handed this same property, so the card cannot offer what this method would refuse.
    /// A caret is NOT part of it — People is reached from a menu, so there is hardly ever one.
    /// </summary>
    internal bool CanWriteAMemorial => _package is not null && _session is not null && _frames is not null;

    private async Task OfferTheMemorialAsync(string name)
    {
        if (!CanWriteAMemorial)
        {
            // The one state where a refusal is honest — and it has to be followable from an empty
            // desk, which "click into some writing" was not.
            Announce(
                $"There is no newsletter open, so there is nowhere to write about {name} yet. "
                + "Open this month's newsletter, or start a new one, and TrestleBoard will put a "
                + "memorial notice in for you.");
            return;
        }

        Core.Phrases.Phrase? memorial = Core.Phrases.PhraseLibrary.Find(MemorialPhraseIdForTest);
        if (memorial is null)
        {
            // M73(a), gate 27: this used to return without a word, so an accepted offer simply
            // produced nothing and the user was left looking for it.
            Announce(
                "TrestleBoard could not find the words it keeps for hard news, so it has not "
                + $"written anything. You can still write about {name} yourself — click into some "
                + "writing and type.");
            return;
        }

        var answers = new Dictionary<string, string>(StringComparer.Ordinal) { ["{name}"] = name };
        string words = memorial.Fill(answers);

        if (_editor is { IsActive: true })
        {
            _editor.InsertBlock(words, "Add a memorial notice");
            Announce(
                $"A memorial notice for {name} is in your newsletter, where the cursor was. It is "
                + "ordinary writing now — change any of it you like.");
        }
        else
        {
            // M66's AddTextFrameWith precedent: the frame and its writing arrive as ONE composite,
            // so one Ctrl+Z takes the whole thing back out.
            string blockId = _frames!.AddTextFrameWith(_pageIndex, words, "Add a memorial notice");
            _frames.Select(blockId);
            Announce(
                $"A memorial notice for {name} is in a new box of writing on page {_pageIndex + 1}. "
                + "It is ordinary writing now — change any of it you like, drag it where you want "
                + "it, and one Ctrl+Z takes the whole thing back out.");
        }

        RefreshSpellingMarks();
        RefreshActions();
    }

    // ---- Check my spelling (PLAN.md §11 M52) --------------------------------------------------

    private SpellingWindow? _spellingWindow;
    private SpellingService? _spelling;

    /// <summary>
    /// Built on first use, never at startup. Loading half a megabyte of word list is noticeable on
    /// an old machine, and somebody who only ever opens a newsletter to look at it should not pay
    /// for a checker they never ask a question of.
    /// </summary>
    internal SpellingService Spelling
    {
        get
        {
            if (_spelling is null)
            {
                _spelling = new SpellingService();
                _spelling.SeedIfEmpty(RosterSurnamesForSpelling());
            }

            return _spelling;
        }
    }

    internal SpellingWindow? SpellingWindowForTest => _spellingWindow;

    /// <summary>Correcting a word without the wizard, so a test can drive the document half alone.</summary>
    internal bool ChangeTheWordForTest(Misspelling word, string replacement) =>
        ChangeTheWord(word, replacement);

    /// <summary>
    /// Names from the address book, so the checker does not ask about half the lodge on the first
    /// run. §0 rule 7: these are real people's surnames and they go into a gitignored AppData file
    /// and nowhere else.
    /// </summary>
    private IEnumerable<string> RosterSurnamesForSpelling()
    {
        if (_roster is null)
        {
            return [];
        }

        char[] separators = [' ', '\t', ',', '.'];
        return _roster.Book.Members
            .SelectMany(m => m.DisplayName.Split(
                separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(part => part.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Opens the word-by-word pass (M52). Not modal — it points at the page behind it.</summary>
    internal void ShowSpellingCheck()
    {
        if (_source is null || _package is null)
        {
            return;
        }

        if (_spellingWindow is not null)
        {
            _spellingWindow.Activate();
            return;
        }

        IReadOnlyList<Misspelling> words = Spelling.ScanDocument(_package.Document);
        _spellingWindow = new SpellingWindow(
            words,
            Spelling.Checker,
            TakeMeToTheWord,
            ChangeTheWord,
            Announce,

            // M52 fix: the walk re-reads the document after each change instead of trusting the
            // list it opened with. Correcting one word moves every later word in its paragraph.
            () => Spelling.ScanDocument(_package!.Document));
        _spellingWindow.Closed += (_, _) =>
        {
            _spellingWindow = null;

            // Words taught to the checker while the wizard was open change what is underlined.
            RefreshSpellingMarks();
        };
        _spellingWindow.Show(this);
        _spellingWindow.Activate();

        Announce(words.Count == 0
            ? "I knew every word in the newsletter."
            : $"There {(words.Count == 1 ? "is 1 word" : $"are {words.Count} words")} I do not know.");
    }

    /// <summary>
    /// Page first, then the words — the M21 ordering lesson, same as M51's.
    ///
    /// <para>M73(e): <c>SelectRange</c>'s false was discarded and "Show me where it is" said
    /// nothing either way, which is the same defect M70 fixed in <see cref="Dialogs.ReviewWindow"/>
    /// and did not generalise. The page is editable behind the spelling window, so a word that has
    /// been moved or deleted since the scan leaves the caret wherever it was and the user staring
    /// at a page with nothing picked out on it.</para>
    /// </summary>
    /// <returns>The page number the word was found and picked out on, or null when it is not where
    /// the scan said it was any more.</returns>
    private int? TakeMeToTheWord(Misspelling word)
    {
        if (_package is null || _editor is null || _session is null)
        {
            return null;
        }

        if (PageOfStory(word.StoryId) is { } page)
        {
            GoToPage(page);
        }

        // M73(g): the same occurrence question the correction asks. Picking out an identical word
        // somewhere else in the paragraph and saying "that is it" would send the user to look at
        // the wrong copy — and the whole point of this button is to be believed.
        if (!_session.Document.TryGetStory(word.StoryId, out Core.Model.Story? story)
            || word.ParagraphIndex >= story.Paragraphs.Count
            || !SpellCheckScan.IsStillWhereItWas(
                Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[word.ParagraphIndex]),
                word))
        {
            return null;
        }

        if (!_editor.SelectRange(word.StoryId, word.ParagraphIndex, word.Offset, word.Word.Length))
        {
            return null;
        }

        RefreshActions();
        return _pageIndex + 1;
    }

    /// <summary>
    /// Swaps the word for the suggestion, through the same one-undo-step composite find-and-replace
    /// uses. Returns false when the word is no longer where the scan said it was — somebody typed
    /// while the wizard was open — because changing the wrong six characters is far worse than
    /// saying so.
    /// </summary>
    private bool ChangeTheWord(Misspelling word, string replacement)
    {
        if (_session is null
            || !_session.Document.TryGetStory(word.StoryId, out Core.Model.Story? story)
            || word.ParagraphIndex >= story.Paragraphs.Count)
        {
            return false;
        }

        // M73(g): "the same characters at the same offset" is not the same question as "the same
        // word". An edit that shifts the paragraph by exactly the gap between two copies of a word
        // puts an identical token at the recorded offset, and the old guard waved it through and
        // corrected the wrong copy. SpellCheckScan owns the answer, because the scan is what
        // decided which copy this is.
        string text = Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[word.ParagraphIndex]);
        if (!SpellCheckScan.IsStillWhereItWas(text, word))
        {
            return false;
        }

        _session.Execute(Editing.TextReplacement.Build(
            word.StoryId, word.ParagraphIndex, word.Offset, word.Length, replacement, "Correct a word"));
        RefreshSpellingMarks();
        RefreshActions();
        return true;
    }

    /// <summary>Recomputes the dotted underlines for the page on show.</summary>
    internal void RefreshSpellingMarks()
    {
        if (_source is null || _package is null || !_settings.ShowSpelling)
        {
            PageCanvas.SpellingRects = [];
            return;
        }

        PageCanvas.SpellingRects = Spelling.MarksOnPage(_package.Document, _source, _pageIndex);
    }

    internal void ToggleShowSpelling()
    {
        _settings = _settings with { ShowSpelling = !_settings.ShowSpelling };
        // M74 (b): the announcement reads the save, so a preference that could not be written down
        // is not passed off in silence as remembered.
        bool remembered = _settings.Save();
        PageCanvas.ShowSpelling = _settings.ShowSpelling;
        RefreshSpellingMarks();
        Announce((_settings.ShowSpelling
            ? "Words TrestleBoard does not know now have a dotted line under them. This never prints."
            : "The dotted lines are hidden again.")
            + (remembered ? "" : ButTheChoiceCouldNotBeRemembered));
        RefreshActions();
    }

    private int? PageOfStory(string storyId)
    {
        if (_package is null)
        {
            return null;
        }

        for (int i = 0; i < _package.Document.Pages.Count; i++)
        {
            foreach (Core.Model.Block block in _package.Document.Pages[i].Blocks)
            {
                if (block is Core.Model.TextBlock text
                    && string.Equals(text.StoryRef, storyId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return null;
    }

    // ---- Keeping the work (PLAN.md §11 M24) ---------------------------------------------------

    /// <summary>
    /// What the user chose when told their newsletter has unsaved changes.
    /// </summary>
    internal enum SaveFirst
    {
        /// <summary>Write it, then carry on with whatever prompted the question.</summary>
        Save,

        /// <summary>Throw the changes away and carry on.</summary>
        Discard,

        /// <summary>Do not carry on at all — the user changed their mind.</summary>
        Stay,
    }

    /// <summary>M24: true when this newsletter holds edits its file does not.</summary>
    internal bool HasUnsavedChangesForTest => _unsavedChanges;

    /// <summary>M24: where this newsletter lives, or null when it has never been saved.</summary>
    internal string? DocumentPathForTest => DocumentPath;

    /// <summary>
    /// Set by tests in place of the three-button dialog, which cannot be answered headlessly.
    /// Null means "ask the user"; a value means "this is what they would have said".
    /// </summary>
    internal SaveFirst? SaveFirstAnswerForTest { get; set; }

    /// <summary>
    /// Saving to a named path, which is what <see cref="SaveAsAsync"/> does once the file picker has
    /// answered. The picker cannot be driven headlessly, so the tests come in here instead — through
    /// the same write, the same failure handling and the same bookkeeping the user's Ctrl+S uses.
    /// </summary>
    internal Task<bool> SaveToPathForTest(string path) => WriteDocumentAsync(path);

    /// <summary>
    /// Writes the newsletter back to its own file, or asks where to put it if it has never had one.
    /// Returns true when the work is on disk.
    /// </summary>
    internal async Task<bool> SaveAsync()
    {
        if (_package is null)
        {
            return false;
        }

        return DocumentPath is { } path
            ? await WriteDocumentAsync(path)
            : await SaveAsAsync();
    }

    /// <summary>
    /// Asks where to put it and writes it there; that file becomes the newsletter's home, so the
    /// next Ctrl+S goes straight to it.
    /// </summary>
    internal async Task<bool> SaveAsAsync()
    {
        if (_package is null)
        {
            return false;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save this newsletter",
            DefaultExtension = "tboard",
            SuggestedFileName = $"{Integration.IssueNaming.FileStem(meta)}.tboard",
            FileTypeChoices = [new FilePickerFileType("TrestleBoard newsletter") { Patterns = ["*.tboard"] }],
        });

        // No local path means somewhere this app cannot write atomically — a phone over MTP, say.
        // Saying so beats writing a file the user will never find again.
        if (file?.TryGetLocalPath() is not { } path)
        {
            if (file is not null)
            {
                await ShowErrorAsync(
                    "Could not save it there",
                    "TrestleBoard can only save onto this computer or a drive attached to it. "
                    + "Choose a folder on the computer, or your USB stick.");
            }

            return false;
        }

        return await WriteDocumentAsync(path);
    }

    /// <summary>
    /// M39: hands back one of the copies the ring kept, per PLAN.md §4.
    ///
    /// <para>Nothing is written. The older version is opened, the newsletter keeps its own file and
    /// is marked unsaved, so the user's file changes only if they then choose to save — which is
    /// what makes picking the wrong version harmless, and what the dialog promises in words.</para>
    /// </summary>
    /// <summary>Set by tests in place of the modal dialog: the generation the user would choose.</summary>
    internal DocumentBackup? RestoreChoiceForTest { get; set; }

    /// <returns>M74 (f): false wherever the newsletter on screen is the one that was already
    /// there — no file of its own, no copies left in the ring, "Go back", a dialog closed without
    /// a generation chosen, or a copy that would not read.</returns>
    internal async Task<bool> RestoreEarlierVersionAsync()
    {
        if (DocumentPath is not { } path)
        {
            return false;
        }

        IReadOnlyList<DocumentBackup> backups = FileRecoveryStore.FindBackups(path);
        if (backups.Count == 0)
        {
            // M70(c): the catalog reads the ring once, when the newsletter's path is assigned, so
            // its answer can outlive the copies themselves — the ring is pruned, and the command is
            // still offered. Whoever presses it is owed the reason it can do nothing.
            Announce("The copies TrestleBoard kept are no longer there, so there is nothing to go back to.");
            return false;
        }

        // The version on screen is about to be replaced, so it gets the same question every other
        // path that replaces it asks (M24).
        if (await ConfirmSaveFirstAsync("and open an earlier version") == SaveFirst.Stay)
        {
            return false;
        }

        // A modal dialog cannot be answered headlessly, so the tests say which generation the user
        // would have picked — the same flag that keeps the start screen out of a test run.
        DocumentBackup? choice;
        if (SuppressStartupForTest)
        {
            choice = RestoreChoiceForTest;
        }
        else
        {
            var dialog = new DocumentRestoreDialog(backups, Path.GetFileName(path));
            await dialog.ShowDialog(this);
            choice = dialog.Chosen;
        }

        if (choice is not { } backup)
        {
            return false;
        }

        TboardPackage package;
        try
        {
            using var buffer = new MemoryStream(File.ReadAllBytes(backup.Path));
            package = TboardContainer.Load(buffer);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or Core.Migrations.UnsupportedFormatException)
        {
            await ShowErrorAsync(
                "Could not open that earlier version",
                "The copy TrestleBoard kept could not be read, so nothing has changed — the "
                + $"newsletter on screen is still here. ({ex.Message})");
            return false;
        }

        // The path stays. This IS that newsletter, at an earlier moment; it is unsaved because the
        // file still holds the newer version, and that is exactly the state the title bar and
        // Ctrl+S should be describing.
        ShowPackage(package, startsDirty: true);
        DocumentPath = path;
        UpdateTitle();
        Announce(
            $"This is the version from {DocumentRestoreDialog.Describe(backup)}. "
            + "Your file still holds the newer one until you save.");
        return true;
    }

    /// <summary>
    /// The one place a newsletter is written to a file the user chose. Failure is reported in plain
    /// language and leaves everything exactly as it was — including <see cref="_unsavedChanges"/>,
    /// so the app never claims work is safe when it is not.
    /// </summary>
    private async Task<bool> WriteDocumentAsync(string path)
    {
        if (_package is not { } package)
        {
            return false;
        }

        try
        {
            RefreshThumbnail(package);

            // ROTATE THEN WRITE, and in that order only (PLAN.md §4). Rotating afterwards would
            // make .bak1 a copy of the version just saved, so "go back to an earlier version" would
            // hand back the very thing the user wanted undone.
            //
            // M39 wired this up. RotateBackups has existed since M9 and was called by nothing but
            // its own unit test — and M24, by giving the app a Save command at all, is what made
            // that omission reachable: between the two milestones, saving overwrote the user's only
            // copy with no way back (review §14.4).
            FileRecoveryStore.RotateBackups(path);
            TboardContainer.SaveToFile(package, path);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            await ShowErrorAsync(
                "Could not save the newsletter",
                "Your work has NOT been lost — it is still here on the screen. TrestleBoard could "
                + "not write it to that file. Make sure it is not open in another program, then try "
                + $"saving again, or use \"Save it as a new file…\" to put it somewhere else. ({ex.Message})");
            return false;
        }

        DocumentPath = path;
        _unsavedChanges = false;

        // The crash snapshot exists to hold work the user's own file does not. It does now, so
        // there is nothing left to recover; the next edit starts a fresh one.
        _recovery?.Complete();

        UpdateTitle();
        Announce($"Saved to {Path.GetFileName(path)}.");
        RefreshActions();
        return true;
    }

    /// <summary>
    /// Asks the three-button question, and performs the "Save it" answer before returning it, so
    /// every caller can treat anything but <see cref="SaveFirst.Stay"/> as "go ahead".
    /// </summary>
    /// <param name="whatHappensNext">
    /// The sentence naming what the user asked for — "opening another newsletter" — so the dialog
    /// says what the changes would be lost TO rather than asking in the abstract.
    /// </param>
    private async Task<SaveFirst> ConfirmSaveFirstAsync(string whatHappensNext)
    {
        if (!_unsavedChanges || _package is null)
        {
            return SaveFirst.Discard;
        }

        // A modal three-button dialog cannot be answered by a headless run, and the same flag that
        // already keeps the start screen and the update check out of the test session keeps this
        // out of it too. A test that wants the question asked answers it with
        // SaveFirstAnswerForTest, which is why that check comes first.
        SaveFirst choice = SaveFirstAnswerForTest
            ?? (SuppressStartupForTest ? SaveFirst.Discard : await AskSaveFirstAsync(whatHappensNext));
        if (choice != SaveFirst.Save)
        {
            return choice;
        }

        // A save the user asked for that then failed must not be read as permission to discard.
        return await SaveAsync() ? SaveFirst.Save : SaveFirst.Stay;
    }

    /// <summary>
    /// The dialog itself: three buttons, all full size, the safe one first and focused (PLAN.md §6).
    /// Esc and the title-bar close both mean "Stay" — the answer that loses nothing.
    /// </summary>
    private async Task<SaveFirst> AskSaveFirstAsync(string whatHappensNext)
    {
        SaveFirst answer = SaveFirst.Stay;
        var dialog = new Window
        {
            Title = "You have changes you have not saved",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var save = new Button { Content = "Save it", FontSize = 18, MinHeight = 44, MinWidth = 150, IsDefault = true };
        var discard = new Button { Content = "Do not save it", FontSize = 18, MinHeight = 44, MinWidth = 150 };
        discard.Action();
        var stay = new Button { Content = "Go back", FontSize = 18, MinHeight = 44, MinWidth = 150, IsCancel = true };
        stay.Action();

        save.Click += (_, _) => { answer = SaveFirst.Save; dialog.Close(); };
        discard.Click += (_, _) => { answer = SaveFirst.Discard; dialog.Close(); };
        stay.Click += (_, _) => { answer = SaveFirst.Stay; dialog.Close(); };

        string where = DocumentPath is { } path
            ? $"The saved copy is {Path.GetFileName(path)}, from before these changes."
            : "This newsletter has never been saved, so there is no copy of it anywhere yet.";

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "You have changed this newsletter since it was last saved.",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 460,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"{where} If you carry on {whatHappensNext} without saving, "
                        + "those changes will be gone.",
                    FontSize = 18,
                    MaxWidth = 460,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { stay, discard, save },
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(dialog, "You have changes you have not saved");
        await dialog.ShowDialog(this);
        return answer;
    }

    /// <summary>
    /// The last gate before the window goes away. Cancels the close, asks, and closes again only
    /// once the answer is in — the standard Avalonia shape, because the event cannot be awaited.
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeAgreed || !_unsavedChanges || _package is null)
        {
            return;
        }

        e.Cancel = true;
        _ = FinishClosingAsync();
    }

    private async Task FinishClosingAsync()
    {
        try
        {
            if (await ConfirmSaveFirstAsync("and close TrestleBoard") == SaveFirst.Stay)
            {
                return;
            }

            _closeAgreed = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The window stays open, which is the outcome that keeps the work. Saying why matters
            // more than closing: an exception escaping here would take the app down with the very
            // edits this method exists to protect.
            Announce($"TrestleBoard could not finish closing. Your work is still here. ({ex.Message})");
        }
    }

    /// <summary>
    /// "TrestleBoard — September 2026" while saved, and the same with a plain-language marker while
    /// not. An asterisk is the convention and means nothing to somebody who has not been taught it.
    ///
    /// <para>M75, the census pass: the name comes through <see cref="Integration.IssueNaming.Title"/>
    /// like every other place a newsletter is named. Nobody is ever asked for a title — that was
    /// settled in M75 (g), because every one of these files is a trestle board and a second question
    /// on the way in is a second question to get wrong. This line was the one place still reading the
    /// raw field, so a template-started newsletter sat all day under the words
    /// <c>"TrestleBoard —  — not saved yet"</c>, with a hole where its name should be.</para>
    /// </summary>
    private void UpdateTitle()
    {
        if (_package is not { } package)
        {
            Title = "TrestleBoard";
            return;
        }

        string name = Integration.IssueNaming.Title(package.Document.Metadata);
        Title = _unsavedChanges
            ? $"TrestleBoard — {name} — not saved yet"
            : $"TrestleBoard — {name}";
    }

    // ---- Updates (PLAN.md §11-M10, docs/M10-spec.md §2) --------------------------------------

    /// <summary>
    /// Fire-and-forget on purpose: the check talks to GitHub, and nothing in the app may wait on
    /// the network. The user is only told when there is something to say.
    /// </summary>
    private void StartUpdateCheck()
    {
        // SuppressStartupForTest is the headless flag: no test run ever talks to GitHub.
        if (StartupOptions.SkipUpdateCheck || SuppressStartupForTest)
        {
            return;
        }

        _updates ??= new UpdateCoordinator(new VelopackUpdateChannel());
        _ = CheckForUpdatesAsync(userAsked: false);
    }

    /// <summary>
    /// M48, review §14.2: the startup check and the Help menu item used to be able to run at the
    /// same time. Opening the app and immediately choosing "Check for an update" started two, and
    /// two downloads of the same release is at best wasted bandwidth on a lodge's connection.
    ///
    /// <para>The second caller is told the truth rather than being silently dropped: somebody who
    /// pressed a menu item is owed an answer, and "already looking" IS the answer.</para>
    /// </summary>
    internal async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (_updateCheckRunning)
        {
            if (userAsked)
            {
                Announce("TrestleBoard is already looking for an update. It will say what it finds.");
            }

            return;
        }

        _updates ??= new UpdateCoordinator(new VelopackUpdateChannel());
        _updateCheckRunning = true;
        try
        {
            UpdateOutcome outcome = await _updates.CheckAsync(userAsked);
            if (outcome.Announce)
            {
                Announce(outcome.Message);
            }
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    internal Task ShowAboutAsync() => ShowErrorAsync("About TrestleBoard", AboutText());

    /// <summary>
    /// What the About window says. Separated from the dialog so a test can read it: M68 requires
    /// About to name the licence, and a claim nothing checks is a claim that rots.
    /// </summary>
    internal static string AboutText() =>
        $"TrestleBoard {AppVersion()}\n\n"
        + "The newsletter editor for Indian Land Masonic Lodge 414.\n\n"
        + "Free for lodges, churches, charities and personal use, under the "
        + $"{AppLicence.Name}. Making money with it needs a separate licence. The whole licence "
        + "is under Help, \"Licence\".\n\n"
        + "Installing and updating are explained in docs/INSTALL.md, which also came with "
        + "your download.";

    internal static string AppVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Lets the headless tests drive the update wiring without a network or an installer.</summary>
    internal void UseUpdateChannelForTest(IUpdateChannel channel) =>
        _updates = new UpdateCoordinator(channel);

    internal Task CheckForUpdatesForTest(bool userAsked) => CheckForUpdatesAsync(userAsked);

    internal UpdateCoordinator? UpdatesForTest => _updates;

    /// <summary>Also the headless-test entry point (no file dialog involved).</summary>
    internal void OpenSample()
    {
        ShowPackage(SampleDocument.CreatePackage(SamplePhoto.CreatePng()));
        SayWhatTheSwitchClosed();
    }

    /// <summary>The whole five-page issue fixture (docs/M8-spec.md §6); the headless tests' entry point.</summary>
    internal void OpenIssueSample()
    {
        ShowPackage(SampleIssue.CreatePackage(SamplePhoto.CreatePng()));
        SayWhatTheSwitchClosed();
    }

    internal string? PageLabelTextForTest => PageLabel.Text;

    internal string? ZoomLabelTextForTest => ZoomLabel.Text;

    internal TextEditorController? EditorForTest => _editor;

    internal FrameEditorController? FramesForTest => _frames;

    internal PhotoController? PhotosForTest => _photos;

    internal WidgetController? WidgetsForTest => _widgets;

    internal PageFlowController? PagesForTest => _pages;

    internal RecoveryService? RecoveryForTest => _recovery;

    /// <summary>
    /// The recovery card while it is on the screen, so a test can answer it the way a user does —
    /// including with the title-bar X, which is the whole point of M73(b3).
    /// </summary>
    internal RestoreDialog? RestoreDialogForTest => _restoreDialogForTest;

    internal void UseRecoveryStoreForTest(IRecoveryStore store) => _recoveryStore = store;

    /// <summary>Opens one of the shipped templates (PLAN.md §7).</summary>
    /// <returns>
    /// M75 (b): false when the issue question went unanswered, and true otherwise. It used to be
    /// "always true — the shipped templates are built in code, so there is no failure to report",
    /// which was accurate about loading and silent about the thing every template is missing.
    /// </returns>
    internal async Task<bool> OpenTemplateAsync(string templateId)
    {
        TboardPackage package = TemplateLibrary.Create(templateId);
        if (!await AskWhichIssueBeforeStartingAsync(package))
        {
            SayNoIssueWasStarted();
            return false;
        }

        DocumentPath = null;
        ShowPackage(package);
        SayWhatTheSwitchClosed();
        return true;
    }

    /// <summary>
    /// M24: carry-forward replaces this month's newsletter with next month's, so it asks about
    /// unsaved work first. The runner calls this one; the synchronous
    /// <see cref="StartFromLastMonth"/> under it is the carry-forward itself.
    /// </summary>
    /// <returns>M74 (f): false where "Go back" was the answer to the save question, or where there
    /// was no newsletter to carry forward — the carry-forward's own answer otherwise.</returns>
    internal async Task<bool> StartFromLastMonthAsync()
    {
        if (await ConfirmSaveFirstAsync("and start next month's newsletter") == SaveFirst.Stay)
        {
            return false;
        }

        return await CarryForwardToNextIssueAsync();
    }

    /// <summary>
    /// Start-from-last-month: carries the data forward, bumps the date, clears the prose
    /// (docs/M9-spec.md §3). The result is a NEW unsaved newsletter, so the path is cleared — and
    /// from M24 it is marked unsaved from the outset, because a carried-forward issue exists in no
    /// file anywhere until somebody saves it.
    ///
    /// <para><b>M75 (b).</b> The bump is a suggestion now, not a decision: the issue question is put
    /// with next month's numbers already filled in, and the user presses past it or corrects it. It
    /// used to increment silently, which is the same assumption the owner's first ruling forbids,
    /// wearing quieter clothes — a committee that skips August, or builds December early, was
    /// overruled without being asked. Cancelling means no new issue, and last month's newsletter is
    /// still on the screen exactly as it was.</para>
    /// </summary>
    internal async Task<bool> CarryForwardToNextIssueAsync()
    {
        if (_package is null)
        {
            return false;
        }

        TboardPackage next = CarryForward.NextIssue(_package);
        if (!await AskWhichIssueBeforeStartingAsync(next))
        {
            Announce(
                "Nothing was carried forward, because TrestleBoard was not told which issue it "
                + "would be. This newsletter is exactly as it was.");
            return false;
        }

        DocumentPath = null;
        ShowPackage(next, startsDirty: true);
        Announce(
            "Carried forward. Last month's articles have been cleared for you to rewrite. "
            + "This is a new newsletter — save it when you are ready (Ctrl+S).");
        return true;
    }

    internal DocumentRenderSource? SourceForTest => _source;

    internal WidgetLayoutProvider WidgetProviderForTest => _widgetProvider;

    internal TboardPackage? PackageForTest => _package;

    internal string? StatusLabelTextForTest => StatusLabel.Text;

    internal DocumentSession? SessionForTest => _session;

    internal PageCanvasControl CanvasForTest => PageCanvas;

    /// <summary>The scroller the canvas sits in — what the M21 zoom and pan tests measure.</summary>
    internal ScrollViewer CanvasScrollerForTest => CanvasScroller;

    /// <summary>The last font sheet this window opened, for the M14 headless tests.</summary>
    internal TextStylesWindow? TextStylesForTest => _textStylesForTest;

    /// <summary>Builds the font sheet without showing it, so the tests can drive its contents.</summary>
    internal TextStylesWindow BuildTextStylesWindowForTest() => new(
        _fonts,
        _session!.Document.StyleSheet,
        _editor?.CurrentCharacterStyleRef,
        FontPreviewRenderer.SampleFrom(FirstWordsOfDocument(), "The Trestle Board"),
        _editor?.CountFontOverrides() ?? 0,
        darkTheme: false);

    internal FontStore FontsForTest => _fonts;

    internal void GoToNextPageForTest() => GoToPage(_pageIndex + 1);

    /// <summary>Which page the canvas is showing, counted from 0 (M51's "Take me there" asserts on it).</summary>
    internal int PageIndexForTest => _pageIndex;

    // ---- Edit / Format ------------------------------------------------------------------------

    /// <summary>
    /// M70(c): the newsletter's own undo announced nothing at all, although the step's name is
    /// right there and the address book's undo has said it since M49 (see UndoPeopleChange). A
    /// change taken back somewhere off screen — on another page, inside a frame the user is not
    /// looking at — was indistinguishable from a key that did nothing.
    /// </summary>
    internal void Undo()
    {
        if (_session is not { CanUndo: true } session)
        {
            return;
        }

        string? description = session.UndoDescription;
        session.Undo();
        Announce(description is null
            ? "Taken back."
            : $"Taken back: {description.ToLowerInvariant()}.");
    }

    internal void Redo()
    {
        if (_session is not { CanRedo: true } session)
        {
            return;
        }

        string? description = session.RedoDescription;
        session.Redo();
        Announce(description is null
            ? "Done again."
            : $"Done again: {description.ToLowerInvariant()}.");
    }

    /// <summary>
    /// Ctrl+X. Inside a piece of writing that is the highlighted words; outside one it is the thing
    /// itself, taken off the page and kept (M91).
    /// </summary>
    internal async Task CutAsync()
    {
        if (_editor is { IsActive: true })
        {
            await _editor.CutAsync();
            return;
        }

        if (_frames is null || !_frames.CutSelection())
        {
            Announce("There is nothing chosen to cut. Click something on the page first.");
            return;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Taken off the page and kept. Choose the page you want it on and press Ctrl+V, "
            + "or press Ctrl+Z to put it back.");
    }

    /// <summary>Ctrl+C — the highlighted words, or the chosen thing (M91).</summary>
    internal async Task CopyAsync()
    {
        if (_editor is { IsActive: true })
        {
            await _editor.CopyAsync();
            return;
        }

        if (!CopyChosenThings())
        {
            Announce("There is nothing chosen to copy. Click something on the page first, or click "
                + "into some writing and drag across the words you want.");
        }
    }

    /// <summary>
    /// Ctrl+V means "put what I copied here". Inside a piece of writing that is words; outside one
    /// it is a picture, which from M18 goes through the same ingest path as every other picture.
    /// </summary>
    internal async Task PasteAsync()
    {
        if (_editor is { IsActive: true })
        {
            // M70(c): the catalog's own description promises this command says so out loud when
            // there is nothing to paste, and only the picture branch below was keeping that promise.
            if (!await _editor.PasteAsync())
            {
                Announce("There is nothing to paste. Copy some words first.");
            }

            return;
        }

        // M91. What was taken off the page outranks whatever the system clipboard is holding: it is
        // the more recent deliberate act, and the one the user is mid-way through. The cost is
        // named rather than hidden — once something on the page has been copied, Ctrl+V puts THAT
        // down for as long as this newsletter stays open, and a picture waiting in another program
        // is reached through Insert ▸ A picture… instead. Closing the newsletter forgets it, so
        // this can never outlive the file whose pictures it names.
        if (_frames is { HasHeldFrames: true })
        {
            PasteHeldThings();
            return;
        }

        await PastePictureAsync();
    }

    /// <summary>M91: puts what was cut or copied onto the page being looked at.</summary>
    private void PasteHeldThings()
    {
        if (_frames is null)
        {
            return;
        }

        IReadOnlyList<string> pasted = _frames.PasteOntoPage(_pageIndex);
        if (pasted.Count == 0)
        {
            Announce("There is nothing to put down here.");
            return;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();

        Announce(pasted.Count == 1
            ? $"Put down on page {_pageIndex + 1}. Drag it where you want it, or press Ctrl+Z to undo."
            : $"{pasted.Count} things put down on page {_pageIndex + 1}. Press Ctrl+Z to undo.");
    }

    internal void SelectAllText() => _editor?.SelectAll();

    // ---- Find and replace (PLAN.md §11 M21) ---------------------------------------------------

    /// <summary>
    /// Opens the find window, or brings the one already open to the front and puts it in the mode
    /// that was asked for. One window either way: Ctrl+F and Ctrl+H are the same window with and
    /// without its replace half, and two of them fighting over one selection would be a bug factory.
    /// </summary>
    internal void ShowFind(bool replacing)
    {
        if (_find is null)
        {
            return;
        }

        if (_findWindow is null)
        {
            _findWindow = new FindWindow(_find);
            _findWindow.Closed += (_, _) => _findWindow = null;

            // M85: the window asks; the shell answers. FindWindow knows nothing about folders and
            // nothing about files, which is what keeps the archive search out of the one control
            // that has to keep working when there is no archive at all.
            _findWindow.SearchTheArchive += () => _ = LookInEarlierNewslettersAsync();
            _findWindow.OpenTheIssue += hit => _ = OpenDocumentFromPathAsync(hit.Path);

            // Not modal: this window exists to point at the page behind it (see FindWindow's own
            // header), which is why M21 had to make the text session survive losing focus first.
            _findWindow.Show(this);
        }

        _findWindow.SetMode(replacing);
        _findWindow.Activate();
    }

    /// <summary>
    /// M85: looks through the folder of old newsletters for the words in the find box.
    ///
    /// <para>"When did we last mention the fish fry?" is a real question and a quarterly one, and
    /// until now the only way to answer it was to open eleven files one at a time.</para>
    ///
    /// <para><b>It says it is working before it starts.</b> Reading a folder of newsletters takes a
    /// moment, and a window that goes quiet for two seconds has told this audience that it has
    /// broken. The sentence goes up first, then the yield, then the work.</para>
    /// </summary>
    internal async Task<bool> LookInEarlierNewslettersAsync()
    {
        if (_findWindow is not { } window)
        {
            return false;
        }

        string words = window.WordsToLookFor;
        bool matchCase = window.MatchCase;

        // Asked once and remembered, exactly as M59 does — and through the same setting, because a
        // committee has one folder of old newsletters rather than one per feature.
        if (string.IsNullOrWhiteSpace(_settings.OldIssuesFolder))
        {
            string? chosen = OldIssuesFolderAnswerForTest ?? await AskForTheOldIssuesFolderAsync();
            if (string.IsNullOrWhiteSpace(chosen))
            {
                window.ShowArchiveResults(new Integration.ArchiveSearchResult(
                    [], 0, "TrestleBoard needs to know where you keep your old newsletters first."));
                return false;
            }

            _settings = _settings with { OldIssuesFolder = chosen };
            _settings.Save();
        }

        window.SayTheArchiveIsBeingRead();

        // Off the UI thread: this opens and parses every newsletter in the folder, and doing that
        // on the dispatcher is how an app stops repainting.
        string? folder = _settings.OldIssuesFolder;
        string? except = DocumentPath;
        Integration.ArchiveSearchResult result = await Task.Run(
            () => Integration.ArchiveSearch.Search(folder, words, matchCase, except));

        LastArchiveSearchForTest = result;
        window.ShowArchiveResults(result);
        return result.Hits.Count > 0;
    }

    /// <summary>What the last archive search found, for the tests.</summary>
    internal Integration.ArchiveSearchResult? LastArchiveSearchForTest { get; private set; }

    internal FindController? FindForTest => _find;

    internal FindWindow? FindWindowForTest => _findWindow;

    /// <summary>Builds the find window without showing it — the headless tests' way in.</summary>
    internal FindWindow BuildFindWindowForTest(bool replacing)
    {
        var window = new FindWindow(_find ?? throw new InvalidOperationException("No newsletter is open."));
        window.SetMode(replacing);
        return window;
    }

    internal void ToggleBold() => ToggleFormat(bold: true);

    /// <summary>
    /// M70(c): with words highlighted the page answers for itself. With a caret and nothing
    /// highlighted the controller correctly arms the next run it types — and nothing visible
    /// happens, so the user presses Ctrl+B again and undoes what they asked for. The behaviour is
    /// right; the silence was the bug.
    /// </summary>
    private void ToggleFormat(bool bold)
    {
        if (_editor is not { IsActive: true } editor)
        {
            return;
        }

        bool caretOnly = editor.SelectedText is null;
        if (bold)
        {
            editor.ToggleBold();
        }
        else
        {
            editor.ToggleItalic();
        }

        if (!caretOnly)
        {
            return;
        }

        bool nowOn = bold ? editor.IsBoldActive : editor.IsItalicActive;
        string what = bold ? "bold" : "italic";
        Announce(nowOn
            ? $"The next words you type will be {what}."
            : $"The next words you type will not be {what}.");
    }

    /// <summary>
    /// M61: makes the paragraphs the selection touches a list, or puts them back to normal writing.
    /// The title the catalog shows says which it will do, because a toggle that does not say which
    /// way it is about to go is a guess.
    /// </summary>
    internal void ToggleList(string listKind)
    {
        if (_editor is not { IsActive: true } editor)
        {
            return;
        }

        bool wasThatKind = string.Equals(editor.CurrentListKind, listKind, StringComparison.Ordinal);
        if (!editor.ToggleList(listKind))
        {
            return;
        }

        Announce(wasThatKind
            ? "Put back to normal writing."
            : listKind == Core.Model.ListKinds.Number
                ? "Numbered. Add another point and TrestleBoard will renumber them for you."
                : "Made into a list of points.");
        RefreshSpellingMarks();
        RefreshActions();
    }

    internal void ToggleItalic() => ToggleFormat(bold: false);

    /// <summary>
    /// The panel's "Paragraph style ▸" opens the same list the Format menu shows, beside the button
    /// that was pressed — a menu the user can reach without leaving the panel.
    /// </summary>
    /// <summary>The menu the panel button opens, for the test that it actually opens.</summary>
    internal MenuFlyout? ParagraphStyleFlyoutForTest { get; private set; }

    internal void ShowParagraphStyles(Control? source)
    {
        if (source is null)
        {
            ParagraphStyleMenu.Open();
            return;
        }

        var flyout = new MenuFlyout();
        foreach (string style in _editor?.AvailableParagraphStyles ?? [])
        {
            string styleRef = style;

            // Same rule as the Format menu above: the role's plain-language name, never its id.
            string label = Core.Text.StyleLabels.Describe(styleRef);
            var item = new MenuItem { Header = label, FontSize = 16 };
            Avalonia.Automation.AutomationProperties.SetName(item, label);
            item.Click += (_, _) =>
            {
                _editor?.ApplyParagraphStyle(styleRef);
                RefreshActions();
            };
            flyout.Items.Add(item);
        }

        ParagraphStyleFlyoutForTest = flyout;

        // NOT `flyout.ShowAt(source)` — and this is the whole of the bug it fixes. Every command
        // ends with ActionRunner refreshing the shell, and the refresh clears the action panel and
        // builds it again from scratch. So the button that was just pressed is torn out of the
        // visual tree a moment after this returns, and a flyout anchored to a detached control
        // closes on the spot: the user pressed "Paragraph style ▸", nothing appeared, and there was
        // nothing to see in any log because nothing had gone wrong — the menu had opened and shut.
        //
        // Opening it on the next turn of the loop puts it against whichever button is standing in
        // the rebuilt panel, which is the same offer in the same place to the person looking at it.
        // M70(h): and NOT `?? source` either, which is what stood here. `source` is the very
        // control the paragraph above is about — so in the one case the fallback existed for, the
        // menu opened against a detached button and shut again: the original bug, in a narrow case.
        //
        // The panel stops offering paragraph style only when the writing it belonged to is no
        // longer being edited, and that is the same moment `AvailableParagraphStyles` goes empty —
        // so the menu being anchored somewhere would be a menu with nothing in it. A sentence is
        // the honest answer, and the catalog already has it in the words the menu bar would use.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (PanelButtonFor(ActionId.ParagraphStyle) is { } offer)
            {
                flyout.ShowAt(offer);
                return;
            }

            ParagraphStyleFlyoutForTest = null;

            ActionAvailability can = ActionCatalog.Evaluate(ActionId.ParagraphStyle, CurrentActionContext);
            if (can.IsAvailable)
            {
                // Allowed, but with no button standing in the panel to hang it on — the panel is
                // folded away on a narrow window. The menu bar carries the same list and never
                // leaves the window, so the offer is still made, one place along.
                ParagraphStyleMenu.Open();
                return;
            }

            Announce(can.Reason);
        });
    }

    /// <summary>
    /// The action panel's button for one command, as it stands right now. Null when the panel is
    /// not offering it — in which case the caller falls back to whatever it was given.
    /// </summary>
    /// <summary>
    /// The action whose panel button currently has focus, "" when focus is in the panel but not on
    /// an offer, and null when focus is somewhere else entirely — which is the overwhelmingly common
    /// case and the one where nothing must be touched.
    /// </summary>
    private string? FocusedPanelActionId()
    {
        if (FocusManager?.GetFocusedElement() is not Control focused)
        {
            return null;
        }

        string? actionId = null;
        foreach (Control control in focused.GetSelfAndLogicalAncestors().OfType<Control>())
        {
            if (actionId is null && control is Button { Tag: string id })
            {
                actionId = id;
            }

            if (ReferenceEquals(control, _panel))
            {
                return actionId ?? string.Empty;
            }
        }

        return null;
    }

    private Button? PanelButtonFor(string actionId) =>
        _panel.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(b =>
                b.Tag is string id
                && string.Equals(id, actionId, StringComparison.Ordinal)
                && b.IsAttachedToVisualTree());

    // ---- Fonts and sizes (PLAN.md M14) ---------------------------------------------------------

    /// <summary>
    /// The font-and-size assignment sheet. Style-first: it changes a whole kind of writing, which
    /// is why the reflow warning and the "nothing changes until Apply" line are both in the sheet
    /// rather than in a confirmation afterwards.
    /// </summary>
    /// <returns>
    /// M74 (f): whether anything about the newsletter's writing changed. The sheet stays open after
    /// Apply and each Apply is its own undo step, so "did something" is not the dialog's result but
    /// whether it applied at least once — or asked for the two font-override commands under it.
    /// A sheet opened, looked at and closed changed nothing.
    /// </returns>
    internal async Task<bool> ShowTextStylesAsync()
    {
        if (_session is null)
        {
            return false;
        }

        var window = new TextStylesWindow(
            _fonts,
            _session.Document.StyleSheet,
            _editor?.CurrentCharacterStyleRef,
            FontPreviewRenderer.SampleFrom(FirstWordsOfDocument(), "The Trestle Board"),
            _editor?.CountFontOverrides() ?? 0,
            _settings.Theme is ThemeChoice.Dark or ThemeChoice.HighContrast);
        _textStylesForTest = window;

        // From M20 the sheet stays open after Apply, so one visit can change two kinds of writing.
        // Each Apply is its own command and its own undo step, applied as it happens.
        bool appliedSomething = false;
        window.Applied += (_, choice) =>
        {
            appliedSomething = true;
            ApplyTextStyleChoice(choice);
        };
        await window.ShowDialog(this);

        if (window.ShowOverridesRequested)
        {
            SetShowFontChanges(true);
            return true;
        }

        if (window.ClearOverridesRequested)
        {
            ClearEveryFontOverride();
            return true;
        }

        return appliedSomething;
    }

    /// <summary>Applies the sheet's answer, and says afterwards if the page count moved.</summary>
    internal void ApplyTextStyleChoice(TextStyleChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (_session is null || _source is null)
        {
            return;
        }

        int pagesBefore = _source.PageCount;
        _session.Execute(new SetCharacterStyleFontCommand(choice.StyleName, choice.FontFamily, choice.SizePt));

        string what = choice.FontFamily is null
            ? $"{StyleLabels.Describe(choice.StyleName)} is now {StyleOverrides.Size(choice.SizePt ?? 0f)}."
            : $"{StyleLabels.Describe(choice.StyleName)} now uses {choice.FontFamily}.";
        int pagesAfter = _source.PageCount;
        Announce(pagesAfter == pagesBefore
            ? what + " Press Ctrl+Z to put it back."
            : what + $" The newsletter is now {pagesAfter} pages instead of {pagesBefore}. "
                + "Press Ctrl+Z to put it back.");
        RefreshActions();
    }

    /// <summary>Format → "Make text bigger" / "Make text smaller": one rung of the fixed ladder.</summary>
    internal void StepTextSize(int direction)
    {
        if (_session is null || _editor?.CurrentCharacterStyle is not { } style)
        {
            return;
        }

        float? next = FontSizeLadder.Step(style.SizePt, direction);
        if (next is null)
        {
            Announce(direction > 0
                ? "This writing is already as large as TrestleBoard goes."
                : "This writing is already as small as TrestleBoard goes.");
            return;
        }

        ApplyTextStyleChoice(new TextStyleChoice(
            CharacterStyleResolver.BaseName(style.Name), null, next.Value));
    }

    /// <summary>
    /// "Use a different font just here…": the picker in its second mode (PLAN.md M20), applied as a
    /// derived style to the highlighted words only. No new command type — EnsureCharacterStyle plus
    /// ApplyCharacterStyle, exactly as bold and italic already work.
    /// <para>
    /// Until M20 this reused the whole-newsletter sheet with its title swapped, so it offered the
    /// other roles it was not going to change and warned about a repagination it was not going to
    /// cause. It is now the same window in <see cref="TextStylesMode.JustHere"/>: no role list, the
    /// selected words as the preview, and a warning that is true.
    /// </para>
    /// </summary>
    /// <returns>
    /// False when nothing changed: the window was cancelled, or the writing already used the font
    /// that was chosen. M73(e) — this was <c>_ = RetargetSpans(...)</c> underneath, and announced
    /// "Those words now use their own font" over a caret with no words highlighted, and over a font
    /// that was already in force.
    /// </returns>
    internal async Task<bool> UseFontJustHereAsync()
    {
        if (_session is null || _editor is null)
        {
            return false;
        }

        _textStylesForTest = BuildJustHereWindowForTest();
        await _textStylesForTest.ShowDialog(this);

        if (_textStylesForTest.Result is { } choice)
        {
            bool caretOnly = _editor.SelectedText is null;
            if (!_editor.UseFontJustHere(choice.FontFamily ?? CurrentFamily(), choice.SizePt))
            {
                Announce("That is already the font this writing uses, so nothing changed.");
                return false;
            }

            Announce(caretOnly
                ? "The next words you type will use that font."
                : "Those words now use their own font. Press Ctrl+Z to put them back.");
            RefreshActions();
            return true;
        }

        // The window was cancelled. Nothing to say — the user knows what they just pressed — but
        // "nothing happened" is what the runner has to be told (M73(f)).
        return false;
    }

    /// <summary>Builds the "just here" picker without showing it, for the M20 headless tests.</summary>
    internal TextStylesWindow BuildJustHereWindowForTest() => new(
        _fonts,
        _session!.Document.StyleSheet,
        _editor?.CurrentCharacterStyleRef,
        FontPreviewRenderer.SampleFrom(
            _editor?.SelectedText ?? FirstWordsOfDocument(), "The Trestle Board"),
        overrideCount: 0,
        _settings.Theme is ThemeChoice.Dark or ThemeChoice.HighContrast,
        TextStylesMode.JustHere);

    /// <summary>
    /// M70(d): "Put back to the usual font." was said before anything was known to have been put
    /// back. With a caret and no words highlighted nothing on the page changes at all — what
    /// changes is the font the NEXT words will be in — so the sentence has to say which of the two
    /// happened, and say nothing when neither did.
    /// </summary>
    internal void ClearFontOverrideHere()
    {
        if (_editor is not { } editor)
        {
            return;
        }

        bool caretOnly = editor.SelectedText is null;
        if (!editor.ClearFontOverride())
        {
            Announce("This writing already uses the font its kind of writing normally uses.");
            return;
        }

        Announce(caretOnly
            ? "The next words you type will use the usual font."
            : "Put back to the usual font.");
        RefreshActions();
    }

    /// <summary>
    /// Puts every "just here" font in the newsletter back, in one undo step.
    ///
    /// <para>M73(b2), gate 27: this used to count the overrides across the whole newsletter, then
    /// clear them with <c>SelectAll(); ClearFontOverride();</c> — which needs a caret and, with
    /// one, reaches only the frame the caret is in — and then announce the count regardless. With
    /// no caret nothing at all changed and the app still said so and told the user to press Ctrl+Z,
    /// which took back some earlier edit instead. The number said out loud is now the number the
    /// editor reports it actually put back, and the clearing is whole-document like the offer.</para>
    /// </summary>
    internal void ClearEveryFontOverride()
    {
        if (_session is null || _editor is null)
        {
            return;
        }

        int putBack = _editor.ClearEveryFontOverride();
        if (putBack == 0)
        {
            Announce("Nothing in this newsletter uses a font of its own.");
            return;
        }

        Announce(putBack == 1
            ? "One piece of text was put back to its usual font. Press Ctrl+Z to undo."
            : $"{putBack} pieces of text were put back to their usual fonts. Press Ctrl+Z to undo.");
        RefreshActions();
    }

    internal void ToggleShowFontChanges() => SetShowFontChanges(!PageCanvas.ShowFontChanges);

    /// <summary>
    /// M47, review §14.3: the page master has carried four margins since M2 and the canvas has
    /// never drawn them, so "is this frame too close to the edge?" was a question the app held the
    /// answer to and would not show.
    ///
    /// <para>Off by default, like the font-change marks: a line nobody asked for is noise on a page
    /// they are trying to read. It is a VIEW setting and never prints — the exporter draws through
    /// <c>RenderPage</c>, which knows nothing about it.</para>
    /// </summary>
    /// <summary>
    /// M78: turns the line along the bottom of every page on or off.
    ///
    /// <para>A document change and therefore undoable, unlike the three view toggles it sits near —
    /// the margin line and the two diagnostic overlays are about what the EDITOR shows, and this one
    /// prints. Somebody who turns it on and does not like it reaches for Ctrl+Z, which is the
    /// gesture they already have.</para>
    /// </summary>
    internal void TogglePageFooter()
    {
        if (_session is null || _package is null)
        {
            return;
        }

        // Read off the first master rather than a field of our own: two things that can disagree
        // eventually will, and here the disagreement prints the wrong page (the M55 rule).
        bool showing = _session.Document.PageMasters.Count > 0
            && _session.Document.PageMasters[0].ShowFooter;

        _session.Execute(new ShowPageFooterCommand(!showing));
        _source?.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        PageCanvas.InvalidateVisual();
        _rail.ForgetEveryThumbnail();
        RefreshActions();

        Announce(showing
            ? "The line along the bottom of each page is hidden."
            : "Every page now says the lodge, the month and which page it is, along the bottom.");
    }

    /// <summary>Whether the footer is on, for the tests and for anything that wants to say so.</summary>
    internal bool PageFooterShowing =>
        _session?.Document.PageMasters is { Count: > 0 } masters && masters[0].ShowFooter;

    internal void ToggleShowMargins()
    {
        PageCanvas.ShowMargins = !PageCanvas.ShowMargins;
        Announce(PageCanvas.ShowMargins
            ? "The edge to keep inside is now drawn as a faint line. It never prints."
            : "The faint line is hidden again.");
        RefreshActions();
    }

    private void SetShowFontChanges(bool show)
    {
        PageCanvas.ShowFontChanges = show;
        int count = _editor?.CountFontOverrides() ?? 0;
        Announce(show
            ? count == 0
                ? "Nothing in this newsletter has had its font changed by hand."
                : $"Writing whose font was changed by hand is now underlined on screen. "
                  + $"There {(count == 1 ? "is 1 piece" : $"are {count} pieces")}. This never prints."
            : "The underlines are hidden again.");
        RefreshActions();
    }

    /// <summary>
    /// Help → "Fonts and licences": the OFL text the licence requires we ship (OFL §II(3)), and
    /// from M52 the spelling dictionary's terms underneath it.
    ///
    /// <para>The dictionary is here rather than behind a third menu item for the reason the app's
    /// own licence is NOT here: this window is "the things that came with TrestleBoard and whose
    /// terms are somebody else's", and a word list is one of those. The app's own grant is a
    /// different question and keeps its own command (M68).</para>
    ///
    /// <para>M74 (f) added the third: PDFium, the library that turns a page of somebody else's PDF
    /// into a picture. It is a native binary rather than data, which is exactly why it was missed —
    /// gate 22 named it and nothing here listed it. Its terms are somebody else's too.</para>
    /// </summary>
    internal Task ShowFontLicencesAsync() =>
        ShowScrollingTextAsync("Fonts and licences", BundledLicences());

    internal static string BundledLicences() =>
        BundledFonts.ReadLicenceText()
        + "\n\n\n"
        + "═══════════════════════════════════════════════════════════════════════\n"
        + $"The spelling dictionary — {BundledDictionary.Language}\n"
        + "═══════════════════════════════════════════════════════════════════════\n\n"
        + "TrestleBoard checks your spelling against the word list reproduced below, which came\n"
        + "from the SCOWL project by way of the LibreOffice dictionaries. It may be passed on only\n"
        + "with the notices that follow, which is why they are here.\n\n"
        + BundledDictionary.ReadLicenceText()
        + "\n\n\n"
        + "═══════════════════════════════════════════════════════════════════════\n"
        + $"Reading PDFs — {BundledPdfium.Name}\n"
        + "═══════════════════════════════════════════════════════════════════════\n\n"
        + "\"Bring in a page from a PDF\" turns one page of somebody else's PDF into a picture. The\n"
        + $"program that reads the PDF is {BundledPdfium.Name}, which came with TrestleBoard by way\n"
        + $"of {BundledPdfium.ShippedBy}. It may be passed on only with the notices that follow —\n"
        + "there are eleven of them, because it has other people's work inside it as well.\n\n"
        + BundledPdfium.ReadLicenceText();

    /// <summary>
    /// Help → "Licence": TrestleBoard's own terms (M68). PolyForm Noncommercial's <i>Notices</i>
    /// section requires the text to travel with the software, so it is read out of the assembly
    /// rather than off the disk beside it.
    /// </summary>
    internal Task ShowLicenceAsync() =>
        ShowScrollingTextAsync("Licence", AppLicence.ReadText());

    private string? FirstWordsOfDocument() =>
        _session?.Document.Stories
            .SelectMany(s => s.Paragraphs)
            .SelectMany(p => p.Runs)
            .Select(r => r.Text)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

    private string CurrentFamily() =>
        _editor?.CurrentCharacterStyle?.FontFamily ?? BundledFonts.BodyFamily;

    // ---- Frames (docs/M5-spec.md §9) ----------------------------------------------------------

    /// <summary>
    /// Adds an empty text frame AND starts the text session in it (PLAN.md §11 M17) — the same
    /// path Enter and F2 take on a selected frame. Before M17 this selected the new frame and
    /// stopped, so the user was left looking at an empty rectangle with no sign that typing was
    /// what to do next; the inline editor underneath has worked since M4.
    /// </summary>
    /// <summary>
    /// M83: two columns in the chosen box of writing, or back to one.
    ///
    /// <para>The sentence about the overset marker is not decoration. On a two-column frame the
    /// writing runs out at the bottom of the RIGHT column, which is where nobody is looking — so
    /// where it happens is said in words rather than left to a marker in the corner of the eye.</para>
    /// </summary>
    internal bool ToggleTwoColumns()
    {
        if (_frames is null || !_frames.ToggleTwoColumns())
        {
            Announce("Choose a box of writing on the page first.");
            return false;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.BlockGeometry));
        PageCanvas.InvalidateVisual();
        _rail.ForgetEveryThumbnail();
        RefreshActions();

        bool twoNow = _frames.SelectionIsTwoColumns;
        bool overset = _frames.IsSelectionOverset;
        Announce((twoNow
            ? "The writing now runs down two columns, filling the left one before the right."
            : "The writing is back to one column.")
            + (overset && twoNow
                ? " There is still more writing than fits — it runs out at the bottom of the "
                  + "right-hand column."
                : string.Empty));
        return true;
    }

    /// <summary>
    /// M82: makes the chosen box taller until the writing fits, and says which of the four things
    /// actually happened rather than "Done".
    /// </summary>
    internal bool GrowTheBox()
    {
        if (_frames is null)
        {
            return false;
        }

        FrameEditorController.GrowResult result = _frames.GrowToFit();
        if (result is FrameEditorController.GrowResult.Fits
            or FrameEditorController.GrowResult.GrewButStillDoesNotFit)
        {
            _source?.Invalidate(new ChangeScope(ChangeKind.BlockGeometry));
            PageCanvas.InvalidateVisual();
            _rail.ForgetEveryThumbnail();
        }

        RefreshActions();
        Announce(result switch
        {
            FrameEditorController.GrowResult.Fits =>
                "The box is taller and all of the writing fits now.",
            FrameEditorController.GrowResult.GrewButStillDoesNotFit =>
                "The box is as tall as the page allows, and there is still more writing than fits. "
                + "Try “Make the rest fit”, which moves the rest to the next page.",
            FrameEditorController.GrowResult.NoRoom =>
                "This box already reaches the bottom of the page, so it cannot be made taller. "
                + "Try “Make the rest fit”, which moves the rest to the next page.",
            FrameEditorController.GrowResult.AlreadyFits =>
                "All of the writing in this box already fits.",
            _ => "Choose a box of writing on the page first.",
        });

        return result is FrameEditorController.GrowResult.Fits
            or FrameEditorController.GrowResult.GrewButStillDoesNotFit;
    }

    // ---- Make another like this, and keep it where it is (PLAN.md §11 M81) ---------------------

    /// <summary>
    /// M81: a copy of the chosen thing, just below it and chosen, so the next keystroke moves it.
    /// </summary>
    internal void DuplicateSelected()
    {
        if (_frames is null)
        {
            return;
        }

        bool wasLinked = _frames.SelectionWasLinked;
        int howMany = _frames.SelectionCount;
        _editor?.End();
        if (_frames.DuplicateSelected() is null)
        {
            Announce("There is nothing chosen to make another of. Click something on the page first.");
            return;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();

        // A copy of a frame that continued its writing elsewhere is NOT a continuation, and the
        // user is told rather than left to discover it: two frames claiming to continue one story
        // is not a thing the flow model can mean.
        // M92: the sentence counts, because the command does. Saying "a copy" after making four
        // is the same defect as a label promising less than the command does.
        Announce(wasLinked
            ? "There is now a copy just below it, holding the same writing. The copy does not "
              + "continue into the next frame — it is a page of its own."
            : howMany > 1
                ? $"There are now {howMany} copies, just below the originals. Drag them where you "
                  + "want them, or press Ctrl+Z to undo."
                : "There is now a copy just below it. Drag it where you want it, or press Ctrl+Z to undo.");
    }

    /// <summary>
    /// M91: takes what is chosen off this page and puts it on the next one, then goes there.
    /// </summary>
    internal bool MoveSelectionToNextPage() => MoveSelectionToPage(_pageIndex + 1);

    /// <summary>M91: the same, backwards.</summary>
    internal bool MoveSelectionToPreviousPage() => MoveSelectionToPage(_pageIndex - 1);

    private bool MoveSelectionToPage(int target)
    {
        if (_frames is null || _source is null)
        {
            return false;
        }

        _editor?.End();
        IReadOnlyList<string> moved = _frames.MoveSelectionToPage(target);
        if (moved.Count == 0)
        {
            Announce("There is nothing chosen to move. Click something on the page first.");
            return false;
        }

        _source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();

        // Page FIRST, then choose. GoToPage clears the selection — deliberately, because a
        // selection you cannot see is worse than none — so re-choosing has to come after the turn,
        // and it must come at all: the thing the user just moved is the thing they are about to
        // put somewhere, and hunting for it again on a new page is the work this command exists to
        // save.
        GoToPage(target);

        // Read, not discarded: SelectAll drops ids that are not on the page it is looking at, so
        // this is the difference between "it is there and ready to move" and "it is there, go and
        // find it". Promising the first while delivering the second is the gate this answers.
        bool stillChosen = _frames.SelectAll(moved);

        PageCanvas.InvalidateVisual();
        RefreshActions();

        string where = moved.Count == 1
            ? $"It is now on page {target + 1}"
            : $"They are now on page {target + 1}";
        string back = moved.Count == 1
            ? "Press Ctrl+Z to put it back."
            : "Press Ctrl+Z to put them back.";

        Announce(stillChosen
            ? $"{where}, still chosen, so you can drag it where you want it. {back}"
            : $"{where}. {back}");
        return true;
    }

    /// <summary>
    /// M91: keeps a copy of whatever is chosen on the page.
    /// </summary>
    private bool CopyChosenThings()
    {
        if (_frames is null || !_frames.CopySelection())
        {
            return false;
        }

        RefreshActions();
        Announce(_frames.HeldFrameWasLinked
            ? "Copied. Choose the page you want it on and press Ctrl+V. The copy will not continue "
              + "into the next frame — it holds the writing, on its own."
            : "Copied. Choose the page you want it on and press Ctrl+V.");
        return true;
    }

    /// <summary>
    /// M93: "How spaced out the writing is…" — the four paragraph fields the layout engine has
    /// honoured since M1 and no command could reach.
    /// </summary>
    internal async Task<bool> ChangeWritingLookAsync()
    {
        if (_writingLook is not { CanChangeTheWritingsLook: true } look)
        {
            Announce("There is no writing to space out yet. Start a newsletter first.");
            return false;
        }

        _editor?.End();
        var dialog = new WritingLookDialog(look.CurrentSpacing, look.FirstLineIsIndented);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        // Two separate decisions on one window, so both are asked and both are reported. Either
        // may be a no-op — the user opened it, looked, and pressed Use this — and the sentence has
        // to say that rather than claim a change that did not happen (M70(c)).
        bool spacingChanged = dialog.ChosenSpacing is { } chosen && look.SetSpacing(chosen);
        bool indentChanged = look.SetFirstLineIndent(dialog.IndentFirstLine);

        if (!spacingChanged && !indentChanged)
        {
            Announce("The writing is already like that, so nothing has changed.");
            return false;
        }

        // A style change reflows every page, so nothing narrower than the whole document will do.
        _source?.Invalidate(new ChangeScope(ChangeKind.Metadata));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();

        Announce(spacingChanged && indentChanged
            ? "The writing is respaced and the paragraphs now start differently. Press Ctrl+Z to undo."
            : spacingChanged
                ? "The writing through the newsletter is respaced. Press Ctrl+Z to undo."
                : dialog.IndentFirstLine
                    ? "Each paragraph now starts pushed in. Press Ctrl+Z to undo."
                    : "Paragraphs no longer start pushed in. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M94: type exactly where the chosen thing goes and how big it is.</summary>
    internal async Task<bool> SayExactlyWhereItGoesAsync()
    {
        if (_frames is not { SelectedBlockId: { } blockId } frames
            || _source is null
            || frames.SelectedRect is not { } current)
        {
            Announce("There is nothing chosen to place. Click something on the page first.");
            return false;
        }

        _editor?.End();

        Core.Model.SizePt page = _source.GetPageSize(_pageIndex);
        var dialog = new PositionAndSizeDialog(current, page);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        if (!frames.SetSelectionGeometry(dialog.Wanted))
        {
            // Either it is kept in place — the controller has already put that sentence in the
            // status bar — or the numbers were the ones it already had.
            Announce(frames.StatusMessage
                ?? "It is already exactly there, so nothing has changed.");
            return false;
        }

        _source.Invalidate(new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Moved to exactly where you asked. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M95: a coloured panel to set a notice apart. ShapeKind.Box, at last reachable.</summary>
    internal async Task<bool> AddBoxAsync()
    {
        if (_frames is null || _source is null)
        {
            return false;
        }

        _editor?.End();

        // Asked BEFORE it is put there rather than after, because a box arrives behind everything
        // else and a see-through one would be invisible — the user would be recolouring something
        // they could not find.
        var dialog = new BoxColoursDialog(
            "What colour should the box be?", Core.Model.PageLooks.BoxColours[0].Argb, null);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        string blockId = _frames.AddBox(_pageIndex, dialog.Fill, dialog.Outline);

        _source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("There is now a box on the page, behind everything else. Drag it where you want "
            + "it, or press Ctrl+Z to undo.");
        return blockId.Length > 0;
    }

    /// <summary>M95: recolours a box or a line already on the page.</summary>
    internal async Task<bool> ChangeShapeColoursAsync()
    {
        if (_frames is not { SelectionIsAShape: true } frames
            || frames.SelectionShapeColours is not { } colours)
        {
            Announce("Colours can only be chosen for a box or a line. Click one first.");
            return false;
        }

        _editor?.End();
        var dialog = new BoxColoursDialog("Change what colour the box is", colours.Fill, colours.Stroke);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        if (!frames.SetSelectionShapeColours(dialog.Fill, dialog.Outline))
        {
            Announce("It is already those colours, so nothing has changed.");
            return false;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.BlockContent));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Recoloured. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M96: lines the chosen paragraphs up left, centred or right.</summary>
    internal bool AlignText(Core.Model.TextAlignment alignment)
    {
        if (_editor is not { IsActive: true } editor)
        {
            Announce("Click into some writing first, then choose how to line it up.");
            return false;
        }

        if (!editor.SetAlignment(alignment))
        {
            Announce("It is already lined up that way, so nothing has changed.");
            return false;
        }

        // A paragraph style change relays the story it is in, and the frames after it in the chain.
        _source?.Invalidate(new ChangeScope(ChangeKind.Text));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();

        Announce(alignment switch
        {
            Core.Model.TextAlignment.Center => "Lined up down the middle. Press Ctrl+Z to undo.",
            Core.Model.TextAlignment.Right => "Lined up on the right. Press Ctrl+Z to undo.",
            _ => "Lined up on the left. Press Ctrl+Z to undo.",
        });
        return true;
    }

    /// <summary>M100: a whole page copied, with everything on it.</summary>
    internal bool DuplicateThisPage()
    {
        if (_pages is null || _source is null)
        {
            return false;
        }

        _editor?.End();
        if (_pages.DuplicatePage(_pageIndex) is null)
        {
            Announce("There is no page to copy.");
            return false;
        }

        _source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();

        // Page first, then the canvas: the copy is the thing the user is about to work on, and
        // leaving them on the original is the commonest way a copy goes unnoticed.
        GoToPage(_pageIndex + 1);
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce($"There is now a copy of that page, and you are looking at it — page "
            + $"{_pageIndex + 1}. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M97: the paper the newsletter is printed on, and the margins round it.</summary>
    internal async Task<bool> ChangeThePaperAsync()
    {
        if (_pages is null || _source is null)
        {
            Announce("There is no newsletter open, so there is no paper to change.");
            return false;
        }

        _editor?.End();
        (Core.Model.SizePt size, float left, float top, float right, float bottom) = _pages.PageSetup;
        var dialog = new PageSetupDialog(size, left, top, right, bottom);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        if (!_pages.SetPageSetup(
                dialog.Size, dialog.LeftPt, dialog.TopPt, dialog.RightPt, dialog.BottomPt))
        {
            Announce("The newsletter is already on that paper, so nothing has changed.");
            return false;
        }

        _source.Invalidate(new ChangeScope(ChangeKind.PageStructure));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();

        // Asked AFTER the change, and said out loud. Making the paper smaller can leave frames
        // hanging off it, and nothing is moved for the user — a dozen frames quietly shuffling is
        // a bigger surprise than one that needs dragging. The count is what makes undo an informed
        // choice rather than a guess.
        int off = _pages.BlocksOffThePaper();
        Announce(off == 0
            ? "The paper is changed and every page has been laid out again. Press Ctrl+Z to undo."
            : off == 1
                ? "The paper is changed. One thing now hangs off the edge of a page — drag it back "
                  + "on, or press Ctrl+Z to put the paper back."
                : $"The paper is changed. {off} things now hang off the edge of a page — drag them "
                  + "back on, or press Ctrl+Z to put the paper back.");
        return true;
    }

    /// <summary>M99: what colour the highlighted writing is.</summary>
    internal async Task<bool> ChangeTextColourAsync()
    {
        if (_editor is not { IsActive: true } editor || editor.SelectedText is not { Length: > 0 } words)
        {
            Announce("No words are highlighted. Drag across some words first.");
            return false;
        }

        var dialog = new TextColourDialog(editor.CurrentTextColour, words);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        if (!editor.UseColourJustHere(dialog.Chosen))
        {
            Announce("Those words are already that colour, so nothing has changed.");
            return false;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.Text));
        _rail.ForgetEveryThumbnail();
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Recoloured. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M98: capitals, small letters, or one capital per word.</summary>
    internal async Task<bool> ChangeCaseAsync()
    {
        if (_editor is not { IsActive: true } editor || editor.SelectedText is not { Length: > 0 } words)
        {
            Announce("No words are highlighted. Drag across some words first.");
            return false;
        }

        var dialog = new ChangeCaseDialog(words);
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        if (!editor.ChangeCase(dialog.Chosen))
        {
            Announce("Those words are already like that, so nothing has changed.");
            return false;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.Text));
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Changed. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M98: how many words are in this piece of writing.</summary>
    internal bool SayHowManyWords()
    {
        if (_editor?.CountWords() is not { } count)
        {
            Announce("Click into some writing first, then ask how many words it has.");
            return false;
        }

        // "About" is honest rather than modest: a word count is a count of whitespace runs, and
        // every program disagrees about hyphens and "St." — saying about means nobody has to
        // wonder why this number differs from the one Word gave them.
        Announce(count.HighlightedWords is { } highlighted
            ? $"About {count.Words} words in this piece of writing, and about {highlighted} of them "
              + "highlighted."
            : $"About {count.Words} words in this piece of writing, and {count.Characters} letters "
              + "and spaces.");
        return true;
    }

    /// <summary>M98: a character the keyboard has no key for.</summary>
    internal async Task<bool> InsertSymbolAsync()
    {
        if (_editor is not { IsActive: true } editor)
        {
            Announce("Click into some writing first, then choose a character to put in.");
            return false;
        }

        var dialog = new SymbolPickerDialog();
        await dialog.ShowDialog(this);

        if (!dialog.Confirmed)
        {
            return false;
        }

        // Straight through InsertText, so it is one ordinary typed character as far as everything
        // downstream is concerned — undo, coalescing, styling and the spell checker included.
        editor.InsertText(dialog.Chosen);

        _source?.Invalidate(new ChangeScope(ChangeKind.Text));
        PageCanvas.InvalidateVisual();
        RefreshActions();
        Announce("Put in. Press Ctrl+Z to undo.");
        return true;
    }

    /// <summary>M81: keeps the chosen thing where it is, or lets it move again.</summary>
    internal void ToggleLocked()
    {
        if (_frames is null || !_frames.ToggleLocked())
        {
            return;
        }

        RefreshActions();
        Announce(_frames.SelectionIsLocked
            ? "This is now kept in place. You can still change what it says, and still delete it."
            : "This can be moved again.");
    }

    // ---- One page as a picture (PLAN.md §11 M81) ------------------------------------------------

    /// <summary>
    /// M81: a picture of one page, for the lodge's page or a message.
    ///
    /// <para>The dialog says two true things rather than none: page 1 is the one people share, and
    /// a picture carries no words a screen reader can read while the PDF still does. Neither is a
    /// refusal — both are what somebody would want to know before sending it.</para>
    /// </summary>
    internal async Task<bool> ExportPageAsPictureAsync()
    {
        if (_source is null || _package is null)
        {
            return false;
        }

        int pageIndex = _pageIndex;
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save this page as a picture",
            DefaultExtension = "jpg",
            SuggestedFileName = Integration.IssueNaming.PagePictureName(_package.Document, pageIndex + 1),
            FileTypeChoices = [new FilePickerFileType("Picture") { Patterns = ["*.jpg"] }],
        });

        if (file?.TryGetLocalPath() is not { } path)
        {
            return false;
        }

        try
        {
            byte[] jpeg = _source.RenderPageToJpeg(pageIndex);
            await File.WriteAllBytesAsync(path, jpeg);
            LastPagePictureForTest = path;
            Announce(
                $"Page {pageIndex + 1} was saved as {Path.GetFileName(path)}. "
                + "A picture carries no words a screen reader can read — the PDF still does, "
                + "so send that to anybody who needs it read out.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(
                "Could not save that picture",
                "TrestleBoard could not write that file. Try somewhere else, such as your Desktop. "
                + ex.Message);
            return false;
        }
    }

    /// <summary>Where the last shared page went, for the tests.</summary>
    internal string? LastPagePictureForTest { get; private set; }

    // ---- Borders, shading and a line across the page (PLAN.md §11 M79) -------------------------

    /// <summary>M79: a thin line round the edge of the chosen box, or off again.</summary>
    internal void ToggleBorder() => ChangeTheLook(
        () => _frames?.ToggleBorder() ?? false,
        () => _frames?.SelectionHasBorder ?? false,
        "There is now a thin line round the edge of it.",
        "The line round the edge is gone.");

    /// <summary>M79: a pale background behind the chosen box, to mark a notice.</summary>
    internal void ToggleShade() => ChangeTheLook(
        () => _frames?.ToggleShade() ?? false,
        () => _frames?.SelectionHasShade ?? false,
        "It now has a pale background behind it.",
        "The pale background is gone.");

    /// <summary>
    /// The half of the two toggles that is the same for both: do it, redraw, and SAY which way it
    /// went.
    ///
    /// <para>The sentence is read AFTER the change, off the block itself, rather than predicted
    /// before it — a toggle that announces what it meant to do rather than what it did is the M73
    /// failure, and here the two could differ if the command ever refused.</para>
    /// </summary>
    private void ChangeTheLook(Func<bool> change, Func<bool> isOnNow, string whenOn, string whenOff)
    {
        if (!change())
        {
            return;
        }

        _source?.Invalidate(new ChangeScope(ChangeKind.BlockContent));
        PageCanvas.InvalidateVisual();
        _rail.ForgetEveryThumbnail();
        RefreshActions();
        Announce(isOnNow() ? whenOn : whenOff);
    }

    /// <summary>M79: a straight line right across the page, under whatever is chosen.</summary>
    internal void AddRuleAcrossThePage()
    {
        if (_frames is null)
        {
            return;
        }

        _editor?.End();
        _frames.AddRuleAcrossThePage(_pageIndex);
        _rail.ForgetEveryThumbnail();
        PageCanvas.Focus();
        RefreshActions();
        Announce("A line now runs across the page. Drag it to move it, or press Delete to take it away.");
    }

    internal void AddTextFrame()
    {
        _editor?.End();
        if (_frames is null)
        {
            return;
        }

        _frames.AddTextFrame(_pageIndex);
        PageCanvas.Focus();
        PageCanvas.BeginTextEditingOnSelection();
    }

    /// <summary>
    /// M49, review §14.3: the keyboard's answer to marquee selection.
    ///
    /// <para>A marquee can only be drawn with a mouse, and what one is nearly always FOR in this app
    /// is taking hold of several things at once to line them up — which the align and distribute
    /// commands then act on. So the keyboard gets that outcome rather than a simulated rubber band:
    /// everything on the page, in one press.</para>
    /// </summary>
    internal void SelectEverythingOnThisPage()
    {
        if (_frames is null || _source is null)
        {
            return;
        }

        _editor?.End();
        string[] ids = [.. FramesOnThisPage()];
        if (ids.Length == 0)
        {
            return;
        }

        // M74 (b): false means nothing is chosen now. It cannot happen with the non-empty list
        // built above, but if it ever could, "chosen" must not be said over a cleared selection.
        if (!_frames.SelectAll(ids))
        {
            return;
        }

        Announce(ids.Length == 1
            ? "One thing on this page is chosen."
            : $"All {ids.Length} things on this page are chosen. Use Arrange to line them up.");
        RefreshActions();
    }

    /// <summary>Every block id on the page being looked at, in the order the page holds them.</summary>
    private IEnumerable<string> FramesOnThisPage()
    {
        if (_session is null || _pageIndex < 0 || _pageIndex >= _session.Document.Pages.Count)
        {
            return [];
        }

        return _session.Document.Pages[_pageIndex].Blocks.Select(b => b.Id);
    }

    /// <summary>
    /// M50: keeps what is chosen and adds the neighbour — the keyboard's Shift+click.
    /// </summary>
    internal void AlsoChoose(bool forward)
    {
        if (_frames is null)
        {
            return;
        }

        _editor?.End();
        if (_frames.AddNeighbourToSelection(_pageIndex, forward))
        {
            int count = _frames.SelectionCount;
            Announce(count == 1
                ? "One thing on this page is chosen."
                : $"{count} things are chosen. Use Arrange to line them up.");
        }

        RefreshActions();
    }

    /// <summary>
    /// M73(e): the bool was discarded and nothing was said either way. A frame vanishing is plain
    /// enough to see, but somebody following the status bar with a screen reader had no way to know
    /// the key had done anything at all.
    /// </summary>
    internal void DeleteSelectedFrame()
    {
        if (_frames is null)
        {
            return;
        }

        // M92: counted before the delete, because afterwards there is nothing left to count.
        int howMany = _frames.SelectionCount;
        Announce(_frames.DeleteSelected()
            ? howMany > 1
                ? $"All {howMany} taken off the page. Press Ctrl+Z to put them back."
                : "Taken off the page. Press Ctrl+Z to put it back."
            : "Nothing is chosen, so there is nothing to take off the page.");
    }

    /// <summary>The controller says which way the wrap went, because only it knows (M73(e)).</summary>
    internal void ToggleWrap() => _frames?.ToggleWrap();

    /// <summary>
    /// M70(c): all four z-order commands returned a false nobody read. Pressing "Move it to the
    /// front" on the frontmost frame is the commonest no-op in the app, and it used to look exactly
    /// like a broken key.
    /// </summary>
    /// <param name="towardsFront">Which end of the pile the command was heading for, so the
    /// "already there" sentence can name it.</param>
    internal void Restack(Func<FrameEditorController, bool> action, bool towardsFront)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_frames is not null && !action(_frames))
        {
            Announce(towardsFront
                ? "This is already in front of everything else on the page."
                : "This is already behind everything else on the page.");
        }
    }

    internal void BeginFrameLink()
    {
        _frames?.BeginLink();
        PageCanvas.Focus();
    }

    internal void UnlinkFrames() => _frames?.Unlink();

    // ---- Lining things up (PLAN.md §11 M21) ---------------------------------------------------

    /// <summary>
    /// Lines the chosen frames up. The controller does the arithmetic and puts one composite command
    /// on the undo stack; the shell's whole job is to refresh what the user is looking at.
    /// </summary>
    internal void AlignSelection(FrameAlignmentKind kind)
    {
        _frames?.Align(kind);
        RefreshActions();
    }

    internal void DistributeSelection(bool horizontal)
    {
        _frames?.Distribute(horizontal);
        RefreshActions();
    }

    /// <summary>
    /// M73(e): M71's own command, and the one that was silent in the most ways. It said nothing at
    /// all unless it had chosen the frame for the user, and <c>PageFlowController.AutoFlow</c> nulls
    /// its own <c>StatusMessage</c> when it refuses — so "Make the rest fit" on writing it could not
    /// move looked exactly like a dead button. What is said is now a function of what it returned.
    /// </summary>
    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal bool AutoFlow()
    {
        if (_pages is null)
        {
            return false;
        }

        if (FlowTarget(out string? chose) is not { } blockId)
        {
            Announce(
                "Nothing in this newsletter has more writing than fits, so there is nothing to move.");
            return false;
        }

        bool moved = _pages.AutoFlow(blockId);
        string said = moved
            ? _pages.StatusMessage is { Length: > 0 } note
                ? note
                : "The rest of the writing now fits. Press Ctrl+Z to undo."
            : "TrestleBoard could not move any of the writing. The frame may be too narrow for the "
              + "words in it, or there may be nowhere left to put them.";

        // The controller's own sentence — "the text still does not all fit" — must survive being
        // told what was chosen, so the two are said together rather than one over the other (M70).
        Announce(chose is not null ? chose + " " + said : said);
        return moved;
    }

    /// <summary>
    /// The frame "Make the rest fit" acts on: the chosen one, or — when nothing is chosen and the
    /// "what's next" card has just said there is more writing than fits — the start of the first
    /// story that has run out of room, which is turned to and chosen first so the user can see what
    /// is being changed. Written in the spirit of <see cref="PictureTarget"/>, and, per M70, it hands
    /// back a sentence saying what it picked rather than quietly acting on something the user never
    /// chose.
    /// </summary>
    private string? FlowTarget(out string? chose)
    {
        chose = null;
        if (SelectedTextBlockId is { } selected)
        {
            return selected;
        }

        if (_pages?.FirstOversetChainHead is not { } overset)
        {
            return null;
        }

        GoToPage(overset.PageIndex);
        _editor?.End();
        _frames?.Select(overset.BlockId);
        chose = $"Nothing was chosen, so TrestleBoard chose the writing on page "
            + $"{overset.PageIndex + 1} that does not all fit.";
        return overset.BlockId;
    }

    /// <summary>The frame the flow actions act on: the selected one, or the one being typed into.</summary>
    private string? SelectedTextBlockId =>
        _editor is { IsActive: true } ? _editor.BlockId : _frames?.SelectedBlockId;

    // ---- Photos (docs/M6-spec.md §7) ----------------------------------------------------------

    /// <summary>
    /// M73(f): returns whether a picture actually reached the page. Pressing Cancel on the picker
    /// is the commonest way this ends, and the help window used to answer it with
    /// "Done: Put a picture here".
    /// </summary>
    internal async Task<bool> InsertPhotoAsync()
    {
        if (_photos is null || _source is null)
        {
            return false;
        }

        // M80: the pictures this committee has used before, offered first. The lodge front and the
        // Master's portrait go into most issues, and every month somebody navigated the same four
        // folders to find the same file.
        List<string> recent = RecentPicturesThatStillExist();
        if (recent.Count > 0 && !SuppressStartupForTest)
        {
            var strip = new Dialogs.RecentPicturesDialog(recent);
            await strip.ShowDialog(this);
            switch (strip.Choice)
            {
                case Dialogs.RecentPictureChoice.Cancelled:
                    return false;
                case Dialogs.RecentPictureChoice.UseThisOne when strip.ChosenPath is { } chosen:
                    return await InsertPhotoFromPathAsync(chosen);
                default:
                    break;
            }
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [PictureFileTypes()],
        });
        if (files.Count == 0)
        {
            return false;
        }

        RememberPictureUsed(files[0].TryGetLocalPath());
        return await InsertPhotoFromFileAsync(files[0]);
    }

    /// <summary>
    /// One the user has used before, opened straight from its path (M80).
    ///
    /// <para>It goes through the same bytes-to-page route as everything else, iPhone conversion
    /// included — the file may have been replaced since it was last used, and a path is not a
    /// promise about what is at the end of it.</para>
    /// </summary>
    private async Task<bool> InsertPhotoFromPathAsync(string path)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not open that picture",
                $"TrestleBoard could not open {Path.GetFileName(path)} any more. It may have been "
                + "moved or renamed. Choose it again with “Choose a file…”. " + ex.Message);
            return false;
        }

        if (await MakeReadableAsync(bytes) is not { } readable)
        {
            return false;
        }

        RememberPictureUsed(path);
        await PlacePictureAsync(readable, Path.GetFileName(path), centre: null);
        return true;
    }

    /// <summary>
    /// The remembered pictures that are still where they were.
    ///
    /// <para>A path that no longer leads anywhere is dropped silently rather than offered: a tile
    /// that fails when pressed is worse than a tile that is not there.</para>
    /// </summary>
    private List<string> RecentPicturesThatStillExist()
    {
        var live = new List<string>();
        foreach (string path in _settings.RecentPictures)
        {
            try
            {
                if (File.Exists(path))
                {
                    live.Add(path);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A network share that is not answering. Leaving it out is the right answer.
                _ = e;
            }
        }

        return live;
    }

    /// <summary>Puts a picture at the front of the remembered list. Best-effort, like every
    /// preference: a list that could not be written is a nuisance next month, not a failure now.</summary>
    private void RememberPictureUsed(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _settings = _settings.WithPictureUsed(path);
        _settings.Save();
    }

    /// <summary>The file's bytes, or null once the failure has been explained to the user.</summary>
    /// <summary>
    /// The one funnel every file-borne picture comes through — the picker, a drag onto the page,
    /// and a file on the clipboard — which is why M80's iPhone conversion lives here and nowhere
    /// else. (A bitmap pasted from the clipboard has already been decoded by whoever put it there,
    /// so it cannot be a HEIC.)
    /// </summary>
    private async Task<byte[]?> ReadPictureBytesAsync(IStorageFile file)
    {
        try
        {
            await using Stream stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return await MakeReadableAsync(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Could not open that picture", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// M80: an iPhone photograph, converted where this computer can and explained where it cannot.
    ///
    /// <para>The format is read from the BYTES, never the file name — a photograph out of a
    /// phone-sync folder is a HEIC whatever it is called, and one renamed to <c>.jpg</c> by a
    /// well-meaning relative is still a HEIC.</para>
    ///
    /// <para>What goes on to the page is the CONVERTED picture. Keeping the original would mean a
    /// newsletter that opened on the machine that converted it and failed on the secretary's.</para>
    /// </summary>
    private async Task<byte[]?> MakeReadableAsync(byte[] bytes)
    {
        if (Imaging.PictureFormat.Sniff(bytes) != Imaging.PictureFormatKind.Heic)
        {
            return bytes;
        }

        HeicResult result = HeicConversion.Handle(HeicConverter, bytes);
        LastHeicMessageForTest = result.WhatToTell;

        if (result.Converted is { } converted)
        {
            // Said in the status bar rather than in a dialog: it worked, the photograph is going on
            // the page, and a modal in the middle of a success is a step nobody asked for.
            Announce(result.WhatToTell);
            return converted;
        }

        await ShowErrorAsync("That is an iPhone photograph", result.WhatToTell);
        return null;
    }

    /// <summary>
    /// How this computer converts iPhone photographs. Settable so a test can drive both answers —
    /// neither is reproducible on CI, which is exactly why the decision has to be testable apart
    /// from the machine.
    /// </summary>
    internal IHeicConverter HeicConverter { get; set; } = HeicConversion.ForThisComputer();

    /// <summary>The last thing an iPhone photograph was explained with, for the tests.</summary>
    internal string? LastHeicMessageForTest { get; private set; }

    /// <summary>The tests' way into the one funnel every file-borne picture comes through.</summary>
    internal Task<byte[]?> MakeReadableForTest(byte[] bytes) => MakeReadableAsync(bytes);

    /// <summary>
    /// What the picker will show (M80).
    ///
    /// <para>Avalonia's own "all images" filter does not list <c>.heic</c>, so on a computer that
    /// CAN convert them an iPhone photograph was not even choosable — the user could see it in the
    /// folder, greyed, with no explanation at all. Where conversion is impossible the extensions
    /// stay out, because a file the app will refuse should not be offered.</para>
    /// </summary>
    private FilePickerFileType PictureFileTypes()
    {
        string[] ordinary = ["*.jpg", "*.jpeg", "*.png", "*.gif", "*.bmp", "*.webp", "*.tif", "*.tiff"];
        return new FilePickerFileType("Pictures")
        {
            Patterns = HeicConverter.CanConvert
                ? [.. ordinary, "*.heic", "*.heif"]
                : ordinary,
        };
    }

    private async Task<bool> InsertPhotoFromFileAsync(IStorageFile file)
    {
        if (await ReadPictureBytesAsync(file) is not { } bytes)
        {
            return false;
        }

        var dialog = new PhotoInsertDialog(file.Name);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return false;
        }

        _editor?.End();
        string? blockId = _photos!.InsertPhoto(_pageIndex, bytes, dialog.AltText, dialog.Caption);
        if (blockId is null)
        {
            await ShowErrorAsync(
                "That file is not a picture",
                "TrestleBoard could not read that file as a picture. JPEG and PNG files work best.");
            return false;
        }

        _frames?.Select(blockId);
        RefreshActions();
        return true;
    }

    internal void FixPhoto()
    {
        if (_photos is not null && _frames?.SelectedBlockId is { } blockId)
        {
            _photos.FixPhoto(blockId);
        }
    }

    internal async Task AdjustPhotoAsync() => await ShowAdjustWindowAsync(startOnTrim: false);

    /// <summary>"Trim the edges…" — the same window, opened with the edge controls showing (M18).</summary>
    internal async Task TrimPictureAsync() => await ShowAdjustWindowAsync(startOnTrim: true);

    private async Task ShowAdjustWindowAsync(bool startOnTrim)
    {
        if (_photos is null || _frames?.SelectedBlockId is not { } blockId || !_photos.IsPhoto(blockId))
        {
            return;
        }

        var window = new PhotoAdjustWindow(_photos, blockId, startOnTrim);
        await window.ShowDialog(this);
        RefreshActions();
    }

    /// <summary>"Position the picture in its frame…" (PLAN.md M22) — recentres an already-sized
    /// crop; separate from Fix/Adjust/Trim, which change the crop's size.</summary>
    internal async Task PositionPictureAsync()
    {
        if (_photos is null || _frames?.SelectedBlockId is not { } blockId || !_photos.IsPhoto(blockId))
        {
            return;
        }

        var window = new PositionPhotoWindow(_photos, blockId);
        await window.ShowDialog(this);
        RefreshActions();
    }

    /// <summary>"Dismiss this note" (PLAN.md M23) — hides the stretched-picture warning until the
    /// frame changes shape again. <c>_photos.Changed</c> refreshes the panel.</summary>
    internal void DismissCropNotice()
    {
        if (_photos is null || _frames?.SelectedBlockId is not { } blockId)
        {
            return;
        }

        // M73(e): the bool was discarded. On the path where it is false the note stays on screen
        // and the button meant to hide it looks broken.
        Announce(_photos.DismissStaleCropNotice(blockId)
            ? "That note is hidden. It will come back if you change the shape of the frame again."
            : "There is no note to hide on this picture.");
    }

    // ---- Filling, swapping and labelling a picture (PLAN.md §11 M18) --------------------------

    /// <summary>
    /// The frame the picture commands act on: the chosen one, or — when nothing is chosen and the
    /// "what's next" card has just said the photo pages are empty — the first unfilled frame in the
    /// newsletter, which is turned to and chosen first so the user can see what they are filling.
    /// </summary>
    private string? PictureTarget()
    {
        if (_photos is null)
        {
            return null;
        }

        if (_frames?.SelectedBlockId is { } selected && _photos.IsPhoto(selected))
        {
            return selected;
        }

        if (_photos.FirstPlaceholder is not { } placeholder)
        {
            return null;
        }

        GoToPage(placeholder.PageIndex);
        _editor?.End();
        _frames?.Select(placeholder.BlockId);
        return placeholder.BlockId;
    }

    /// <summary>
    /// The block the caption and description commands act on (M72).
    ///
    /// <para>A chosen DRAWING is the answer before <see cref="PictureTarget"/> is consulted, and
    /// that ordering is the whole of it. <c>PictureTarget</c> falls through to "the first empty
    /// picture frame in the newsletter" when what is chosen is not a photograph — which is right
    /// for "Put a picture here…" and would have been badly wrong here: the catalog offers "Describe
    /// this picture" with a drawing chosen, and the shell would have gone off and described
    /// something else on another page. That is precisely the offer-time / do-time disagreement
    /// gate 26 exists for.</para>
    /// </summary>
    private string? WordsTarget()
    {
        if (_photos is not null && _frames?.SelectedBlockId is { } selected && _photos.IsVector(selected))
        {
            return selected;
        }

        return PictureTarget();
    }

    /// <summary>
    /// "Put a picture here…" / "Swap this picture…". The bytes land in the package verbatim, exactly
    /// as on the insert path — a swap never re-encodes — and the whole change is one undo step.
    /// </summary>
    /// <returns>
    /// M74 (c): false where the user backed out — no picture chosen, the file unreadable, the
    /// description dialog cancelled. This is a review remedy, and the review said "Done: Swap this
    /// picture..." to somebody who had just pressed Cancel on it.
    /// </returns>
    internal async Task<bool> ReplacePictureAsync()
    {
        if (_photos is null || PictureTarget() is not { } blockId)
        {
            return false;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [PictureFileTypes()],
        });
        if (files.Count == 0)
        {
            return false;
        }

        if (await ReadPictureBytesAsync(files[0]) is not { } bytes)
        {
            return false;
        }

        return await ReplacePictureFromBytesAsync(blockId, bytes, files[0].Name);
    }

    /// <summary>
    /// The shared tail of every replace: ask for the words, then put the bytes in. Drag-and-drop and
    /// paste both come through here, so a picture arriving by any route is described before it lands
    /// (PLAN.md §6).
    /// </summary>
    private async Task<bool> ReplacePictureFromBytesAsync(string blockId, byte[] bytes, string fileName)
    {
        var dialog = new PhotoInsertDialog(fileName);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return false;
        }

        _editor?.End();
        if (!_photos!.ReplacePhoto(blockId, bytes, dialog.AltText, dialog.Caption))
        {
            await ShowErrorAsync(
                "That file is not a picture",
                "TrestleBoard could not read that file as a picture. JPEG and PNG files work best.");
            return false;
        }

        _frames?.Select(blockId);
        RefreshActions();
        return true;
    }

    /// <summary>
    /// What a headless test types into <see cref="PictureWordsDialog"/>. Null means nobody typed
    /// anything, which is what pressing Cancel amounts to - there is no other way to reach the
    /// cancelled path of a modal dialog from a test.
    /// </summary>
    internal string? PictureWordsAnswerForTest { get; set; }

    /// <summary>
    /// M74 (c): false where the description dialog was cancelled. "Add a description" is the remedy
    /// the review offers for a picture nobody can see, so a false "Done: ..." there has the user
    /// tick off a real accessibility worry they had just declined to fix.
    /// </summary>
    internal async Task<bool> DescribePictureAsync()
    {
        if (_photos is null || WordsTarget() is not { } blockId)
        {
            return false;
        }

        if (PictureWordsAnswerForTest is { } typed)
        {
            _photos.SetAltText(blockId, typed);
            RefreshActions();
            return true;
        }

        if (SuppressStartupForTest)
        {
            return false;
        }

        PictureWordsDialog dialog = PictureWordsDialog.ForAltText(_photos.GetWorded(blockId)?.AltText);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed && dialog.Text is { } description)
        {
            _photos.SetAltText(blockId, description);
            RefreshActions();
            return true;
        }

        return false;
    }

    /// <summary>M74 (c): false where the caption dialog was cancelled - see above.</summary>
    internal async Task<bool> CaptionPictureAsync()
    {
        if (_photos is null || WordsTarget() is not { } blockId)
        {
            return false;
        }

        if (PictureWordsAnswerForTest is { } typed)
        {
            _photos.SetCaption(blockId, typed);
            RefreshActions();
            return true;
        }

        if (SuppressStartupForTest)
        {
            return false;
        }

        PictureWordsDialog dialog = PictureWordsDialog.ForCaption(_photos.GetWorded(blockId)?.Caption);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed)
        {
            _photos.SetCaption(blockId, dialog.Text);
            RefreshActions();
            return true;
        }

        return false;
    }

    /// <summary>Drag-and-drop is an accelerator; the Insert menu item is the primary path (PLAN.md §6).</summary>
    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = _photos is not null && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// A picture lands WHERE IT WAS DROPPED (PLAN.md §11 M18). The position was in the event all
    /// along and was thrown away: every dropped photograph went to the same spot in the middle of
    /// the page, which reads as the app ignoring the gesture it just accepted.
    ///
    /// <para>A drop onto a picture frame replaces what is in it — one undo step — because that is
    /// the only thing dropping a photograph onto a photograph can sensibly mean.</para>
    /// </summary>
    /// <summary>
    /// An <c>async void</c> event handler, because Avalonia's drop event gives no other shape — and
    /// therefore one whose exceptions have nowhere to go but the process. Everything it does is
    /// inside the guard for that reason: a dropped file that turns out to be unreadable, or a
    /// picture Skia refuses, must not take the newsletter down with it (review §14.2).
    /// </summary>
    private async void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (_photos is null
                || e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file)
            {
                return;
            }

            (float x, float y) = PageCanvas.ToClampedPagePoint(e.GetPosition(PageCanvas));
            if (await ReadPictureBytesAsync(file) is not { } bytes)
            {
                return;
            }

            if (_source?.HitTestBlock(_pageIndex, x, y) is { } hit && _source.IsImageBlock(hit))
            {
                await ReplacePictureFromBytesAsync(hit, bytes, file.Name);
                return;
            }

            await PlacePictureAsync(bytes, file.Name, (x, y));
        }
        catch (Exception ex)
        {
            Announce(
                "TrestleBoard could not put that file on the page. Your newsletter is unchanged. "
                + $"({ex.Message})");
        }
    }

    /// <summary>
    /// The one ingest path a picture arriving without a frame goes through, whether it was dropped
    /// or pasted: ask for its description, then put it on the page at the point given (or at the
    /// M6 default placement when there is no pointer to ask, as on the keyboard path).
    /// </summary>
    private async Task PlacePictureAsync(byte[] bytes, string fileName, (float X, float Y)? centre)
    {
        var dialog = new PhotoInsertDialog(fileName);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return;
        }

        _editor?.End();
        string? blockId = _photos!.InsertPhoto(_pageIndex, bytes, dialog.AltText, dialog.Caption, centre);
        if (blockId is null)
        {
            await ShowErrorAsync(
                "That file is not a picture",
                "TrestleBoard could not read that file as a picture. JPEG and PNG files work best.");
            return;
        }

        _frames?.Select(blockId);
        RefreshActions();
    }

    /// <summary>
    /// Ctrl+V outside a piece of writing (PLAN.md §11 M18). A picture on the clipboard goes on the
    /// page through the SAME ingest path a dropped or chosen one takes — description asked for and
    /// all — and a chosen picture frame means "put it in here" rather than "add another one".
    ///
    /// <para>A file on the clipboard is preferred over a bitmap on it: the file's own bytes land in
    /// the package untouched, where a clipboard bitmap has no file behind it and must be encoded
    /// once, here, to become one. That encode is the only one in the application, and it happens
    /// because there is nothing else to store — not to "improve" a picture the user gave us.</para>
    /// </summary>
    private async Task PastePictureAsync()
    {
        if (_photos is null || Clipboard is not { } clipboard)
        {
            return;
        }

        if (await clipboard.TryGetValueAsync(DataFormat.File) is IStorageFile file)
        {
            if (await ReadPictureBytesAsync(file) is { } fileBytes)
            {
                await PastePictureBytesAsync(fileBytes, file.Name);
            }

            return;
        }

        if (await clipboard.TryGetValueAsync(DataFormat.Bitmap) is { } bitmap)
        {
            using var buffer = new MemoryStream();
            bitmap.Save(buffer);
            await PastePictureBytesAsync(buffer.ToArray(), "the picture you copied");
            return;
        }

        Announce(
            "There is no picture to paste. Copy a picture first, or click into some writing to "
            + "paste words.");
    }

    private async Task PastePictureBytesAsync(byte[] bytes, string name)
    {
        if (_frames?.SelectedBlockId is { } selected && _photos!.IsPhoto(selected))
        {
            await ReplacePictureFromBytesAsync(selected, bytes, name);
            return;
        }

        await PlacePictureAsync(bytes, name, centre: null);
    }

    // ---- The lodge address book (PLAN.md §11 M12) ---------------------------------------------

    /// <summary>
    /// The address book, loaded the first time anything asks for it. Lazy on purpose: an elderly
    /// user who never opens the People window should never pay for reading the file, and a startup
    /// that cannot fail is a startup that does not read anything it does not need.
    ///
    /// It reads <see cref="AppPaths.RosterFile"/> rather than building the AppData path itself,
    /// which is what lets a harness be pointed somewhere else and be structurally unable to see real
    /// members' details (PLAN.md §0 rule 5).
    /// </summary>
    internal RosterService Roster => _roster ??= CreateRoster();

    private RosterService CreateRoster()
    {
        var service = new RosterService(new RosterStore(AppPaths.RosterFile));

        // The "what's next" card and the People menu both read the book, so a change to it has to
        // reach the same refresh every other change in the app goes through.
        service.Changed += (_, _) => RefreshActions();
        return service;
    }

    /// <summary>
    /// Points this window at a book of its own. Tests use it so a fictional roster never has to be
    /// written to the shared app-state root and cannot leak into the next test — the roster is the
    /// one file in the app that holds real people, and a fixture sitting in its place is exactly the
    /// confusion PLAN.md §0 rule 5 is trying to prevent.
    /// </summary>
    internal void UseRosterForTest(RosterService roster)
    {
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _roster.Changed += (_, _) => RefreshActions();
        RefreshActions();
    }

    internal async Task ShowPeopleAsync()
    {
        // M73(a), gate 26: the card's offer to write a memorial is gated on the shell's own
        // precondition for writing one, handed in rather than guessed at over there.
        var window = new PeopleWindow(Roster, () => CanWriteAMemorial);
        await window.ShowDialog(this);
        RefreshActions();

        // M55: the People window can record that a brother has passed, but it has no newsletter to
        // write a memorial in. It records the request; this opens M54's shelf with his name ready.
        foreach (string brother in window.MemorialsRequestedFor)
        {
            await OfferTheMemorialAsync(brother);
        }
    }

    /// <summary>What a headless test brings back from the import wizard; null is "stopped".</summary>
    internal RosterBook? ImportAnswerForTest { get; set; }

    /// <summary>
    /// M74 (c): the outcome follows what the wizard brought back. This method already announced
    /// "The import was stopped..." for itself, while Help - reading a bare <c>Task</c> as success -
    /// said "Done:" at the same moment: two live regions asserting opposite outcomes of one action,
    /// which is worse than either of them being wrong on its own.
    /// </summary>
    internal async Task<bool> ImportPeopleAsync()
    {
        RosterBook? result = ImportAnswerForTest;
        if (result is null && !SuppressStartupForTest)
        {
            var window = new RosterImportWindow(Roster.Book);
            await window.ShowDialog(this);
            result = window.Result;
        }

        // M73(b1), gate 27: what is said is a function of what the window returned, and both
        // answers are said. Stopping the import used to be met with silence, which reads exactly
        // like a window that did something and did not mention it.
        if (result is { } book)
        {
            Roster.Replace(book, "Import people from a file");
            Announce($"Your address book now has {book.Count} people.");
            return true;
        }

        Announce("The import was stopped. Nothing in your address book was changed.");
        return false;
    }

    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> ExportPeopleAsync()
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save your address book as a spreadsheet",
            DefaultExtension = "xlsx",
            SuggestedFileName = RosterExport.SuggestedFileName(DateTimeOffset.Now),
            FileTypeChoices = [new FilePickerFileType("Excel workbook") { Patterns = ["*.xlsx"] }],
        });

        // Only ever where the user browsed to. There is no default location beside the repository
        // or the newsletter (PLAN.md §0 rule 5).
        if (file?.TryGetLocalPath() is not { } path)
        {
            return false;
        }

        try
        {
            RosterExport.Save(Roster.Book, path);
            Announce($"Your address book was saved as {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not save your address book",
                "TrestleBoard could not write that file. It may be open in Excel. " + ex.Message);
            return false;
        }
    }

    internal void UndoPeopleChange()
    {
        string? description = Roster.UndoDescription;
        if (Roster.Undo())
        {
            Announce(description is null
                ? "The last change to your address book was taken back."
                : $"Taken back: {description.ToLowerInvariant()}.");
        }
    }

    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> RestorePeopleAsync()
    {
        var dialog = new RosterRestoreDialog(Roster.Backups());
        await dialog.ShowDialog(this);

        if (dialog.Chosen is { } backup)
        {
            bool put = Roster.Restore(backup);
            // M74 (d): the sentence follows what Restore says happened. False means the kept copy
            // could not be read and nothing was written — saying "put back" then would have the
            // user trusting an address book that never changed.
            Announce(put
                ? $"Your address book was put back as it was on {RosterRestoreDialog.Describe(backup)}. "
                    + "Undo the last change reverses this."
                : "That earlier version could not be read, so your address book was not changed. "
                    + "It is exactly as it was. Try a different earlier version.");
            return put;
        }

        return false;
    }

    /// <summary>
    /// M12's "what's next" source: a list of people is on the page and the address book is empty, so
    /// nothing can be filled in for the user. Read here because only the shell knows both halves.
    /// </summary>
    private bool RosterEmptyButNeeded()
    {
        if (_session is null || Roster.Book.Count > 0)
        {
            return false;
        }

        foreach (Core.Model.Page page in _session.Document.Pages)
        {
            foreach (Core.Model.Block block in page.Blocks)
            {
                if (block is Core.Model.WidgetBlock { WidgetType: "officersTable" or "birthdayList" or "committeeList" })
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---- Birthdays from the address book (PLAN.md §11 M13) -----------------------------------

    /// <summary>The two widget types the shell fills in from the address book (M13, M19).</summary>
    private const string BirthdayListTypeId = "birthdayList";

    private const string OfficersTableTypeId = "officersTable";

    /// <summary>
    /// Does a generated birthday list on the page disagree with the address book? Asking is all this
    /// does. PLAN.md §11 M13 makes it a hard rule that staleness never mutates the document:
    /// auto-applying on open would dirty a newsletter the user opened only to look at, trip the
    /// 60-second autosave and grow the recovery snapshot. The nudge is a caption and nothing more.
    /// </summary>
    private bool BirthdayListNeedsUpdating()
    {
        if (_session is null)
        {
            return false;
        }

        int month = _session.Document.Metadata.IssueMonth;
        foreach (string blockId in BirthdayListBlockIds())
        {
            if (TryReadBirthdayList(blockId, out BirthdayListData data)
                && BirthdayRosterProjection.IsStale(data, Roster.Book.Members, month))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// M75 (e): is there an empty birthday list on the page that the address book could fill in?
    ///
    /// <para>The other half of <see cref="BirthdayListNeedsUpdating"/>, and the half that matters on
    /// a brand-new newsletter: the list a template ships and the list the Insert wizard leaves are
    /// both <c>Manual</c>, so staleness — rightly — says nothing about them.</para>
    /// </summary>
    private bool BirthdayListCouldBeFilledIn()
    {
        if (_session is null)
        {
            return false;
        }

        int month = _session.Document.Metadata.IssueMonth;
        foreach (string blockId in BirthdayListBlockIds())
        {
            if (TryReadBirthdayList(blockId, out BirthdayListData data)
                && BirthdayRosterProjection.CouldBeFilledIn(data, Roster.Book.Members, month))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> BirthdayListBlockIds() => WidgetBlockIds(BirthdayListTypeId);

    private bool TryReadBirthdayList(string blockId, out BirthdayListData data)
    {
        data = null!;
        if (_session is null
            || !_widgetProvider.Registry.TryGet(BirthdayListTypeId, out IWidgetDefinition? definition)
            || !_session.Document.TryFindBlock(blockId, out _, out Core.Model.Block? block)
            || block is not Core.Model.WidgetBlock widget
            || widget.WidgetType != BirthdayListTypeId
            || !definition.TryReadData(widget.Data, widget.DataVersion, out object typed)
            || typed is not BirthdayListData typedData)
        {
            return false;
        }

        data = typedData;
        return true;
    }

    /// <summary>
    /// Fills the selected birthday list in from the address book, showing the three-way diff first.
    /// One sync is ONE undo step labelled in the user's words, because it commits through the same
    /// <see cref="WidgetController.ApplyWidgetData"/> the wizard uses — no new command type, so
    /// Ctrl+Z restores exactly what was printed before (PLAN.md §11 M13).
    /// </summary>
    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> SyncBirthdaysAsync()
    {
        if (_session is null || _widgets is null)
        {
            return false;
        }

        // The panel offers this beside a selected list; the "what's next" card offers it with
        // nothing selected at all, and then the shell finds the list and shows the user where it is.
        string? blockId = _frames?.SelectedBlockId;
        if (blockId is null || _widgets.GetWidgetType(blockId) != BirthdayListTypeId)
        {
            int wanted = _session.Document.Metadata.IssueMonth;

            // M75 (e) defect 2: an EMPTY list is worth finding too. Looking only for a stale one
            // meant the "what's next" card could never lead the user to the list a template ships,
            // because a template's list is manual and manual lists are never stale.
            blockId = BirthdayListBlockIds().FirstOrDefault(
                id => TryReadBirthdayList(id, out BirthdayListData d)
                    && (BirthdayRosterProjection.IsStale(d, Roster.Book.Members, wanted)
                        || BirthdayRosterProjection.CouldBeFilledIn(d, Roster.Book.Members, wanted)));
            if (blockId is null)
            {
                Announce(NothingToBringIn(wanted));
                return false;
            }

            // Page FIRST, then select. GoToPage clears the selection — deliberately, because a
            // selection carried to another page would make the panel act on something the user
            // cannot see — so doing it in the other order destroyed the very selection this line
            // exists to make. The whole point of these two lines is to show the user where the list
            // it is talking about actually is (review §14.2).
            GoToPage(PageOf(blockId));
            _frames?.Select(blockId);
        }

        if (!_widgets.CanEdit(blockId))
        {
            Announce(WidgetController.NewerVersionMessage);
            return false;
        }

        if (!TryReadBirthdayList(blockId, out BirthdayListData current))
        {
            Announce("TrestleBoard could not read what is in this birthday list, so it left it alone.");
            return false;
        }

        int month = _session.Document.Metadata.IssueMonth;
        BirthdayProjection plan = BirthdayRosterProjection.Plan(current, Roster.Book.Members, month);

        // Nothing to show is not nothing to do: the list may still be recorded against last month,
        // and leaving that behind would nag from the "what's next" card forever. The user pressed
        // the button, so the provenance is brought up to date without a dialog nobody needs.
        if (!plan.ChangesAnything)
        {
            // Still a change: the provenance stamp is written even though no name moved.
            ApplyBirthdayPlan(blockId, plan);

            // M75 (e) defect 4: "already matches your address book" having matched NOBODY is the
            // most reassuring wrong answer in the app — it is what the owner was told while his
            // July address book was being read for January. The two cases are different facts and
            // now read differently, and both name the month.
            int fromTheBook = plan.Result.Entries.Count(e => e.MemberId is { Length: > 0 } && !e.IsManual);
            Announce(fromTheBook == 0
                ? $"Nobody from your address book has a birthday in {BirthdaySyncDialog.MonthName(month)}, "
                    + "which is the month this issue is for, so there was nothing to bring in. "
                    + "Anything you typed in yourself has been left alone."
                : $"The birthday list already matches the {BirthdaySyncDialog.MonthName(month)} "
                    + "birthdays in your address book.");
            return true;
        }

        if (!await ConfirmBirthdaysAsync(plan, month, inserting: false))
        {
            Announce("The birthday list was left exactly as it was.");
            return false;
        }

        ApplyBirthdayPlan(blockId, plan);
        Announce(Describe(plan, month));
        return true;
    }

    /// <summary>
    /// M75 (e) defect 3: what the user is told when "Bring in birthdays" was pressed with nothing
    /// chosen and the shell found no list worth acting on.
    ///
    /// <para>The old sentence — "There is no birthday list on this newsletter yet. Add one from the
    /// Insert menu." — was said with a birthday list sitting on page three, because the search that
    /// preceded it looked for a <b>stale</b> list and reported its own emptiness as the page's.
    /// Three different facts get three different sentences now, and each of the two about the
    /// address book names the month.</para>
    /// </summary>
    private string NothingToBringIn(int month)
    {
        if (!BirthdayListBlockIds().Any())
        {
            return "There is no birthday list on this newsletter yet. Add one from the Insert menu.";
        }

        // M75 (f): an unreadable book is not an empty one, here as in the catalog.
        if (Roster.CouldNotBeRead)
        {
            return Editing.Actions.ActionCatalog.CouldNotReadTheAddressBook;
        }

        string monthName = BirthdaySyncDialog.MonthName(month);
        if (BirthdayRosterProjection.CountFor(Roster.Book.Members, month) == 0)
        {
            return $"Nobody in your address book has a birthday in {monthName}, which is the month "
                + "this issue is for, so the birthday list was left as it is.";
        }

        // A list that WAS filled in from the address book and still agrees with it is up to date,
        // and may be said to be. A list somebody typed is not — it was never asked, and saying
        // "already up to date" of it would be the same reassuring falsehood defect 4 is about. It
        // is left alone here because filling in a list with rows in it is a change worth choosing
        // deliberately, with the list in front of you.
        bool oneIsGenerated = BirthdayListBlockIds().Any(
            id => TryReadBirthdayList(id, out BirthdayListData d) && d.Source == BirthdayListSource.Roster);

        return oneIsGenerated
            ? $"The birthday list is already up to date with the {monthName} birthdays in your "
                + "address book."
            : $"The birthday list on this newsletter was typed in by hand, so TrestleBoard has left "
                + $"it alone. Choose it on the page first if you would like the {monthName} "
                + "birthdays brought in.";
    }

    /// <summary>
    /// At insert time only: the extra first screen. With an empty address book — or a month nobody
    /// was born in — the wizard is exactly what it has always been.
    ///
    /// <para>M75 (e) defect 5: but it no longer happens in silence. Every one of these three ways
    /// out used to return null and let the plain empty wizard open with nothing said, so a user who
    /// had just pressed "Birthdays" expecting his lodge to appear was left to work out for himself
    /// whether the address book, the month or the program was at fault. It was the month.</para>
    /// </summary>
    internal async Task<System.Text.Json.JsonElement?> OfferBirthdaysFromRosterAsync()
    {
        if (_session is null)
        {
            return null;
        }

        if (Roster.CouldNotBeRead)
        {
            Announce(
                Editing.Actions.ActionCatalog.CouldNotReadTheAddressBook
                + " The birthday list opens empty for you to type into.");
            return null;
        }

        int month = _session.Document.Metadata.IssueMonth;
        string monthName = BirthdaySyncDialog.MonthName(month);
        if (Roster.Book.Count == 0)
        {
            Announce(
                "Your address book is empty, so there are no birthdays to bring in. The birthday "
                + "list opens empty for you to type into.");
            return null;
        }

        BirthdayProjection plan = BirthdayRosterProjection.Plan(new BirthdayListData(), Roster.Book.Members, month);
        if (plan.Additions.Count == 0)
        {
            Announce(
                $"Nobody in your address book has a birthday in {monthName}, which is the month "
                + "this issue is for, so the birthday list opens empty for you to type into.");
            return null;
        }

        return await ConfirmBirthdaysAsync(plan, month, inserting: true) ? Stamped(plan) : null;
    }

    /// <summary>
    /// Set by the headless tests, which cannot stand in front of a modal window. It answers the
    /// dialog's question and nothing else — every rule about what the answer then does stays in the
    /// code the user exercises.
    /// </summary>
    internal Func<BirthdayProjection, bool>? BirthdayConfirmForTest { get; set; }

    private async Task<bool> ConfirmBirthdaysAsync(BirthdayProjection plan, int month, bool inserting) =>
        BirthdayConfirmForTest is { } answer
            ? answer(plan)
            : await BirthdaySyncDialog.AskAsync(this, plan, month, inserting);

    private void ApplyBirthdayPlan(string blockId, BirthdayProjection plan)
    {
        if (_widgets is null
            || !_widgetProvider.Registry.TryGet(BirthdayListTypeId, out IWidgetDefinition? definition))
        {
            return;
        }

        _widgets.ApplyWidgetData(
            blockId, Stamped(plan), definition.CurrentDataVersion, BirthdayRosterProjection.UndoLabel);
    }

    /// <summary>
    /// The projection is a pure function and deliberately knows nothing about the clock, so the
    /// "when" is stamped here, in the layer that already owns wall-clock time.
    /// </summary>
    private System.Text.Json.JsonElement Stamped(BirthdayProjection plan)
    {
        plan.Result.GeneratedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _widgetProvider.Registry.TryGet(BirthdayListTypeId, out IWidgetDefinition? definition);
        return definition!.WriteData(plan.Result);
    }

    /// <summary>Plain counts, in the status bar's polite live region — never "3 records synced".</summary>
    private static string Describe(BirthdayProjection plan, int month)
    {
        var parts = new List<string>();
        if (plan.Additions.Count > 0)
        {
            parts.Add(plan.Additions.Count == 1 ? "one added" : $"{plan.Additions.Count} added");
        }

        if (plan.Removals.Count > 0)
        {
            parts.Add(plan.Removals.Count == 1 ? "one taken away" : $"{plan.Removals.Count} taken away");
        }

        if (plan.Updates.Count > 0)
        {
            parts.Add(plan.Updates.Count == 1
                ? "one brought up to date"
                : $"{plan.Updates.Count} brought up to date");
        }

        string monthName = BirthdaySyncDialog.MonthName(month);
        return parts.Count == 0
            ? $"The {monthName} birthday list is up to date."
            : $"The {monthName} birthday list now has {string.Join(", ", parts)}. Press Ctrl+Z to take it back.";
    }

    // ---- Officers from the address book (PLAN.md §11 M19) ------------------------------------

    /// <summary>
    /// Does a generated officers table on the page disagree with the address book? Asking is all
    /// this does — M13's hard rule carries over word for word: staleness never mutates the document.
    /// </summary>
    private bool OfficersTableNeedsUpdating()
    {
        if (_session is null)
        {
            return false;
        }

        foreach (string blockId in WidgetBlockIds(OfficersTableTypeId))
        {
            if (TryReadOfficers(blockId, out OfficersTableData data)
                && OfficersRosterProjection.IsStale(data, Roster.Book.Members))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// "14 July 2026" when the selected widget was filled in from the address book, an empty string
    /// when it was but the stamp cannot be read, and null when it was typed by hand. The panel and
    /// the widget editor both print this, so a generated list finally SAYS it is one (M19).
    /// </summary>
    private string? FilledInFromRoster(string? blockId)
    {
        if (blockId is null || _widgets is null)
        {
            return null;
        }

        return _widgets.GetWidgetType(blockId) switch
        {
            OfficersTableTypeId when TryReadOfficers(blockId, out OfficersTableData officers)
                && officers.Source == OfficersTableSource.Roster =>
                OfficersSyncDialog.WhenText(officers.GeneratedUtc),
            BirthdayListTypeId when TryReadBirthdayList(blockId, out BirthdayListData birthdays)
                && birthdays.Source == BirthdayListSource.Roster =>
                OfficersSyncDialog.WhenText(birthdays.GeneratedUtc),
            _ => null,
        };
    }

    /// <summary>The officers table on a block, or null. The screenshot harness builds a projection
    /// against a fictional book with it; nothing in the shipping app goes through here.</summary>
    internal OfficersTableData? ReadOfficersForTest(string blockId) =>
        TryReadOfficers(blockId, out OfficersTableData data) ? data : null;

    private bool TryReadOfficers(string blockId, out OfficersTableData data)
    {
        data = null!;
        if (_session is null
            || !_widgetProvider.Registry.TryGet(OfficersTableTypeId, out IWidgetDefinition? definition)
            || !_session.Document.TryFindBlock(blockId, out _, out Core.Model.Block? block)
            || block is not Core.Model.WidgetBlock widget
            || widget.WidgetType != OfficersTableTypeId
            || !definition.TryReadData(widget.Data, widget.DataVersion, out object typed)
            || typed is not OfficersTableData typedData)
        {
            return false;
        }

        data = typedData;
        return true;
    }

    /// <summary>
    /// Fills the selected officers table in from the address book, showing the per-office diff
    /// first. One sync is ONE undo step labelled in the user's words, because it commits through the
    /// same <see cref="WidgetController.ApplyWidgetData"/> the wizard uses — no new command type, so
    /// Ctrl+Z restores exactly what was printed before (PLAN.md §11 M19).
    /// </summary>
    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> SyncOfficersAsync()
    {
        if (_session is null || _widgets is null)
        {
            return false;
        }

        // The panel offers this beside a selected table; the "what's next" card offers it with
        // nothing selected at all, and then the shell finds the table and shows the user where it is.
        string? blockId = _frames?.SelectedBlockId;
        if (blockId is null || _widgets.GetWidgetType(blockId) != OfficersTableTypeId)
        {
            blockId = WidgetBlockIds(OfficersTableTypeId).FirstOrDefault();
            if (blockId is null)
            {
                Announce("There is no officers table on this newsletter yet. Add one from the Insert menu.");
                return false;
            }

            // Page FIRST, then select. GoToPage clears the selection — deliberately, because a
            // selection carried to another page would make the panel act on something the user
            // cannot see — so doing it in the other order destroyed the very selection this line
            // exists to make. The whole point of these two lines is to show the user where the list
            // it is talking about actually is (review §14.2).
            GoToPage(PageOf(blockId));
            _frames?.Select(blockId);
        }

        if (!_widgets.CanEdit(blockId))
        {
            Announce(WidgetController.NewerVersionMessage);
            return false;
        }

        if (!TryReadOfficers(blockId, out OfficersTableData current))
        {
            Announce("TrestleBoard could not read what is in this officers table, so it left it alone.");
            return false;
        }

        OfficersProjection plan = OfficersRosterProjection.Plan(current, Roster.Book.Members);

        // Nothing to show is not nothing to do: the table may never have been stamped as generated,
        // and leaving that behind would nag from the "what's next" card forever. The user pressed
        // the button, so the provenance is brought up to date without a dialog nobody needs.
        if (!plan.HasAnythingToSay)
        {
            // Still a change: the provenance stamp is written even though no office moved.
            ApplyOfficerDecisions(blockId, current, plan, plan.DefaultDecisions);
            Announce("The officers table already matches your address book.");
            return true;
        }

        OfficersSyncAnswer answer = await AskAboutOfficersAsync(plan, inserting: false);
        if (!answer.Confirmed)
        {
            Announce("The officers table was left exactly as it was.");
            return false;
        }

        ApplyOfficerDecisions(blockId, current, plan, answer.Decisions);
        WritePhoneNumbersBack(answer.PhoneWriteBacks);
        Announce(Describe(answer.Decisions, plan));
        return true;
    }

    /// <summary>
    /// At insert time only: the extra first screen, exactly as M13 offers one for birthdays. With an
    /// empty address book — or a book nobody has written offices into — the wizard is exactly what it
    /// has always been, and the user is asked nothing.
    /// </summary>
    private async Task<System.Text.Json.JsonElement?> OfferOfficersFromRosterAsync()
    {
        if (_session is null
            || Roster.Book.Count == 0
            || !_widgetProvider.Registry.TryGet(OfficersTableTypeId, out IWidgetDefinition? definition))
        {
            return null;
        }

        var empty = (OfficersTableData)definition.CreateEmptyData(
            WidgetController.SeedFrom(_session.Document));
        OfficersProjection plan = OfficersRosterProjection.Plan(empty, Roster.Book.Members);
        if (plan.Proposals.Count == 0)
        {
            return null;
        }

        OfficersSyncAnswer answer = await AskAboutOfficersAsync(plan, inserting: true);
        return answer.Confirmed
            ? StampedOfficers(OfficersRosterProjection.Apply(empty, answer.Decisions, plan.Fingerprint))
            : null;
    }

    /// <summary>What the officers dialog came back with, flattened so a test can stand in for it.</summary>
    internal sealed record OfficersSyncAnswer(
        bool Confirmed,
        IReadOnlyList<OfficerDecision> Decisions,
        IReadOnlyList<(string MemberId, string Phone)> PhoneWriteBacks);

    /// <summary>
    /// Set by the headless tests, which cannot stand in front of a modal window. It answers the
    /// dialog's question and nothing else — every rule about what the answer then does stays in the
    /// code the user exercises.
    /// </summary>
    internal Func<OfficersProjection, OfficersSyncAnswer>? OfficersConfirmForTest { get; set; }

    private async Task<OfficersSyncAnswer> AskAboutOfficersAsync(OfficersProjection plan, bool inserting)
    {
        if (OfficersConfirmForTest is { } answer)
        {
            return answer(plan);
        }

        OfficersSyncDialog dialog = await OfficersSyncDialog.AskAsync(this, plan, inserting);
        return new OfficersSyncAnswer(dialog.Confirmed, dialog.Decisions, dialog.PhoneWriteBacks);
    }

    private void ApplyOfficerDecisions(
        string blockId,
        OfficersTableData current,
        OfficersProjection plan,
        IReadOnlyList<OfficerDecision> decisions)
    {
        if (_widgets is null
            || !_widgetProvider.Registry.TryGet(OfficersTableTypeId, out IWidgetDefinition? definition))
        {
            return;
        }

        OfficersTableData result = OfficersRosterProjection.Apply(current, decisions, plan.Fingerprint);
        _widgets.ApplyWidgetData(
            blockId,
            StampedOfficers(result),
            definition.CurrentDataVersion,
            OfficersRosterProjection.UndoLabel);
    }

    /// <summary>
    /// The projection is a pure function and deliberately knows nothing about the clock, so the
    /// "when" is stamped here, in the layer that already owns wall-clock time.
    /// </summary>
    private System.Text.Json.JsonElement StampedOfficers(OfficersTableData data)
    {
        data.GeneratedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _widgetProvider.Registry.TryGet(OfficersTableTypeId, out IWidgetDefinition? definition);
        return definition!.WriteData(data);
    }

    /// <summary>Plain counts, in the status bar's polite live region — never "12 records synced".</summary>
    private static string Describe(IReadOnlyList<OfficerDecision> decisions, OfficersProjection plan)
    {
        int filled = decisions.Count(d => d.Chosen is not null);
        int cleared = decisions.Count(d => d.Chosen is null);
        int leftAlone = plan.Proposals.Count - decisions.Count;

        var parts = new List<string>();
        if (filled > 0)
        {
            parts.Add(filled == 1 ? "one office filled in" : $"{filled} offices filled in");
        }

        if (cleared > 0)
        {
            parts.Add(cleared == 1 ? "one office now vacant" : $"{cleared} offices now vacant");
        }

        if (leftAlone > 0)
        {
            parts.Add(leftAlone == 1 ? "one left as it was" : $"{leftAlone} left as they were");
        }

        return parts.Count == 0
            ? "The officers table is up to date."
            : $"The officers table now has {string.Join(", ", parts)}. Press Ctrl+Z to take it back.";
    }

    private IEnumerable<string> WidgetBlockIds(string typeId)
    {
        if (_session is null)
        {
            yield break;
        }

        foreach (Core.Model.Page page in _session.Document.Pages)
        {
            foreach (Core.Model.Block block in page.Blocks)
            {
                if (block is Core.Model.WidgetBlock widget && widget.WidgetType == typeId)
                {
                    yield return widget.Id;
                }
            }
        }
    }

    private int PageOf(string blockId)
    {
        if (_session is null)
        {
            return _pageIndex;
        }

        for (int i = 0; i < _session.Document.Pages.Count; i++)
        {
            if (_session.Document.Pages[i].Blocks.Any(b => b.Id == blockId))
            {
                return i;
            }
        }

        return _pageIndex;
    }

    // ---- Look and size (PLAN.md §6, docs/M9-spec.md §4) -------------------------------------

    internal async Task ShowSettingsAsync()
    {
        var dialog = new SettingsDialog(_settings);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return;
        }

        // Both strips of chrome the user can put away are carried across by hand: the settings
        // window does not offer either of them, so its result holds the DEFAULT for each, and
        // taking it whole would silently reopen a panel or a rail somebody had closed.
        _settings = dialog.Result with
        {
            ShowActionPanel = _settings.ShowActionPanel,
            ShowPageRail = _settings.ShowPageRail,
        };

        // M73(e): Save was `void` with an empty catch, and this said "Saved." whatever happened.
        // Raise the text to 150%, be told it saved, find it back at 100% next time — and the one
        // setting somebody most needs to keep is the one they cannot read the screen without.
        bool saved = _settings.Save();
        ApplySettings(_settings);
        Announce(saved
            ? "Saved. You can change this again from View, How things look."
            : "TrestleBoard has changed how it looks for now, but it could not write the change "
              + "down, so it will be back to how it was the next time you open it. Your newsletter "
              + "is not affected.");
    }

    /// <summary>
    /// Gives every toolbar button its glyph (PLAN.md §11 M16).
    ///
    /// <para>Done here rather than in the markup so that the label a button shows and the glyph it
    /// carries cannot disagree: the label stays in <c>MainWindow.axaml</c> where a reader expects it,
    /// and the glyph is looked up from the same <c>Tag</c> the click handler dispatches on. A button
    /// whose action has no entry in <see cref="ActionIcons"/> is left exactly as the markup wrote
    /// it — text only, which is what most of the application still is.</para>
    ///
    /// <para><b>No toolbar button is added or removed.</b> M11 trimmed the toolbar from eighteen
    /// controls to nine and M14 recorded that it must not grow back. What changes is that
    /// <c>↶ ↷ ◀ ▶ − +</c> stop being Unicode characters rendered in whatever fallback font happens
    /// to carry them — which is also why those six buttons never shared a baseline with the other
    /// three — and become geometry that scales with the chrome. Every
    /// <c>AutomationProperties.Name</c> is untouched, so nothing a screen reader says changes.</para>
    /// </summary>
    private void DressToolbar()
    {
        // M76(d): and the canvas footer with it. The four view controls kept their glyphs, their
        // tooltips and the M37 pressable affordance when they moved off the bar — a control that
        // loses its dressing by changing which strip it stands on is a control that got worse for
        // a layout reason, which is not a trade this application makes.
        IEnumerable<Button> dressed = ToolbarScale.GetLogicalDescendants().OfType<Button>()
            .Concat(CanvasFooterScale.GetLogicalDescendants().OfType<Button>());

        foreach (Button button in dressed)
        {
            // M37: the affordance that says "you can press this". Applied in the loop that already
            // walks the toolbar, so a button added to the XAML cannot miss it — and before the
            // early-out below, because a button without an icon still needs to look pressable.
            // M76(e): "Make the PDF" takes this class like the rest and keeps the primary look
            // anyway, because the Theme it sets in the markup is a LOCAL value and the "action"
            // class sets Theme through a Style — a local value wins. It is still counted as a
            // button the app made, which is all this class is asked to prove.
            button.Action();

            if (button is not { Tag: string actionId, Content: string label })
            {
                continue;
            }

            // M45, review §14.3: there was not one ToolTip.Tip anywhere in the App project. The
            // toolbar is where it earns its keep — the buttons are short words beside a glyph, and
            // the catalog already holds a sentence saying what each command DOES, written for this
            // audience. Nothing new is authored here; the tooltip is the description that a screen
            // reader has been getting since M11, finally shown to people who use a mouse.
            if (ActionCatalog.TryGet(actionId, out EditorAction? action))
            {
                ToolTip.SetTip(button, TooltipFor(action));
            }

            if (ActionIcons.ForAction(actionId) is not { } glyph)
            {
                continue;
            }

            button.Content = new IconText(glyph, label, labelSize: 16);
        }
    }

    /// <summary>
    /// M45: the sentence a toolbar button shows on hover — what the command does, and the keys that
    /// do it without the mouse.
    ///
    /// <para>The shortcut is included deliberately. A tooltip is read by somebody who is already
    /// using the mouse, and it is the one moment they are looking straight at a place that can
    /// teach them the keyboard instead.</para>
    /// </summary>
    internal static string TooltipFor(EditorAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return action.DisplayGesture is { Length: > 0 } keys
            ? $"{action.ShortDescription} ({keys})"
            : action.ShortDescription;
    }

    /// <summary>
    /// Chrome only. The CANVAS is deliberately left out of the scale transform and out of the theme:
    /// the page is a piece of paper, white in dark mode too, and its own zoom is a separate control
    /// (docs/M9-spec.md §4). The action panel IS scaled — it is chrome, and a 16pt panel beside a
    /// 32pt menu bar would be the one part of the window an elderly user could not read.
    /// </summary>
    internal void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings.Normalised();

        if (Avalonia.Application.Current is { } application)
        {
            ThemeManager.Apply(application, _settings);
        }

        double scale = _settings.UiScale;
        // M76(d): CanvasFooterScale joins them. The footer is chrome ABOUT the page, not the page —
        // 16pt labels on 44px targets, which is exactly the text this setting exists to raise. The
        // scroller between the toolbar and the footer is still deliberately left out.
        // M76(g): RailScale joins them for the same reason CanvasFooterScale did — the rail is
        // chrome ABOUT the page rather than the page, and its 16pt labels are exactly the text this
        // setting exists to raise.
        foreach (LayoutTransformControl host in new[]
                 { MenuScale, ToolbarScale, StatusScale, PanelScale, CanvasFooterScale, RailScale })
        {
            // A LAYOUT transform, not a render transform: scaled chrome has to take up the room it
            // now occupies, or the buttons simply overlap each other.
            host.LayoutTransform = scale == 1d ? null : new Avalonia.Media.ScaleTransform(scale, scale);
        }

        ApplyPanelVisibility();
        ApplyRailVisibility();
        PageCanvas.ShowSpelling = _settings.ShowSpelling;
    }

    internal AppSettings SettingsForTest => _settings;

    /// <summary>Where the page view is scrolled to, so M50's zoom-anchor test can watch it move.</summary>
    internal Vector ScrollerOffsetForTest => CanvasScroller.Offset;

    /// <summary>Headless tests drive the shell directly; they must not get a modal start screen.</summary>
    internal static bool SuppressStartupForTest { get; set; } = true;

    internal Task RunStartupForTest() => RunStartupAsync();

    /// <summary>The chrome hosts the UI scale is applied to; the canvas is deliberately not one.</summary>
    internal LayoutTransformControl[] ChromeScaleHostsForTest =>
        [MenuScale, ToolbarScale, StatusScale, PanelScale, CanvasFooterScale, RailScale];

    // ---- Pages and flow (docs/M8-spec.md §2/§3) ---------------------------------------------

    internal void AddPage()
    {
        if (_pages is not null)
        {
            _pages.AddPage(_pageIndex);
            GoToPage(_pageIndex + 1);
        }
    }

    internal void RemovePage()
    {
        // M39: the caret goes before the page it is standing on does. Leaving a text session open
        // over a frame that is about to be deleted is what makes every question asked afterwards —
        // "is this a text frame?", "can it be linked?" — a question about a block that no longer
        // exists.
        _editor?.End();

        if (_pages is not null && _pages.RemovePage(_pageIndex))
        {
            GoToPage(Math.Min(_pageIndex, _pages.PageCount - 1));
        }
    }

    internal void MovePage(int delta)
    {
        if (_pages is not null && _pages.MovePage(_pageIndex, _pageIndex + delta))
        {
            GoToPage(_pageIndex + delta);
        }
    }

    // ---- Widgets (docs/M7-spec.md §7/§8) ----------------------------------------------------

    /// <summary>
    /// Insert puts an EMPTY widget on the page and opens its wizard straight away: inserting means
    /// "I want to fill this in", and the box is already visible while the questions are answered.
    /// </summary>
    internal async Task InsertWidgetAsync(string typeId)
    {
        if (_widgets is null)
        {
            return;
        }

        string blockId = _widgets.InsertWidget(_pageIndex, typeId);
        _frames?.Select(blockId);
        RefreshActions();

        // M13: one extra screen at the front, and only when there is something to offer. The answer
        // seeds the wizard rather than committing on its own, so filling the list in and pressing
        // "Save it" is still a single undo step.
        System.Text.Json.JsonElement? seeded = typeId switch
        {
            BirthdayListTypeId => await OfferBirthdaysFromRosterAsync(),
            OfficersTableTypeId => await OfferOfficersFromRosterAsync(),
            _ => null,
        };

        await RunWizardAsync(blockId, grid: false, seeded, justInserted: true);
    }

    /// <returns>M74 (c): false where the user backed out, so no surface built on <c>ActionOutcome</c>
    /// says "Done" about work that was declined.</returns>
    internal async Task<bool> EditWidgetAsync(bool grid) =>
        WidgetTarget() is { } blockId && await RunWizardAsync(blockId, grid);

    /// <summary>
    /// The item "Change what this says" acts on: the chosen one, or — when nothing is chosen and the
    /// "what's next" card has just said the cover has no meeting date — the cover heading itself,
    /// which is turned to and chosen first so the user can see what they are filling in. Written in
    /// the spirit of <see cref="PictureTarget"/>, and, per M70, it says what it picked rather than
    /// quietly opening an editor on something the user never chose.
    /// </summary>
    private string? WidgetTarget()
    {
        if (_frames?.SelectedBlockId is { } selected)
        {
            return selected;
        }

        if (DatelessCoverHeading() is not { } cover)
        {
            return null;
        }

        GoToPage(cover.PageIndex);
        _editor?.End();
        _frames?.Select(cover.BlockId);
        Announce(
            $"Nothing was chosen, so TrestleBoard chose the cover heading on page "
            + $"{cover.PageIndex + 1} and is opening it for you. The meeting date goes in it.");
        return cover.BlockId;
    }

    /// <summary>
    /// M70(c): straight after any widget edit this is ALWAYS a no-op, because
    /// <see cref="WidgetController.ApplyWidgetData"/> has already resized the box to what is in it.
    /// It used to discard the "nothing to do" and say nothing at all, which is the commonest way
    /// this command is pressed.
    /// </summary>
    internal void FitWidgetToContents()
    {
        if (_frames?.SelectedBlockId is { } blockId
            && _widgets is not null
            && !_widgets.FitToContents(blockId))
        {
            Announce("This box is already exactly as tall as what is in it.");
        }
    }

    /// <summary>
    /// Both editors run the SAME session and commit through the SAME controller call, so "one
    /// wizard run = one undo step" holds however the user got there (docs/M7-spec.md §7.3).
    /// </summary>
    /// <summary>
    /// Presses the wizard's Cancel for a test. Neither wizard window can be answered headlessly, and
    /// cancel is the path M73(c) is about.
    /// </summary>
    internal bool CancelTheWizardForTest { get; set; }

    /// <summary>
    /// What to say when a wizard is cancelled. M73(c): the sentence is a function of which path
    /// actually ran, not of the insert path's assumption. Only insert put a box on the page, so
    /// only insert may tell the user that Ctrl+Z will take one off it — on the re-edit path Ctrl+Z
    /// would take back whatever they last did instead, which M71 turned into a common ending by
    /// multiplying the routes to re-edit.
    /// </summary>
    private void SayTheWizardWasCancelled(bool justInserted) => Announce(justInserted
        ? "Nothing was filled in yet. Press Ctrl+Z to take it back off the page."
        : "Nothing was changed. This box is exactly as it was.");

    private async Task<bool> RunWizardAsync(
        string blockId,
        bool grid,
        System.Text.Json.JsonElement? seeded = null,
        bool justInserted = false)
    {
        if (_widgets is null || _session is null || !_widgets.CanEdit(blockId))
        {
            RefreshActions();
            return false;
        }

        if (CreateSession(blockId, seeded) is not { } wizard)
        {
            return false;
        }

        IReadOnlyList<PersonSuggestion> people = PeopleForWizards();

        // M19: the widget editor says where the list came from, in the same words the panel uses.
        string? banner = FilledInFromRoster(blockId) is { } filledIn
            ? ActionCatalog.DescribeFilledIn(filledIn)
            : null;

        // M75 (a): the cover heading's wizard now asks which issue this is, and that answer belongs
        // to the document. Reading it here — off the same session both windows edited — is what
        // makes "one wizard run, one undo step" survive the answer landing in two places.
        void Commit(System.Text.Json.JsonElement data, int dataVersion, string undoLabel) =>
            _widgets.ApplyWidgetData(
                blockId,
                WithTheMeetingDateFilledIn(wizard, data),
                dataVersion,
                undoLabel,
                IssueDateCommandFrom(wizard));

        // M75: the one way a test can answer the issue question. Neither wizard window can be
        // answered headlessly, and this fills the real session's real fields and commits through
        // the real path — only the Avalonia window is skipped.
        if (AnswerTheIssueWizardForTest is { } canned && IsCoverHeading(blockId))
        {
            if (!TryAnswerTheIssueWizard(wizard, canned, out System.Text.Json.JsonElement answered, out int version))
            {
                return false;
            }

            Commit(answered, version, wizard.UndoLabel);
            RefreshActions();
            return true;
        }

        if (grid)
        {
            if (CancelTheWizardForTest)
            {
                SayTheWizardWasCancelled(justInserted);
                return false;
            }

            var window = new WidgetGridWindow(wizard, people, banner);
            await window.ShowDialog(this);
            if (!window.Confirmed)
            {
                // M73(c): this branch used to say nothing at all where its twin below spoke —
                // asymmetric silence in the same `if`, which reads like a window that did
                // something and did not mention it.
                SayTheWizardWasCancelled(justInserted);
                return false;
            }

            Commit(window.Data, window.DataVersion, window.UndoLabel);
        }
        else
        {
            if (CancelTheWizardForTest)
            {
                SayTheWizardWasCancelled(justInserted);
                return false;
            }

            var window = new WizardWindow(wizard, people, banner);
            await window.ShowDialog(this);
            if (!window.Confirmed)
            {
                SayTheWizardWasCancelled(justInserted);
                return false;
            }

            Commit(window.Data, window.DataVersion, window.UndoLabel);
            WritePhoneNumbersBack(window.PhoneWriteBacks);
        }

        RefreshActions();
        return true;
    }

    // ---- Which issue is this? (PLAN.md §11 M75) -----------------------------------------------

    private const string CoverBannerTypeId = "coverBanner";

    /// <summary>
    /// M75: what a test types into the cover heading's issue question, in place of the window.
    ///
    /// <para>The session, the bindings, the validators, the commit and the metadata write-back are
    /// all the real ones — the wizard's own <c>TryCommit</c> refuses these answers if they do not
    /// pass, exactly as pressing "Save it" would. A null field is left at whatever the wizard
    /// pre-filled, which is how the carry-forward pre-fill can be tested by not correcting it.</para>
    /// </summary>
    internal sealed record IssueAnswerForTest(
        int? Month = null,
        int? Year = null,
        string? MeetingRule = null,
        string? LodgeName = null);

    /// <summary>Set by tests in place of the cover wizard. See <see cref="IssueAnswerForTest"/>.</summary>
    internal IssueAnswerForTest? AnswerTheIssueWizardForTest { get; set; }

    /// <summary>
    /// "Which issue is this?…" — M75 (a), and the owner's second ruling in code: the ask lives in
    /// the cover heading's own wizard, not in a properties dialog of its own.
    ///
    /// <para>It turns to the cover heading, chooses it so the user can see what they are answering
    /// about, and opens the wizard on its first screen — the same window "Change what this says…"
    /// opens, because a second place to set the date is a second place to forget.</para>
    /// </summary>
    /// <returns>False when the user backed out, per M74's contract.</returns>
    internal async Task<bool> AskWhichIssueThisIsAsync()
    {
        if (CoverHeading() is not { } cover)
        {
            return false;
        }

        GoToPage(cover.PageIndex);
        _editor?.End();
        _frames?.Select(cover.BlockId);
        return await RunWizardAsync(cover.BlockId, grid: false);
    }

    /// <summary>The first cover heading in the newsletter, and the page it stands on.</summary>
    private (string BlockId, int PageIndex)? CoverHeading()
    {
        if (_session is null)
        {
            return null;
        }

        for (int i = 0; i < _session.Document.Pages.Count; i++)
        {
            foreach (Core.Model.Block block in _session.Document.Pages[i].Blocks)
            {
                if (block is Core.Model.WidgetBlock { WidgetType: CoverBannerTypeId } cover)
                {
                    return (cover.Id, i);
                }
            }
        }

        return null;
    }

    private bool IsCoverHeading(string blockId) =>
        string.Equals(_widgets?.GetWidgetType(blockId), CoverBannerTypeId, StringComparison.Ordinal);

    /// <summary>
    /// The <see cref="Core.Commands.SetMetadataCommand"/> a finished cover wizard produces, or null
    /// when this wizard was not the cover heading's (M75 (a) — the command's first production use in
    /// the sixty-odd milestones since M2 put it in Core).
    ///
    /// <para><b>(d) rides along.</b> <c>Metadata.MeetingRule</c> had the same write-back hole: it
    /// was set only by the two sample documents, while the cover banner carried its own editable
    /// copy that never flowed back — so <c>CarryForward.RecomputeMeetingDates</c> always failed to
    /// parse and took the <c>ClearMeetingDates</c> branch, <b>blanking the cover date</b> for every
    /// real user who started next month's issue. The rule the user typed into the banner is written
    /// to the metadata here. A blank one never overwrites a rule the document already had: leaving
    /// a field empty is not the same as asking for something to be forgotten.</para>
    ///
    /// <para><b>And the third one, found by the census pass.</b> <c>Metadata.LodgeName</c> had the
    /// hole in exactly the shape above: asked for on the banner, kept on the banner, and set on the
    /// newsletter itself only by the two samples. So a newsletter started from a template emailed the
    /// whole lodge under a subject with no lodge in it and produced a PDF with a blank Author. The
    /// same write-through, under the same blank-never-erases rule.</para>
    /// </summary>
    private Core.Commands.SetMetadataCommand? IssueDateCommandFrom(WizardSession wizard)
    {
        if (_session is null || IssueAnswers(wizard) is not { } answers)
        {
            return null;
        }

        Core.Model.DocumentMetadata updated = _session.Document.Metadata.Clone();
        updated.IssueMonth = answers.Month;
        updated.IssueYear = answers.Year;
        updated.IssueDateChosen = true;
        if (TypedMeetingRule(wizard) is { } rule)
        {
            updated.MeetingRule = rule;
        }

        if (TypedLodgeName(wizard) is { } lodge)
        {
            updated.LodgeName = lodge;
        }

        return new Core.Commands.SetMetadataCommand(updated);
    }

    /// <summary>The month and year the wizard holds, or null when it is not a cover wizard or the
    /// two questions have not been answered readably.</summary>
    private static (int Month, int Year)? IssueAnswers(WizardSession wizard)
    {
        if (!wizard.TryGetAnswer(CoverBannerDefinition.IssueMonthFieldKey, out string monthName)
            || !CoverBannerDefinition.TryReadMonth(monthName, out int month)
            || !wizard.TryGetAnswer(CoverBannerDefinition.IssueYearFieldKey, out string yearText)
            || !int.TryParse(
                yearText.Trim(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out int year))
        {
            return null;
        }

        return (month, year);
    }

    /// <summary>
    /// The lodge name the wizard holds, or null when it is blank.
    ///
    /// <para>Null rather than "" for the reason the meeting rule is: a blank answer never overwrites
    /// a name the newsletter already has. The wizard requires this field, so a blank one cannot in
    /// fact arrive through it today — the guard is here because the rule is the rule, and the next
    /// field to be written back through this method may well be optional.</para>
    /// </summary>
    private static string? TypedLodgeName(WizardSession wizard) =>
        wizard.TryGetAnswer(CoverBannerDefinition.LodgeNameFieldKey, out string lodge)
            && !string.IsNullOrWhiteSpace(lodge)
                ? lodge.Trim()
                : null;

    private static string? TypedMeetingRule(WizardSession wizard) =>
        wizard.TryGetAnswer(CoverBannerDefinition.MeetingRuleFieldKey, out string rule)
            && !string.IsNullOrWhiteSpace(rule)
                ? rule.Trim()
                : null;

    /// <summary>
    /// M75 (d), the other half: with the issue month now known and the meeting rule now written
    /// down, the printed date on the cover can be worked out — <b>if the user has not written one
    /// themselves</b>.
    ///
    /// <para>Only a blank one is filled in. <c>meetingDateText</c> is printed prose the committee
    /// owns ("July 7th", no year), and the metadata being the authority for <i>which issue this is</i>
    /// does not make it the author of what the cover says. Overwriting a date somebody typed in the
    /// very same wizard would be the mirror image of the defect this milestone is about.</para>
    /// </summary>
    private static System.Text.Json.JsonElement WithTheMeetingDateFilledIn(
        WizardSession wizard,
        System.Text.Json.JsonElement data)
    {
        if (IssueAnswers(wizard) is not { } answers
            || TypedMeetingRule(wizard) is not { } rule
            || System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())
                is not System.Text.Json.Nodes.JsonObject payload)
        {
            return data;
        }

        if (payload[CoverBannerDefinition.MeetingDateFieldKey]?.GetValue<string>() is { } already
            && !string.IsNullOrWhiteSpace(already))
        {
            return data;
        }

        if (CarryForward.MeetingDateTextFor(rule, answers.Year, answers.Month) is not { } text)
        {
            return data;
        }

        payload[CoverBannerDefinition.MeetingDateFieldKey] = System.Text.Json.Nodes.JsonValue.Create(text);
        return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(payload.ToJsonString());
    }

    /// <summary>Fills the issue fields in and commits, the way pressing "Save it" would.</summary>
    private static bool TryAnswerTheIssueWizard(
        WizardSession wizard,
        IssueAnswerForTest answer,
        out System.Text.Json.JsonElement data,
        out int dataVersion)
    {
        if (answer.Month is { } month)
        {
            wizard.TrySetAnswer(CoverBannerDefinition.IssueMonthFieldKey, CoverBannerDefinition.MonthName(month));
        }

        if (answer.Year is { } year)
        {
            wizard.TrySetAnswer(
                CoverBannerDefinition.IssueYearFieldKey,
                year.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (answer.MeetingRule is { } rule)
        {
            wizard.TrySetAnswer(CoverBannerDefinition.MeetingRuleFieldKey, rule);
        }

        // The banner's own questions are still the banner's: a template ships with no lodge name on
        // it, and the wizard refuses to finish without one exactly as it would for a user.
        if (answer.LodgeName is { } lodge)
        {
            wizard.TrySetAnswer(CoverBannerDefinition.LodgeNameFieldKey, lodge);
        }

        return wizard.TryCommit(out data, out dataVersion, out _);
    }

    /// <summary>
    /// M75 (b): every route that starts an issue asks which issue it is, <b>before</b> the newsletter
    /// comes up on screen.
    ///
    /// <para>Asking first is what makes "cancel means no new issue" true rather than nearly true:
    /// there is nothing to undo, nothing half-open, and the route simply answers false the way M74's
    /// contract says a backed-out command must. The wizard needs no open document — it works from
    /// the package's own cover heading and its own metadata.</para>
    ///
    /// <para>It asks only when the newsletter does not already know. A shipped template, a saved
    /// template and a carried-forward issue all arrive not knowing: the first two are reset by
    /// <c>NewsletterTemplate.ClearIssueDate</c>, and the third by <c>CarryForward.BumpIssueDate</c>,
    /// which pre-fills next month as a suggestion and no longer counts it as an answer.</para>
    /// </summary>
    private async Task<bool> AskWhichIssueBeforeStartingAsync(TboardPackage package)
    {
        if (package.Document.Metadata.HasIssueDate)
        {
            return true;
        }

        // No cover heading means nowhere to ask — a user template somebody stripped one out of. The
        // newsletter still opens: the "what's next" card leads with the question and every command
        // that needs the date refuses with a reason, which is a better answer than a dead end.
        if (FindCoverHeading(package.Document) is not { } cover
            || !_widgetProvider.Registry.TryGet(CoverBannerTypeId, out IWidgetDefinition? definition))
        {
            return true;
        }

        WizardSession wizard = WizardSession.Create(
            definition,
            cover.Data,
            cover.DataVersion,
            WidgetController.SeedFrom(package.Document));

        System.Text.Json.JsonElement data;
        int dataVersion;
        if (AnswerTheIssueWizardForTest is { } canned)
        {
            if (!TryAnswerTheIssueWizard(wizard, canned, out data, out dataVersion))
            {
                return false;
            }
        }
        else
        {
            if (CancelTheWizardForTest)
            {
                return false;
            }

            var window = new WizardWindow(wizard, PeopleForWizards(), null);
            await window.ShowDialog(this);
            if (!window.Confirmed)
            {
                return false;
            }

            data = window.Data;
            dataVersion = window.DataVersion;
        }

        if (IssueAnswers(wizard) is not { } answers)
        {
            return false;
        }

        cover.Data = WithTheMeetingDateFilledIn(wizard, data);
        cover.DataVersion = dataVersion;
        package.Document.Metadata.IssueMonth = answers.Month;
        package.Document.Metadata.IssueYear = answers.Year;
        package.Document.Metadata.IssueDateChosen = true;
        if (TypedMeetingRule(wizard) is { } rule)
        {
            package.Document.Metadata.MeetingRule = rule;
        }

        if (TypedLodgeName(wizard) is { } lodge)
        {
            package.Document.Metadata.LodgeName = lodge;
        }

        return true;
    }

    /// <summary>The cover heading block of a package that is not open yet.</summary>
    private static Core.Model.WidgetBlock? FindCoverHeading(Core.Model.Document document)
    {
        foreach (Core.Model.Page page in document.Pages)
        {
            foreach (Core.Model.Block block in page.Blocks)
            {
                if (block is Core.Model.WidgetBlock { WidgetType: CoverBannerTypeId } cover)
                {
                    return cover;
                }
            }
        }

        return null;
    }

    /// <summary>What is said when the question goes unanswered and so no newsletter is started.</summary>
    private void SayNoIssueWasStarted() => Announce(
        "No newsletter was started, because TrestleBoard was not told which issue it would be. "
        + "Nothing has changed. Try again whenever you are ready.");

    /// <summary>
    /// The names a wizard may offer (M13). Handed to the window rather than reached for by it, which
    /// is PLAN.md §5's projection rule applied to the second way people's names could have got into
    /// <c>TrestleBoard.Widgets</c>.
    /// </summary>
    internal IReadOnlyList<PersonSuggestion> PeopleForWizards() =>
        [.. Roster.Book.InListOrder().Select(m => new PersonSuggestion(m.Id, m.DisplayName, m.Phone))];

    /// <summary>
    /// The other half of the officers wizard's small favour: a phone number the user corrected on
    /// the page goes back to the address book too — but only for the brothers they ticked, and it
    /// is its own address-book change with its own undo, because Ctrl+Z never crosses that boundary
    /// (PLAN.md §11 M12).
    /// </summary>
    private void WritePhoneNumbersBack(IReadOnlyList<(string MemberId, string Phone)> writes)
    {
        int written = 0;
        foreach ((string memberId, string phone) in writes)
        {
            if (Roster.Book.Find(memberId) is not { } member)
            {
                continue;
            }

            Roster.Save(member with { Phone = phone }, $"Change {member.DisplayName}'s phone number");
            written++;
        }

        if (written > 0)
        {
            Announce(written == 1
                ? "The phone number in your address book was updated too."
                : $"{written} phone numbers in your address book were updated too.");
        }
    }

    /// <summary>
    /// Pre-filled from whatever is already on the block — that IS the re-edit path (§7.1).
    /// <paramref name="seeded"/> stands in for it exactly once, when a freshly inserted birthday
    /// list has been projected from the address book and the user is about to check it over (M13).
    /// </summary>
    internal WizardSession? CreateSession(string blockId, System.Text.Json.JsonElement? seeded = null)
    {
        if (_widgets?.GetWidgetType(blockId) is not { } typeId
            || _session is null
            || !_widgetProvider.Registry.TryGet(typeId, out IWidgetDefinition? definition))
        {
            return null;
        }

        (_, Core.Model.Block block) = _session.Document.FindBlock(blockId);
        var widget = (Core.Model.WidgetBlock)block;
        return WizardSession.Create(
            definition,
            seeded ?? widget.Data,
            seeded is null ? widget.DataVersion : definition.CurrentDataVersion,
            WidgetController.SeedFrom(_session.Document));
    }

    // ---- Navigation / zoom ------------------------------------------------------------------

    internal void GoToRelativePage(int delta) => GoToPage(_pageIndex + delta);

    /// <summary>
    /// M76 (g): <c>page.goTo</c>, the catalog's first command with a parameter.
    ///
    /// <para><b>Which page is read off the control the user pressed</b>, not off an id per page —
    /// see <see cref="ActionTarget"/> for why. A rail tile carries one; the Page menu's item cannot,
    /// because a menu item written in the XAML has no way of knowing which page somebody wants. So
    /// the menu's half of this command is the honest one: it puts the row of pages on screen and
    /// stands the keyboard on the page you are already looking at, where the arrows walk and Enter
    /// goes. That is what "go to a page" means when nobody has said which one yet.</para>
    /// </summary>
    /// <returns>Whether anything actually happened (M73(f)): pressing the page you are already on
    /// changes nothing, and saying "done" about it would be the app claiming work it did not do.</returns>
    internal bool GoToPageFrom(Control? source)
    {
        if (ActionTarget.PageOf(source) is not { } index)
        {
            return ShowTheRailAndChooseAPage();
        }

        if (_source is null || index < 0 || index >= _source.PageCount)
        {
            // Only reachable if a tile outlived the newsletter it was built for, which the refresh
            // is meant to prevent. The user still gets a sentence rather than a button that does
            // nothing, and it says their work is untouched, because that is the question.
            Announce(
                "That page is not there any more, so nothing has happened and your newsletter has "
                + "not changed.");
            return false;
        }

        if (index == _pageIndex)
        {
            Announce($"You are already looking at page {index + 1} of {_source.PageCount}.");
            return false;
        }

        GoToPage(index);
        Announce($"Showing page {index + 1} of {_source.PageCount}.");
        return true;
    }

    /// <summary>
    /// The menu's answer to "go to a page": show the chooser and stand on it. Never silent — F6 and
    /// M70(c) settled that a navigation command which cannot go anywhere still has to say so.
    /// </summary>
    private bool ShowTheRailAndChooseAPage()
    {
        // Asking to go to a page IS asking for the chooser, so a rail the user had put away is
        // brought back rather than the command refusing with instructions for bringing it back by
        // hand. A rail folded away for WIDTH is a different answer: the setting is not what is in
        // the way, so turning it on would change a preference and still show nothing.
        if (!RailScale.IsVisible && !_settings.ShowPageRail)
        {
            TogglePageRail();
        }

        if (!RailScale.IsVisible)
        {
            Announce(
                "This window is too narrow for the row of pages. Make the window wider, or use "
                + "Next page and Previous page to move through the newsletter.");
            return false;
        }

        if (_rail.FocusTile(_pageIndex))
        {
            Announce(
                "Choose a page: use the arrow keys to move along the pages down the side, then "
                + "press Enter.");
            return true;
        }

        Announce("There are no pages to choose from yet.");
        return false;
    }

    /// <summary>
    /// A PNG of one page for the rail, or null when there is no newsletter to draw. The rail is
    /// handed this rather than the render source itself, because the source belongs to the open
    /// document and is replaced under it every time somebody opens another one.
    /// </summary>
    private byte[]? RenderPageThumbnail(int pageIndex) =>
        _source is { } source && pageIndex >= 0 && pageIndex < source.PageCount
            ? source.RenderPageToPng(pageIndex, PageRail.ThumbnailScale)
            : null;

    /// <summary>
    /// The id of every page, in order — what the rail is refreshed from.
    ///
    /// <para><b>Identities rather than a count, and the review that made it so.</b> The rail cached
    /// each miniature against the page's POSITION, which is stable only until somebody uses the two
    /// buttons the rail exists to give a home to. Moving a page, adding one or deleting one shifts
    /// every position at or past the edit while every picture stays where it was, and a move leaves
    /// the page COUNT alone, so the tiles are not even rebuilt — the rail would go on showing a
    /// miniature of a page that is somewhere else, under a label and an accessible name recomputed
    /// correctly on the same pass. Handed the ids, the rail matches a picture to the page it is a
    /// picture of and the question of what moved never arises.</para>
    /// </summary>
    private IReadOnlyList<string> PageIdsForRail() =>
        _package is { } package
            ? [.. package.Document.Pages.Select(p => p.Id)]
            : [];

    /// <summary>The rail, for M76's tests.</summary>
    internal PageRail RailForTest => _rail;

    /// <summary>Whether the rail is on screen — which is not the same as the setting (M70(d)).</summary>
    internal bool RailIsShowingForTest => RailScale.IsVisible;

    /// <summary>
    /// How much window is left for the newsletter itself. The fold rules exist to keep this above a
    /// floor (see <c>MinimumRoomForThePage</c>), and a test that only asserted which strips are
    /// showing would pass just as happily if a future strip took the room a folded one gave back.
    /// </summary>
    internal double CanvasScrollerWidthForTest => CanvasScroller.Bounds.Width;

    /// <summary>The labelled button the rail folds away to, so a test can prove it takes its place.</summary>
    internal Button ShowRailButtonForTest => ShowRailButton;

    /// <summary>
    /// The strip that HOLDS that button, which is the thing whose visibility is actually toggled.
    ///
    /// <para>Exposed because a test asserted on the button's own <c>IsVisible</c>, and nothing in
    /// the application ever writes it — see <see cref="ApplyRailVisibility"/>, which toggles this
    /// Border. The assertion was therefore always true, and the one claim the fold-away test made
    /// about the fold-away was the one claim it did not check.</para>
    /// </summary>
    internal Border CollapsedRailHostForTest => CollapsedRailHost;

    internal void ZoomToActualSize() => SetZoom(1d, fit: false);

    internal void FitPage()
    {
        _fitToWindow = true;
        ApplyFitZoom();
    }

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_fitToWindow)
        {
            ApplyFitZoom();
        }

        if (_editor is not null)
        {
            _editor.ViewportHeightPt = (float)(CanvasScroller.Bounds.Height / Math.Max(PageCanvas.Zoom, 0.1));
        }

        ApplyPanelVisibility();
        ApplyRailVisibility();
    }

    /// <summary>
    /// One table, matched on exact modifiers (see <see cref="KeyboardMap"/>). The switch this
    /// replaced matched with HasFlag, so <c>case Key.Y when ctrl:</c> also caught Ctrl+Shift+Y and
    /// silently redid instead of fitting the box to its contents.
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool typing = _editor is { IsActive: true };
        if (KeyboardMap.Resolve(e.Key, e.KeyModifiers, typing) is { } actionId)
        {
            _ = _actions.RunAsync(actionId);
            e.Handled = true;
        }
    }

    // ---- Document lifecycle -----------------------------------------------------------------

    /// <param name="startsDirty">
    /// M24: true when what is about to be shown exists nowhere on disk — a carry-forward, or work
    /// put back by the recovery dialog. False for a newsletter opened from its own file and for a
    /// fresh template, so closing one of those untouched asks the user nothing.
    /// </param>
    private void ShowPackage(TboardPackage package, bool startsDirty = false)
    {
        // Before the first paint, not during it (PLAN.md M14). The document is never rewritten —
        // the unknown family stays in styles.json, so a round trip through this build is lossless
        // and a newer TrestleBoard will render it properly.
        WarnAboutUnknownFonts(package.Document);

        var session = new DocumentSession(package.Document);
        DocumentRenderSource source = DocumentRenderSource.CreateEditable(
            package.Document, package.Assets, _fonts, session, options: null, widgets: _widgetProvider);
        var editor = new TextEditorController(session, source, new AvaloniaTextClipboard(this));
        var frames = new FrameEditorController(session, source);
        var photos = new PhotoController(session, source, new PackageAssetStore(package));
        var widgets = new WidgetController(session, source, _widgetProvider);
        var pages = new PageFlowController(session, source);

        _source?.Dispose();

        // M76 (g): every miniature in the rail belongs to the newsletter that is being replaced.
        // Dropped here rather than redrawn, because the tiles themselves are rebuilt on the next
        // refresh — this newsletter may not even have the same number of pages.
        _rail.ForgetEveryThumbnail();
        _source = source;
        _session = session;
        _editor = editor;
        _frames = frames;
        _photos = photos;
        _widgets = widgets;
        _pages = pages;
        _writingLook = new WritingLookController(session);
        _package = package;
        _pageIndex = 0;
        _exportedThisSession = false;

        // M21 for the find window, M70(g) for the other four: a window left open over the
        // newsletter that has just been closed is showing a newsletter nobody has any more.
        _switchNote = CloseWhatIsShowingTheOldNewsletter();
        _find = new FindController(session, editor);
        _find.Changed += (_, _) =>
        {
            if (_find?.StatusMessage is { } said)
            {
                Announce(said);
            }
        };
        PageCanvas.Source = source;

        // M43: the overset badge says "does not fit" in words beside it, and the words need a face.
        // The shell owns the bundled store, so it hands one over rather than the canvas loading a
        // second copy of the same files.
        PageCanvas.OverlayLabelFace ??= _fonts.Resolve(
            new FontKey(BundledFonts.BodyFamily, Layout.Fonts.FontWeight.Regular, FontStyleSlant.Normal)).Typeface;
        PageCanvas.Editor = editor;
        PageCanvas.FrameEditor = frames;
        PageCanvas.PageIndex = 0;

        // M52: the underlines are recomputed when the page changes, when a newsletter is opened,
        // and when the user stops typing in a frame — NOT on every keystroke. Marks appearing and
        // vanishing under a half-typed word is exactly the jitter this audience does not need, and
        // a whole page re-checked per character is work nobody asked for. The dotted line is a
        // reminder, not a running commentary.
        RefreshSpellingMarks();

        // M24: a newsletter just opened from a file matches that file; one carried forward or put
        // back by the recovery dialog matches nothing on disk and says so from the first moment.
        _unsavedChanges = startsDirty;
        _closeAgreed = false;
        UpdateTitle();

        session.Changed += (_, _) =>
        {
            if (!_unsavedChanges)
            {
                _unsavedChanges = true;
                UpdateTitle();
            }

            // M76 (g): the miniature of the page being edited is now out of date. Marking is all
            // that happens here — the redraw is delayed until the typing stops, because this fires
            // once per change to the newsletter and a page re-rendered per keystroke would be felt
            // (see PageRail.CatchUpDelay).
            _rail.NoteThePageChanged(_pageIndex);

            RefreshActions();
        };
        StartRecovery(package);
        editor.Changed += (_, _) =>
        {
            // M52: when the caret leaves a frame, whatever was typed in it is finished writing, and
            // that is the moment to re-mark it. While the session is active the marks stay as they
            // were — see the note where the newsletter is adopted.
            if (!editor.IsActive)
            {
                RefreshSpellingMarks();
            }

            RefreshActions();
        };
        frames.Changed += (_, _) => RefreshActions();
        photos.Changed += (_, _) => RefreshActions();
        widgets.Changed += (_, _) => RefreshActions();
        pages.Changed += (_, _) => RefreshActions();
        editor.RevealRequested += OnCaretReveal;

        _fitToWindow = true;
        ApplyFitZoom();
        RefreshActions();
    }

    /// <summary>
    /// M70(g): closes the non-modal windows that are showing the newsletter being replaced, and
    /// returns the one sentence that says so — null when none of them was open.
    ///
    /// <para>Four of the five hold a copy of a newsletter taken at the moment they opened. The
    /// review's findings are built once and never re-run; the read-back copies the sentence list at
    /// construction; the spelling walk is a scan of a particular document; and last year's window
    /// was chosen for <em>this</em> issue's month, so its "Copy this into this month" would put a
    /// June article into whatever happens to be on screen now. Their "Take me there" buttons would
    /// be pointing into a newsletter nobody has open. The find window has been closed here since
    /// M21 for exactly this reason — all that is new is that the app now says so.</para>
    ///
    /// <para><b>"How do I…?" is deliberately left open.</b> It holds nothing of the document: the
    /// menu paths are the app's own, and whether a command can run is asked of the live catalog at
    /// the moment the button is pressed, which is why it already refuses in plain language rather
    /// than going somewhere wrong. The one stale thing in it is the answer drawn on screen, which is
    /// re-checked below against the newsletter that is open now.</para>
    /// </summary>
    private string? CloseWhatIsShowingTheOldNewsletter()
    {
        var closed = new List<string>();

        if (_findWindow is { } find)
        {
            closed.Add(find.Title ?? "Find");
            _findWindow = null;
            find.Close();
        }

        if (_reviewWindow is { } review)
        {
            closed.Add(review.Title ?? "Look it over");
            _reviewWindow = null;
            review.Close();
        }

        if (_spellingWindow is { } spelling)
        {
            closed.Add(spelling.Title ?? "Check my spelling");
            _spellingWindow = null;
            spelling.Close();
        }

        if (_readAloudWindow is { } reading)
        {
            // Its own Closed handler hushes the voice and takes the highlight off the page, which is
            // the whole of what stopping means here.
            closed.Add(reading.Title ?? "Read it back to me");
            _readAloudWindow = null;
            reading.Close();
        }

        if (_lastYearWindow is { } lastYear)
        {
            closed.Add("Last year's newsletter");
            _lastYearWindow = null;
            lastYear.Close();
        }

        _helpWindow?.ADifferentNewsletterIsOpen();

        if (closed.Count == 0)
        {
            return null;
        }

        string names = closed.Count == 1
            ? Quote(closed[0])
            : string.Join(", ", closed.Take(closed.Count - 1).Select(Quote)) + " and " + Quote(closed[^1]);

        return closed.Count == 1
            ? $"The {names} window has closed, because it was showing the newsletter you had open "
                + "before this one."
            : $"The {names} windows have closed, because they were showing the newsletter you had "
                + "open before this one.";

        static string Quote(string title) => "“" + title + "”";
    }

    private void OnCaretReveal(object? sender, CaretRevealEventArgs e)
    {
        if (e.PageIndex != _pageIndex)
        {
            GoToPage(e.PageIndex);
        }

        // Scroll the caret rect (page points → control pixels) into the viewport.
        double zoom = PageCanvas.Zoom;
        var target = new Avalonia.Rect(
            (e.LeftPt * zoom) + 24 - 40,
            (e.TopPt * zoom) + 24 - 40,
            ((e.RightPt - e.LeftPt) * zoom) + 80,
            ((e.BottomPt - e.TopPt) * zoom) + 80);
        PageCanvas.BringIntoView(target);
    }

    internal void GoToPage(int index)
    {
        if (_source is null || index < 0 || index >= _source.PageCount)
        {
            return;
        }

        _pageIndex = index;
        PageCanvas.PageIndex = index;
        // Selection is per page; carrying it to another page would make the panel act on something
        // the user cannot see.
        _frames?.ClearSelection();
        if (_fitToWindow)
        {
            ApplyFitZoom();
        }

        RefreshSpellingMarks();
        RefreshActions();
    }

    /// <summary>
    /// M50, review §14.3: zoom with no pointer in it — the toolbar buttons, Ctrl+= and Ctrl+−.
    ///
    /// <para>The review asked for pointer-anchored zoom from the keyboard and noted the obstacle:
    /// without a pointer there is no anchor. That is true, and it is not the end of the question —
    /// when something is CHOSEN, the application already knows what the user is looking at. So a
    /// keyboard zoom anchors on the chosen thing's centre and falls back to zooming about the
    /// middle of the view when nothing is chosen, which is what it always did.</para>
    ///
    /// <para>It routes through <see cref="ZoomAtPointer"/> rather than repeating its arithmetic:
    /// the anchor is a point on the page either way, and M21 already worked out how to keep one
    /// still across a zoom step.</para>
    /// </summary>
    internal void StepZoom(int direction)
    {
        if (_source is null)
        {
            return;
        }

        if (_frames?.SelectedRect is { } chosen)
        {
            double zoom = PageCanvas.Zoom;
            var anchor = new Point(
                ((chosen.X + (chosen.Width / 2f)) * zoom) + PageCanvasControl.PagePaddingPx,
                ((chosen.Y + (chosen.Height / 2f)) * zoom) + PageCanvasControl.PagePaddingPx);
            ZoomAtPointer(anchor, direction);
            return;
        }

        StepZoomAboutTheCentre(direction);
    }

    /// <summary>The zoom step itself, with no anchoring — also what <see cref="ZoomAtPointer"/> calls
    /// once it has read the page point it intends to hold still.</summary>
    private void StepZoomAboutTheCentre(int direction)
    {
        if (_source is null)
        {
            return;
        }

        double current = PageCanvas.Zoom;
        double next = direction > 0
            ? ZoomSteps.FirstOrDefault(z => z > current + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < current - 0.001, ZoomSteps[0]);

        // M70(c): at either end of the ladder this does nothing and used to say nothing. The text
        // size ladder has said so since M14 (see StepTextSize); this is the same sentence for the
        // page. Every zoom path — the buttons, the keys and Ctrl+wheel — arrives here.
        if (Math.Abs(next - current) < 0.0001)
        {
            Announce(direction > 0
                ? "The page is already as large as TrestleBoard shows it."
                : "The page is already as small as TrestleBoard shows it.");
            return;
        }

        SetZoom(next, fit: false);
    }

    /// <summary>
    /// Ctrl+wheel zoom, anchored on the pointer (PLAN.md §11 M21): the point of the page under the
    /// pointer is still under the pointer afterwards.
    ///
    /// <para>Zooming about the centre — which is what the toolbar buttons do, and what this did
    /// before M21 — means that to look closely at something the user must zoom, hunt for what they
    /// were reading, scroll to it, and repeat. The arithmetic is small and worth naming: the page
    /// point under the pointer is read BEFORE the zoom changes, the same page point is worked back
    /// into control pixels AFTER it, and the scroller is shifted by the difference.</para>
    /// </summary>
    internal void ZoomAtPointer(Point pointInCanvas, int direction)
    {
        if (_source is null)
        {
            return;
        }

        double before = PageCanvas.Zoom;
        if (PageCanvas.TranslatePoint(pointInCanvas, CanvasScroller) is not { } pointerInScroller)
        {
            StepZoomAboutTheCentre(direction);
            return;
        }

        double pageX = (pointInCanvas.X - PageCanvasControl.PagePaddingPx) / before;
        double pageY = (pointInCanvas.Y - PageCanvasControl.PagePaddingPx) / before;

        StepZoomAboutTheCentre(direction);
        double after = PageCanvas.Zoom;
        if (Math.Abs(after - before) < 0.0001)
        {
            return; // Already at the end of the ladder: nothing moved, so nothing to correct.
        }

        // The canvas has just changed size, and the scroller's extent with it. Without this the
        // offsets below would be computed against the layout as it was one zoom step ago.
        CanvasScroller.UpdateLayout();

        var samePointNow = new Point(
            (pageX * after) + PageCanvasControl.PagePaddingPx,
            (pageY * after) + PageCanvasControl.PagePaddingPx);
        if (PageCanvas.TranslatePoint(samePointNow, CanvasScroller) is not { } drifted)
        {
            return;
        }

        Vector offset = CanvasScroller.Offset + (drifted - pointerInScroller);
        CanvasScroller.Offset = new Vector(Math.Max(0, offset.X), Math.Max(0, offset.Y));
    }

    /// <summary>Middle-drag and Space+drag (M21): the canvas reports the delta, the scroller moves.</summary>
    internal void PanBy(Vector delta)
    {
        Vector offset = CanvasScroller.Offset - delta;
        CanvasScroller.Offset = new Vector(Math.Max(0, offset.X), Math.Max(0, offset.Y));
    }

    /// <summary>
    /// M76(d), docs/M76-spec.md §7: the zoom percentage stopped being dead text.
    ///
    /// <para>It sat between two steppers showing a number nobody could change — the one readout in
    /// the window that looked like a control and was not one. Pressing it now opens the same ladder
    /// of magnifications the steppers walk, so somebody who wants the page at 200% presses once
    /// instead of four times, and — the part that matters more for this audience — can SEE what the
    /// choices are instead of having to discover them by pressing until they stop changing.</para>
    ///
    /// <para><b>Which ladder, and why not <see cref="ZoomLadder"/>'s.</b> The rungs are
    /// <see cref="ZoomSteps"/>, which is the ladder every zoom path in this window already
    /// walks — the buttons, Ctrl+plus/minus and Ctrl+wheel all land in
    /// <c>StepZoomAboutTheCentre</c>, which reads it. A chooser that offered a different set of
    /// rungs from the steppers beside it would be two ladders for one number.
    /// <c>ZoomLadder</c> is the PHOTO-POSITIONING ladder (M22) and starts at 100% on purpose,
    /// because there the whole picture already fits at 1× and there is nothing to zoom out to; the
    /// page can and must go below 100%. What is borrowed from it is <c>ZoomLadder.Label</c>, so the
    /// percentage is spelt the same way in both places.</para>
    ///
    /// <para>The current rung carries a radio mark, which is a SHAPE, not a colour (PLAN.md §6),
    /// and each item says its whole sentence to a screen reader.</para>
    /// </summary>
    private void OnZoomLadderClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        // M70's rule about silent no-ops: with nothing open there is no page to magnify, and an
        // empty popup would be the app shrugging. It says so instead, in the status bar, and names
        // the way out — which is also why this button needs no catalog entry to stay honest.
        if (_source is null || _source.PageCount == 0)
        {
            Announce(
                "There is no newsletter open yet, so there is nothing to make larger or smaller. "
                + "Open one from the File menu, or start a new one.");
            return;
        }

        double current = PageCanvas.Zoom;
        var flyout = new MenuFlyout { Placement = PlacementMode.Top };

        foreach (double rung in ZoomSteps)
        {
            double chosen = rung;
            string label = ZoomLadder.Label((float)chosen);
            var item = new MenuItem
            {
                Header = label,
                FontSize = 16,
                MinHeight = 44,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = Math.Abs(chosen - current) < 0.001,
            };

            Avalonia.Automation.AutomationProperties.SetName(item, $"Show the page at {label}");

            // No Tag: every walk in the app and in the test suite reads a MenuItem's Tag as an
            // ActionId, and this is a size rather than a command. M76 adds no ActionId — Zoom in,
            // Zoom out and Fit page keep theirs and this chooser is a second way to reach the same
            // number, not a tenth entry in the catalog.
            item.Click += (_, _) => SetZoom(chosen, fit: false);
            flyout.Items.Add(item);
        }

        flyout.ShowAt(button);
    }

    private void SetZoom(double zoom, bool fit)
    {
        _fitToWindow = fit;
        PageCanvas.Zoom = zoom;
        ZoomLabel.Text = $"{Math.Round(zoom * 100)}%";
    }

    private void ApplyFitZoom()
    {
        if (_source is null || _source.PageCount == 0)
        {
            return;
        }

        Core.Model.SizePt size = _source.GetPageSize(_pageIndex);
        double viewportW = CanvasScroller.Bounds.Width - 60;
        double viewportH = CanvasScroller.Bounds.Height - 60;
        if (viewportW <= 0 || viewportH <= 0)
        {
            return;
        }

        double zoom = Math.Min(viewportW / size.Width, viewportH / size.Height);
        SetZoom(Math.Clamp(zoom, 0.1, 4.0), fit: true);
    }

    private void UpdatePageChrome()
    {
        PageLabel.Text = _source is { PageCount: > 0 }
            ? $"Page {_pageIndex + 1} of {_source.PageCount}"
            : "No newsletter";

        // M41: the save state in the toolbar, in the same words the title bar uses. Blank when
        // there is no newsletter, because "Saved" over an empty window would be answering a
        // question nobody asked.
        SaveStateLabel.Text = _package is null
            ? string.Empty
            : _unsavedChanges ? "Not saved yet" : "Saved";

        // M82: while there is work to lose, SAVE is the offer the user came for, and the toolbar
        // says so with the treatment rather than with a grey whisper beside it. "Not saved yet" in
        // muted text is a fact stated at somebody who is not looking for a fact.
        //
        // The two swap rather than both being primary: M76 (f) settled that a group has at most one
        // primary, and three navy slabs at once is the absence of an answer rather than three of
        // them. Save and "Make the PDF" are both ActionGroup.Newsletter, so the rule applies as
        // written.
        bool saveLeads = _package is not null && _unsavedChanges;
        SetPrimary(SaveButton, saveLeads);
        SetPrimary(ExportPdfButton, !saveLeads);
    }

    /// <summary>
    /// Gives a toolbar button the primary treatment, or takes it away.
    ///
    /// <para>Clearing has to be an explicit local-value clear rather than assigning some other
    /// theme: M76 (e) records that the markup's <c>Theme</c> is a LOCAL value and beats the
    /// "action" class's Style, so a button that once held it keeps it until the local value is
    /// removed.</para>
    /// </summary>
    private static void SetPrimary(Button button, bool primary)
    {
        if (primary)
        {
            button.Primary();
        }
        else
        {
            button.ClearValue(StyledElement.ThemeProperty);
        }
    }

    private void UpdateStatus()
    {
        // Polled, deliberately. A controller's StatusMessage is LIVE STATE, not a one-shot: link
        // mode says "click the frame to continue into" for exactly as long as link mode is armed,
        // and it disappears because the controller stops saying it. Draining these instead was
        // tried and reverted — it made a sentence vanish on the next selection change, and left a
        // stale link-mode instruction on screen after the mode ended. The staleness bug the audit
        // found is a one-shot message stored in a state-shaped field, and it is fixed where it is
        // written rather than here.
        string? message = _pages?.StatusMessage
            ?? _widgets?.StatusMessage
            ?? _photos?.StatusMessage
            ?? _frames?.StatusMessage
            ?? (_context.HasOversetText
                // M17: the marker at the outflow corner has been drawn since M5 and said nothing
                // about what to do. This names the command and its shortcut, so the red square
                // stops being a symbol the user has to look up.
                ? "There is more writing than fits — 'Make the rest fit' (Ctrl+Shift+M) will flow it."
                : null);

        // M70. Two rules, and the order is the whole of the fix.
        //
        // THE ANNOUNCEMENT WINS. It is the sentence the command the user just invoked chose to say —
        // most often the plain-language reason it refused — and it used to lose to whatever a
        // controller happened to be holding, so M11's "nothing becomes unavailable without saying
        // why" was broken at the point of delivery.
        //
        // A SENTENCE STAYS UNTIL SOMETHING NEWER REPLACES IT. Draining the controllers above stops a
        // message resurfacing three commands later, but a refresh happens on every selection change,
        // so displaying only what was drained THIS pass would wipe a sentence a fraction of a second
        // after it appeared — over-correcting the bug into a different way of not being read. What
        // was last said is held here and kept on screen until the app has something newer.
        StatusLabel.Text = _announcement ?? message ?? ModeHint() ?? "";
        _announcement = null;
    }

    /// <summary>
    /// M44, review §14.3: which of the canvas's two modes the user is in, and the key that leaves
    /// it.
    ///
    /// <para>The same click on a text frame places a caret or chooses the frame depending on
    /// whether it was already chosen, and Enter, F2 and Esc move between those two states — a mode
    /// with no indicator anywhere, and three keys that appear in no menu, no panel and no catalog
    /// entry. This is the lowest-priority thing the status bar can say, so anything the app
    /// actually needs to report still wins; when there is nothing else to say, it says where you
    /// are.</para>
    /// </summary>
    private string? ModeHint()
    {
        if (_context.IsEditingText)
        {
            return "You are writing in this frame. Esc chooses the frame instead; "
                + "Tab moves on to the next one.";
        }

        return _context.Selection is SelectionKind.TextFrame
            ? "This frame is chosen. Press Enter to write in it, or Tab to move to the next one."
            : null;
    }

    /// <summary>
    /// Where inserted photo bytes are kept: the open package, verbatim. Nothing re-encodes them,
    /// which is what makes "originals byte-identical in the container" true (docs/M6-spec.md §6).
    /// </summary>
    private sealed class PackageAssetStore(TboardPackage package) : IPhotoAssetStore
    {
        public void Register(string assetRef, byte[] bytes) => package.Assets[assetRef] = bytes;
    }

    /// <summary>
    /// Says, in plain language, that this build cannot draw one of the newsletter's fonts. The
    /// status bar rather than a modal: the newsletter is perfectly usable, and a dialog in front of
    /// it on open would be a bigger interruption than the problem deserves.
    /// </summary>
    private void WarnAboutUnknownFonts(Core.Model.Document document)
    {
        IReadOnlyList<UnknownFontUse> unknown =
            DocumentFontAudit.FindUnknownFamilies(document, BundledFontCatalog.FamilyNames);
        if (DocumentFontAudit.DescribeWarning(unknown, BundledFonts.BodyFamily) is { } warning)
        {
            Announce(warning);
        }
    }

    /// <summary>A read-only wall of text with a scrollbar — the font licences, and nothing else yet.</summary>
    private async Task ShowScrollingTextAsync(string title, string body)
    {
        var close = new Button
        {
            Content = "Close",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        Avalonia.Automation.AutomationProperties.SetName(close, "Close");

        var text = new TextBox
        {
            Text = body,
            IsReadOnly = true,
            AcceptsReturn = true,
            FontSize = 16,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 520,
        };
        Avalonia.Automation.AutomationProperties.SetName(text, title);

        var dialog = new Window
        {
            Title = title,
            Width = 760,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children = { text, close },
            },
        };
        Avalonia.Automation.AutomationProperties.SetName(dialog, title);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    /// <summary>The last thing the app told the user had gone wrong, title and message.</summary>
    internal string? LastErrorForTest { get; private set; }

    /// <summary>
    /// Set by tests that drive a path which is supposed to fail. The error is still recorded in
    /// <see cref="LastErrorForTest"/> — the point is to assert the sentence, not to skip it — but
    /// the modal window is not opened, because a headless test has nobody to press its button.
    /// </summary>
    internal bool SwallowErrorsForTest { get; set; }

    private async Task ShowErrorAsync(string title, string message)
    {
        LastErrorForTest = $"{title}: {message}";
        if (SwallowErrorsForTest)
        {
            return;
        }

        // Plain-language error dialog (PLAN.md §6).
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, FontSize = 18, MaxWidth = 480, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    // IsDefault AND IsCancel: this dialog has one button, so Enter and Esc must
                    // both dismiss it. Neither did, so the app's standard error message could only
                    // be closed with the mouse — the one dialog a keyboard-only user is most likely
                    // to be looking at, since something has just gone wrong (review §14.2).
                    new Button
                    {
                        Content = "OK",
                        FontSize = 18,
                        MinHeight = 44,
                        MinWidth = 120,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        IsDefault = true,
                        IsCancel = true,
                    },
                },
            },
        };
        if (dialog.Content is StackPanel panel && panel.Children[^1] is Button ok)
        {
            ok.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }

    // ---- When something goes wrong (PLAN.md §11 M77) -------------------------------------------

    /// <summary>
    /// The first thing the crash guard does. Writes the recovery snapshot the app already knows how
    /// to write, so that whatever happens next, the evening's work is on the disk.
    ///
    /// <para>It sets <see cref="_lastCrashKeptTheWork"/> rather than returning, because the guard
    /// deliberately swallows what this throws — and the card that comes next has to know which of
    /// its two promises it is allowed to make. M73's standard: never claim what did not happen.</para>
    /// </summary>
    private void KeepTheWorkAfterACrash()
    {
        _lastCrashKeptTheWork = false;
        if (_recovery is not { } recovery)
        {
            // Nothing open. There is no work to lose, so the promise is true by vacancy — and
            // saying "your newsletter has been kept" over an empty window is still the right
            // sentence, because what the user is being told is that they have lost nothing.
            _lastCrashKeptTheWork = true;
            return;
        }

        // SaveNow returns false when the document is not dirty, which also means nothing was lost.
        recovery.SaveNow();
        _lastCrashKeptTheWork = true;
    }

    /// <summary>
    /// The second thing the crash guard does. One card, two answers, and the exception nowhere on
    /// screen (docs — <see cref="Dialogs.ProblemCard"/> says why).
    /// </summary>
    private void ShowTheProblemCard(Diagnostics.CrashGuard.CrashReport report)
    {
        LastCrashForTest = report;
        if (SuppressStartupForTest || SwallowErrorsForTest)
        {
            // A headless test has nobody to press the buttons. The facts are still recorded above,
            // because what a test wants to assert is what the shell decided, not that a window
            // appeared.
            return;
        }

        var card = new Dialogs.ProblemCard(_lastCrashKeptTheWork, report.AppWillClose);
        _ = ShowTheProblemCardAsync(card, report.Error);
    }

    private async Task ShowTheProblemCardAsync(Dialogs.ProblemCard card, Exception error)
    {
        try
        {
            await card.ShowDialog(this);
            if (card.Choice == Dialogs.ProblemCardChoice.SaveTheReport)
            {
                await SaveAProblemReportAsync(error);
            }
        }
        catch (Exception)
        {
            // Showing the card is already the recovery path. There is nowhere left to report a
            // failure to report a failure, and throwing from here would re-enter the guard.
        }
    }

    /// <summary>What the crash guard last caught, for the tests. Null until something does.</summary>
    internal Diagnostics.CrashGuard.CrashReport? LastCrashForTest { get; private set; }

    /// <summary>Whether the last crash was able to keep the work, for the tests.</summary>
    internal bool LastCrashKeptTheWorkForTest => _lastCrashKeptTheWork;

    /// <summary>Lets a test drive the guard without a real crash, which is the whole design.</summary>
    internal void SimulateCrashForTest(Exception error, bool appWillClose = false) =>
        _crashGuard?.Handle(new Diagnostics.CrashGuard.CrashReport(error, appWillClose));

    /// <summary>
    /// Writes the report the user emails to whoever looks after TrestleBoard (M77).
    /// </summary>
    /// <param name="error">
    /// What went wrong, or null when nobody crashed and the user asked from the Help menu.
    /// </param>
    internal async Task<bool> SaveAProblemReportAsync(Exception? error)
    {
        string text = Diagnostics.ProblemReport.Compose(
            new Diagnostics.ProblemReportFacts(
                AppVersion(),
                Environment.OSVersion.VersionString,
                _settings.UiScalePercent,
                _settings.Theme.ToString(),
                error,
                Diagnostics.ActionTrail.Shared.Recent(Diagnostics.ProblemReport.ActionsShown)),
            DateTimeOffset.Now);

        LastProblemReportForTest = text;

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a report of the problem",
            DefaultExtension = "txt",
            SuggestedFileName = Diagnostics.ProblemReport.SuggestedFileName,
            FileTypeChoices = [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }],
        });

        // Only ever where the user browsed to — the same rule the address book export follows
        // (PLAN.md §0 rule 5). There is no default folder beside the newsletter.
        if (file?.TryGetLocalPath() is not { } path)
        {
            return false;
        }

        try
        {
            await File.WriteAllTextAsync(path, text);
            Announce(
                $"The report was saved as {Path.GetFileName(path)}. "
                + "Email it to whoever looks after TrestleBoard for the lodge.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not save the report",
                "TrestleBoard could not write that file. Try somewhere else, such as your Desktop. "
                + ex.Message);
            return false;
        }
    }

    /// <summary>The text of the last report composed, for the tests that check what is in it.</summary>
    internal string? LastProblemReportForTest { get; private set; }

    /// <summary>Lets a test set the remembered geometry without going through the settings file.</summary>
    internal void SetSettingsForTest(AppSettings settings) => _settings = settings;

    // ---- Where the window opens (PLAN.md §11 M77) ----------------------------------------------

    /// <summary>
    /// The size the user left it at. Done in the constructor, before the window is shown, because
    /// resizing a visible window is a flicker the audience would notice.
    /// </summary>
    private void RestoreRememberedSize()
    {
        (int width, int height) = Settings.WindowPlacement.ChooseSize(
            _settings.WindowWidth, _settings.WindowHeight);
        Width = width;
        Height = height;
    }

    /// <summary>
    /// The position, once Avalonia knows what screens exist — which it does not in the constructor.
    ///
    /// <para>A remembered position that is no longer on any screen is thrown away rather than
    /// clamped: a window half off the edge of a monitor that is no longer plugged in cannot be
    /// dragged back by somebody who cannot see its title bar, and this audience would not think to
    /// try the keyboard.</para>
    /// </summary>
    private void RestoreRememberedPosition()
    {
        if (_settings.WindowMaximised)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        if (_settings.WindowLeft is not { } left || _settings.WindowTop is not { } top)
        {
            return;
        }

        var remembered = new Settings.PlacementRect(left, top, (int)Width, (int)Height);
        List<Settings.PlacementRect> screens = Screens.All
            .Select(screen => new Settings.PlacementRect(
                screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height))
            .ToList();

        if (Settings.WindowPlacement.CanRestore(remembered, screens))
        {
            Position = new PixelPoint(left, top);
        }
    }

    /// <summary>
    /// Writes the geometry back on close. Best-effort like every other preference: a size that
    /// could not be written is a nuisance next week, not a failure now.
    /// </summary>
    private void RememberWherePlaced()
    {
        // Not under test. Every window in the headless suite shares one app-state root, and several
        // of them resize themselves on purpose — ToolbarFitTests opens at 2560 to check the bar at
        // 200%, PageRailTests narrows the window to make the rail fold. Writing those sizes back
        // would make one test's deliberate geometry the next test's starting point, which is a
        // failure that appears and disappears with the order the runner happens to choose. In the
        // app there is one window and one person, so the rule this guards is not weakened by it:
        // what is skipped is only ever a size nobody chose.
        if (SuppressStartupForTest)
        {
            return;
        }

        try
        {
            _settings = GeometryRemembered();
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Nothing to say and nobody to say it to: the window is closing.
            _ = ex;
        }
    }

    /// <summary>
    /// What the settings would become, given where the window is right now. Split out from the
    /// write so that a test can assert the RULE — a maximised window keeps the size it had before —
    /// without a file, and without the suppression above standing in its way.
    /// </summary>
    internal AppSettings GeometryRemembered() => Settings.WindowPlacement.Remember(
        _settings,
        WindowState == WindowState.Maximized,
        (int)Width,
        (int)Height,
        Position.X,
        Position.Y);
}
