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
    private string? _documentPath;
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
    /// M24: the newsletter has been edited since it was last written to <see cref="_documentPath"/>.
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
        OpenButton, UndoButton, RedoButton, PrevPageButton, NextPageButton,
        ZoomOutButton, ZoomInButton, FitButton,
    ];

    internal ActionPanel PanelForTest => _panel;

    /// <summary>
    /// Says something in the status bar, which is a polite live region (PLAN.md §6) — this is where
    /// the reason an action could not run is spoken. It survives the next refresh and is cleared by
    /// the one after, so a refusal is readable but does not sit there for the rest of the session.
    /// </summary>
    internal void Announce(string message)
    {
        _announcement = message;
        StatusLabel.Text = message;
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

        _panel.Update(
            _context,
            ActionCatalog.ForSelection(_context),
            WhatsNext.Suggestions(_context),
            (id, source) => _ = _actions.RunAsync(id, source));

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
                DocumentFileName: _documentPath is { } saved ? Path.GetFileName(saved) : null));
    }

    /// <summary>
    /// The one "what's next" source that needs to read widget data: a cover heading on the page with
    /// no meeting date typed into it. Read here rather than in Editing, which knows nothing about
    /// what is inside a widget's payload.
    /// </summary>
    private bool CoverHeadingNeedsADate()
    {
        if (_session is null)
        {
            return false;
        }

        foreach (Core.Model.Page page in _session.Document.Pages)
        {
            foreach (Core.Model.Block block in page.Blocks)
            {
                if (block is not Core.Model.WidgetBlock { WidgetType: "coverBanner" } cover)
                {
                    continue;
                }

                if (cover.Data is not { } data
                    || !data.TryGetProperty("meetingDateText", out System.Text.Json.JsonElement date)
                    || string.IsNullOrWhiteSpace(date.GetString()))
                {
                    return true;
                }
            }
        }

        return false;
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
            var item = new MenuItem
            {
                Header = offer.Action.Title,
                FontSize = 16,
                IsEnabled = offer.IsAvailable,
                Tag = offer.Action.Id,
            };
            Avalonia.Automation.AutomationProperties.SetName(item, offer.Action.Title);
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

    internal void ToggleActionPanel()
    {
        _settings = _settings with { ShowActionPanel = !_settings.ShowActionPanel };
        _settings.Save();
        ApplyPanelVisibility();
        Announce(_settings.ShowActionPanel
            ? "The panel of things you can do is showing."
            : "The panel is hidden. Bring it back from View, Show what I can do.");
    }

    /// <summary>
    /// The chrome budget (PLAN.md §11 M11): the panel folds itself away on a narrow window rather
    /// than leaving the page a strip down the middle.
    /// </summary>
    private void ApplyPanelVisibility()
    {
        bool roomForIt = Bounds.Width <= 0 || Bounds.Width >= PanelFoldWidth;
        bool showPanel = _settings.ShowActionPanel && roomForIt;
        PanelScale.IsVisible = showPanel;
        CollapsedPanelHost.IsVisible = !showPanel;
        ShowPanelButton.Content = _settings.ShowActionPanel && !roomForIt
            ? "▸"
            : "What can I do? ▸";
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
            () => new RecoveryService.RecoveryPayload(SnapshotBytes(package), _documentPath));

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

        var start = new StartDialog(canStartFromLastMonth: false);
        await start.ShowDialog(this);

        switch (start.Choice)
        {
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
        await dialog.ShowDialog(this);

        if (!dialog.Restore)
        {
            _recoveryStore.Delete(snapshot.Id);
            return false;
        }

        _documentPath = snapshot.OriginalPath;

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
            _documentPath = files[0].TryGetLocalPath();
            ShowPackage(TboardContainer.Load(buffer));
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
            _documentPath = path;
            ShowPackage(TboardContainer.Load(buffer));
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidDataException
            or Core.Migrations.UnsupportedFormatException)
        {
            _documentPath = null;
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

        var start = new StartDialog(canStartFromLastMonth: _package is not null);
        await start.ShowDialog(this);

        switch (start.Choice)
        {
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

    internal async Task ExportPdfAsync()
    {
        if (_source is null || _package is null)
        {
            return;
        }

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export as PDF",
            DefaultExtension = "pdf",
            SuggestedFileName = $"{_package.Document.Metadata.Title} {_package.Document.Metadata.IssueYear}-{_package.Document.Metadata.IssueMonth:00}.pdf",
            FileTypeChoices = [new FilePickerFileType("PDF document") { Patterns = ["*.pdf"] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            Core.Model.DocumentMetadata meta = _package.Document.Metadata;
            DocumentPdfExporter.Export(
                stream,
                _source,
                new PdfMetadata(meta.Title, meta.LodgeName, $"Trestle board {meta.IssueYear}-{meta.IssueMonth:00}"));
            _exportedThisSession = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(
                "Could not export the PDF",
                "The PDF could not be saved. Make sure the file is not open in another program and try again. "
                + $"({ex.Message})");
        }
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
    internal string? DocumentPathForTest => _documentPath;

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

        return _documentPath is { } path
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

        _documentPath = path;
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
        var stay = new Button { Content = "Go back", FontSize = 18, MinHeight = 44, MinWidth = 150, IsCancel = true };

        save.Click += (_, _) => { answer = SaveFirst.Save; dialog.Close(); };
        discard.Click += (_, _) => { answer = SaveFirst.Discard; dialog.Close(); };
        stay.Click += (_, _) => { answer = SaveFirst.Stay; dialog.Close(); };

        string where = _documentPath is { } path
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

    internal async Task CheckForUpdatesAsync(bool userAsked)
    {
        _updates ??= new UpdateCoordinator(new VelopackUpdateChannel());
        UpdateOutcome outcome = await _updates.CheckAsync(userAsked);
        if (outcome.Announce)
        {
            Announce(outcome.Message);
        }
    }

    internal Task ShowAboutAsync() =>
        ShowErrorAsync(
            "About TrestleBoard",
            $"TrestleBoard {AppVersion()}\n\n"
                + "The newsletter editor for Indian Land Masonic Lodge 414.\n\n"
                + "Installing and updating are explained in docs/INSTALL.md, which also came with "
                + "your download.");

    internal static string AppVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Lets the headless tests drive the update wiring without a network or an installer.</summary>
    internal void UseUpdateChannelForTest(IUpdateChannel channel) =>
        _updates = new UpdateCoordinator(channel);

    internal Task CheckForUpdatesForTest(bool userAsked) => CheckForUpdatesAsync(userAsked);

    internal UpdateCoordinator? UpdatesForTest => _updates;

    /// <summary>Also the headless-test entry point (no file dialog involved).</summary>
    internal void OpenSample() => ShowPackage(SampleDocument.CreatePackage(SamplePhoto.CreatePng()));

    /// <summary>The whole five-page issue fixture (docs/M8-spec.md §6); the headless tests' entry point.</summary>
    internal void OpenIssueSample() => ShowPackage(SampleIssue.CreatePackage(SamplePhoto.CreatePng()));

    internal string? PageLabelTextForTest => PageLabel.Text;

    internal string? ZoomLabelTextForTest => ZoomLabel.Text;

    internal TextEditorController? EditorForTest => _editor;

    internal FrameEditorController? FramesForTest => _frames;

    internal PhotoController? PhotosForTest => _photos;

    internal WidgetController? WidgetsForTest => _widgets;

    internal PageFlowController? PagesForTest => _pages;

    internal RecoveryService? RecoveryForTest => _recovery;

    internal void UseRecoveryStoreForTest(IRecoveryStore store) => _recoveryStore = store;

    /// <summary>Opens one of the shipped templates (PLAN.md §7).</summary>
    internal void OpenTemplate(string templateId)
    {
        _documentPath = null;
        ShowPackage(TemplateLibrary.Create(templateId));
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
        _documentPath = null;
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

    // ---- Edit / Format ------------------------------------------------------------------------

    internal void Undo() => _session?.Undo();

    internal void Redo() => _session?.Redo();

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
            await _editor.PasteAsync();
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

    internal void ToggleBold() => _editor?.ToggleBold();

    internal void ToggleItalic() => _editor?.ToggleItalic();

    /// <summary>
    /// The panel's "Paragraph style ▸" opens the same list the Format menu shows, beside the button
    /// that was pressed — a menu the user can reach without leaving the panel.
    /// </summary>
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

        flyout.ShowAt(source);
    }

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

    internal void ClearFontOverrideHere()
    {
        _editor?.ClearFontOverride();
        Announce("Put back to the usual font.");
        RefreshActions();
    }

    /// <summary>Puts every "just here" font in the newsletter back, in one undo step.</summary>
    internal void ClearEveryFontOverride()
    {
        if (_session is null || _editor is null)
        {
            return;
        }

        int count = _editor.CountFontOverrides();
        if (count == 0)
        {
            Announce("Nothing in this newsletter uses a font of its own.");
            return;
        }

        _editor.SelectAll();
        _editor.ClearFontOverride();
        Announce(count == 1
            ? "One piece of text was put back to its usual font. Press Ctrl+Z to undo."
            : $"{count} pieces of text were put back to their usual fonts. Press Ctrl+Z to undo.");
        RefreshActions();
    }

    internal void ToggleShowFontChanges() => SetShowFontChanges(!PageCanvas.ShowFontChanges);

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

    /// <summary>Help → "Fonts and licences": the OFL text the licence requires we ship (OFL §II(3)).</summary>
    internal Task ShowFontLicencesAsync() =>
        ShowScrollingTextAsync("Fonts and licences", BundledFonts.ReadLicenceText());

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

    internal void DeleteSelectedFrame() => _frames?.DeleteSelected();

    internal void ToggleWrap() => _frames?.ToggleWrap();

    internal void Restack(Func<FrameEditorController, bool> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_frames is not null)
        {
            action(_frames);
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
        if (_pages is not null && SelectedTextBlockId is { } blockId)
        {
            _pages.AutoFlow(blockId);
        }
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
    }

    internal async Task ImportPeopleAsync()
    {
        var window = new RosterImportWindow(Roster.Book);
        await window.ShowDialog(this);

        if (window.Result is { } book)
        {
            Roster.Replace(book, "Import people from a file");
            Announce($"Your address book now has {book.Count} people.");
        }
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

            _frames?.Select(blockId);
            GoToPage(PageOf(blockId));
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

            _frames?.Select(blockId);
            GoToPage(PageOf(blockId));
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
            if (button is not { Tag: string actionId, Content: string label })
            {
                continue;
            }

            if (ActionIcons.ForAction(actionId) is not { } glyph)
            {
                continue;
            }

            button.Content = new IconText(glyph, label, labelSize: 16);
        }
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
    }

    internal AppSettings SettingsForTest => _settings;

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
        if (_frames?.SelectedBlockId is { } blockId)
        {
            await RunWizardAsync(blockId, grid);
        }
    }

    internal void FitWidgetToContents()
    {
        if (_frames?.SelectedBlockId is { } blockId)
        {
            _widgets?.FitToContents(blockId);
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

        // M21: a find window left open over the newsletter that has just been closed would be
        // searching a document nobody is looking at any more.
        _findWindow?.Close();
        _find = new FindController(session, editor);
        _find.Changed += (_, _) =>
        {
            if (_find?.StatusMessage is { } said)
            {
                Announce(said);
            }
        };
        PageCanvas.Source = source;
        PageCanvas.Editor = editor;
        PageCanvas.FrameEditor = frames;
        PageCanvas.PageIndex = 0;

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
        editor.Changed += (_, _) => RefreshActions();
        frames.Changed += (_, _) => RefreshActions();
        photos.Changed += (_, _) => RefreshActions();
        widgets.Changed += (_, _) => RefreshActions();
        pages.Changed += (_, _) => RefreshActions();
        editor.RevealRequested += OnCaretReveal;

        _fitToWindow = true;
        ApplyFitZoom();
        RefreshActions();
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

        RefreshActions();
    }

    internal void StepZoom(int direction)
    {
        if (_source is null)
        {
            return;
        }

        double current = PageCanvas.Zoom;
        double next = direction > 0
            ? ZoomSteps.FirstOrDefault(z => z > current + 0.001, ZoomSteps[^1])
            : ZoomSteps.LastOrDefault(z => z < current - 0.001, ZoomSteps[0]);
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
            StepZoom(direction);
            return;
        }

        double pageX = (pointInCanvas.X - PageCanvasControl.PagePaddingPx) / before;
        double pageY = (pointInCanvas.Y - PageCanvasControl.PagePaddingPx) / before;

        StepZoom(direction);
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

    private void UpdatePageChrome() =>
        PageLabel.Text = _source is { PageCount: > 0 }
            ? $"Page {_pageIndex + 1} of {_source.PageCount}"
            : "No newsletter";

    private void UpdateStatus()
    {
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

        StatusLabel.Text = message ?? _announcement ?? "";
        _announcement = null;
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

    private async Task ShowErrorAsync(string title, string message)
    {
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
                    new Button { Content = "OK", FontSize = 18, MinHeight = 44, MinWidth = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
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
