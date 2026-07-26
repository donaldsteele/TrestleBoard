using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TrestleBoard.App.Canvas;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Samples;
using TrestleBoard.Editing;
using TrestleBoard.Export.Pdf;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;

namespace TrestleBoard.App;

/// <summary>
/// M3 viewer shell + M4 text editing: open a .tboard (or the built-in sample), click into a
/// text frame, type with full undo/redo, page through, zoom/fit, export the PDF. Every mouse
/// action has a keyboard path (PLAN.md §6).
/// </summary>
public partial class MainWindow : Window
{
    private static readonly double[] ZoomSteps = [0.5, 0.65, 0.8, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0];

    private readonly FontStore _fonts = BundledFonts.CreateDefaultStore();
    private TboardPackage? _package;
    private DocumentRenderSource? _source;
    private DocumentSession? _session;
    private TextEditorController? _editor;
    private int _pageIndex;
    private bool _fitToWindow = true;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _source?.Dispose();
            _fonts.Dispose();
        };
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

    internal string? PageLabelTextForTest => PageLabel.Text;

    internal string? ZoomLabelTextForTest => ZoomLabel.Text;

    internal TextEditorController? EditorForTest => _editor;

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
            case Key.Y when ctrl:
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
            package.Document, package.Assets, _fonts, session);
        var editor = new TextEditorController(session, source, new AvaloniaTextClipboard(this));

        _source?.Dispose();
        _source = source;
        _session = session;
        _editor = editor;
        _package = package;
        _pageIndex = 0;
        PageCanvas.Source = source;
        PageCanvas.Editor = editor;
        PageCanvas.PageIndex = 0;
        Title = $"TrestleBoard — {package.Document.Metadata.Title}";

        session.Changed += (_, _) => UpdateEditChrome();
        editor.Changed += (_, _) => UpdateEditChrome();
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
