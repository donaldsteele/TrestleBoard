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
    private Avalonia.Automation.Peers.AutomationPeer? _peer;
    private TextEditorController? _editor;
    private FrameEditorController? _frames;
    private bool _caretVisible = true;
    private bool _draggingFrame;

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
    /// <summary>
    /// Without this the canvas is one opaque control and a screen reader sees nothing inside it
    /// (PLAN.md §6, docs/M9-spec.md §5).
    /// </summary>
    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        _peer = new PageCanvasAutomationPeer(this);

    /// <summary>The peer a screen reader would get; the headless a11y tests assert on it.</summary>
    internal Avalonia.Automation.Peers.AutomationPeer CreateAutomationPeerForTest() =>
        _peer ??= OnCreateAutomationPeer();

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

                    // Avalonia caches an automation peer's children until it is told otherwise, so
                    // without this a screen reader keeps reading the page as it was when it first
                    // looked — every edit invisible to it (docs/M9-spec.md §5).
                    (_peer as PageCanvasAutomationPeer)?.InvalidateBlocks();
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

    /// <summary>Frame selection and drag/resize (docs/M5-spec.md §1); wired by the shell.</summary>
    public FrameEditorController? FrameEditor
    {
        get => _frames;
        set
        {
            if (_frames is not null)
            {
                _frames.Changed -= OnFrameEditorChanged;
            }

            _frames = value;
            if (_frames is not null)
            {
                _frames.Changed += OnFrameEditorChanged;
            }

            InvalidateVisual();
        }
    }

    /// <summary>Overlay chrome is sized in screen points, so it divides out the zoom.</summary>
    private float OverlayScale => (float)(1d / Math.Max(Zoom, 0.01));

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

        FrameOverlay frameOverlay = _frames?.BuildOverlay(currentPage) ?? FrameOverlay.Empty;

        context.Custom(new PageDrawOperation(
            new Rect(Bounds.Size), _source, PageIndex, Zoom, PagePaddingPx, selection, caret, frameOverlay));
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
            _frames?.ClearSelection();
            return;
        }

        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed)
        {
            // A right-click during a drag cancels it (docs/M5-spec.md §4.3).
            if (_draggingFrame)
            {
                EndFrameDrag(commit: false, e.Pointer);
                e.Handled = true;
            }

            return;
        }

        if (TryHandleFramePress(x, y, e))
        {
            e.Handled = true;
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
            _frames?.ClearSelection();
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

    /// <summary>
    /// Frame-mode arbitration (docs/M5-spec.md §1.1): handles win, then link mode, then the
    /// topmost block — a text frame keeps the M4 behaviour (click in the body types) unless the
    /// press lands on its edge band or it is already the frame selection.
    /// </summary>
    private bool TryHandleFramePress(float x, float y, PointerPressedEventArgs e)
    {
        if (_frames is null || _source is null)
        {
            return false;
        }

        float scale = OverlayScale;
        if (_frames.SelectedPageIndex == PageIndex
            && _frames.HitHandle(x, y, scale) is not FrameHandle.None
            && _frames.TryBeginDrag(x, y, scale))
        {
            BeginFrameDrag(e.Pointer);
            return true;
        }

        string? hit = _source.HitTestBlock(PageIndex, x, y);
        if (_frames.IsLinkModeActive)
        {
            if (hit is not null)
            {
                _frames.CompleteLink(hit);
            }
            else
            {
                _frames.CancelLink();
            }

            return true;
        }

        if (hit is null)
        {
            _frames.ClearSelection();
            _editor?.End();
            return false;
        }

        bool isText = _source.IsTextBlock(hit);
        bool alreadySelected = string.Equals(_frames.SelectedBlockId, hit, StringComparison.Ordinal);
        bool onEdge = FrameGeometry.IsOnEdgeBand(_source.GetEffectiveRect(hit), x, y, scale);
        if (isText && !alreadySelected && !onEdge)
        {
            return false; // fall through to the text path
        }

        _editor?.End();
        _frames.Select(hit);
        if (_frames.TryBeginDrag(x, y, scale))
        {
            BeginFrameDrag(e.Pointer);
        }

        return true;
    }

    private void BeginFrameDrag(IPointer pointer)
    {
        _draggingFrame = true;
        pointer.Capture(this);
        InvalidateVisual();
    }

    private void EndFrameDrag(bool commit, IPointer pointer)
    {
        _draggingFrame = false;
        _frames?.EndDrag(commit);
        if (ReferenceEquals(pointer.Captured, this))
        {
            pointer.Capture(null);
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!TryToPagePoint(e.GetPosition(this), out float x, out float y))
        {
            return;
        }

        if (_draggingFrame && _frames is not null)
        {
            // Alt suppresses snapping for a free drag (docs/M5-spec.md §3.2).
            _frames.DragTo(x, y, snap: !e.KeyModifiers.HasFlag(KeyModifiers.Alt), OverlayScale);
            return;
        }

        UpdateCursor(x, y);
        if (_editor is { IsActive: true } && ReferenceEquals(e.Pointer.Captured, this))
        {
            _editor.ExtendTo(PageIndex, x, y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_draggingFrame)
        {
            EndFrameDrag(commit: true, e.Pointer);
            e.Handled = true;
            return;
        }

        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }
    }

    /// <summary>Pointer feedback: resize arrows over a handle, the move cursor over a grabbable
    /// frame edge, the I-beam over text.</summary>
    private void UpdateCursor(float x, float y)
    {
        if (_frames is null || _source is null)
        {
            return;
        }

        StandardCursorType type = StandardCursorType.Ibeam;
        if (_frames.SelectedPageIndex == PageIndex)
        {
            type = _frames.HitHandle(x, y, OverlayScale) switch
            {
                FrameHandle.TopLeft or FrameHandle.BottomRight => StandardCursorType.TopLeftCorner,
                FrameHandle.TopRight or FrameHandle.BottomLeft => StandardCursorType.TopRightCorner,
                FrameHandle.Left or FrameHandle.Right => StandardCursorType.SizeWestEast,
                FrameHandle.Top or FrameHandle.Bottom => StandardCursorType.SizeNorthSouth,
                _ => StandardCursorType.Ibeam,
            };
        }

        if (type is StandardCursorType.Ibeam && _source.HitTestBlock(PageIndex, x, y) is { } hit)
        {
            if (!_source.IsTextBlock(hit)
                || FrameGeometry.IsOnEdgeBand(_source.GetEffectiveRect(hit), x, y, OverlayScale))
            {
                type = StandardCursorType.SizeAll;
            }
        }

        Cursor = new Cursor(type);
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
            if (HandleFrameModeKey(e))
            {
                e.Handled = true;
            }

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
            Key.Escape => DoEdit(ExitTextEditingToFrameSelection),
            Key.A when ctrl => DoEdit(_editor.SelectAll),
            Key.Tab => true, // swallowed inside a session; Tab cycles frames in frame mode
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

    /// <summary>Escape is the discoverable route from typing to frame manipulation: it ends the
    /// session and leaves the frame you were editing selected (docs/M5-spec.md §1.2).</summary>
    private void ExitTextEditingToFrameSelection()
    {
        string? blockId = _editor?.BlockId;
        _editor?.End();
        if (blockId is not null)
        {
            _frames?.Select(blockId);
        }
    }

    /// <summary>The keyboard half of direct manipulation (docs/M5-spec.md §9). Menu-level
    /// shortcuts (add frame, wrap, z-order, link) live on the window; these are the ones that
    /// only make sense with canvas focus.</summary>
    private bool HandleFrameModeKey(KeyEventArgs e)
    {
        if (_frames is null)
        {
            return false;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Link mode owns Tab and Enter while it is armed: Tab moves the link cursor (NOT the
        // selection, which must stay on the source frame) and Enter confirms (docs/M5-spec.md §9).
        if (_frames.IsLinkModeActive)
        {
            switch (e.Key)
            {
                case Key.Tab:
                    return _frames.CycleLinkTarget(PageIndex, forward: !shift);
                case Key.Enter:
                    return _frames.CompleteLinkAtTarget();
                case Key.Escape:
                    _frames.CancelLink();
                    return true;
                default:
                    break;
            }
        }

        switch (e.Key)
        {
            case Key.Tab:
                return _frames.CycleSelection(PageIndex, forward: !shift);
            case Key.Escape when _frames.IsDragging:
                _frames.EndDrag(commit: false);
                _draggingFrame = false;
                return true;
            case Key.Escape:
                _frames.ClearSelection();
                return true;
            case Key.Delete or Key.Back:
                return _frames.DeleteSelected();
            case Key.Enter or Key.F2:
                return EnterTextEditingOnSelection();
            case Key.Left or Key.Right or Key.Up or Key.Down when _frames.HasSelection:
                float dx = e.Key == Key.Left ? -1f : e.Key == Key.Right ? 1f : 0f;
                float dy = e.Key == Key.Up ? -1f : e.Key == Key.Down ? 1f : 0f;
                return ctrl
                    ? _frames.NudgeResize(dx, dy, shift)
                    : _frames.Nudge(dx, dy, shift);
            default:
                return false;
        }
    }

    private bool EnterTextEditingOnSelection()
    {
        if (_editor is null
            || _frames is not { SelectedBlockId: { } blockId }
            || _frames.SelectedRect is not { } rect
            || _source is null
            || !_source.IsTextBlock(blockId))
        {
            return false;
        }

        int page = _frames.SelectedPageIndex;
        if (page < 0 || !_editor.TryBeginAt(page, rect.X + 2f, rect.Y + 2f))
        {
            return false;
        }

        _frames.ClearSelection();
        ResetCaretBlink();
        return true;
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

    private void OnFrameEditorChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(InvalidateVisual);

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
        CaretGeometry? caret,
        FrameOverlay frameOverlay) : ICustomDrawOperation
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

                // Frame chrome on top of everything, sized in screen points.
                FrameOverlayRenderer.Draw(canvas, frameOverlay, (float)(1d / Math.Max(zoom, 0.01)));
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }
}
