using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TrestleBoard.App.Canvas;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Workflow;
using TrestleBoard.Editing;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.App;

/// <summary>
/// M3 viewer shell + M4 text editing: open a .tboard (or the built-in sample), click into a
/// text frame, type with full undo/redo, page through, zoom/fit, export the PDF. Every mouse
/// action has a keyboard path (PLAN.md §6).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime is its Closed event, not IDisposable; the recovery service, "
        + "render source and font store are all released there.")]
public partial class MainWindow : Window
{
    private static readonly double[] ZoomSteps = [0.5, 0.65, 0.8, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0];

    private readonly FontStore _fonts = BundledFonts.CreateDefaultStore();
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
    private readonly WidgetLayoutProvider _widgetProvider = WidgetLayoutProvider.CreateDefault();
    private int _pageIndex;
    private bool _fitToWindow = true;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
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
        };
    }

    /// <summary>
    /// Wires autosave to the open document. One tick a second; the service decides whether a rule
    /// says it is time to write (docs/M9-spec.md §1.1).
    /// </summary>
    private void StartRecovery(TboardPackage package)
    {
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

    private void OnRecoveryTick(object? sender, EventArgs e) => _recovery?.Poll();

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
        StatusLabel.Text = "Your work is back. Save it when you are ready.";
        return true;
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
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
            ShowPackage(TboardContainer.Load(buffer));
        }
        catch (Exception ex) when (ex is Core.Migrations.UnsupportedFormatException or System.IO.InvalidDataException)
        {
            await ShowErrorAsync("Could not open that file", ex.Message);
        }
    }

    private void OnOpenSampleClicked(object? sender, RoutedEventArgs e) => OpenSample();

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
        StatusLabel.Text = "Carried forward. Last month's articles have been cleared for you to rewrite.";
        return true;
    }

    internal DocumentRenderSource? SourceForTest => _source;

    internal WidgetLayoutProvider WidgetProviderForTest => _widgetProvider;

    internal TboardPackage? PackageForTest => _package;

    internal string? StatusLabelTextForTest => StatusLabel.Text;

    internal DocumentSession? SessionForTest => _session;

    internal PageCanvasControl CanvasForTest => PageCanvas;

    internal void GoToNextPageForTest() => GoToPage(_pageIndex + 1);

    private async void OnExportPdfClicked(object? sender, RoutedEventArgs e)
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ShowErrorAsync(
                "Could not export the PDF",
                "The PDF could not be saved. Make sure the file is not open in another program and try again. "
                + $"({ex.Message})");
        }
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e) => Close();

    // ---- Edit / Format ----------------------------------------------------------------------

    private void OnUndoClicked(object? sender, RoutedEventArgs e) => _session?.Undo();

    private void OnRedoClicked(object? sender, RoutedEventArgs e) => _session?.Redo();

    private async void OnCutClicked(object? sender, RoutedEventArgs e)
    {
        if (_editor is not null)
        {
            await _editor.CutAsync();
        }
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (_editor is not null)
        {
            await _editor.CopyAsync();
        }
    }

    private async void OnPasteClicked(object? sender, RoutedEventArgs e)
    {
        if (_editor is not null)
        {
            await _editor.PasteAsync();
        }
    }

    private void OnSelectAllClicked(object? sender, RoutedEventArgs e) => _editor?.SelectAll();

    private void OnBoldClicked(object? sender, RoutedEventArgs e)
    {
        _editor?.ToggleBold();
        UpdateEditChrome();
    }

    private void OnItalicClicked(object? sender, RoutedEventArgs e)
    {
        _editor?.ToggleItalic();
        UpdateEditChrome();
    }

    private void OnParagraphStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_editor is { IsActive: true } && ParagraphStyleCombo.SelectedItem is string styleRef)
        {
            _editor.ApplyParagraphStyle(styleRef);
            UpdateEditChrome();
        }
    }

    // ---- Object (frames, docs/M5-spec.md §9) ------------------------------------------------

    private void OnAddTextFrameClicked(object? sender, RoutedEventArgs e)
    {
        _editor?.End();
        _frames?.AddTextFrame(_pageIndex);
        UpdateEditChrome();
    }

    private void OnDeleteFrameClicked(object? sender, RoutedEventArgs e)
    {
        _frames?.DeleteSelected();
        UpdateEditChrome();
    }

    private void OnToggleWrapClicked(object? sender, RoutedEventArgs e)
    {
        _frames?.ToggleWrap();
        UpdateEditChrome();
    }

    private void OnBringForwardClicked(object? sender, RoutedEventArgs e) => Restack(f => f.BringForward());

    private void OnSendBackwardClicked(object? sender, RoutedEventArgs e) => Restack(f => f.SendBackward());

    private void OnBringToFrontClicked(object? sender, RoutedEventArgs e) => Restack(f => f.BringToFront());

    private void OnSendToBackClicked(object? sender, RoutedEventArgs e) => Restack(f => f.SendToBack());

    private void Restack(Func<FrameEditorController, bool> action)
    {
        if (_frames is not null)
        {
            action(_frames);
            UpdateEditChrome();
        }
    }

    // ---- Photos (docs/M6-spec.md §7) --------------------------------------------------------

    private async void OnInsertPhotoClicked(object? sender, RoutedEventArgs e)
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
        UpdateEditChrome();
    }

    private void OnFixPhotoClicked(object? sender, RoutedEventArgs e)
    {
        if (_photos is not null && _frames?.SelectedBlockId is { } blockId)
        {
            _photos.FixPhoto(blockId);
            UpdateEditChrome();
        }
    }

    private async void OnAdjustPhotoClicked(object? sender, RoutedEventArgs e)
    {
        if (_photos is null || _frames?.SelectedBlockId is not { } blockId || !_photos.IsPhoto(blockId))
        {
            return;
        }

        var window = new PhotoAdjustWindow(_photos, blockId);
        await window.ShowDialog(this);
        UpdateEditChrome();
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

    // ---- Pages and flow (docs/M8-spec.md §2/§3) ---------------------------------------------

    private void OnAddPageClicked(object? sender, RoutedEventArgs e)
    {
        if (_pages is not null)
        {
            _pages.AddPage(_pageIndex);
            GoToPage(_pageIndex + 1);
        }
    }

    private void OnRemovePageClicked(object? sender, RoutedEventArgs e)
    {
        if (_pages is not null && _pages.RemovePage(_pageIndex))
        {
            GoToPage(Math.Min(_pageIndex, _pages.PageCount - 1));
        }
    }

    private void OnMovePageEarlierClicked(object? sender, RoutedEventArgs e) => MovePage(-1);

    private void OnMovePageLaterClicked(object? sender, RoutedEventArgs e) => MovePage(1);

    private void MovePage(int delta)
    {
        if (_pages is not null && _pages.MovePage(_pageIndex, _pageIndex + delta))
        {
            GoToPage(_pageIndex + delta);
        }
    }

    private void OnAutoFlowClicked(object? sender, RoutedEventArgs e)
    {
        if (_pages is not null && _frames?.SelectedBlockId is { } blockId)
        {
            _pages.AutoFlow(blockId);
        }
    }

    // ---- Widgets (docs/M7-spec.md §7/§8) ----------------------------------------------------

    /// <summary>
    /// Insert puts an EMPTY widget on the page and opens its wizard straight away: inserting means
    /// "I want to fill this in", and the box is already visible while the questions are answered.
    /// </summary>
    private async void OnInsertWidgetClicked(object? sender, RoutedEventArgs e)
    {
        if (_widgets is null || sender is not MenuItem { Tag: string typeId })
        {
            return;
        }

        string blockId = _widgets.InsertWidget(_pageIndex, typeId);
        _frames?.Select(blockId);
        UpdateEditChrome();
        await RunWizardAsync(blockId, grid: false);
    }

    private async void OnEditWidgetClicked(object? sender, RoutedEventArgs e)
    {
        if (_frames?.SelectedBlockId is { } blockId)
        {
            await RunWizardAsync(blockId, grid: false);
        }
    }

    private async void OnEditWidgetListClicked(object? sender, RoutedEventArgs e)
    {
        if (_frames?.SelectedBlockId is { } blockId)
        {
            await RunWizardAsync(blockId, grid: true);
        }
    }

    private void OnFitToContentsClicked(object? sender, RoutedEventArgs e)
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
            UpdateEditChrome();
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
                StatusLabel.Text =
                    "Nothing was filled in yet. Press Ctrl+Z to take it back off the page.";
                return;
            }
        }

        UpdateEditChrome();
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

    private void OnLinkFramesClicked(object? sender, RoutedEventArgs e)
    {
        _frames?.BeginLink();
        PageCanvas.Focus();
        UpdateEditChrome();
    }

    private void OnUnlinkFramesClicked(object? sender, RoutedEventArgs e)
    {
        _frames?.Unlink();
        UpdateEditChrome();
    }

    // ---- Navigation / zoom ------------------------------------------------------------------

    private void OnNextPageClicked(object? sender, RoutedEventArgs e) => GoToPage(_pageIndex + 1);

    private void OnPreviousPageClicked(object? sender, RoutedEventArgs e) => GoToPage(_pageIndex - 1);

    private void OnZoomInClicked(object? sender, RoutedEventArgs e) => StepZoom(+1);

    private void OnZoomOutClicked(object? sender, RoutedEventArgs e) => StepZoom(-1);

    private void OnZoomResetClicked(object? sender, RoutedEventArgs e) => SetZoom(1d, fit: false);

    private void OnFitPageClicked(object? sender, RoutedEventArgs e)
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
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool editing = _editor is { IsActive: true };
        switch (e.Key)
        {
            // Bare PageUp/Down belongs to the caret while editing; page navigation keeps the
            // bare gesture only when no session is active (docs/M4-spec.md §4.3).
            case Key.PageDown when ctrl || !editing:
                GoToPage(_pageIndex + 1);
                e.Handled = true;
                break;
            case Key.PageUp when ctrl || !editing:
                GoToPage(_pageIndex - 1);
                e.Handled = true;
                break;
            case Key.Z when ctrl:
                _session?.Undo();
                e.Handled = true;
                break;
            // MUST exclude Shift: a bare "when ctrl" also matches Ctrl+Shift+Y, which would shadow
            // Fit to contents below and silently redo instead.
            case Key.Y when ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                _session?.Redo();
                e.Handled = true;
                break;
            case Key.B when ctrl && editing:
                _editor!.ToggleBold();
                UpdateEditChrome();
                e.Handled = true;
                break;
            case Key.I when ctrl && editing:
                _editor!.ToggleItalic();
                UpdateEditChrome();
                e.Handled = true;
                break;
            case Key.X when ctrl && editing:
                OnCutClicked(sender, e);
                e.Handled = true;
                break;
            case Key.C when ctrl && editing:
                OnCopyClicked(sender, e);
                e.Handled = true;
                break;
            case Key.V when ctrl && editing:
                OnPasteClicked(sender, e);
                e.Handled = true;
                break;
            case Key.OemPlus when ctrl:
                StepZoom(+1);
                e.Handled = true;
                break;
            case Key.OemMinus when ctrl:
                StepZoom(-1);
                e.Handled = true;
                break;
            case Key.D0 when ctrl:
                SetZoom(1d, fit: false);
                e.Handled = true;
                break;
            case Key.D1 when ctrl:
                _fitToWindow = true;
                ApplyFitZoom();
                e.Handled = true;
                break;
            case Key.T when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnAddTextFrameClicked(sender, e);
                e.Handled = true;
                break;
            case Key.W when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnToggleWrapClicked(sender, e);
                e.Handled = true;
                break;
            case Key.P when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnInsertPhotoClicked(sender, e);
                e.Handled = true;
                break;
            case Key.F when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnFixPhotoClicked(sender, e);
                e.Handled = true;
                break;
            case Key.A when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnAdjustPhotoClicked(sender, e);
                e.Handled = true;
                break;
            case Key.L when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnLinkFramesClicked(sender, e);
                e.Handled = true;
                break;
            case Key.K when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnUnlinkFramesClicked(sender, e);
                e.Handled = true;
                break;
            case Key.OemCloseBrackets when ctrl:
                Restack(f => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? f.BringToFront() : f.BringForward());
                e.Handled = true;
                break;
            case Key.OemOpenBrackets when ctrl:
                Restack(f => e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? f.SendToBack() : f.SendBackward());
                e.Handled = true;
                break;
            case Key.E when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnEditWidgetClicked(sender, e);
                e.Handled = true;
                break;
            case Key.G when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnEditWidgetListClicked(sender, e);
                e.Handled = true;
                break;
            case Key.Y when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnFitToContentsClicked(sender, e);
                e.Handled = true;
                break;
            case Key.M when ctrl && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                OnAutoFlowClicked(sender, e);
                e.Handled = true;
                break;
            case Key.O when ctrl:
                OnOpenClicked(sender, e);
                e.Handled = true;
                break;
            case Key.E when ctrl:
                OnExportPdfClicked(sender, e);
                e.Handled = true;
                break;
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
        PageCanvas.Source = source;
        PageCanvas.Editor = editor;
        PageCanvas.FrameEditor = frames;
        PageCanvas.PageIndex = 0;
        Title = $"TrestleBoard — {package.Document.Metadata.Title}";

        session.Changed += (_, _) => UpdateEditChrome();
        StartRecovery(package);
        editor.Changed += (_, _) => UpdateEditChrome();
        frames.Changed += (_, _) => UpdateEditChrome();
        photos.Changed += (_, _) => UpdateEditChrome();
        widgets.Changed += (_, _) => UpdateEditChrome();
        pages.Changed += (_, _) => { UpdatePageChrome(); UpdateEditChrome(); };
        editor.RevealRequested += OnCaretReveal;

        bool hasDoc = source.PageCount > 0;
        ExportPdfMenuItem.IsEnabled = hasDoc;
        ZoomInButton.IsEnabled = hasDoc;
        ZoomOutButton.IsEnabled = hasDoc;
        FitButton.IsEnabled = hasDoc;
        ParagraphStyleCombo.ItemsSource = editor.AvailableParagraphStyles;
        _fitToWindow = true;
        ApplyFitZoom();
        UpdatePageChrome();
        UpdateEditChrome();
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
        // Selection is per page; carrying it to another page would make the Object menu act on
        // something the user cannot see.
        _frames?.ClearSelection();
        if (_fitToWindow)
        {
            ApplyFitZoom();
        }

        UpdatePageChrome();
    }

    private void StepZoom(int direction)
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

    private void UpdatePageChrome()
    {
        if (_source is null)
        {
            return;
        }

        PageLabel.Text = $"Page {_pageIndex + 1} of {_source.PageCount}";
        PrevPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = _pageIndex < _source.PageCount - 1;
    }

    private void UpdateEditChrome()
    {
        bool canUndo = _session?.CanUndo == true;
        bool canRedo = _session?.CanRedo == true;
        UndoMenuItem.IsEnabled = canUndo;
        RedoMenuItem.IsEnabled = canRedo;
        UndoButton.IsEnabled = canUndo;
        RedoButton.IsEnabled = canRedo;
        // Plain-language labels straight from the command descriptions (PLAN.md §4).
        UndoMenuItem.Header = canUndo ? $"_Undo {_session!.UndoDescription}" : "_Undo";
        RedoMenuItem.Header = canRedo ? $"_Redo {_session!.RedoDescription}" : "_Redo";

        bool editing = _editor is { IsActive: true };
        bool hasSelection = editing && !_editor!.Selection.IsEmpty;
        CutMenuItem.IsEnabled = hasSelection;
        CopyMenuItem.IsEnabled = hasSelection;
        PasteMenuItem.IsEnabled = editing;
        SelectAllMenuItem.IsEnabled = editing;
        BoldMenuItem.IsEnabled = editing;
        ItalicMenuItem.IsEnabled = editing;
        BoldButton.IsEnabled = editing;
        ItalicButton.IsEnabled = editing;
        ParagraphStyleCombo.IsEnabled = editing;
        BoldButton.IsChecked = editing && _editor!.IsBoldActive;
        ItalicButton.IsChecked = editing && _editor!.IsItalicActive;

        UpdateFrameChrome();
    }

    private void UpdateFrameChrome()
    {
        bool hasDocument = _source is { PageCount: > 0 };
        bool hasFrame = _frames is { HasSelection: true };
        bool isTextFrame = hasFrame
            && _source is not null
            && _source.IsTextBlock(_frames!.SelectedBlockId!);

        AddTextFrameMenuItem.IsEnabled = hasDocument;
        AddTextFrameButton.IsEnabled = hasDocument;
        DeleteFrameMenuItem.IsEnabled = hasFrame;
        WrapMenuItem.IsEnabled = hasFrame;
        WrapButton.IsEnabled = hasFrame;
        BringForwardMenuItem.IsEnabled = hasFrame;
        SendBackwardMenuItem.IsEnabled = hasFrame;
        BringToFrontMenuItem.IsEnabled = hasFrame;
        SendToBackMenuItem.IsEnabled = hasFrame;
        BringForwardButton.IsEnabled = hasFrame;
        SendBackwardButton.IsEnabled = hasFrame;
        LinkFramesMenuItem.IsEnabled = isTextFrame;
        UnlinkFramesMenuItem.IsEnabled = isTextFrame;

        bool multiPage = _pages is { PageCount: > 1 };
        AddPageMenuItem.IsEnabled = hasDocument;
        RemovePageMenuItem.IsEnabled = multiPage;
        MovePageUpMenuItem.IsEnabled = multiPage && _pageIndex > 0;
        MovePageDownMenuItem.IsEnabled = multiPage && _pageIndex < (_pages?.PageCount ?? 0) - 1;
        AutoFlowMenuItem.IsEnabled = hasFrame && _pages?.CanAutoFlow(_frames!.SelectedBlockId) == true;

        bool isWidget = hasFrame && _widgets?.IsWidget(_frames!.SelectedBlockId) == true;
        bool canEditWidget = isWidget && _widgets!.CanEdit(_frames!.SelectedBlockId);
        InsertOfficersMenuItem.IsEnabled = hasDocument;
        InsertBirthdaysMenuItem.IsEnabled = hasDocument;
        InsertCommitteesMenuItem.IsEnabled = hasDocument;
        InsertDistrictMenuItem.IsEnabled = hasDocument;
        InsertAnnouncementMenuItem.IsEnabled = hasDocument;
        InsertCoverMenuItem.IsEnabled = hasDocument;
        EditWidgetMenuItem.IsEnabled = canEditWidget;
        EditWidgetListMenuItem.IsEnabled = canEditWidget && CreateSession(_frames!.SelectedBlockId!)?.HasListSteps == true;
        FitToContentsMenuItem.IsEnabled = isWidget;

        bool isPhoto = hasFrame && _photos?.IsPhoto(_frames!.SelectedBlockId) == true;
        InsertPhotoMenuItem.IsEnabled = hasDocument;
        InsertPhotoButton.IsEnabled = hasDocument;
        FixPhotoMenuItem.IsEnabled = isPhoto;
        FixPhotoButton.IsEnabled = isPhoto;
        AdjustPhotoMenuItem.IsEnabled = isPhoto;

        StatusLabel.Text = _pages?.StatusMessage
            ?? _widgets?.StatusMessage
            ?? _photos?.StatusMessage
            ?? _frames?.StatusMessage
            ?? (_source is { IsOverset: true }
                ? "Some text does not fit in its frame. Select that frame to see what to do."
                : "");
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
        // Plain-language error dialog (PLAN.md §6); a richer shared dialog arrives with M9.
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
