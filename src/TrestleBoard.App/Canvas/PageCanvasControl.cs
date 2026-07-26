using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using TrestleBoard.Editing;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Canvas;

/// <summary>
/// The page canvas: draws one document page through the app's own SkiaSharp pipeline via
/// Avalonia's <see cref="ISkiaSharpApiLeaseFeature"/> (PLAN.md §11 M3) — the exact renderer
/// the PDF export uses, so what you see IS what prints. M4 adds focus, pointer and keyboard
/// input, the caret blink timer (UI-only — no clock reaches Layout), and the editor overlay
/// (selection + caret) drawn after the page content, never on the export path.
///
/// Fallback note (plan-required): if a platform ever runs a non-Skia Avalonia backend the
/// lease feature returns null and the page is not drawn. The documented fallback is to render
/// into a WriteableBitmap (lock the framebuffer, wrap it in SKSurface.Create(info, address,
/// rowBytes), call RenderPage, then DrawImage the bitmap). All Avalonia 11.3 desktop targets
/// ship the Skia backend, so the fallback stays unimplemented until a real platform needs it.
/// </summary>
public sealed class PageCanvasControl : Control
{
    /// <summary>Empty margin around the page so the sheet reads as a sheet (points × zoom).</summary>
    private const double PagePaddingPx = 24d;

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PageCanvasControl, double>(nameof(Zoom), defaultValue: 1d);

    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<PageCanvasControl, int>(nameof(PageIndex), defaultValue: 0);

    private readonly DispatcherTimer _caretBlink;
    private DocumentRenderSource? _source;
    private TextEditorController? _editor;
    private bool _caretVisible = true;

    static PageCanvasControl()
    {
        AffectsRender<PageCanvasControl>(ZoomProperty, PageIndexProperty);
        AffectsMeasure<PageCanvasControl>(ZoomProperty, PageIndexProperty);
        FocusableProperty.OverrideDefaultValue<PageCanvasControl>(true);
    }

