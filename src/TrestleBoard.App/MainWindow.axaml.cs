using Avalonia.Controls;
using Avalonia.Input;
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
using TrestleBoard.App.Updates;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Workflow;
using TrestleBoard.Editing;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using TrestleBoard.Widgets;
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

    /// <summary>Below this much window width the panel folds away rather than squeezing the page out.</summary>
    private const double PanelFoldWidth = 900d;

    private readonly FontStore _fonts = BundledFonts.CreateDefaultStore();
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
    private readonly WidgetLayoutProvider _widgetProvider = WidgetLayoutProvider.CreateDefault();
    private ActionContext _context = ActionContext.Empty;
    private string? _announcement;
    private int _pageIndex;
    private bool _fitToWindow = true;
    private bool _exportedThisSession;
    private int _regionIndex;

    public MainWindow()
    {
        InitializeComponent();
        _actions = new ActionRunner(this);
        ActionPanelHost.Content = _panel;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        PageCanvas.ContextRequested += OnCanvasContextRequested;
        PageCanvas.PeerAskedForContextMenu += (_, _) => ShowContextActions();
        ApplySettings(_settings);
        RefreshActions();

        // The start screen and the recovery offer are the app's front door; without this they exist
        // but nobody ever sees them.
        Opened += async (_, _) =>
        {
            if (!SuppressStartupForTest)
            {
                await RunStartupAsync();
            }
        };
        Closed += (_, _) =>
        {
            // A clean close removes the recovery file, so a file surviving startup MEANS the app
            // did not close cleanly (docs/M9-spec.md §1.4). Any unsaved work is written first.
            _recoveryTimer?.Stop();
            _recovery?.SaveNow();
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

        foreach (Button button in new[]
                 {
                     OpenButton, UndoButton, RedoButton, PrevPageButton, NextPageButton,
                     ZoomOutButton, ZoomInButton, FitButton,
                 })
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
                CoverDateMissing: CoverHeadingNeedsADate()));
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
            var item = new MenuItem { Header = styleRef, FontSize = 16 };
            Avalonia.Automation.AutomationProperties.SetName(item, styleRef);
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
        // SaveNow BEFORE Complete, exactly as the close path does. Complete() deletes the snapshot,
        // so dropping it without writing would throw away edits the user has not autosaved yet —
        // and there is no manual Save to fall back on.
        _recovery?.SaveNow();
        _recovery?.Complete();
        _recovery?.Dispose();
        _recoveryStore ??= new FileRecoveryStore();

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
        if (_source is { PageCount: > 0 })
        {
            try
            {
                package.Thumbnails["page-1.png"] = _source.RenderPageToPng(0, scale: 0.35f);
            }
            catch (InvalidOperationException)
            {
                // A thumbnail is a nicety; the document bytes are the point.
            }
        }

        using var buffer = new MemoryStream();
        TboardContainer.Save(package, buffer);
        return buffer.ToArray();
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
        _recoveryStore ??= new FileRecoveryStore();
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
        ShowPackage(package);
        Announce("Your work is back. Save it when you are ready.");
        return true;
    }

    // ---- The newsletter -----------------------------------------------------------------------

    internal async Task OpenNewsletterAsync()
    {
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
    /// Start-from-last-month: carries the data forward, bumps the date, clears the prose
    /// (docs/M9-spec.md §3). The result is a NEW unsaved newsletter, so the path is cleared.
    /// </summary>
    internal bool StartFromLastMonth()
    {
        if (_package is null)
        {
            return false;
        }

        TboardPackage next = CarryForward.NextIssue(_package);
        _documentPath = null;
        ShowPackage(next);
        Announce("Carried forward. Last month's articles have been cleared for you to rewrite.");
        return true;
    }

    internal DocumentRenderSource? SourceForTest => _source;

    internal WidgetLayoutProvider WidgetProviderForTest => _widgetProvider;

    internal TboardPackage? PackageForTest => _package;

    internal string? StatusLabelTextForTest => StatusLabel.Text;

    internal DocumentSession? SessionForTest => _session;

    internal PageCanvasControl CanvasForTest => PageCanvas;

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

    internal async Task PasteAsync()
    {
        if (_editor is not null)
        {
            await _editor.PasteAsync();
        }
    }

    internal void SelectAllText() => _editor?.SelectAll();

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
            var item = new MenuItem { Header = styleRef, FontSize = 16 };
            Avalonia.Automation.AutomationProperties.SetName(item, styleRef);
            item.Click += (_, _) =>
            {
                _editor?.ApplyParagraphStyle(styleRef);
                RefreshActions();
            };
            flyout.Items.Add(item);
        }

        flyout.ShowAt(source);
    }

    // ---- Frames (docs/M5-spec.md §9) ----------------------------------------------------------

    internal void AddTextFrame()
    {
        _editor?.End();
        _frames?.AddTextFrame(_pageIndex);
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

    private async Task InsertPhotoFromFileAsync(IStorageFile file)
    {
        byte[] bytes;
        try
        {
            await using Stream stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Could not open that picture", ex.Message);
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

    internal async Task AdjustPhotoAsync()
    {
        if (_photos is null || _frames?.SelectedBlockId is not { } blockId || !_photos.IsPhoto(blockId))
        {
            return;
        }

        var window = new PhotoAdjustWindow(_photos, blockId);
        await window.ShowDialog(this);
    }

    /// <summary>Drag-and-drop is an accelerator; the Insert menu item is the primary path (PLAN.md §6).</summary>
    private void OnCanvasDragOver(object? sender, DragEventArgs e)
    {
        bool hasFiles = _photos is not null && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnCanvasDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_photos is null
            || e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault() is not { } file)
        {
            return;
        }

        await InsertPhotoFromFileAsync(file);
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
        await RunWizardAsync(blockId, grid: false);
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
    private async Task RunWizardAsync(string blockId, bool grid)
    {
        if (_widgets is null || _session is null || !_widgets.CanEdit(blockId))
        {
            RefreshActions();
            return;
        }

        if (CreateSession(blockId) is not { } wizard)
        {
            return;
        }

        if (grid)
        {
            var window = new WidgetGridWindow(wizard);
            await window.ShowDialog(this);
            if (window.Confirmed)
            {
                _widgets.ApplyWidgetData(blockId, window.Data, window.DataVersion, window.UndoLabel);
            }
        }
        else
        {
            var window = new WizardWindow(wizard);
            await window.ShowDialog(this);
            if (window.Confirmed)
            {
                _widgets.ApplyWidgetData(blockId, window.Data, window.DataVersion, window.UndoLabel);
            }
            else
            {
                Announce("Nothing was filled in yet. Press Ctrl+Z to take it back off the page.");
                return;
            }
        }

        RefreshActions();
    }

    /// <summary>Pre-filled from whatever is already on the block — that IS the re-edit path (§7.1).</summary>
    private WizardSession? CreateSession(string blockId)
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
            definition, widget.Data, widget.DataVersion, WidgetController.SeedFrom(_session.Document));
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

    private void ShowPackage(TboardPackage package)
    {
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
        PageCanvas.Source = source;
        PageCanvas.Editor = editor;
        PageCanvas.FrameEditor = frames;
        PageCanvas.PageIndex = 0;
        Title = $"TrestleBoard — {package.Document.Metadata.Title}";

        session.Changed += (_, _) => RefreshActions();
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

    private void GoToPage(int index)
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
            ?? (_source is { IsOverset: true }
                ? "Some text does not fit in its frame. Select that frame to see what to do."
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
