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
using TrestleBoard.App.Theme;
using TrestleBoard.Editing;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Canvas;

/// <summary>
/// A Ctrl+wheel turn: where the pointer was, in control pixels, and which way it went (M21).
/// </summary>
internal readonly record struct CanvasZoomRequest(Point Position, int Direction);

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
    internal const double PagePaddingPx = 24d;

    /// <summary>
    /// A marquee smaller than this (in page points) is a click that missed, not a drag: the user
    /// let go where they pressed and meant "nothing", and selecting whatever the two-point box
    /// happened to graze would be an answer to a question nobody asked.
    /// </summary>
    private const float MarqueeMinimumPt = 4f;

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PageCanvasControl, double>(nameof(Zoom), defaultValue: 1d);

    public static readonly StyledProperty<int> PageIndexProperty =
        AvaloniaProperty.Register<PageCanvasControl, int>(nameof(PageIndex), defaultValue: 0);

    /// <summary>
    /// One <see cref="Cursor"/> per shape, made once. Until M17 <c>UpdateCursor</c> allocated a
    /// fresh native cursor handle on every pointer move — a hundred a second while the mouse
    /// crossed the page, each one needing the finaliser to give it back.
    /// </summary>
    private static readonly Dictionary<StandardCursorType, Cursor> CursorCache = [];

    private readonly DispatcherTimer _caretBlink;
    private DocumentRenderSource? _source;
    private Avalonia.Automation.Peers.AutomationPeer? _peer;
    private TextEditorController? _editor;
    private FrameEditorController? _frames;
    private bool _caretVisible = true;
    private bool _draggingFrame;
    private StandardCursorType _cursorType = StandardCursorType.Arrow;
    private string? _hoverBlockId;

    // ---- M21: panning and the marquee ----------------------------------------------------------
    private bool _spaceHeld;
    private bool _panning;
    private Point _panFrom;
    private bool _marquee;
    private Point _marqueeFrom;
    private Point _marqueeTo;

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

        // The desk around the sheet is a themed colour from M16, so a theme change has to repaint
        // it. The page inside it does not change: it is a piece of paper (docs/M9-spec.md §4).
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
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

    /// <summary>
    /// M14's View → "Show where fonts were changed". Off by default: a mark the user did not ask
    /// for is noise, and this one is only useful while hunting down a stray font.
    /// </summary>
    public bool ShowFontChanges
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
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

    /// <summary>
    /// Raised when a screen-reader user asks for the context menu through the automation peer — the
    /// Applications key. Until M11 that request was refused outright and they got nothing at all.
    /// </summary>
    internal event EventHandler? PeerAskedForContextMenu;

    internal void RaisePeerContextMenuRequest() => PeerAskedForContextMenu?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raised when the user double-clicks one of the lists TrestleBoard fills in (M17). The canvas
    /// says what happened; the shell decides what to do about it, because "open its editor" means
    /// running <c>item.edit</c> through the action runner, availability rules and all.
    /// </summary>
    internal event EventHandler<string>? WidgetActivated;

    /// <summary>
    /// Raised when the user double-clicks a picture frame (M18). M17 deliberately left a photo's
    /// double-click alone — "there is no one obvious thing it should do" — and M18 is where there
    /// is one: an empty frame asks for a picture, a full one offers to swap it.
    /// </summary>
    internal event EventHandler<string>? PictureActivated;

    /// <summary>
    /// Ctrl+wheel (M21). The canvas reports WHERE and WHICH WAY; the shell owns the zoom ladder and
    /// the scroll offset, because keeping the point under the pointer still is a question about the
    /// scroller, which the canvas cannot see.
    /// </summary>
    internal event EventHandler<CanvasZoomRequest>? ZoomAtPointerRequested;

    /// <summary>
    /// Middle-drag or Space+drag (M21), as a delta in control pixels. Same division of labour: the
    /// canvas knows the gesture, the shell owns the scroller.
    /// </summary>
    internal event EventHandler<Vector>? PanRequested;

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

        // M14's "show where fonts were changed": an EDITOR overlay, gathered on the UI thread like
        // the caret and the selection. It never goes through PageRenderer, or it would print.
        IReadOnlyList<SourceSpan> fontOverrides = [];
        IReadOnlyList<FrameLayout> pageFrames = [];
        if (ShowFontChanges && _editor is not null)
        {
            fontOverrides = _editor.FontOverrideSpans();
            if (fontOverrides.Count > 0)
            {
                pageFrames = FramesOnPage(currentPage);
            }
        }

        context.Custom(new PageDrawOperation(
            new Rect(Bounds.Size), _source, PageIndex, Zoom, PagePaddingPx, selection, caret,
            frameOverlay, pageFrames, fontOverrides, BackdropColour()));

        DrawAdornments(context);
    }

    // ---- Editor adornments (PLAN.md §11 M17) --------------------------------------------------

    /// <summary>What an empty text frame says so that a first-time user knows it is for typing in.</summary>
    internal const string EmptyFrameHint = "Click here and start typing";

    /// <summary>
    /// A palette brush, resolved against the CURRENT variant every time it is asked for. These two
    /// are measured against <c>Page.Sheet</c> rather than the chrome background, because the sheet
    /// is what is behind them — see the palette's header.
    ///
    /// <para>Null when the key does not resolve, and the adornment is then simply not drawn. There
    /// is no literal fallback on purpose: a hard-coded colour here is the exact defect M16 spent a
    /// milestone removing, and <c>ThemeCompositionTests</c> already fails the build if a token goes
    /// missing from any variant.</para>
    /// </summary>
    private IBrush? BrushFor(string token) =>
        this.TryFindResource(token, ActualThemeVariant, out object? value) ? value as IBrush : null;

    /// <summary>
    /// Hints and hover outlines, drawn with AVALONIA's own primitives on top of the page.
    ///
    /// <para>This is the whole reason M17 could add them without moving a snapshot baseline. Every
    /// other overlay in this control — selection, caret, frame chrome — is Skia drawn through
    /// <see cref="PageDrawOperation"/>, which shares its renderers with <c>TrestleBoard.Rendering</c>.
    /// These are not: they are chrome about the editor, they use the interface's font rather than
    /// the newsletter's, and there is no path from here to <c>SKDocument</c> at all. A hint that
    /// reached the renderer would print.</para>
    /// </summary>
    private void DrawAdornments(DrawingContext context)
    {
        if (_source is null)
        {
            return;
        }

        if (_hoverBlockId is { } hovered
            && !string.Equals(hovered, _frames?.SelectedBlockId, StringComparison.Ordinal)
            && BlockIsOnPage(hovered, PageIndex)
            && BrushFor(Tokens.AdornmentHover) is { } hoverBrush)
        {
            context.DrawRectangle(
                null, new Pen(hoverBrush, 1d), ToControlRect(_source.GetEffectiveRect(hovered)));
        }

        DrawMultiSelection(context);
        DrawMarquee(context);

        if (BrushFor(Tokens.AdornmentHint) is not { } hintBrush)
        {
            return;
        }

        string? typingIn = _editor is { IsActive: true } ? _editor.BlockId : null;
        foreach ((string blockId, Core.Model.RectPt rect) in _source.GetEmptyTextFrameRects(PageIndex))
        {
            if (string.Equals(blockId, typingIn, StringComparison.Ordinal))
            {
                continue;
            }

            DrawHint(context, EmptyFrameHint, rect, hintBrush);
        }

        // M18: the photo template's frames say what to do with them. Until M18 they said nothing,
        // because nothing could be done with them — there was no replace command at all.
        foreach ((string _, Core.Model.RectPt rect) in _source.GetPlaceholderPictureRects(PageIndex))
        {
            DrawHint(context, EmptyPictureHint, rect, hintBrush);
        }
    }

    /// <summary>What an empty picture frame says, in the same voice as <see cref="EmptyFrameHint"/>.</summary>
    internal const string EmptyPictureHint = "Double-click to choose a picture";

    /// <summary>
    /// The frames chosen alongside the primary one (M21), outlined in the accent colour with the
    /// interface's own primitives.
    ///
    /// <para>Avalonia rather than Skia on purpose, and it is the whole reason this milestone can add
    /// a visible thing to the page and still move no snapshot baseline: the M5 frame chrome goes
    /// through <c>FrameOverlayRenderer</c>, which lives in <c>TrestleBoard.Rendering</c> and is
    /// shared with the PDF, and M17–M21 may not touch that project (PLAN.md §11 M18). The primary
    /// selection keeps its Skia handles; the others get an outline drawn here.</para>
    /// </summary>
    private void DrawMultiSelection(DrawingContext context)
    {
        if (_source is null
            || _frames is not { HasMultiSelection: true }
            || BrushFor(Tokens.Accent) is not { } accent)
        {
            return;
        }

        var pen = new Pen(accent, 2d, dashStyle: DashStyle.Dash);
        foreach (string blockId in _frames.SelectedBlockIds.Skip(1))
        {
            if (BlockIsOnPage(blockId, PageIndex))
            {
                context.DrawRectangle(null, pen, ToControlRect(_source.GetEffectiveRect(blockId)));
            }
        }
    }

    /// <summary>The rubber band, while it is being dragged.</summary>
    private void DrawMarquee(DrawingContext context)
    {
        if (!_marquee || BrushFor(Tokens.Accent) is not { } accent)
        {
            return;
        }

        var box = new Rect(_marqueeFrom, _marqueeTo);
        context.DrawRectangle(null, new Pen(accent, 1d, dashStyle: DashStyle.Dash), box);
    }

    private void DrawHint(DrawingContext context, string hint, Core.Model.RectPt rect, IBrush brush)
    {
        var text = new FormattedText(
            hint,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,

            // M27: 16, like every other piece of chrome text. This is the hint that teaches a
            // first-time user what an empty frame is for ("Click here and start typing"), drawn at
            // the one size in the app that broke §6's floor — smallest where it mattered most.
            // It is painted with Skia rather than laid out as a TextBlock, so PlainLanguageTests
            // cannot see it; this comment is the only guard there is.
            16d,
            brush);

        Rect box = ToControlRect(rect);
        if (box.Width < text.Width + 16 || box.Height < text.Height + 12)
        {
            // Too small to say it in: a clipped half-sentence would be worse than nothing.
            return;
        }

        context.DrawText(text, new Point(box.X + 8, box.Y + 6));
    }

    private Rect ToControlRect(Core.Model.RectPt rect) => new(
        PagePaddingPx + (rect.X * Zoom),
        PagePaddingPx + (rect.Y * Zoom),
        rect.Width * Zoom,
        rect.Height * Zoom);

    /// <summary>
    /// The colour of the desk the sheet lies on (PLAN.md §11 M16). It is CHROME, not the document —
    /// the page itself never themes — so it comes from the palette like every other chrome colour,
    /// and it must stay the same value <c>MainWindow.axaml</c> gives <c>CanvasScroller</c>, because
    /// the two meet wherever the page does not fill the scroller.
    /// </summary>
    private SKColor BackdropColour()
    {
        if (this.TryFindResource("TrestleBoard.Page.Backdrop", ActualThemeVariant, out object? value)
            && value is ISolidColorBrush brush)
        {
            Color c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        // Only reachable if the palette failed to merge, which ThemeCompositionTests would have
        // caught. Mid-grey keeps the sheet visible rather than drawing it on black.
        return new SKColor(0xFF, 0x6B, 0x6B, 0x6B);
    }


    /// <summary>Every laid-out text frame on one page, for the font-change overlay.</summary>
    private List<FrameLayout> FramesOnPage(int pageIndex)
    {
        if (_source is null)
        {
            return [];
        }

        var frames = new List<FrameLayout>();
        foreach ((string blockId, string _) in _source.DescribeBlocks(pageIndex))
        {
            if (_source.TryGetFrameLayout(blockId, out FrameLayout? layout))
            {
                frames.Add(layout);
            }
        }

        return frames;
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

        // M21: panning comes first, before anything asks what is under the pointer. The middle
        // button and Space+drag are the two gestures every publishing program uses for it, and
        // neither means anything else here.
        PointerPointProperties pressed = e.GetCurrentPoint(this).Properties;
        if (pressed.IsMiddleButtonPressed || (_spaceHeld && pressed.IsLeftButtonPressed))
        {
            _panning = true;
            _panFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
            Cursor = CursorFor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }

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

        if (e.ClickCount == 2 && (TryActivateWidgetAt(x, y) || TryActivatePictureAt(x, y)))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleFramePress(x, y, e))
        {
            e.Handled = true;
            return;
        }

        // M21: a press on bare page starts a marquee. TryHandleFramePress has already cleared the
        // selection by the time this is reached, so a marquee that selects nothing behaves exactly
        // as a click on nothing always did.
        if (_frames is { IsLinkModeActive: false }
            && _source?.HitTestBlock(PageIndex, x, y) is null
            && e.ClickCount == 1)
        {
            _marquee = true;
            _marqueeFrom = e.GetPosition(this);
            _marqueeTo = _marqueeFrom;
            e.Pointer.Capture(this);
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
    /// The double-click half of M17: a second click on one of the lists TrestleBoard fills in
    /// selects it and asks the shell to open its editor. Returns false for everything else, so a
    /// double-click on ordinary text still selects a word.
    ///
    /// <para>Widgets only, deliberately. Their text is drawn from a payload rather than a story, so
    /// a click inside one lands on no paragraph at all (<c>ParagraphIndex == -1</c>, the M7 design)
    /// and nothing happens — which reads as broken. A photo's double-click is left alone; there is
    /// no one obvious thing it should do, and guessing wrong is worse than doing nothing.</para>
    /// </summary>
    internal bool TryActivateWidgetAt(float xPt, float yPt)
    {
        if (_frames is not { IsLinkModeActive: false }
            || _source?.HitTestBlock(PageIndex, xPt, yPt) is not { } hit
            || !_source.IsWidgetBlock(hit))
        {
            return false;
        }

        _editor?.End();
        _frames.Select(hit);
        WidgetActivated?.Invoke(this, hit);
        return true;
    }

    /// <summary>
    /// The M18 half of the same gesture: a second click on a picture frame chooses it and asks the
    /// shell to fill it in. The gesture the app already taught for the lists now means the same kind
    /// of thing for pictures — "this is the thing you edit by double-clicking".
    /// </summary>
    internal bool TryActivatePictureAt(float xPt, float yPt)
    {
        if (_frames is not { IsLinkModeActive: false }
            || _source?.HitTestBlock(PageIndex, xPt, yPt) is not { } hit
            || !_source.IsImageBlock(hit))
        {
            return false;
        }

        _editor?.End();
        _frames.Select(hit);
        PictureActivated?.Invoke(this, hit);
        return true;
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

        // M21: Shift+click adds to (or takes out of) the selection. It is checked before the
        // text-frame fall-through, because Shift+clicking a text frame means "line this one up
        // too", not "put the caret in it".
        if (hit is not null
            && !_frames.IsLinkModeActive
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && _frames.HasSelection)
        {
            _editor?.End();
            _frames.AddToSelection(hit);
            return true;
        }

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
        Point position = e.GetPosition(this);
        bool onPage = TryToPagePoint(position, out float x, out float y);

        if (_panning)
        {
            // The anchor deliberately does NOT move: scrolling drags the canvas out from under the
            // pointer, so the next position measured against this control comes back to the anchor
            // by itself. Re-anchoring each move would double every delta.
            PanRequested?.Invoke(this, position - _panFrom);
            return;
        }

        if (_marquee)
        {
            _marqueeTo = position;
            InvalidateVisual();
            return;
        }

        if (_draggingFrame && _frames is not null)
        {
            // M17: a drag whose pointer leaves the sheet CLAMPS at the page edge. It used to
            // return here, so the frame froze mid-drag and then jumped when the pointer came back
            // — indistinguishable, while it was happening, from the app having hung.
            (float dragX, float dragY) = ToClampedPagePoint(position);

            // Alt suppresses snapping for a free drag (docs/M5-spec.md §3.2).
            _frames.DragTo(dragX, dragY, snap: !e.KeyModifiers.HasFlag(KeyModifiers.Alt), OverlayScale);
            return;
        }

        if (!onPage)
        {
            SetHover(null);
            return;
        }

        UpdateCursor(x, y);
        SetHover(_source?.HitTestBlock(PageIndex, x, y));
        if (_editor is { IsActive: true } && ReferenceEquals(e.Pointer.Captured, this))
        {
            _editor.ExtendTo(PageIndex, x, y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning)
        {
            _panning = false;
            Cursor = CursorFor(_spaceHeld ? StandardCursorType.Hand : _cursorType);
            if (ReferenceEquals(e.Pointer.Captured, this))
            {
                e.Pointer.Capture(null);
            }

            e.Handled = true;
            return;
        }

        if (_marquee)
        {
            EndMarquee();
            if (ReferenceEquals(e.Pointer.Captured, this))
            {
                e.Pointer.Capture(null);
            }

            e.Handled = true;
            return;
        }

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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        ZoomAtPointerRequested?.Invoke(
            this, new CanvasZoomRequest(e.GetPosition(this), e.Delta.Y > 0 ? +1 : -1));

        // Handled, or the scroller underneath would scroll the page away at the same time.
        e.Handled = true;
    }

    /// <summary>
    /// Turns the rubber-band box into a selection: everything on this page that the box touches,
    /// in stacking order. A box too small to have been meant selects nothing at all.
    /// </summary>
    private void EndMarquee()
    {
        _marquee = false;
        if (_frames is null || _source is null)
        {
            InvalidateVisual();
            return;
        }

        (float fromX, float fromY) = ToClampedPagePoint(_marqueeFrom);
        (float toX, float toY) = ToClampedPagePoint(_marqueeTo);
        float left = Math.Min(fromX, toX);
        float top = Math.Min(fromY, toY);
        float right = Math.Max(fromX, toX);
        float bottom = Math.Max(fromY, toY);
        InvalidateVisual();

        if (right - left < MarqueeMinimumPt && bottom - top < MarqueeMinimumPt)
        {
            return;
        }

        var caught = new List<string>();
        foreach ((string blockId, string _) in _source.DescribeBlocks(PageIndex))
        {
            Core.Model.RectPt rect = _source.GetEffectiveRect(blockId);
            bool overlaps = rect.X < right
                && rect.X + rect.Width > left
                && rect.Y < bottom
                && rect.Y + rect.Height > top;
            if (overlaps)
            {
                caught.Add(blockId);
            }
        }

        _frames.SelectAll(caught);
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

        if (type == _cursorType)
        {
            return;
        }

        _cursorType = type;
        Cursor = CursorFor(type);
    }

    /// <summary>Which shape the pointer currently wears, for the headless pointer tests.</summary>
    internal StandardCursorType CursorTypeForTest => _cursorType;

    /// <summary>Drags a marquee between two control points and lets go — the headless equivalent.</summary>
    internal void MarqueeForTest(Point from, Point to)
    {
        _marquee = true;
        _marqueeFrom = from;
        _marqueeTo = to;
        EndMarquee();
    }

    /// <summary>Whether Space has armed panning, for the headless pan test.</summary>
    internal bool PanArmedForTest => _spaceHeld;

    /// <summary>The block the pointer is over, or null. Drives the faint hover outline.</summary>
    internal string? HoverBlockIdForTest => _hoverBlockId;

    private void SetHover(string? blockId)
    {
        if (string.Equals(_hoverBlockId, blockId, StringComparison.Ordinal))
        {
            return;
        }

        _hoverBlockId = blockId;
        InvalidateVisual();
    }

    /// <summary>
    /// A control point turned into a page point and held inside the sheet, so a drag follows the
    /// pointer to the edge and stops there rather than freezing wherever it was when the pointer
    /// left the page (M17).
    /// </summary>
    internal (float X, float Y) ToClampedPagePoint(Point controlPoint)
    {
        TryToPagePoint(controlPoint, out float x, out float y);
        if (_source is null || PageIndex >= _source.PageCount)
        {
            return (x, y);
        }

        Core.Model.SizePt size = _source.GetPageSize(PageIndex);
        return (Math.Clamp(x, 0f, size.Width), Math.Clamp(y, 0f, size.Height));
    }

    /// <summary>The cached cursor for a shape — one native handle per shape for the app's life.</summary>
    internal static Cursor CursorFor(StandardCursorType type)
    {
        if (!CursorCache.TryGetValue(type, out Cursor? cursor))
        {
            cursor = new Cursor(type);
            CursorCache[type] = cursor;
        }

        return cursor;
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

        // M21: Space arms panning while the user is NOT typing — where it would otherwise do
        // nothing at all. Inside a text session a space is a space, and always will be.
        if (e.Key == Key.Space && _editor is not { IsActive: true } && e.KeyModifiers == KeyModifiers.None)
        {
            if (!_spaceHeld)
            {
                _spaceHeld = true;
                Cursor = CursorFor(StandardCursorType.Hand);
            }

            e.Handled = true;
            return;
        }

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
            // M28: Delete and Backspace are NOT handled here any more. Both are rows in
            // KeyboardMap, so both go through the action runner like every other command — which
            // means both can be refused with a reason, both appear in the keyboard audit, and the
            // panel can advertise them. Deleting from here bypassed all three, and Backspace was
            // additionally invisible to the map, so nothing in the app knew it was a shortcut.
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

    /// <summary>
    /// Puts the caret in the selected text frame — what Enter and F2 have always done, and from
    /// M17 what "Add a text frame" does too. Creating a frame means you wanted to type in it;
    /// leaving the user looking at a selected empty rectangle was the bug.
    /// </summary>
    internal bool BeginTextEditingOnSelection() => EnterTextEditingOnSelection();

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

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            if (!_panning)
            {
                Cursor = CursorFor(_cursorType);
            }
        }
    }

    /// <summary>
    /// The text session SURVIVES losing focus (PLAN.md §11 M21). Until this it ended — "v1: focus
    /// loss ends the session", docs/M4-spec.md §7.5 — which meant every click on the chrome threw
    /// the caret and the highlight away: choose a menu, press a panel button, tick a box in a
    /// dialog, and whatever you had selected in the story was gone with nothing said about it.
    /// M17 named this as an investigation and passed it here.
    ///
    /// <para>The caret stops blinking and hides instead, because a caret blinking in a window that
    /// does not have focus is a lie about where typing will go. The highlight stays: it is what
    /// the find window, the font window and the paragraph-style flyout are all ABOUT.</para>
    /// </summary>
    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _spaceHeld = false;
        _caretBlink.Stop();
        _caretVisible = false;
        InvalidateVisual();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        ResetCaretBlink();
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
        FrameOverlay frameOverlay,
        IReadOnlyList<FrameLayout> pageFrames,
        IReadOnlyList<SourceSpan> fontOverrides,
        SKColor backdrop) : ICustomDrawOperation
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

                // Themed backdrop; the white sheet with a soft edge sits centered inside it.
                canvas.DrawColor(backdrop);
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

                if (fontOverrides.Count > 0)
                {
                    TextOverlayRenderer.DrawFontOverrides(canvas, pageFrames, fontOverrides);
                }

                // Frame chrome on top of everything, sized in screen points.
                FrameOverlayRenderer.Draw(canvas, frameOverlay, (float)(1d / Math.Max(zoom, 0.01)));
            }
            catch (Exception e) when (e is ObjectDisposedException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or KeyNotFoundException)
            {
                // This runs on the compositor thread, against a render source the UI thread owns.
                // A draw operation is built with a page index and handed over; by the time it runs,
                // the user may have opened another newsletter (disposing that source) or removed
                // the page it was told to draw. Neither is a bug to fix here — the frame is simply
                // out of date, and the repaint that follows the change draws the right thing.
                //
                // What matters is that it must not throw: an exception on the render thread takes
                // the process down, and unlike a dropped frame the user notices that (review
                // §14.2). A skipped frame is invisible; the next one is already on its way.
            }
            finally
            {
                canvas.RestoreToCount(save);
            }
        }
    }
}
