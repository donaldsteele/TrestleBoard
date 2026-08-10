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
    /// Below this much window width the panel folds away rather than squeezing the page out.
    ///
    /// <para><b>Derived from the panel width, not written beside it.</b> Until M16 this was a
    /// hard-coded 900 sitting next to a hard-coded 320, and widening one without the other would
    /// have changed the fold behaviour §11.9 of <c>docs/accessibility-test-script.md</c> tests by
    /// hand without anybody deciding to change it. The rule the original number encoded is "fold
    /// when the panel would take more than about a third of the window", and that is what this
    /// expression says.</para>
    /// </summary>
    private const double PanelFoldWidth = ActionPanel.PanelWidth * 2.5;

    /// <summary>
    /// The app is the one place where "the newsletter must still open" outranks "fail loudly", so
    /// unlike the engine default this store substitutes rather than throwing when a document names
    /// a family this build does not bundle. The user is told once, at open, by DocumentFontAudit —
    /// never by a crash in the paint pass. Still never a system font (PLAN.md §1).
    /// </summary>
    private readonly FontStore _fonts = CreateAppFontStore();
    private readonly ActionPanel _panel = new();
    private readonly ActionRunner _actions;
    private TboardPackage? _package;
    private DocumentRenderSource? _source;
    private DocumentSession? _session;
    private TextEditorController? _editor;
    private FrameEditorController? _frames;
    private PhotoController? _photos;
    private WidgetController? _widgets;
    private PageFlowController? _pages;
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
        DressToolbar();
        _actions = new ActionRunner(this);
        ActionPanelHost.Content = _panel;
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

        // The start screen and the recovery offer are the app's front door; without this they exist
        // but nobody ever sees them.
        Opened += async (_, _) =>
        {
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
            _findWindow?.Close();
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
        ZoomOutButton, ZoomInButton, FitButton,
    ];

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
        if (sender is Control { Tag: string actionId })
        {
            _ = _actions.RunAsync(actionId, sender as Control);
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

        foreach (Button button in ToolbarButtons)
        {
            if (button.Tag is string actionId && ActionCatalog.TryGet(actionId, out _))
            {
                ActionAvailability availability = ActionCatalog.Evaluate(actionId, _context);
                button.IsEnabled = availability.IsAvailable;
                Avalonia.Automation.AutomationProperties.SetHelpText(button, availability.Reason);
            }
        }

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
    /// M70(d): this said the panel was showing whether it was or not. On a narrow window
    /// <see cref="ApplyPanelVisibility"/> keeps the panel folded away however the setting is set,
    /// so the user was told a thing had happened that had not — a false report, which is worse than
    /// saying nothing. It now says what is actually on screen.
    /// </summary>
    internal void ToggleActionPanel()
    {
        _settings = _settings with { ShowActionPanel = !_settings.ShowActionPanel };
        _settings.Save();
        bool showing = ApplyPanelVisibility();
        Announce(showing
            ? "The panel of things you can do is showing."
            : _settings.ShowActionPanel
                ? "This window is too narrow for the panel, so it stays folded away. "
                    + "Make the window wider and it will come back."
                : "The panel is hidden. Bring it back from View, Show what I can do.");
    }

    /// <summary>
    /// The chrome budget (PLAN.md §11 M11): the panel folds itself away on a narrow window rather
    /// than leaving the page a strip down the middle.
    /// </summary>
    /// <returns>Whether the panel is now on screen — which is not the same as the setting.</returns>
    private bool ApplyPanelVisibility()
    {
        bool roomForIt = Bounds.Width <= 0 || Bounds.Width >= PanelFoldWidth;
        bool showPanel = _settings.ShowActionPanel && roomForIt;
        PanelScale.IsVisible = showPanel;
        CollapsedPanelHost.IsVisible = !showPanel;
        ShowPanelButton.Content = _settings.ShowActionPanel && !roomForIt
            ? "▸"
            : "What can I do? ▸";
        return showPanel;
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

        switch (start.Choice)
        {
            case StartChoice.MyTemplate when start.SelectedUserTemplateId is { } mine:
                OpenUserTemplate(mine);
                break;
            case StartChoice.Template:
                OpenTemplate(start.SelectedTemplateId);
                break;
            case StartChoice.OpenFile:
                await OpenNewsletterAsync();
                break;
            case StartChoice.LastMonth:
                // Only reachable once a newsletter is open; the tile explains that and is disabled.
                break;
        }
    }

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
                _recoveryStore.Delete(snapshot.Id);
                Announce("You chose to start fresh, so the work TrestleBoard had kept was thrown away.");
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

    internal async Task OpenNewsletterAsync()
    {
        if (await ConfirmSaveFirstAsync("and open another newsletter") == SaveFirst.Stay)
        {
            return;
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
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            // Recovery offers to put the work back where it came from, so the path has to be known.
            DocumentPath = files[0].TryGetLocalPath();
            ShowPackage(TboardContainer.Load(buffer));
            SayWhatTheSwitchClosed();
        }
        catch (Exception ex) when (ex is Core.Migrations.UnsupportedFormatException or System.IO.InvalidDataException)
        {
            await ShowErrorAsync("Could not open that file", ex.Message);
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
            DocumentPath = path;
            ShowPackage(TboardContainer.Load(buffer));
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
            DocumentPath = null;
            Announce($"TrestleBoard could not open {Path.GetFileName(path)}. {ex.Message}");
            return false;
        }
    }

    internal async Task NewFromTemplateAsync()
    {
        if (await ConfirmSaveFirstAsync("and start another newsletter") == SaveFirst.Stay)
        {
            return;
        }

        var start = new StartDialog(canStartFromLastMonth: _package is not null, Templates.All());
        await start.ShowDialog(this);

        switch (start.Choice)
        {
            case StartChoice.MyTemplate when start.SelectedUserTemplateId is { } mine:
                OpenUserTemplate(mine);
                break;
            case StartChoice.Template:
                OpenTemplate(start.SelectedTemplateId);
                break;
            case StartChoice.LastMonth:
                StartFromLastMonth();
                break;
            case StartChoice.OpenFile:
                await OpenNewsletterAsync();
                break;
        }
    }

    internal Task ExportPdfAsync() => ExportPdfAsync(draft: false);

    /// <summary>
    /// M53: "Make a draft copy" — the same renderer, the same bytes, plus a diagonal saying what it
    /// is. The review copy and the final copy differed only in the sender's memory before this.
    /// </summary>
    internal Task ExportDraftPdfAsync() => ExportPdfAsync(draft: true);

    internal async Task ExportPdfAsync(bool draft)
    {
        if (_source is null || _package is null)
        {
            return;
        }

        // M51. The offer, and only an offer: whatever the answer, the export goes ahead. The one
        // way this can stop an export is the user choosing to look it over, which is them changing
        // their mind, not the app refusing. PLAN.md's acceptance is explicit that "Make the PDF" is
        // never blocked. A draft is not offered the review — the whole point of a draft is that it
        // is going to somebody who will read it.
        if (!draft && await OfferTheReviewAsync())
        {
            return;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        string stem = $"{meta.Title} {meta.IssueYear}-{meta.IssueMonth:00}";
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = draft ? "Save the draft copy" : "Export as PDF",
            DefaultExtension = "pdf",
            SuggestedFileName = draft ? $"{stem} DRAFT.pdf" : $"{stem}.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            await using (Stream stream = await file.OpenWriteAsync())
            {
                DocumentPdfExporter.Export(
                    stream,
                    _source,
                    new PdfMetadata(meta.Title, meta.LodgeName, $"Trestle board {meta.IssueYear}-{meta.IssueMonth:00}"),
                    draft ? WatermarkRenderer.DraftText : null);
            }

            _exportedThisSession = true;

            // The stream is closed before anything is offered: handing a half-written file to a
            // printer would be a worse bug than not offering to print at all.
            LastExportedPdf = file.TryGetLocalPath();
            RefreshActions();
            await OfferToPrintAsync(draft);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(
                draft ? "Could not make the draft copy" : "Could not export the PDF",
                "The PDF could not be saved. Make sure the file is not open in another program and try again. "
                + $"({ex.Message})");
        }
    }

    // ---- Print it (PLAN.md §11 M53) ------------------------------------------------------------

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
    internal Func<ReviewFinding, bool> TakeMeToTheFindingForTest => TakeMeToTheFinding;

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
    private bool TakeMeToTheFinding(ReviewFinding finding)
    {
        GoToPage(Math.Clamp(finding.PageNumber - 1, 0, Math.Max(0, (_source?.PageCount ?? 1) - 1)));

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
        return found;
    }

    // ---- Show me last year's (PLAN.md §11 M59) -------------------------------------------------

    private LastYearWindow? _lastYearWindow;

    internal LastYearWindow? LastYearWindowForTest => _lastYearWindow;

    /// <summary>Set by tests in place of the folder picker.</summary>
    internal string? OldIssuesFolderAnswerForTest { get; set; }

    /// <summary>
    /// M59: opens the same month of last year beside this one, to look at.
    /// </summary>
    internal async Task ShowLastYearAsync()
    {
        if (_package is null)
        {
            return;
        }

        if (_lastYearWindow is not null)
        {
            _lastYearWindow.Activate();
            return;
        }

        Core.Model.DocumentMetadata meta = _package.Document.Metadata;
        PastIssue issue = PastIssues.Find(_settings.OldIssuesFolder, meta.IssueMonth, meta.IssueYear);

        if (issue.Problem == PastIssueProblem.NoFolderYet)
        {
            string? folder = OldIssuesFolderAnswerForTest ?? await AskForTheOldIssuesFolderAsync();
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            _settings = _settings with { OldIssuesFolder = folder };
            _settings.Save();
            issue = PastIssues.Find(folder, meta.IssueMonth, meta.IssueYear);
        }

        if (!issue.Opened)
        {
            await ShowErrorAsync("Last year's newsletter", issue.Message ?? "It could not be opened.");
            return;
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
        Announce($"Last year's issue is open beside this one, to look at. It cannot be changed.");
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

        var session = ReadAloudSession.For(_package.Document);
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
    /// </summary>
    private void ShowTheSentence(Core.Text.Sentence? sentence)
    {
        if (_source is null || _package is null || sentence is null)
        {
            PageCanvas.SpokenRects = [];
            return;
        }

        if (PageOfStory(sentence.StoryId) is { } page && page != _pageIndex)
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
    internal async Task SaveAsTemplateAsync()
    {
        if (_package is null)
        {
            return;
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
            return;
        }

        Core.Container.TboardPackage template = Core.Workflow.NewsletterTemplate.From(_package);
        UserTemplate? saved = Templates.Save(template, name, DateTimeOffset.Now);

        Announce(saved is null
            ? $"TrestleBoard could not write “{name.Trim()}” to your templates."
            : $"“{saved.Name}” is one of your templates now. You will find it on the screen that "
              + "asks what you would like to do, and under File, “My templates”.");
        RefreshActions();
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
    internal void OpenUserTemplate(string id)
    {
        if (Templates.Open(id) is not { } package)
        {
            Announce("That template could not be opened. It may have been moved or removed.");
            return;
        }

        DocumentPath = null;
        ShowPackage(package, startsDirty: true);
        Announce("Started from one of your templates. It has no file yet, so Save it when you are ready.");
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
    internal async Task BringInPdfPageAsync()
    {
        if (_photos is null || _package is null)
        {
            return;
        }

        if (!PdfPageRasterizer.IsAvailable)
        {
            await ShowErrorAsync("PDFs cannot be read on this computer", PdfPageRasterizer.NotAvailableReason);
            return;
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
            return;
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
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "That file could not be opened",
                $"TrestleBoard could not read that file. ({e.Message})");
            return;
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
            return;
        }

        byte[] png;
        try
        {
            png = PdfPageRasterizer.RenderPage(pdf, pageNumber);
        }
        catch (PdfPageException e)
        {
            await ShowErrorAsync("That page could not be brought in", e.Message);
            return;
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
            return;
        }

        _package.Assets[pdfAsset] = pdf;
        _frames?.Select(blockId);
        Announce($"Page {pageNumber} is on the newsletter as a picture. Use \"Describe this "
            + "picture\" to say what is on it — a screen reader cannot read a picture of writing, "
            + "and right now all it knows is which page this was.");
        RefreshActions();
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
    internal async Task BringInWritingAsync()
    {
        if (_photos is null || _frames is null)
        {
            return;
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
            return;
        }

        ImportedWriting writing;
        try
        {
            writing = WritingImport.Read(path);
        }
        catch (Core.Migrations.UnsupportedFormatException e)
        {
            await ShowErrorAsync("That file could not be brought in", e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "That file could not be opened",
                $"TrestleBoard could not read that file. ({e.Message})");
            return;
        }

        if (writing.IsEmpty)
        {
            await ShowErrorAsync(
                "There was nothing in that file",
                "TrestleBoard found no writing and no pictures in it. If the words are inside a "
                + "table or a text box, they will not come through — copy them into an ordinary "
                + "paragraph first.");
            return;
        }

        if (!(BringItInAnswerForTest ?? await ConfirmTheWritingAsync(writing, Path.GetFileName(path))))
        {
            return;
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

        await OfferThePicturesAsync(writing.Pictures);
        RefreshActions();
    }

    /// <summary>
    /// Each picture from the document, one at a time, through M18's single picture ingest path.
    ///
    /// <para>Never all of them unasked: a Word document's media folder holds the author's
    /// letterhead and their signature scan as readily as the photograph they meant to send.</para>
    /// </summary>
    private async Task OfferThePicturesAsync(IReadOnlyList<ImportedPicture> pictures)
    {
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
        }
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
    /// <para><b>It becomes an ordinary picture.</b> The emblem is drawn once, here, into PNG bytes,
    /// and handed to the same <c>PhotoController.InsertPhoto</c> that a photograph from the user's
    /// camera goes through — so it can be moved, resized, wrapped, captioned and re-described with
    /// the commands that already exist, it lands in the container as an ordinary asset, and the
    /// layout engine and the PDF export never learn that emblems are a thing. A second kind of
    /// frame would have been a second thing to keep working for ever.</para>
    ///
    /// <para>No description dialog, unlike a photograph: the app knows what this picture is, and
    /// asking somebody to describe the square and compasses to the app that just drew it would be
    /// the software pretending not to know something. M23's "Describe this picture" changes it.</para>
    /// </summary>
    internal async Task InsertEmblemAsync()
    {
        if (_photos is null)
        {
            return;
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
            return;
        }

        _editor?.End();
        string? blockId = _photos.InsertPhoto(
            _pageIndex,
            EmblemRenderer.ToPng(chosen),
            chosen.Description,
            caption: "",
            fit: Core.Model.ImageFit.Contain);

        if (blockId is null)
        {
            await ShowErrorAsync(
                "That emblem could not be added",
                "TrestleBoard could not put that emblem on the page. Your newsletter is unchanged.");
            return;
        }

        _frames?.Select(blockId);
        Announce($"{chosen.Name} is on the page. Drag its corners to size it, and it can be moved, "
            + "captioned and wrapped like any other picture.");
        RefreshActions();
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
    internal async Task PackUpForSuccessorAsync()
    {
        SuccessorPackage pack = SuccessorPackService.Gather(DateTimeOffset.Now, AppVersion());
        if (pack.Manifest.Parts.Count == 0)
        {
            await ShowErrorAsync(
                "There is nothing to pack up yet",
                "TrestleBoard has not gathered an address book, any templates or any saved wordings "
                + "on this computer yet, so there is nothing a successor would need. Come back when "
                + "there is.");
            return;
        }

        if (!(PackConfirmForTest ?? await ConfirmThePackAsync(pack)))
        {
            return;
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
            return;
        }

        try
        {
            SuccessorPackContainer.SaveToFile(pack, path);
            Announce($"Everything is packed up in {Path.GetFileName(path)}. Give that one file to "
                + "whoever takes over, and they can bring it in on their own computer. Keep it "
                + "somewhere safe — it has the lodge's address book in it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not write that file",
                $"The pack could not be saved there. ({ex.Message})");
        }
    }

    /// <summary>
    /// Reads a pack and puts back whichever parts of it the user asks for.
    ///
    /// <para>Every refusal here is one sentence about what to do next. Somebody bringing in a pack
    /// is on a new computer, on their first day of a job they did not ask for, holding a file they
    /// cannot look inside.</para>
    /// </summary>
    internal async Task BringInAPackAsync()
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
            return;
        }

        SuccessorPackage pack;
        try
        {
            pack = SuccessorPackContainer.LoadFromFile(path);
        }
        catch (Core.Migrations.UnsupportedFormatException e)
        {
            await ShowErrorAsync("That pack could not be brought in", e.Message);
            return;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or NotSupportedException)
        {
            await ShowErrorAsync(
                "That pack could not be brought in",
                "TrestleBoard could not read that file. It may be damaged, or it may not be a "
                + "TrestleBoard pack.");
            return;
        }

        IReadOnlyList<PackPartChoice> choices = SuccessorPackService.Choices(pack);
        if (choices.Count == 0)
        {
            await ShowErrorAsync(
                "There is nothing in that pack",
                "That file is a TrestleBoard pack, but there is nothing inside it that this version "
                + "of TrestleBoard knows what to do with.");
            return;
        }

        IReadOnlyList<string> chosen = PackPartsAnswerForTest ?? await AskWhatToTakeAsync(pack, choices, path);
        if (chosen.Count == 0)
        {
            Announce("Nothing was brought in, so nothing on this computer has changed.");
            return;
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
    internal async Task SendItAsync()
    {
        if (_package is null)
        {
            return;
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
            return;
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
            return;
        }

        // Either the link was too long for a mail program to be trusted with, or nothing answered.
        // Both end the same way: the addresses go on the clipboard, and the card says what to do.
        LastMailOutcomeForTest = uri is null ? MailOutcome.TooManyForOneLink : MailOutcome.NothingAnswered;
        await CopyTheAddressesAsync(addresses, subject, fileName, printed, LastMailOutcomeForTest.Value);
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
    internal async Task InsertPhraseAsync()
    {
        if (_editor is not { IsActive: true })
        {
            return;
        }

        string? words = PhraseAnswerForTest;
        if (words is null)
        {
            if (SuppressStartupForTest)
            {
                return;
            }

            var window = new PhraseWindow(Phrases.All(), WhatTheAppAlreadyKnows(_settings));
            await window.ShowDialog(this);
            if (!window.Confirmed)
            {
                return;
            }

            words = window.Words;
        }

        if (string.IsNullOrWhiteSpace(words))
        {
            return;
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
    }

    /// <summary>
    /// M54: keep the highlighted words on the shelf. The committee's own wording for a hard moment
    /// is usually better than ours, and next year they will want it again.
    /// </summary>
    internal async Task SavePhraseAsync()
    {
        if (_editor is not { IsActive: true } editor || _session is null)
        {
            return;
        }

        string words = Core.Text.StoryNavigator.GetRangeText(
            _session.Document.Stories.Find(s => s.Id == editor.Selection.Range.StoryId)
                ?? throw new InvalidOperationException("The highlighted writing is not in a story."),
            editor.Selection.Range);
        if (string.IsNullOrWhiteSpace(words))
        {
            Announce("Highlight the words you would like to keep first.");
            return;
        }

        string? title = PhraseTitleAnswerForTest;
        if (title is null)
        {
            if (SuppressStartupForTest)
            {
                return;
            }

            title = await AskForTextAsync(
                "Keep these words for next time",
                "What would you like to call them? You will pick them from a list by this name.",
                FirstWordsOf(words));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        Phrases.Save(title, words);
        Announce(Phrases.CouldNotBeSaved
            ? $"“{title}” is on your shelf for now, but TrestleBoard could not write it down, so "
              + "it will be gone when you close the program."
            : $"“{title}” is on your shelf. You will find it under Insert, “Words for hard news”.");
        RefreshActions();
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

    private async Task OfferTheMemorialAsync(string name)
    {
        if (_editor is not { IsActive: true })
        {
            // Nowhere for the words to go. Saying where to start beats opening a wizard whose last
            // button cannot do anything.
            Announce(
                $"When you are ready to write about {name}, click into some writing and choose "
                + "\"Words for hard news\" from the Insert menu.");
            return;
        }

        Core.Phrases.Phrase? memorial = Core.Phrases.PhraseLibrary.Find("memorial");
        if (memorial is null)
        {
            return;
        }

        var answers = new Dictionary<string, string>(StringComparer.Ordinal) { ["{name}"] = name };
        _editor.InsertBlock(memorial.Fill(answers), "Add a memorial notice");
        Announce(
            $"A memorial notice for {name} is in your newsletter. It is ordinary writing now — "
            + "change any of it you like.");
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

    /// <summary>Page first, then the words — the M21 ordering lesson, same as M51's.</summary>
    private void TakeMeToTheWord(Misspelling word)
    {
        if (_package is null || _editor is null)
        {
            return;
        }

        if (PageOfStory(word.StoryId) is { } page)
        {
            GoToPage(page);
        }

        _editor.SelectRange(word.StoryId, word.ParagraphIndex, word.Offset, word.Word.Length);
        RefreshActions();
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

        string text = Core.Text.StoryNavigator.GetParagraphText(story.Paragraphs[word.ParagraphIndex]);
        if (word.Offset + word.Length > text.Length
            || !text.AsSpan(word.Offset, word.Length).SequenceEqual(word.Word))
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
        _settings.Save();
        PageCanvas.ShowSpelling = _settings.ShowSpelling;
        RefreshSpellingMarks();
        Announce(_settings.ShowSpelling
            ? "Words TrestleBoard does not know now have a dotted line under them. This never prints."
            : "The dotted lines are hidden again.");
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
            SuggestedFileName =
                $"{meta.Title} {meta.IssueYear}-{meta.IssueMonth:00}.tboard",
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

    internal async Task RestoreEarlierVersionAsync()
    {
        if (DocumentPath is not { } path)
        {
            return;
        }

        IReadOnlyList<DocumentBackup> backups = FileRecoveryStore.FindBackups(path);
        if (backups.Count == 0)
        {
            // M70(c): the catalog reads the ring once, when the newsletter's path is assigned, so
            // its answer can outlive the copies themselves — the ring is pruned, and the command is
            // still offered. Whoever presses it is owed the reason it can do nothing.
            Announce("The copies TrestleBoard kept are no longer there, so there is nothing to go back to.");
            return;
        }

        // The version on screen is about to be replaced, so it gets the same question every other
        // path that replaces it asks (M24).
        if (await ConfirmSaveFirstAsync("and open an earlier version") == SaveFirst.Stay)
        {
            return;
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
            return;
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
            return;
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
    /// </summary>
    private void UpdateTitle()
    {
        if (_package is not { } package)
        {
            Title = "TrestleBoard";
            return;
        }

        string name = package.Document.Metadata.Title;
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
    internal void OpenTemplate(string templateId)
    {
        DocumentPath = null;
        ShowPackage(TemplateLibrary.Create(templateId));
        SayWhatTheSwitchClosed();
    }

    /// <summary>
    /// M24: carry-forward replaces this month's newsletter with next month's, so it asks about
    /// unsaved work first. The runner calls this one; the synchronous
    /// <see cref="StartFromLastMonth"/> under it is the carry-forward itself.
    /// </summary>
    internal async Task StartFromLastMonthAsync()
    {
        if (await ConfirmSaveFirstAsync("and start next month's newsletter") == SaveFirst.Stay)
        {
            return;
        }

        StartFromLastMonth();
    }

    /// <summary>
    /// Start-from-last-month: carries the data forward, bumps the date, clears the prose
    /// (docs/M9-spec.md §3). The result is a NEW unsaved newsletter, so the path is cleared — and
    /// from M24 it is marked unsaved from the outset, because a carried-forward issue exists in no
    /// file anywhere until somebody saves it.
    /// </summary>
    internal bool StartFromLastMonth()
    {
        if (_package is null)
        {
            return false;
        }

        TboardPackage next = CarryForward.NextIssue(_package);
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

    internal async Task CutAsync()
    {
        if (_editor is not null)
        {
            await _editor.CutAsync();
        }
    }

    internal async Task CopyAsync()
    {
        if (_editor is not null)
        {
            await _editor.CopyAsync();
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

        await PastePictureAsync();
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

            // Not modal: this window exists to point at the page behind it (see FindWindow's own
            // header), which is why M21 had to make the text session survive losing focus first.
            _findWindow.Show(this);
        }

        _findWindow.SetMode(replacing);
        _findWindow.Activate();
    }

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
    internal async Task ShowTextStylesAsync()
    {
        if (_session is null)
        {
            return;
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
        window.Applied += (_, choice) => ApplyTextStyleChoice(choice);
        await window.ShowDialog(this);

        if (window.ShowOverridesRequested)
        {
            SetShowFontChanges(true);
            return;
        }

        if (window.ClearOverridesRequested)
        {
            ClearEveryFontOverride();
            return;
        }

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
    internal async Task UseFontJustHereAsync()
    {
        if (_session is null || _editor is null)
        {
            return;
        }

        _textStylesForTest = BuildJustHereWindowForTest();
        await _textStylesForTest.ShowDialog(this);

        if (_textStylesForTest.Result is { } choice)
        {
            _editor.UseFontJustHere(choice.FontFamily ?? CurrentFamily(), choice.SizePt);
            Announce("Those words now use their own font. Press Ctrl+Z to put them back.");
            RefreshActions();
        }
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
        + BundledDictionary.ReadLicenceText();

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

        _frames.SelectAll(ids);
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

    internal void DeleteSelectedFrame() => _frames?.DeleteSelected();

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

    internal void AutoFlow()
    {
        if (_pages is null || FlowTarget(out string? chose) is not { } blockId)
        {
            return;
        }

        _pages.AutoFlow(blockId);

        // The controller's own sentence — "the text still does not all fit" — must survive being
        // told what was chosen, so the two are said together rather than one over the other (M70).
        if (chose is not null)
        {
            Announce(_pages.StatusMessage is { Length: > 0 } note ? chose + " " + note : chose);
        }
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

    internal async Task InsertPhotoAsync()
    {
        if (_photos is null || _source is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
        {
            return;
        }

        await InsertPhotoFromFileAsync(files[0]);
    }

    /// <summary>The file's bytes, or null once the failure has been explained to the user.</summary>
    private async Task<byte[]?> ReadPictureBytesAsync(IStorageFile file)
    {
        try
        {
            await using Stream stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Could not open that picture", ex.Message);
            return null;
        }
    }

    private async Task InsertPhotoFromFileAsync(IStorageFile file)
    {
        if (await ReadPictureBytesAsync(file) is not { } bytes)
        {
            return;
        }

        var dialog = new PhotoInsertDialog(file.Name);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return;
        }

        _editor?.End();
        string? blockId = _photos!.InsertPhoto(_pageIndex, bytes, dialog.AltText, dialog.Caption);
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
        if (_photos is not null && _frames?.SelectedBlockId is { } blockId)
        {
            _photos.DismissStaleCropNotice(blockId);
        }
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
    /// "Put a picture here…" / "Swap this picture…". The bytes land in the package verbatim, exactly
    /// as on the insert path — a swap never re-encodes — and the whole change is one undo step.
    /// </summary>
    internal async Task ReplacePictureAsync()
    {
        if (_photos is null || PictureTarget() is not { } blockId)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a picture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
        {
            return;
        }

        if (await ReadPictureBytesAsync(files[0]) is not { } bytes)
        {
            return;
        }

        await ReplacePictureFromBytesAsync(blockId, bytes, files[0].Name);
    }

    /// <summary>
    /// The shared tail of every replace: ask for the words, then put the bytes in. Drag-and-drop and
    /// paste both come through here, so a picture arriving by any route is described before it lands
    /// (PLAN.md §6).
    /// </summary>
    private async Task ReplacePictureFromBytesAsync(string blockId, byte[] bytes, string fileName)
    {
        var dialog = new PhotoInsertDialog(fileName);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed)
        {
            return;
        }

        _editor?.End();
        if (!_photos!.ReplacePhoto(blockId, bytes, dialog.AltText, dialog.Caption))
        {
            await ShowErrorAsync(
                "That file is not a picture",
                "TrestleBoard could not read that file as a picture. JPEG and PNG files work best.");
            return;
        }

        _frames?.Select(blockId);
        RefreshActions();
    }

    internal async Task DescribePictureAsync()
    {
        if (_photos is null || PictureTarget() is not { } blockId)
        {
            return;
        }

        PictureWordsDialog dialog = PictureWordsDialog.ForAltText(_photos.GetPhoto(blockId)?.AltText);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed && dialog.Text is { } description)
        {
            _photos.SetAltText(blockId, description);
            RefreshActions();
        }
    }

    internal async Task CaptionPictureAsync()
    {
        if (_photos is null || PictureTarget() is not { } blockId)
        {
            return;
        }

        PictureWordsDialog dialog = PictureWordsDialog.ForCaption(_photos.GetPhoto(blockId)?.Caption);
        await dialog.ShowDialog(this);
        if (dialog.Confirmed)
        {
            _photos.SetCaption(blockId, dialog.Text);
            RefreshActions();
        }
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
        var window = new PeopleWindow(Roster);
        await window.ShowDialog(this);
        RefreshActions();

        // M55: the People window can record that a brother has passed, but it has no newsletter to
        // write a memorial in. It records the request; this opens M54's shelf with his name ready.
        if (window.MemorialRequestedFor is { } brother)
        {
            await OfferTheMemorialAsync(brother);
        }
    }

    internal async Task ImportPeopleAsync()
    {
        var window = new RosterImportWindow(Roster.Book);
        await window.ShowDialog(this);

        // M73(b1), gate 27: what is said is a function of what the window returned, and both
        // answers are said. Stopping the import used to be met with silence, which reads exactly
        // like a window that did something and did not mention it.
        if (window.Result is { } book)
        {
            Roster.Replace(book, "Import people from a file");
            Announce($"Your address book now has {book.Count} people.");
            return;
        }

        Announce("The import was stopped. Nothing in your address book was changed.");
    }

    internal async Task ExportPeopleAsync()
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
            return;
        }

        try
        {
            RosterExport.Save(Roster.Book, path);
            Announce($"Your address book was saved as {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync(
                "Could not save your address book",
                "TrestleBoard could not write that file. It may be open in Excel. " + ex.Message);
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

    internal async Task RestorePeopleAsync()
    {
        var dialog = new RosterRestoreDialog(Roster.Backups());
        await dialog.ShowDialog(this);

        if (dialog.Chosen is { } backup)
        {
            Roster.Restore(backup);
            Announce(
                $"Your address book was put back as it was on {RosterRestoreDialog.Describe(backup)}. "
                + "Undo the last change reverses this.");
        }
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
    internal async Task SyncBirthdaysAsync()
    {
        if (_session is null || _widgets is null)
        {
            return;
        }

        // The panel offers this beside a selected list; the "what's next" card offers it with
        // nothing selected at all, and then the shell finds the list and shows the user where it is.
        string? blockId = _frames?.SelectedBlockId;
        if (blockId is null || _widgets.GetWidgetType(blockId) != BirthdayListTypeId)
        {
            blockId = BirthdayListBlockIds().FirstOrDefault(
                id => TryReadBirthdayList(id, out BirthdayListData d)
                    && BirthdayRosterProjection.IsStale(d, Roster.Book.Members, _session.Document.Metadata.IssueMonth));
            if (blockId is null)
            {
                Announce("There is no birthday list on this newsletter yet. Add one from the Insert menu.");
                return;
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
            return;
        }

        if (!TryReadBirthdayList(blockId, out BirthdayListData current))
        {
            Announce("TrestleBoard could not read what is in this birthday list, so it left it alone.");
            return;
        }

        int month = _session.Document.Metadata.IssueMonth;
        BirthdayProjection plan = BirthdayRosterProjection.Plan(current, Roster.Book.Members, month);

        // Nothing to show is not nothing to do: the list may still be recorded against last month,
        // and leaving that behind would nag from the "what's next" card forever. The user pressed
        // the button, so the provenance is brought up to date without a dialog nobody needs.
        if (!plan.ChangesAnything)
        {
            ApplyBirthdayPlan(blockId, plan);
            Announce("The birthday list already matches your address book.");
            return;
        }

        if (!await ConfirmBirthdaysAsync(plan, month, inserting: false))
        {
            Announce("The birthday list was left exactly as it was.");
            return;
        }

        ApplyBirthdayPlan(blockId, plan);
        Announce(Describe(plan, month));
    }

    /// <summary>
    /// At insert time only: the extra first screen. With an empty address book — or a month nobody
    /// was born in — the wizard is exactly what it has always been, and the user is asked nothing.
    /// </summary>
    private async Task<System.Text.Json.JsonElement?> OfferBirthdaysFromRosterAsync()
    {
        if (_session is null || Roster.Book.Count == 0)
        {
            return null;
        }

        int month = _session.Document.Metadata.IssueMonth;
        BirthdayProjection plan = BirthdayRosterProjection.Plan(new BirthdayListData(), Roster.Book.Members, month);
        if (plan.Additions.Count == 0)
        {
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
    internal async Task SyncOfficersAsync()
    {
        if (_session is null || _widgets is null)
        {
            return;
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
                return;
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
            return;
        }

        if (!TryReadOfficers(blockId, out OfficersTableData current))
        {
            Announce("TrestleBoard could not read what is in this officers table, so it left it alone.");
            return;
        }

        OfficersProjection plan = OfficersRosterProjection.Plan(current, Roster.Book.Members);

        // Nothing to show is not nothing to do: the table may never have been stamped as generated,
        // and leaving that behind would nag from the "what's next" card forever. The user pressed
        // the button, so the provenance is brought up to date without a dialog nobody needs.
        if (!plan.HasAnythingToSay)
        {
            ApplyOfficerDecisions(blockId, current, plan, plan.DefaultDecisions);
            Announce("The officers table already matches your address book.");
            return;
        }

        OfficersSyncAnswer answer = await AskAboutOfficersAsync(plan, inserting: false);
        if (!answer.Confirmed)
        {
            Announce("The officers table was left exactly as it was.");
            return;
        }

        ApplyOfficerDecisions(blockId, current, plan, answer.Decisions);
        WritePhoneNumbersBack(answer.PhoneWriteBacks);
        Announce(Describe(answer.Decisions, plan));
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

        _settings = dialog.Result with { ShowActionPanel = _settings.ShowActionPanel };
        _settings.Save();
        ApplySettings(_settings);
        Announce("Saved. You can change this again from View, How things look.");
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
        foreach (Button button in ToolbarScale.GetLogicalDescendants().OfType<Button>())
        {
            // M37: the affordance that says "you can press this". Applied in the loop that already
            // walks the toolbar, so a button added to the XAML cannot miss it — and before the
            // early-out below, because a button without an icon still needs to look pressable.
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
        foreach (LayoutTransformControl host in new[] { MenuScale, ToolbarScale, StatusScale, PanelScale })
        {
            // A LAYOUT transform, not a render transform: scaled chrome has to take up the room it
            // now occupies, or the buttons simply overlap each other.
            host.LayoutTransform = scale == 1d ? null : new Avalonia.Media.ScaleTransform(scale, scale);
        }

        ApplyPanelVisibility();
        PageCanvas.ShowSpelling = _settings.ShowSpelling;
    }

    internal AppSettings SettingsForTest => _settings;

    /// <summary>Where the page view is scrolled to, so M50's zoom-anchor test can watch it move.</summary>
    internal Vector ScrollerOffsetForTest => CanvasScroller.Offset;

    /// <summary>Headless tests drive the shell directly; they must not get a modal start screen.</summary>
    internal static bool SuppressStartupForTest { get; set; } = true;

    internal Task RunStartupForTest() => RunStartupAsync();

    /// <summary>The chrome hosts the UI scale is applied to; the canvas is deliberately not one.</summary>
    internal LayoutTransformControl[] ChromeScaleHostsForTest => [MenuScale, ToolbarScale, StatusScale, PanelScale];

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

        await RunWizardAsync(blockId, grid: false, seeded);
    }

    internal async Task EditWidgetAsync(bool grid)
    {
        if (WidgetTarget() is { } blockId)
        {
            await RunWizardAsync(blockId, grid);
        }
    }

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
    private async Task RunWizardAsync(string blockId, bool grid, System.Text.Json.JsonElement? seeded = null)
    {
        if (_widgets is null || _session is null || !_widgets.CanEdit(blockId))
        {
            RefreshActions();
            return;
        }

        if (CreateSession(blockId, seeded) is not { } wizard)
        {
            return;
        }

        IReadOnlyList<PersonSuggestion> people = PeopleForWizards();

        // M19: the widget editor says where the list came from, in the same words the panel uses.
        string? banner = FilledInFromRoster(blockId) is { } filledIn
            ? ActionCatalog.DescribeFilledIn(filledIn)
            : null;

        if (grid)
        {
            var window = new WidgetGridWindow(wizard, people, banner);
            await window.ShowDialog(this);
            if (window.Confirmed)
            {
                _widgets.ApplyWidgetData(blockId, window.Data, window.DataVersion, window.UndoLabel);
            }
        }
        else
        {
            var window = new WizardWindow(wizard, people, banner);
            await window.ShowDialog(this);
            if (window.Confirmed)
            {
                _widgets.ApplyWidgetData(blockId, window.Data, window.DataVersion, window.UndoLabel);
                WritePhoneNumbersBack(window.PhoneWriteBacks);
            }
            else
            {
                Announce("Nothing was filled in yet. Press Ctrl+Z to take it back off the page.");
                return;
            }
        }

        RefreshActions();
    }

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
        _source = source;
        _session = session;
        _editor = editor;
        _frames = frames;
        _photos = photos;
        _widgets = widgets;
        _pages = pages;
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
}