    public PageCanvasControl()
    {
        _caretBlink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _caretBlink.Tick += (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        };
        TextInputOptions.SetContentType(this, TextInputContentType.Normal);
        TextInputOptions.SetMultiline(this, true);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int PageIndex
    {
        get => GetValue(PageIndexProperty);
        set => SetValue(PageIndexProperty, value);
    }

    /// <summary>The laid-out document; not an AvaloniaProperty because it is set wholesale on open.</summary>
    public DocumentRenderSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            if (_source is not null)
            {
                _source.LayoutInvalidated += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    InvalidateMeasure();
                    InvalidateVisual();
                });
            }

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Wired by the shell; input handlers below feed it (docs/M4-spec.md §7.5).</summary>
    public TextEditorController? Editor
    {
        get => _editor;
        set
        {
            if (_editor is not null)
            {
                _editor.Changed -= OnEditorChanged;
            }

            _editor = value;
            if (_editor is not null)
            {
                _editor.Changed += OnEditorChanged;
            }
        }
    }

    /// <summary>Inverse of the padding+zoom transform: control point → page point.</summary>
    public bool TryToPagePoint(Point controlPoint, out float xPt, out float yPt)
    {
        xPt = (float)((controlPoint.X - PagePaddingPx) / Zoom);
        yPt = (float)((controlPoint.Y - PagePaddingPx) / Zoom);
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return false;
        }

        Core.Model.SizePt size = _source.GetPageSize(PageIndex);
        return xPt >= 0 && yPt >= 0 && xPt <= size.Width && yPt <= size.Height;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return default;
        }

        Core.Model.SizePt size = _source.GetPageSize(PageIndex);
        return new Size(
            (size.Width * Zoom) + (2 * PagePaddingPx),
            (size.Height * Zoom) + (2 * PagePaddingPx));
    }

    public override void Render(DrawingContext context)
    {
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return;
        }

        // Snapshot editor overlay state on the UI thread; the draw op runs on the render thread.
        IReadOnlyList<SelectionRect> selection = [];
        CaretGeometry? caret = null;
        if (_editor is { IsActive: true })
        {
            selection = _editor.GetSelectionRects();
            if (_caretVisible && _editor.TryGetCaretGeometry(out CaretGeometry g))
            {
                caret = g;
            }
        }

        int currentPage = PageIndex;
        selection = selection
            .Where(r => r.BlockId is null || BlockIsOnPage(r.BlockId, currentPage))
            .ToList();
        if (caret is { BlockId: { } caretBlock } && !BlockIsOnPage(caretBlock, currentPage))
        {
            caret = null;
        }

        context.Custom(new PageDrawOperation(
            new Rect(Bounds.Size), _source, PageIndex, Zoom, PagePaddingPx, selection, caret));
    }

    private bool BlockIsOnPage(string blockId, int pageIndex) =>
        _source is not null
        && _source.TryGetPageIndexOfBlock(blockId, out int page)
        && page == pageIndex;

    // ---- Input (docs/M4-spec.md §7.5) -------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_editor is null || !TryToPagePoint(e.GetPosition(this), out float x, out float y))
        {
            _editor?.End();
            return;
        }

        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
        {
            return;
        }

        bool handled = e.ClickCount switch
        {
            >= 3 => Do(() => _editor.SelectParagraphAt(PageIndex, x, y)),
            2 => Do(() => _editor.SelectWordAt(PageIndex, x, y)),
            _ when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _editor.IsActive =>
                Do(() => _editor.ExtendTo(PageIndex, x, y)),
            _ => _editor.TryBeginAt(PageIndex, x, y),
        };
        if (handled)
        {
            ResetCaretBlink();
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        static bool Do(Action action)
        {
            action();
            return true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_editor is { IsActive: true }
            && ReferenceEquals(e.Pointer.Captured, this)
            && TryToPagePoint(e.GetPosition(this), out float x, out float y))
        {
            _editor.ExtendTo(PageIndex, x, y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_editor is { IsActive: true } && !string.IsNullOrEmpty(e.Text))
        {
            _editor.InsertText(e.Text);
            ResetCaretBlink();
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_editor is not { IsActive: true })
        {
            return;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool handled = e.Key switch
        {
            Key.Left => _editor.Move(ctrl ? CaretMotion.WordLeft : CaretMotion.Left, shift),
            Key.Right => _editor.Move(ctrl ? CaretMotion.WordRight : CaretMotion.Right, shift),
            Key.Up => _editor.Move(CaretMotion.Up, shift),
            Key.Down => _editor.Move(CaretMotion.Down, shift),
            Key.Home => _editor.Move(ctrl ? CaretMotion.StoryStart : CaretMotion.LineStart, shift),
            Key.End => _editor.Move(ctrl ? CaretMotion.StoryEnd : CaretMotion.LineEnd, shift),
            Key.PageUp when !ctrl => _editor.Move(CaretMotion.PageUp, shift),
            Key.PageDown when !ctrl => _editor.Move(CaretMotion.PageDown, shift),
            Key.Back => DoEdit(_editor.Backspace),
            Key.Delete => DoEdit(_editor.DeleteForward),
            Key.Enter => DoEdit(_editor.InsertParagraphBreak),
            Key.Escape => DoEdit(_editor.End),
            Key.A when ctrl => DoEdit(_editor.SelectAll),
            Key.Tab => true, // swallowed inside a session; block cycling is M5
            _ => false,
        };
        if (handled)
        {
            ResetCaretBlink();
            e.Handled = true;
        }

        static bool DoEdit(Action action)
        {
            action();
            return true;
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        // v1: focus loss ends the session — simplest and clearest (docs/M4-spec.md §7.5).
        _editor?.End();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _caretBlink.Stop();
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_editor is { IsActive: true })
        {
            if (!_caretBlink.IsEnabled)
            {
                _caretBlink.Start();
            }
        }
        else
        {
            _caretBlink.Stop();
        }

        ResetCaretBlink();
    }

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        if (_editor is { IsActive: true })
        {
            _caretBlink.Stop();
            _caretBlink.Start();
        }

        InvalidateVisual();
    }

    private sealed class PageDrawOperation(
        Rect bounds,
        DocumentRenderSource source,
        int pageIndex,
        double zoom,
        double padding,
        IReadOnlyList<SelectionRect> selection,
        CaretGeometry? caret) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = (ISkiaSharpApiLeaseFeature?)context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature));
            if (leaseFeature is null)
            {
                return; // Non-Skia backend: see the WriteableBitmap fallback note on the control.
            }

            using ISkiaSharpApiLease lease = leaseFeature.Lease();
            SKCanvas canvas = lease.SkCanvas;
            int save = canvas.Save();
            try
            {
                Core.Model.SizePt size = source.GetPageSize(pageIndex);
                float pageW = (float)(size.Width * zoom);
                float pageH = (float)(size.Height * zoom);

                // Neutral backdrop; the white sheet with a soft edge sits centered inside it.
                canvas.DrawColor(new SKColor(0xFF6B6B6B));
                var page = SKRect.Create((float)padding, (float)padding, pageW, pageH);
                using (var shadow = new SKPaint { Color = new SKColor(0x55000000), IsAntialias = true })
                {
                    canvas.DrawRect(new SKRect(page.Left + 3, page.Top + 3, page.Right + 3, page.Bottom + 3), shadow);
                }

                canvas.ClipRect(page);
                canvas.Translate(page.Left, page.Top);
                canvas.Scale((float)zoom);
                source.RenderPage(canvas, pageIndex);

                // Editor overlay: selection beneath the caret, both zoomed with the page.
                if (selection.Count > 0)
                {
                    TextOverlayRenderer.DrawSelection(canvas, selection);
                }

                if (caret is { } c)
                {
                    TextOverlayRenderer.DrawCaret(canvas, c);
                }
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }
}
