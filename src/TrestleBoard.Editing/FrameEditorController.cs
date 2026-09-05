using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing;

/// <summary>
/// Direct manipulation of frames: selection, drag/resize with live reflow, snapping, nudging,
/// z-order, wrap, add/delete, and frame linking (docs/M5-spec.md). Headless by construction — it
/// owns no Avalonia types, so the interaction loop is testable without a window, exactly like
/// <see cref="TextEditorController"/>.
///
/// The document is mutated only at the END of a drag: while the pointer moves, the new rect lives
/// in <see cref="DocumentRenderSource.SetGeometryPreview"/>, so a 60-step drag costs one undo step
/// (docs/M5-spec.md §4).
/// </summary>
public sealed class FrameEditorController
{
    /// <summary>Arrow-key step; Shift multiplies it (PLAN.md §6).</summary>
    public const float NudgeStepPt = 1f;

    public const float LargeNudgeStepPt = 10f;

    public const float DefaultWrapMarginPt = 6f;

    private static readonly SizePt NewFrameSize = new(200f, 120f);

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _layout;
    private readonly List<SnapGuide> _snapGuides = [];

    /// <summary>
    /// The blocks chosen ALONGSIDE the primary one (M21), in the order the user added them. The
    /// primary stays <see cref="_selectedBlockId"/> and keeps every M5 behaviour it ever had —
    /// handles, drag, nudge, link — so multi-select adds a capability instead of rewriting one.
    /// </summary>
    private readonly List<string> _alsoSelected = [];

    /// <summary>
    /// M92: where each companion frame started, and where it is being shown right now, while a
    /// multi-selection is dragged as one. Empty for a single drag and for every resize — handles
    /// belong to the primary frame's edges, and there is no sense in which five frames share one.
    /// </summary>
    private readonly Dictionary<string, RectPt> _dragStartRects = [];
    private readonly Dictionary<string, RectPt> _dragPreviews = [];

    private string? _selectedBlockId;
    private FrameHandle _dragHandle = FrameHandle.None;
    private RectPt _dragStartRect;
    private float _dragStartXPt;
    private float _dragStartYPt;
    private RectPt _previewRect;

    public FrameEditorController(DocumentSession session, DocumentRenderSource layout)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _session.Changed += (_, _) => OnDocumentChanged();
    }

    /// <summary>Raised whenever selection, drag state, or status text changes.</summary>
    public event EventHandler? Changed;

    public string? SelectedBlockId => _selectedBlockId;

    public bool HasSelection => _selectedBlockId is not null;

    public bool IsDragging => _dragHandle is not FrameHandle.None;

    /// <summary>True while a link is armed and waiting for its target (docs/M5-spec.md §8.2).</summary>
    public bool IsLinkModeActive { get; private set; }

    /// <summary>
    /// The candidate the keyboard has landed on while link mode is armed. Link mode needs its own
    /// cursor: the SELECTION must stay on the source frame, so Tab may not move it here.
    /// </summary>
    public string? LinkTargetBlockId { get; private set; }

    /// <summary>Plain-language feedback for the shell's status line (PLAN.md §6).</summary>
    public string? StatusMessage { get; private set; }


    public IReadOnlyList<SnapGuide> SnapGuides => _snapGuides;

    /// <summary>Selection rect: the live preview while dragging, else the block's own rect.</summary>
    public RectPt? SelectedRect => _selectedBlockId is { } id
        ? (IsDragging ? _previewRect : _layout.GetEffectiveRect(id))
        : null;

    public int SelectedPageIndex =>
        _selectedBlockId is { } id && _layout.TryGetPageIndexOfBlock(id, out int page) ? page : -1;

    public bool IsSelectionOverset =>
        _selectedBlockId is { } id && _layout.GetOversetTailBlockIds().Contains(id, StringComparer.Ordinal);

    // ---- Selection (docs/M5-spec.md §1) ------------------------------------------------------

    public void Select(string? blockId)
    {
        // The early return skipped the link-mode reset below, so clicking the ALREADY-selected
        // source frame while link mode was armed left it armed, with its "now click the frame to
        // continue into" prompt still in the status bar and no way to tell it was still live
        // (review §14.2). Re-selecting what is already selected is a way of saying "never mind",
        // and it now means that.
        if (_selectedBlockId == blockId && _alsoSelected.Count == 0 && !IsLinkModeActive)
        {
            return;
        }

        CancelDragIfAny();
        _alsoSelected.Clear();
        _selectedBlockId = blockId;
        IsLinkModeActive = false;
        LinkTargetBlockId = null;
        StatusMessage = blockId is not null && IsOverset(blockId)
            ? OversetMessage
            : null;
        Raise();
    }

    public void ClearSelection() => Select(null);

    /// <summary>M44: said while a frame is being dragged or resized, and only then.</summary>
    public const string DragHintMessage =
        "Hold down Alt while you drag to ignore the lining-up guides.";

    // ---- Choosing more than one thing (PLAN.md §11 M21) ---------------------------------------

    /// <summary>Everything chosen right now, the primary first. Empty when nothing is chosen.</summary>
    public IReadOnlyList<string> SelectedBlockIds =>
        _selectedBlockId is { } id ? [id, .. _alsoSelected] : [];

    public int SelectionCount => _selectedBlockId is null ? 0 : _alsoSelected.Count + 1;

    public bool HasMultiSelection => SelectionCount > 1;

    /// <summary>
    /// Shift+click: adds a block to the selection, or takes it out again if it was already in.
    ///
    /// <para>Everything chosen has to be on ONE page, and the refusal says so rather than doing
    /// nothing: lining up two frames the user cannot see at the same time would move something
    /// off-screen with no way to tell what happened.</para>
    /// </summary>
    public bool AddToSelection(string blockId)
    {
        ArgumentNullException.ThrowIfNull(blockId);

        if (_selectedBlockId is null)
        {
            Select(blockId);
            return true;
        }

        CancelDragIfAny();

        if (string.Equals(blockId, _selectedBlockId, StringComparison.Ordinal))
        {
            // Un-choosing the primary promotes the next one rather than emptying the selection.
            if (_alsoSelected.Count == 0)
            {
                ClearSelection();
                return true;
            }

            _selectedBlockId = _alsoSelected[0];
            _alsoSelected.RemoveAt(0);
            Raise();
            return true;
        }

        if (_alsoSelected.Remove(blockId))
        {
            Raise();
            return true;
        }

        if (!_layout.TryGetPageIndexOfBlock(blockId, out int page) || page != SelectedPageIndex)
        {
            StatusMessage = "Everything you line up has to be on the same page. "
                + "Choose things on one page at a time.";
            Raise();
            return false;
        }

        _alsoSelected.Add(blockId);
        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>
    /// Chooses a whole set at once — what a marquee drag hands over. The first id becomes the
    /// primary; ids on another page are dropped rather than refused, because a marquee is drawn on
    /// one page by definition.
    /// </summary>
    public bool SelectAll(IEnumerable<string> blockIds)
    {
        ArgumentNullException.ThrowIfNull(blockIds);

        List<string> ids = [.. blockIds];
        if (ids.Count == 0)
        {
            ClearSelection();
            return false;
        }

        CancelDragIfAny();
        _selectedBlockId = ids[0];
        _alsoSelected.Clear();
        int page = SelectedPageIndex;
        foreach (string id in ids.Skip(1))
        {
            if (_layout.TryGetPageIndexOfBlock(id, out int itsPage) && itsPage == page)
            {
                _alsoSelected.Add(id);
            }
        }

        IsLinkModeActive = false;
        LinkTargetBlockId = null;
        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>
    /// Lines the chosen frames up, as ONE undo step whatever their number (PLAN.md §12 gate 18).
    /// False when fewer than two things are chosen — the catalog has already said so in words by
    /// the time this is reached, so this is a guard rather than the explanation.
    /// </summary>
    public bool Align(FrameAlignmentKind kind)
    {
        IReadOnlyList<string> ids = SelectedBlockIds;
        if (ids.Count < 2 || IsDragging)
        {
            return false;
        }

        return ApplyMoves(
            ids,
            FrameAlignment.Align([.. ids.Select(_layout.GetEffectiveRect)], kind),
            FrameAlignment.Describe(kind));
    }

    /// <summary>Equal gaps between the chosen frames, as one undo step. Needs three or more.</summary>
    public bool Distribute(bool horizontal)
    {
        IReadOnlyList<string> ids = SelectedBlockIds;
        if (ids.Count < 3 || IsDragging)
        {
            return false;
        }

        return ApplyMoves(
            ids,
            FrameAlignment.Distribute([.. ids.Select(_layout.GetEffectiveRect)], horizontal),
            horizontal ? "Space them out side to side" : "Space them out top to bottom");
    }

    /// <summary>
    /// One <see cref="CompositeCommand"/> of <see cref="MoveBlockCommand"/>s, with the frames that
    /// were already in the right place left out. Returns false when there is nothing to move, so
    /// lining up an already-lined-up selection does not put an empty step on the undo stack.
    /// </summary>
    private bool ApplyMoves(IReadOnlyList<string> ids, IReadOnlyList<RectPt> rects, string description)
    {
        var moves = new List<IDocumentCommand>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (rects[i] != _layout.GetEffectiveRect(ids[i]))
            {
                moves.Add(new MoveBlockCommand(ids[i], rects[i]));
            }
        }

        if (moves.Count == 0)
        {
            StatusMessage = "They are already lined up.";
            Raise();
            return false;
        }

        _session.Execute(new CompositeCommand(description, new ChangeScope(ChangeKind.BlockGeometry), moves));
        StatusMessage = $"{description} — {moves.Count} of {ids.Count} moved.";
        Raise();
        return true;
    }

    /// <summary>Selects the topmost block under the point; returns false when the point is empty.</summary>
    public bool SelectAt(int pageIndex, float xPt, float yPt)
    {
        string? hit = _layout.HitTestBlock(pageIndex, xPt, yPt);
        Select(hit);
        return hit is not null;
    }

    /// <summary>Tab / Shift+Tab cycle the page's blocks in stacking order (docs/M5-spec.md §9).</summary>
    public bool CycleSelection(int pageIndex, bool forward)
    {
        List<Block> blocks = PageBlocksInZOrder(pageIndex);
        if (blocks.Count == 0)
        {
            return false;
        }

        int current = _selectedBlockId is null ? -1 : blocks.FindIndex(b => b.Id == _selectedBlockId);
        int next = current < 0
            ? (forward ? 0 : blocks.Count - 1)
            : ((current + (forward ? 1 : -1) + blocks.Count) % blocks.Count);
        Select(blocks[next].Id);
        return true;
    }

    /// <summary>
    /// M50, review §14.3: the keyboard's answer to Shift+click.
    ///
    /// <para>Shift+click adds one thing at a time to what is already chosen, and had no keyboard
    /// equivalent at all. The review said the missing piece was "a cursor that moves independently
    /// of the selection" — a second highlight the user drives with the arrow keys and commits with
    /// Space, the way a Windows list box works. That is a real mechanism, and it is the wrong one
    /// here: it adds a mode this audience would have to be taught, to reach an outcome they can
    /// state in one sentence.</para>
    ///
    /// <para>What they actually want is "and that one too". So this KEEPS everything already
    /// chosen and adds the next block in stacking order — the same order <see cref="CycleSelection"/>
    /// walks, so Tab and this agree about what "next" means. Press it three times and four adjacent
    /// things are chosen, ready for the align commands. No cursor, no mode.</para>
    /// </summary>
    public bool AddNeighbourToSelection(int pageIndex, bool forward)
    {
        List<Block> blocks = PageBlocksInZOrder(pageIndex);
        if (blocks.Count == 0)
        {
            return false;
        }

        if (_selectedBlockId is null)
        {
            // Nothing chosen yet, so "also choose" is just "choose" — the same generosity
            // AddToSelection already shows a first Shift+click.
            Select(blocks[forward ? 0 : ^1].Id);
            return true;
        }

        // Walk outward from the LAST thing added rather than from the primary, so repeated presses
        // travel instead of flipping between two neighbours of the first frame.
        string from = _alsoSelected.Count > 0 ? _alsoSelected[^1] : _selectedBlockId;
        int at = blocks.FindIndex(b => b.Id == from);
        if (at < 0)
        {
            return false;
        }

        // Skip what is already chosen, so a press always adds something or truthfully reports that
        // there is nothing left to add.
        for (int step = 1; step <= blocks.Count; step++)
        {
            int index = ((at + (forward ? step : -step)) % blocks.Count + blocks.Count) % blocks.Count;
            string candidate = blocks[index].Id;
            if (IsSelected(candidate))
            {
                continue;
            }

            return AddToSelection(candidate);
        }

        StatusMessage = "Everything on this page is already chosen.";
        Raise();
        return false;
    }

    /// <summary>True when this block is the primary choice or one of the others.</summary>
    public bool IsSelected(string blockId) =>
        string.Equals(blockId, _selectedBlockId, StringComparison.Ordinal)
        || _alsoSelected.Contains(blockId);

    // ---- Drag / resize (docs/M5-spec.md §4) --------------------------------------------------

    /// <summary>Which grip (if any) the point grabs on the current selection.</summary>
    public FrameHandle HitHandle(float xPt, float yPt, float overlayScale) =>
        SelectedRect is { } rect
            ? FrameGeometry.HitHandle(rect, xPt, yPt, overlayScale)
            : FrameHandle.None;

    /// <summary>
    /// Starts a move or resize on the selection. A handle grab resizes; a press inside the frame
    /// (or on its edge band) moves.
    /// </summary>
    public bool TryBeginDrag(float xPt, float yPt, float overlayScale)
    {
        if (_selectedBlockId is null || SelectedRect is not { } rect)
        {
            return false;
        }

        FrameHandle handle = FrameGeometry.HitHandle(rect, xPt, yPt, overlayScale);
        if (handle is FrameHandle.None)
        {
            if (!FrameGeometry.Contains(rect, xPt, yPt) && !FrameGeometry.IsOnEdgeBand(rect, xPt, yPt, overlayScale))
            {
                return false;
            }

            handle = FrameHandle.Body;
        }

        return BeginDrag(handle, xPt, yPt);
    }

    /// <summary>Starts a drag with an explicit handle (the keyboard/test path).</summary>
    public bool BeginDrag(FrameHandle handle, float xPt, float yPt)
    {
        if (_selectedBlockId is null || SelectedRect is not { } rect || handle is FrameHandle.None)
        {
            return false;
        }

        // M81: a block being kept in place refuses the gesture and SAYS SO. A drag that silently
        // does nothing is the M11 failure — the user tries harder, then decides the app is broken.
        if (SelectionIsLocked)
        {
            StatusMessage = LockedMessage;
            Raise();
            return false;
        }

        _dragHandle = handle;
        _dragStartRect = rect;
        _previewRect = rect;
        _dragStartXPt = xPt;
        _dragStartYPt = yPt;
        _snapGuides.Clear();
        _dragStartRects.Clear();
        _dragPreviews.Clear();

        // M92: everything else chosen travels with it, but only on a MOVE. A resize handle is an
        // edge of the primary frame, and "drag five frames by one frame's corner" means nothing.
        // A companion that is kept in place stays put rather than blocking the whole gesture — the
        // point of pinning one thing down is that the others still move.
        if (handle is FrameHandle.Body)
        {
            foreach (string id in _alsoSelected)
            {
                if (_session.Document.TryFindBlock(id, out _, out Block? companion) && !companion.Locked)
                {
                    _dragStartRects[id] = _layout.GetEffectiveRect(id);
                }
            }
        }

        // M44, review §14.3: snapping has been suppressible with Alt since M5 and was advertised
        // nowhere at all. It is said at the one moment it can be acted on — while a drag is
        // actually happening — because a fact about a modifier key is useless before the gesture
        // it modifies has begun.
        StatusMessage = DragHintMessage;
        Raise();
        return true;
    }

    /// <summary>
    /// A drag delta applied the way this kind of block should take it (M69).
    ///
    /// <para>A picture dragged by a <b>corner</b> scales and keeps its shape; everything else
    /// reshapes as it always has. The reason is that reshaping a picture frame does not reshape the
    /// picture — <c>ImageFit.Cover</c> crops the source to the frame's new aspect — so a corner
    /// drag used to cut the bottom off an emblem with no way back short of undo.</para>
    ///
    /// <para>M72 keeps the emblem in this rule, deliberately, now that it is a
    /// <see cref="VectorBlock"/> rather than a picture. The mechanism no longer applies — a drawing
    /// is scaled to fit and cannot be cropped by its frame — but the gesture is the point: the
    /// square and compasses is a symbol, and stretching it out of shape by dragging a corner is not
    /// something a corner drag should be able to do by accident.</para>
    /// </summary>
    private RectPt ResizeForBlock(string blockId, RectPt start, FrameHandle handle, float dxPt, float dyPt)
    {
        if (FrameGeometry.IsCorner(handle)
            && _session.Document.TryFindBlock(blockId, out _, out Block? block)
            && block is ImageFrame or VectorBlock
            && start.Height > 0f)
        {
            return FrameGeometry.ResizeKeepingAspect(start, handle, dxPt, dyPt, start.Width / start.Height);
        }

        return FrameGeometry.Resize(start, handle, dxPt, dyPt);
    }

    /// <summary>
    /// Live drag step: recomputes the rect, snaps it (unless suppressed), and installs it as the
    /// render source's preview so text reflows around it without touching the document.
    /// </summary>
    public void DragTo(float xPt, float yPt, bool snap = true, float overlayScale = 1f)
    {
        if (!IsDragging || _selectedBlockId is null)
        {
            return;
        }

        RectPt candidate = ResizeForBlock(
            _selectedBlockId, _dragStartRect, _dragHandle, xPt - _dragStartXPt, yPt - _dragStartYPt);

        _snapGuides.Clear();
        if (snap && TryBuildSnapContext(out SnapContext context))
        {
            SnapResult result = SnapEngine.Snap(
                candidate, _dragHandle, context, SnapEngine.DefaultThresholdPt * overlayScale);
            candidate = result.Rect;
            _snapGuides.AddRange(result.Guides);
        }

        _previewRect = candidate;
        _layout.SetGeometryPreview(_selectedBlockId, candidate);

        // M92: the companions follow by the SAME delta, taken after snapping, so the whole
        // selection keeps its shape and the one frame that snapped pulls the rest into line with it.
        float dx = candidate.X - _dragStartRect.X;
        float dy = candidate.Y - _dragStartRect.Y;
        foreach ((string id, RectPt start) in _dragStartRects)
        {
            var moved = new RectPt(start.X + dx, start.Y + dy, start.Width, start.Height);
            _dragPreviews[id] = moved;
            _layout.SetGeometryPreview(id, moved);
        }

        Raise();
    }

    /// <summary>Ends the drag: one <see cref="MoveBlockCommand"/>/<see cref="ResizeBlockCommand"/>
    /// when committing, nothing at all when cancelling (docs/M5-spec.md §4.3).</summary>
    public void EndDrag(bool commit)
    {
        if (!IsDragging || _selectedBlockId is null)
        {
            return;
        }

        string blockId = _selectedBlockId;
        FrameHandle handle = _dragHandle;
        RectPt finalRect = _previewRect;
        RectPt startRect = _dragStartRect;

        _dragHandle = FrameHandle.None;
        _snapGuides.Clear();
        _layout.SetGeometryPreview(blockId, null);

        var companions = new List<(string Id, RectPt Rect)>();
        foreach ((string id, RectPt rect) in _dragPreviews)
        {
            _layout.SetGeometryPreview(id, null);
            companions.Add((id, rect));
        }

        _dragStartRects.Clear();
        _dragPreviews.Clear();

        if (commit && finalRect != startRect)
        {
            IDocumentCommand primary = handle is FrameHandle.Body
                ? new MoveBlockCommand(blockId, finalRect)
                : new ResizeBlockCommand(blockId, finalRect);

            // M92: one undo step for the whole gesture. Dragging four frames and having to press
            // Ctrl+Z four times to put them back is not undoing what the user did.
            _session.Execute(companions.Count == 0
                ? primary
                : new CompositeCommand(
                    "Move what was chosen",
                    new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId),
                    [primary, .. companions.Select(c => new MoveBlockCommand(c.Id, c.Rect))]));
        }

        Raise();
    }

    private void CancelDragIfAny()
    {
        if (IsDragging)
        {
            EndDrag(commit: false);
        }
    }

    /// <summary>
    /// Puts the chosen frame at an exact place and size (PLAN.md §11 M94).
    ///
    /// <para><b>The accessible route to geometry.</b> Until this, position and size could be set
    /// only by dragging with the mouse or by pressing an arrow key a point at a time — and a long,
    /// precise drag is the fine-motor task §6 exists to avoid. Somebody with a tremor could not put
    /// a box exactly where the last issue had it; now they can type it.</para>
    ///
    /// <para>One command per changed aspect, joined so that one Ctrl+Z takes the whole thing back,
    /// and nothing at all runs when the numbers match what is already there.</para>
    /// </summary>
    /// <returns>False when nothing is chosen, the frame is kept in place, or nothing would change.</returns>
    public bool SetSelectionGeometry(RectPt wanted)
    {
        if (_selectedBlockId is not { } blockId || SelectedRect is not { } current)
        {
            return false;
        }

        if (SelectionIsLocked)
        {
            StatusMessage = LockedMessage;
            Raise();
            return false;
        }

        // Clamped onto the paper for the same reason a paste is: a frame the user cannot see is a
        // change that appears not to have happened, and a typed number is easier to get wrong than
        // a drag, which cannot leave the sheet in the first place.
        Document document = _session.Document;
        (Page page, _) = document.FindBlock(blockId);
        RectPt rect = ClampOntoThePage(document, page, wanted);

        bool moved = Math.Abs(rect.X - current.X) > 0.001f || Math.Abs(rect.Y - current.Y) > 0.001f;
        bool resized = Math.Abs(rect.Width - current.Width) > 0.001f
            || Math.Abs(rect.Height - current.Height) > 0.001f;

        if (!moved && !resized)
        {
            return false;
        }

        CancelDragIfAny();

        // Resize THEN move, because a resize takes the top-left as its anchor: doing it the other
        // way puts the frame where it was asked for and then drags it back off that spot.
        var children = new List<IDocumentCommand>();
        if (resized)
        {
            children.Add(new ResizeBlockCommand(blockId, rect));
        }

        if (moved)
        {
            children.Add(new MoveBlockCommand(blockId, rect));
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                "Put it exactly here",
                new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId),
                children));

        Raise();
        return true;
    }

    // ---- Keyboard equivalents (docs/M5-spec.md §9) -------------------------------------------

    /// <summary>Arrow-key move. Never snaps — a nudge must move exactly what was asked.</summary>
    public bool Nudge(float dxSteps, float dySteps, bool large = false)
    {
        if (_selectedBlockId is null || IsDragging || SelectedRect is not { } rect)
        {
            return false;
        }

        // The keyboard route is refused for the same reason as the mouse one, and by the same
        // sentence. Two ways in, one rule (M28's standard).
        if (SelectionIsLocked)
        {
            StatusMessage = LockedMessage;
            Raise();
            return false;
        }

        float step = large ? LargeNudgeStepPt : NudgeStepPt;
        float dx = dxSteps * step;
        float dy = dySteps * step;

        // M92: the whole selection moves, in one undo step. A companion kept in place stays put
        // rather than refusing the gesture for everything else — the same rule the mouse drag uses.
        var children = new List<IDocumentCommand>
        {
            new MoveBlockCommand(_selectedBlockId, FrameGeometry.Translate(rect, dx, dy)),
        };

        foreach (string companionId in _alsoSelected)
        {
            if (_session.Document.TryFindBlock(companionId, out _, out Block? companion)
                && !companion.Locked)
            {
                children.Add(new MoveBlockCommand(
                    companionId,
                    FrameGeometry.Translate(_layout.GetEffectiveRect(companionId), dx, dy)));
            }
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                "Move what was chosen",
                new ChangeScope(ChangeKind.BlockGeometry, BlockId: _selectedBlockId),
                children));
        return true;
    }

    /// <summary>Ctrl+arrow resize — moves the right/bottom edges, the keyboard twin of the
    /// bottom-right handle.</summary>
    public bool NudgeResize(float dxSteps, float dySteps, bool large = false)
    {
        if (_selectedBlockId is null || IsDragging || SelectedRect is not { } rect)
        {
            return false;
        }

        if (SelectionIsLocked)
        {
            StatusMessage = LockedMessage;
            Raise();
            return false;
        }

        float step = large ? LargeNudgeStepPt : NudgeStepPt;
        _session.Execute(new ResizeBlockCommand(
            _selectedBlockId,
            FrameGeometry.Resize(rect, FrameHandle.BottomRight, dxSteps * step, dySteps * step)));
        return true;
    }

    // ---- Structure ---------------------------------------------------------------------------

    /// <summary>Adds an empty text frame inside the page margins and selects it
    /// (docs/M5-spec.md §7). Returns the new block id.</summary>
    public string AddTextFrame(int pageIndex) => AddFrame(pageIndex, paragraphs: null, "Add text frame");

    /// <summary>
    /// A new frame that already has writing in it (M66): the same frame the command above makes,
    /// with somebody else's article in place of the empty paragraph.
    ///
    /// <para><b>One command, so one Ctrl+Z.</b> The acceptance says the import is a single undo
    /// step, and it is single because the frame and its writing arrive as one composite — not
    /// because anything afterwards merges a run of little edits back together.</para>
    /// </summary>
    public string AddTextFrameWith(int pageIndex, IReadOnlyList<StoryParagraph> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        return AddFrame(pageIndex, paragraphs, "Bring in writing");
    }

    /// <summary>
    /// The same one-undo-step frame, from plain writing rather than paragraphs somebody else has
    /// already styled (M73(a), for the memorial notice).
    ///
    /// <para>The paragraphs take the document's own body style, so the words look like the rest of
    /// the newsletter the moment they land.</para>
    /// </summary>
    public string AddTextFrameWith(int pageIndex, string text, string undoLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(undoLabel);

        string styleRef = DefaultParagraphStyleRef(_session.Document);
        StoryParagraph[] paragraphs =
        [
            .. text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => new StoryParagraph
                {
                    ParagraphStyleRef = styleRef,
                    Runs = [new StoryRun { Text = line }],
                }),
        ];

        return AddFrame(pageIndex, paragraphs, undoLabel);
    }

    private string AddFrame(int pageIndex, IReadOnlyList<StoryParagraph>? paragraphs, string label)
    {
        Document document = _session.Document;
        Page page = document.Pages[pageIndex];
        PageMaster master = document.GetMaster(page.MasterRef);

        string storyId = NextId("story", id => document.Stories.Any(s => s.Id == id));
        string blockId = NextId("frame", id => document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));

        var story = new Story { Id = storyId };
        if (paragraphs is { Count: > 0 })
        {
            story.Paragraphs.AddRange(paragraphs);
        }
        else
        {
            story.Paragraphs.Add(new StoryParagraph
            {
                ParagraphStyleRef = DefaultParagraphStyleRef(document),
                Runs = [new StoryRun { Text = "" }],
            });
        }

        float cascade = Math.Min(page.Blocks.Count * 18f, 90f);
        float maxX = Math.Max(
            master.MarginLeftPt,
            master.Size.Width - master.MarginRightPt - NewFrameSize.Width);
        float maxY = Math.Max(
            master.MarginTopPt,
            master.Size.Height - master.MarginBottomPt - NewFrameSize.Height);
        var rect = new RectPt(
            Math.Min(master.MarginLeftPt + cascade, maxX),
            Math.Min(master.MarginTopPt + cascade, maxY),
            NewFrameSize.Width,
            NewFrameSize.Height);

        var block = new TextBlock
        {
            Id = blockId,
            StoryRef = storyId,
            FrameRect = rect,
            ZOrder = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1,
        };

        _session.Execute(new CompositeCommand(
            label,
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: blockId),
            [new AddStoryCommand(story), new AddBlockCommand(page.Id, block)]));
        Select(blockId);
        return blockId;
    }




    /// <summary>Whether the chosen box of writing is in two columns (M83).</summary>
    public bool SelectionIsTwoColumns =>
        _selectedBlockId is { } id
        && _session.Document.TryFindBlock(id, out _, out Block? block)
        && block is Core.Model.TextBlock { ColumnCount: > 1 };

    /// <summary>
    /// Splits the chosen box of writing into two columns, or puts it back to one (M83).
    ///
    /// <para><b>A toggle rather than a number.</b> Two columns look "proper" and the old tool's
    /// issues used them; every column is also another place for writing to hide, and three of them
    /// in a letter-width frame is an inch and a half each with no hyphenation to rescue it. Two or
    /// one is the whole of the offer, and there is no gutter to set.</para>
    /// </summary>
    /// <returns>False when nothing suitable is chosen.</returns>
    public bool ToggleTwoColumns()
    {
        if (_selectedBlockId is not { } blockId
            || !_session.Document.TryFindBlock(blockId, out _, out Block? block)
            || block is not Core.Model.TextBlock text)
        {
            return false;
        }

        _session.Execute(new SetColumnCountCommand(blockId, text.ColumnCount > 1 ? 1 : 2));
        return true;
    }

    /// <summary>
    /// Makes the chosen box taller until the writing fits, or until it reaches the bottom margin
    /// (PLAN.md §11 M82).
    ///
    /// <para><b>One of the two verbs the overset card offers.</b> The other is "send the rest to the
    /// next page", which M8 already built. Between them they cover what somebody does about writing
    /// that will not fit: give it more room here, or give it a room of its own.</para>
    ///
    /// <para><b>It stops at the margin M47 draws.</b> A box grown past the edge of the text area
    /// would put the newsletter outside the line the app has told the user to keep inside — and the
    /// caller is told it stopped short, so nobody is left thinking it worked when the writing still
    /// does not fit.</para>
    /// </summary>
    /// <returns>
    /// How it went, so the shell can say the truth rather than "Done" over a box that grew and
    /// still cannot hold the article.
    /// </returns>
    public GrowResult GrowToFit()
    {
        if (_selectedBlockId is not { } blockId
            || !_session.Document.TryFindBlock(blockId, out Page? page, out Block? block)
            || block is not Core.Model.TextBlock)
        {
            return GrowResult.NothingChosen;
        }

        if (block.Locked)
        {
            StatusMessage = LockedMessage;
            Raise();
            return GrowResult.NothingChosen;
        }

        if (!IsOverset(blockId))
        {
            return GrowResult.AlreadyFits;
        }

        PageMaster master = _session.Document.GetMaster(page.MasterRef);
        RectPt rect = block.FrameRect;
        float room = master.Size.Height - master.MarginBottomPt - rect.Y;
        if (room <= rect.Height + 1f)
        {
            return GrowResult.NoRoom;
        }

        // Grown in one step to the whole of the room available, then measured. A search downwards
        // from the largest size would relayout the story a dozen times for a result the user cannot
        // see the difference in; what matters is whether it fits at all, and if it does, a box that
        // reaches the margin is where somebody would have dragged it to anyway.
        _session.Execute(new ResizeBlockCommand(
            blockId, new RectPt(rect.X, rect.Y, rect.Width, room)));

        return IsOverset(blockId) ? GrowResult.GrewButStillDoesNotFit : GrowResult.Fits;
    }

    /// <summary>What happened when the box was made taller (M82).</summary>
    public enum GrowResult
    {
        /// <summary>Nothing was chosen, or what was chosen is not a box of writing.</summary>
        NothingChosen,

        /// <summary>It all fitted already, so nothing was done.</summary>
        AlreadyFits,

        /// <summary>The box already reaches the bottom margin. Nothing was done.</summary>
        NoRoom,

        /// <summary>It is taller and the writing fits.</summary>
        Fits,

        /// <summary>It is taller and there is still more writing than fits.</summary>
        GrewButStillDoesNotFit,
    }

    // ---- Make another like this, and keep this where it is (PLAN.md §11 M81) --------------------

    /// <summary>How far down and across a copy lands, so it visibly IS a copy.</summary>
    private const float CopyOffsetPt = 24f;

    /// <summary>
    /// What a locked block says when it is dragged. It names the way out, which is the M11 rule:
    /// nothing in this app becomes unavailable without saying why and what to do instead.
    /// </summary>
    internal const string LockedMessage =
        "This is being kept in place. Choose “Let it move” to move it.";

    /// <summary>Whether the chosen block is being kept in place.</summary>
    public bool SelectionIsLocked =>
        _selectedBlockId is { } id
        && _session.Document.TryFindBlock(id, out _, out Block? block)
        && block.Locked;

    /// <summary>
    /// Keeps the chosen block where it is, or lets it move again (M81).
    /// </summary>
    /// <returns>False when nothing is chosen.</returns>
    public bool ToggleLocked()
    {
        if (_selectedBlockId is not { } blockId
            || !_session.Document.TryFindBlock(blockId, out _, out Block? block))
        {
            return false;
        }

        // M92: the primary decides which way the whole selection goes, so a mixed set ends up all
        // the same rather than each item flipping to its own opposite — a toggle that left things
        // disagreeing would need pressing twice to mean anything.
        bool locked = !block.Locked;
        var children = new List<IDocumentCommand> { new SetBlockLockedCommand(blockId, locked) };

        foreach (string companionId in _alsoSelected)
        {
            if (_session.Document.TryFindBlock(companionId, out _, out Block? companion)
                && companion.Locked != locked)
            {
                children.Add(new SetBlockLockedCommand(companionId, locked));
            }
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                locked ? "Keep them where they are" : "Let them move again",
                new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
                children));
        return true;
    }

    /// <summary>
    /// A copy of the chosen block, a little down and across (PLAN.md §11 M81).
    ///
    /// <para><b>This is how a novice makes a second event card.</b> Copy has meant words since M4,
    /// so the only way to get a second announcement box was to run the wizard again and re-answer
    /// every question. The copy lands offset rather than on top, so it is visibly a copy rather
    /// than a page that appears not to have changed, and it is chosen afterwards so the next
    /// keystroke moves it.</para>
    ///
    /// <para><b>A linked frame copies as an UNLINKED frame holding the same writing.</b> Copying a
    /// link would mean two frames claiming to continue the same story, which is not a thing the
    /// flow model can mean — so the copy gets a story of its own with the same paragraphs in it,
    /// and the caller says so.</para>
    ///
    /// <para><b>A picture SHARES its asset.</b> The bytes are already in the package and a second
    /// copy of a three-megabyte photograph would double the file for no reason a reader could
    /// see.</para>
    /// </summary>
    /// <returns>The copy's id, or null when nothing was chosen.</returns>
    public string? DuplicateSelected()
    {
        if (_selectedBlockId is not { } blockId
            || !_session.Document.TryFindBlock(blockId, out Page? page, out Block? original))
        {
            return null;
        }

        Document document = _session.Document;
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var children = new List<IDocumentCommand>();
        var copies = new List<string>();

        // M92: everything chosen is copied, in one undo step. The primary is done first so that its
        // copy is the one returned and chosen, which is what every caller since M81 has relied on.
        foreach (string id in SelectedBlockIds)
        {
            if (!document.TryFindBlock(id, out Page? from, out Block? source) || from.Id != page.Id)
            {
                continue;
            }

            if (CopyOnto(document, source, page, OffsetOnThePage(document, page, source.FrameRect), taken, children)
                is { } id2)
            {
                copies.Add(id2);
            }
        }

        if (copies.Count == 0)
        {
            return null;
        }

        string copyId = copies[0];
        _session.Execute(new CompositeCommand(
            copies.Count == 1 ? "Make another like this" : "Make another of each",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: copyId),
            children));

        SelectAll(copies);
        return copyId;
    }

    /// <summary>
    /// Where a copy lands when it is put down on the page it came from: a little below and to the
    /// right, and never off the paper.
    ///
    /// <para>Kept on the paper because a copy the user cannot see is a copy that did not happen,
    /// and "nothing happened" is the one outcome this command must never produce. Offset because a
    /// copy laid exactly on top of its original is a page that appears not to have changed.</para>
    /// </summary>
    private static RectPt OffsetOnThePage(Document document, Page page, RectPt from)
    {
        PageMaster master = document.GetMaster(page.MasterRef);
        return new RectPt(
            Math.Min(from.X + CopyOffsetPt, Math.Max(0f, master.Size.Width - from.Width)),
            Math.Min(from.Y + CopyOffsetPt, Math.Max(0f, master.Size.Height - from.Height)),
            from.Width,
            from.Height);
    }

    /// <summary>
    /// Puts a copy of <paramref name="original"/> onto <paramref name="page"/> at
    /// <paramref name="rect"/>, appending the commands that do it to <paramref name="children"/>.
    /// Returns the copy's id, or null when the block is of a kind the copier refuses.
    ///
    /// <para><b><paramref name="taken"/> is why this exists rather than two call sites minting
    /// their own ids.</b> <see cref="NextId"/> answers "what is free" by looking at the document,
    /// and the document is not mutated until the composite runs — so pasting three frames at once
    /// would mint the same id three times and produce three blocks claiming to be one. Every id
    /// this method mints is added to the set, so the next call round the loop steps over it.</para>
    /// </summary>
    private static string? CopyOnto(
        Document document,
        Block original,
        Page page,
        RectPt rect,
        HashSet<string> taken,
        List<IDocumentCommand> children,
        IReadOnlyList<StoryParagraph>? paragraphs = null)
    {
        string copyId = NextId("copy", id => taken.Contains(id) || document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));
        string? storyId = null;

        if (original is TextBlock)
        {
            storyId = NextId("story", id => taken.Contains(id) || document.Stories.Any(s => s.Id == id));
        }

        if (BlockCopier.Copy(original, copyId, storyId) is not { } copy)
        {
            return null;
        }

        taken.Add(copyId);

        if (original is TextBlock text && storyId is not null)
        {
            taken.Add(storyId);
            var story = new Story { Id = storyId };
            if (paragraphs is not null)
            {
                story.Paragraphs.AddRange(paragraphs.Select(BlockCopier.CopyParagraph));
            }
            else if (document.TryGetStory(text.StoryRef, out Story? source))
            {
                story.Paragraphs.AddRange(source.Paragraphs.Select(BlockCopier.CopyParagraph));
            }

            children.Add(new AddStoryCommand(story));
        }

        copy.FrameRect = rect;
        copy.ZOrder = NextZOrder(page, children.Count);
        children.Add(new AddBlockCommand(page.Id, copy));
        return copyId;
    }

    /// <summary>
    /// The top of a page's stacking order, stepped by <paramref name="offset"/> so a batch of
    /// copies keeps the order it was copied in instead of all claiming the same rung.
    /// </summary>
    private static int NextZOrder(Page page, int offset) =>
        (page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1) + offset;

    /// <summary>Whether the chosen block was continuing its writing somewhere else — which a copy
    /// cannot do, so the shell says so.</summary>
    public bool SelectionWasLinked =>
        _selectedBlockId is { } id
        && _session.Document.TryFindBlock(id, out _, out Block? block)
        && block is TextBlock { LinkNext: not null };

    // ---- Copy, cut, paste and moving between pages (PLAN.md §11 M91) --------------------------

    /// <summary>
    /// Something taken off the page and kept, ready to be put down again.
    ///
    /// <para><b>The block is a COPY taken at the moment of copying</b>, never a reference to the
    /// one on the page. Two reasons, each a bug if it were the other way: changing the original
    /// afterwards must not change what comes out of the clipboard, and after a CUT the original is
    /// gone. The paragraphs come out of the story for the same reason.</para>
    /// </summary>
    private sealed record HeldFrame(Block Prototype, IReadOnlyList<StoryParagraph> Paragraphs);

    private List<HeldFrame>? _held;
    private string? _heldFromPageId;
    private bool _heldWasLinked;

    /// <summary>
    /// Whether anything is waiting to be put down.
    ///
    /// <para><b>What is held dies with this controller, and that is on purpose.</b> A held picture
    /// names bytes in ONE newsletter's package, so it must never outlive that newsletter — and the
    /// shell builds a new controller for every newsletter it opens, which makes the guarantee
    /// structural rather than something a close handler has to remember.</para>
    /// </summary>
    public bool HasHeldFrames => _held is { Count: > 0 };

    /// <summary>
    /// Whether what is being held was continuing its writing somewhere else — which a copy cannot
    /// do, so the shell says so, exactly as it does for <see cref="SelectionWasLinked"/>.
    /// </summary>
    public bool HeldFrameWasLinked => _heldWasLinked;

    /// <summary>
    /// Keeps a copy of everything chosen. Changes nothing on the page and runs no command.
    /// </summary>
    /// <returns>False when nothing is chosen, or when nothing chosen is of a kind that can be
    /// copied at all.</returns>
    public bool CopySelection()
    {
        if (_selectedBlockId is null)
        {
            return false;
        }

        Document document = _session.Document;
        var held = new List<HeldFrame>();
        bool anyLinked = false;
        string? fromPageId = null;

        foreach (string id in SelectedBlockIds)
        {
            if (!document.TryFindBlock(id, out Page? page, out Block? block))
            {
                continue;
            }

            // Asked before copying rather than after: a kind the copier refuses must not take up a
            // place in the clipboard that paste would then silently skip.
            if (BlockCopier.Copy(block, "probe", "probe-story") is null)
            {
                continue;
            }

            IReadOnlyList<StoryParagraph> paragraphs =
                block is TextBlock text && document.TryGetStory(text.StoryRef, out Story? story)
                    ? [.. story.Paragraphs.Select(BlockCopier.CopyParagraph)]
                    : [];

            anyLinked |= block is TextBlock { LinkNext: not null };
            fromPageId ??= page.Id;
            held.Add(new HeldFrame(CloneForClipboard(block), paragraphs));
        }

        if (held.Count == 0)
        {
            return false;
        }

        _held = held;
        _heldFromPageId = fromPageId;
        _heldWasLinked = anyLinked;
        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>
    /// Keeps a copy of everything chosen and then takes it off the page, in one undo step.
    ///
    /// <para><b>Being kept in place does not stop a cut.</b> <see cref="Block.Locked"/> is about
    /// position and size — a locked block has always still been deletable — and a cut that refused
    /// would be the only way of removing something that asked a different question from Delete.</para>
    /// </summary>
    public bool CutSelection() => CopySelection() && DeleteSelected();

    /// <summary>
    /// Puts everything held onto <paramref name="pageIndex"/>, in one undo step, and chooses what
    /// it put down so the next keystroke moves it.
    ///
    /// <para><b>Onto a different page it keeps its exact position</b>, which is the whole point:
    /// "the same box in the same place on page four" is the thing that could not be done before.
    /// Only a paste onto the page it was copied from is offset, so that a paste-in-place is
    /// visibly a copy rather than a page that appears not to have changed.</para>
    /// </summary>
    /// <returns>The ids put down; empty when nothing was held or the page does not exist.</returns>
    public IReadOnlyList<string> PasteOntoPage(int pageIndex)
    {
        Document document = _session.Document;
        if (_held is not { Count: > 0 } held
            || pageIndex < 0 || pageIndex >= document.Pages.Count)
        {
            return [];
        }

        CancelDragIfAny();
        Page page = document.Pages[pageIndex];
        bool ontoItsOwnPage = string.Equals(page.Id, _heldFromPageId, StringComparison.Ordinal);

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var children = new List<IDocumentCommand>();
        var pasted = new List<string>();

        foreach (HeldFrame frame in held)
        {
            RectPt rect = ontoItsOwnPage
                ? OffsetOnThePage(document, page, frame.Prototype.FrameRect)
                : ClampOntoThePage(document, page, frame.Prototype.FrameRect);

            if (CopyOnto(document, frame.Prototype, page, rect, taken, children, frame.Paragraphs)
                is { } id)
            {
                pasted.Add(id);
            }
        }

        if (pasted.Count == 0)
        {
            return [];
        }

        _session.Execute(new CompositeCommand(
            pasted.Count == 1 ? "Put the copy here" : "Put the copies here",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: pasted[0]),
            children));

        SelectAll(pasted);
        return pasted;
    }

    /// <summary>
    /// Takes everything chosen off this page and puts it on <paramref name="pageIndex"/>, keeping
    /// its position, in one undo step.
    ///
    /// <para><b>This is not a copy.</b> Each block keeps its id, its writing and its links, because
    /// the user moved a frame and not a story. A chain is a list of ids and has always crossed
    /// pages — moving a page does not touch <c>linkNext</c> either, and pouring an article onward
    /// creates linked frames on other pages as its ordinary business — so an article that ran on
    /// still runs on afterwards.</para>
    ///
    /// <para>The rect is clamped onto the target paper in case that page's master is a different
    /// size, and the blocks land on top of whatever is already there.</para>
    /// </summary>
    /// <returns>The ids moved; empty when nothing was chosen or the page does not exist.</returns>
    public IReadOnlyList<string> MoveSelectionToPage(int pageIndex)
    {
        Document document = _session.Document;
        if (_selectedBlockId is null
            || pageIndex < 0 || pageIndex >= document.Pages.Count)
        {
            return [];
        }

        CancelDragIfAny();
        Page target = document.Pages[pageIndex];
        IReadOnlyList<string> ids = SelectedBlockIds;

        var moving = new List<(Page From, Block Block)>();
        foreach (string id in ids)
        {
            if (document.TryFindBlock(id, out Page? from, out Block? block)
                && !string.Equals(from.Id, target.Id, StringComparison.Ordinal))
            {
                moving.Add((from, block));
            }
        }

        if (moving.Count == 0)
        {
            return [];
        }

        // Removals first, then the additions, then the stacking. A composite reverts its children
        // in REVERSE, so this order is what makes one Ctrl+Z put every block back on the page it
        // came from at the index it held — and it works only because the very same Block instance
        // is handed to Add, since AddBlockCommand.Revert removes by reference.
        var children = new List<IDocumentCommand>();
        foreach ((Page from, Block block) in moving)
        {
            children.Add(new RemoveBlockCommand(from.Id, block.Id));
        }

        int rung = 0;
        foreach ((Page _, Block block) in moving)
        {
            block.FrameRect = ClampOntoThePage(document, target, block.FrameRect);
            children.Add(new AddBlockCommand(target.Id, block));
            children.Add(new SetZOrderCommand(block.Id, NextZOrder(target, rung++)));
        }

        _session.Execute(new CompositeCommand(
            moving.Count == 1 ? "Move it to another page" : "Move them to another page",
            new ChangeScope(ChangeKind.PageStructure, PageId: target.Id, BlockId: moving[0].Block.Id),
            children));

        return [.. moving.Select(m => m.Block.Id)];
    }

    /// <summary>
    /// The same rectangle, put onto the paper of the page it is going to — which may not be the
    /// same size as the paper it came from.
    ///
    /// <para><b>Size is trimmed before position is clamped, and only when it has to be.</b> Moving
    /// something is not a request to resize it, so on same-sized paper — which is every page in
    /// every newsletter this app makes today, since a document has one master — nothing here
    /// changes anything at all. But a frame wider than its page cannot be clamped into view: there
    /// is no X that keeps a 400pt box inside a 300pt sheet. Left alone it would hang off the edge
    /// and print cut in half, and the alternative to trimming is a move the user cannot see the
    /// result of.</para>
    /// </summary>
    private static RectPt ClampOntoThePage(Document document, Page page, RectPt rect)
    {
        PageMaster master = document.GetMaster(page.MasterRef);
        float width = Math.Min(rect.Width, master.Size.Width);
        float height = Math.Min(rect.Height, master.Size.Height);

        return new RectPt(
            Math.Clamp(rect.X, 0f, Math.Max(0f, master.Size.Width - width)),
            Math.Clamp(rect.Y, 0f, Math.Max(0f, master.Size.Height - height)),
            width,
            height);
    }

    /// <summary>
    /// A detached copy of a block for the clipboard, keeping its id and geometry — the id so paste
    /// can tell a text block from the rest, the geometry so it can land in the same place.
    /// </summary>
    private static Block CloneForClipboard(Block block)
    {
        Block clone = BlockCopier.Copy(block, block.Id, block is TextBlock text ? text.StoryRef : null)!;
        clone.FrameRect = block.FrameRect;
        clone.ZOrder = block.ZOrder;
        return clone;
    }

    // ---- Borders, shading and a line across the page (PLAN.md §11 M79) -------------------------

    /// <summary>
    /// Whether the chosen frame has a border round it.
    ///
    /// <para>Read off the block rather than kept in a field: two things that can disagree
    /// eventually will, and here the disagreement is a button whose label is the opposite of what
    /// the page shows (the M55 rule).</para>
    /// </summary>
    public bool SelectionHasBorder =>
        _selectedBlockId is { } id
        && _session.Document.TryFindBlock(id, out _, out Block? block)
        && PageLooks.HasBorder(block.FrameStyleRef);

    /// <summary>Whether the chosen frame is shaded.</summary>
    public bool SelectionHasShade =>
        _selectedBlockId is { } id
        && _session.Document.TryFindBlock(id, out _, out Block? block)
        && PageLooks.HasShade(block.FrameStyleRef);

    /// <summary>
    /// Turns the border on the chosen frame on or off (M79).
    ///
    /// <para><b>A toggle rather than a menu of looks.</b> The plan offered "none, a thin line, a
    /// tint" as one choice; two independent toggles give the same four answers with two verbs the
    /// user already understands, no dialog to open, and no list to read. "Put a border round it"
    /// and "Shade it" are the two sentences a committee says out loud.</para>
    /// </summary>
    /// <returns>False when nothing is chosen, so the caller can stay quiet rather than claim a
    /// change nobody made (M73's standard).</returns>
    public bool ToggleBorder() => SetLook(border: !SelectionHasBorder, shade: SelectionHasShade);

    /// <summary>Turns the shading on the chosen frame on or off (M79).</summary>
    public bool ToggleShade() => SetLook(border: SelectionHasBorder, shade: !SelectionHasShade);

    private bool SetLook(bool border, bool shade)
    {
        if (_selectedBlockId is not { } blockId
            || !_session.Document.TryFindBlock(blockId, out _, out _))
        {
            return false;
        }

        // M92: same rule as the lock — the primary decides, and everything chosen follows it.
        var children = new List<IDocumentCommand> { new SetFrameLookCommand(blockId, border, shade) };

        foreach (string companionId in _alsoSelected)
        {
            if (_session.Document.TryFindBlock(companionId, out _, out _))
            {
                children.Add(new SetFrameLookCommand(companionId, border, shade));
            }
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                "Change how they look",
                new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
                children));
        return true;
    }

    /// <summary>
    /// Puts a line right across the page, under whatever is chosen (M79).
    ///
    /// <para><b>Not a shape tool.</b> A line the user draws is a line the user drags by accident,
    /// and dragging a hairline back to level is precisely the fine-motor task §6 exists to avoid.
    /// This one arrives the width of the text area, level, in the right place, and can then be
    /// moved and deleted like anything else on the page.</para>
    ///
    /// <para><b>Its height is fixed and its width is the margin's.</b> The block is a few points
    /// tall so there is something to take hold of, and the line is drawn across the middle of it —
    /// so a resize handle cannot turn a line into a rectangle, which is the one way this could stop
    /// being the thing the user asked for.</para>
    /// </summary>
    /// <returns>The new block's id.</returns>
    public string AddRuleAcrossThePage(int pageIndex)
    {
        Document document = _session.Document;
        Page page = document.Pages[pageIndex];
        PageMaster master = document.GetMaster(page.MasterRef);

        float left = master.MarginLeftPt;
        float width = Math.Max(0f, master.Size.Width - master.MarginLeftPt - master.MarginRightPt);

        // Under the chosen frame when there is one — which is what "a line across the page" means
        // when somebody has just finished a heading — and otherwise a little way down from the top
        // margin, where it can be seen and moved.
        float top = master.MarginTopPt + 24f;
        if (_selectedBlockId is { } chosen && document.TryFindBlock(chosen, out Page? owner, out Block? block)
            && owner.Id == page.Id)
        {
            top = block.FrameRect.Bottom + 6f;
        }

        float maxTop = Math.Max(
            master.MarginTopPt,
            master.Size.Height - master.MarginBottomPt - PageLooks.RuleBlockHeightPt);

        string blockId = NextId("rule", id => document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));
        var rule = new ShapeBlock
        {
            Id = blockId,
            Kind = ShapeKind.Rule,
            FrameRect = new RectPt(left, Math.Min(top, maxTop), width, PageLooks.RuleBlockHeightPt),
            StrokeArgb = PageLooks.RuleArgb,
            StrokeWidthPt = PageLooks.RuleWidthPt,
            ZOrder = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1,
        };

        _session.Execute(new AddBlockCommand(page.Id, rule));
        Select(blockId);
        return blockId;
    }

    /// <summary>Deletes everything chosen, detaching each frame from any chain first and dropping
    /// stories nothing else shows (docs/M5-spec.md §7; whole-selection from M91).</summary>
    public bool DeleteSelected()
    {
        if (_selectedBlockId is null)
        {
            return false;
        }

        CancelDragIfAny();
        Document document = _session.Document;
        IReadOnlyList<string> ids = SelectedBlockIds;
        (Page page, _) = document.FindBlock(ids[0]);

        var children = new List<IDocumentCommand>();
        foreach (string id in ids)
        {
            children.AddRange(DeleteChildrenFor(document, id, ids));
        }

        _session.Execute(new CompositeCommand(
            ids.Count == 1 ? "Delete frame" : "Delete what was chosen",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: ids[0]),
            children));
        Select(null);
        return true;
    }

    /// <summary>
    /// The primitives that take one block off the page: chain repair, the removal itself, and the
    /// story when nothing else is showing it.
    /// </summary>
    /// <param name="alsoGoing">
    /// Every id being removed in the same breath, this one included.
    ///
    /// <para><b>The chain must be healed against the document as it will be, not as it is.</b>
    /// Deleting A and B out of A→B→C one at a time would leave A's predecessor pointing at B — a
    /// frame that is about to stop existing — so the walk below steps over every id that is also
    /// going. Nulling the predecessor instead would break the invariant the whole linking model
    /// rests on, that a story has exactly one head: A→B→C with B deleted once left A and C both
    /// pointing at nothing and both still referencing the same story, and the article was drawn
    /// TWICE, from its first paragraph, in two places on the page.</para>
    /// </param>
    private static List<IDocumentCommand> DeleteChildrenFor(
        Document document, string blockId, IReadOnlyList<string> alsoGoing)
    {
        (Page page, Block block) = document.FindBlock(blockId);
        var children = new List<IDocumentCommand>();

        string? healTo = SurvivingContinuationOf(document, block, alsoGoing);

        if (FindPredecessor(document, blockId) is { } predecessor
            && !alsoGoing.Contains(predecessor.Id, StringComparer.Ordinal))
        {
            children.Add(new SetLinkNextCommand(predecessor.Id, healTo));
        }

        if (block is TextBlock { LinkNext: not null } textBlock)
        {
            children.Add(new SetLinkNextCommand(textBlock.Id, null));
        }

        children.Add(new RemoveBlockCommand(page.Id, blockId));

        if (block is TextBlock text && !AnyOtherBlockUsesStory(document, text.StoryRef, alsoGoing))
        {
            children.Add(new RemoveStoryCommand(text.StoryRef));
        }

        return children;
    }

    /// <summary>
    /// The first frame after <paramref name="block"/> that is staying, or null when the rest of the
    /// chain is going too. Walks rather than taking <c>LinkNext</c> at face value, because the
    /// continuation may itself be on the way out.
    /// </summary>
    private static string? SurvivingContinuationOf(
        Document document, Block block, IReadOnlyList<string> alsoGoing)
    {
        string? next = block is TextBlock text ? text.LinkNext : null;

        // Bounded by the number of blocks in the document: a chain cannot visit one twice without
        // the model already being broken, and this must not hang if it ever is.
        int guard = document.Pages.Sum(p => p.Blocks.Count) + 1;
        while (next is not null && guard-- > 0)
        {
            if (!alsoGoing.Contains(next, StringComparer.Ordinal))
            {
                return next;
            }

            next = document.TryFindBlock(next, out _, out Block? following) && following is TextBlock link
                ? link.LinkNext
                : null;
        }

        return null;
    }

    /// <summary>
    /// Turns text wrap on or off for the selected block (docs/M5-spec.md §6).
    ///
    /// <para>M73(e): the shell discarded the bool and said nothing either way, so the one command
    /// in the app whose whole effect is a reflow a few lines further down the page was invisible —
    /// and there was no way to tell which of the two things it had just done. The sentence is set
    /// here because only this method knows which way it went.</para>
    /// </summary>
    public bool ToggleWrap()
    {
        if (_selectedBlockId is not { } blockId)
        {
            StatusMessage = "Nothing is chosen, so there is nothing for the writing to flow around.";
            Raise();
            return false;
        }

        (_, Block block) = _session.Document.FindBlock(blockId);
        bool turningOn = block.WrapMode is WrapMode.None;

        // M92: the primary decides, and everything chosen follows — each block keeping its own
        // wrap margin, which is a property of that frame and not of the decision being made.
        var children = new List<IDocumentCommand> { WrapCommandFor(block, turningOn) };
        foreach (string companionId in _alsoSelected)
        {
            if (_session.Document.TryFindBlock(companionId, out _, out Block? companion))
            {
                children.Add(WrapCommandFor(companion, turningOn));
            }
        }

        _session.Execute(children.Count == 1
            ? children[0]
            : new CompositeCommand(
                turningOn ? "Flow the writing around them" : "Stop the writing flowing around them",
                new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId),
                children));

        StatusMessage = turningOn
            ? "The writing on the page now flows around this. Press Ctrl+Z to undo."
            : "The writing on the page no longer flows around this. Press Ctrl+Z to undo.";
        Raise();
        return true;
    }

    private static SetWrapModeCommand WrapCommandFor(Block block, bool turningOn) => new(
        block.Id,
        turningOn ? WrapMode.Rectangle : WrapMode.None,
        turningOn
            ? (block.WrapMarginPt > 0f ? block.WrapMarginPt : DefaultWrapMarginPt)
            : block.WrapMarginPt);

    // ---- Z-order (docs/M5-spec.md §5) --------------------------------------------------------

    public bool BringForward() => Restack(+1, "Bring forward");

    public bool SendBackward() => Restack(-1, "Send backward");

    public bool BringToFront() => Restack(int.MaxValue, "Bring to front");

    public bool SendToBack() => Restack(int.MinValue, "Send to back");

    private bool Restack(int delta, string description)
    {
        if (_selectedBlockId is not { } blockId || SelectedPageIndex < 0)
        {
            return false;
        }

        List<Block> ordered = PageBlocksInZOrder(SelectedPageIndex);

        // M92: the whole selection restacks as one GROUP, keeping its own front-to-back order and
        // moving relative to everything that is not chosen. Restacking each chosen block in turn
        // would shuffle them against each other, so two frames sent to the back would come out in
        // the opposite order from the one the user was looking at.
        var chosen = new HashSet<string>(SelectedBlockIds, StringComparer.Ordinal);
        List<Block> moving = [.. ordered.Where(b => chosen.Contains(b.Id))];
        if (moving.Count == 0)
        {
            return false;
        }

        List<Block> rest = [.. ordered.Where(b => !chosen.Contains(b.Id))];
        int anchor = ordered.IndexOf(moving[0]);
        int index = ordered.Take(anchor).Count(b => !chosen.Contains(b.Id));

        int target = delta switch
        {
            int.MaxValue => rest.Count,
            int.MinValue => 0,
            _ => index + delta,
        };
        target = Math.Clamp(target, 0, rest.Count);

        // M70(c): target == index is the commonest no-op in the app — the thing is already at that
        // end of the pile. The false is no longer discarded: the shell says so (MainWindow.Restack).
        if (target == index)
        {
            return false;
        }

        rest.InsertRange(target, moving);
        ordered = rest;

        // Dense renumbering keeps repeated bring-forward well defined and the saved file tidy.
        var children = new List<IDocumentCommand>();
        for (int z = 0; z < ordered.Count; z++)
        {
            if (ordered[z].ZOrder != z)
            {
                children.Add(new SetZOrderCommand(ordered[z].Id, z));
            }
        }

        if (children.Count == 0)
        {
            return false;
        }

        _session.Execute(new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId),
            children));
        return true;
    }

    // ---- Linking (docs/M5-spec.md §8) --------------------------------------------------------

    /// <summary>Arms link mode on the selected text frame.</summary>
    public bool BeginLink()
    {
        if (_selectedBlockId is null || SelectedTextBlock() is not { } source)
        {
            StatusMessage = "Pick a text frame first, then link it to another one.";
            Raise();
            return false;
        }

        if (source.LinkNext is not null)
        {
            StatusMessage = "This frame already continues into another frame. Unlink it first.";
            Raise();
            return false;
        }

        IsLinkModeActive = true;
        LinkTargetBlockId = null;
        StatusMessage =
            "Click the frame the text should continue into, or press Tab to pick one and Enter to confirm.";
        Raise();
        return true;
    }

    public void CancelLink()
    {
        if (!IsLinkModeActive)
        {
            return;
        }

        IsLinkModeActive = false;
        LinkTargetBlockId = null;
        StatusMessage = null;
        Raise();
    }

    /// <summary>
    /// Tab through the frames that could receive the armed link (docs/M5-spec.md §9). This moves
    /// the link cursor only — the frame selection stays on the source, which is what makes the
    /// keyboard link path work at all.
    /// </summary>
    public bool CycleLinkTarget(int pageIndex, bool forward)
    {
        if (!IsLinkModeActive)
        {
            return false;
        }

        IReadOnlyList<string> candidates = GetLinkCandidates(pageIndex);
        if (candidates.Count == 0)
        {
            StatusMessage = "There is no other empty text frame on this page to continue into.";
            Raise();
            return false;
        }

        int current = LinkTargetBlockId is null ? -1 : candidates.ToList().IndexOf(LinkTargetBlockId);
        int next = current < 0
            ? (forward ? 0 : candidates.Count - 1)
            : ((current + (forward ? 1 : -1) + candidates.Count) % candidates.Count);
        LinkTargetBlockId = candidates[next];
        StatusMessage = "Press Enter to continue the text into the highlighted frame.";
        Raise();
        return true;
    }

    /// <summary>Confirms the link on the frame Tab landed on.</summary>
    public bool CompleteLinkAtTarget() =>
        LinkTargetBlockId is { } target && CompleteLink(target);

    /// <summary>Frames that could receive the armed link right now (for highlighting).</summary>
    public IReadOnlyList<string> GetLinkCandidates(int pageIndex)
    {
        if (!IsLinkModeActive || SelectedTextBlock() is null)
        {
            return [];
        }

        return
        [
            .. _session.Document.Pages[pageIndex].Blocks
                .OfType<TextBlock>()
                .Where(b => CanLinkTo(b.Id, out _))
                .Select(b => b.Id),
        ];
    }

    /// <summary>Completes the armed link. Refuses (with plain-language status) rather than
    /// destroying text or breaking the one-head-per-story invariant.</summary>
    public bool CompleteLink(string targetBlockId)
    {
        if (SelectedTextBlock() is not { } source)
        {
            return false;
        }

        if (!CanLinkTo(targetBlockId, out string? reason))
        {
            StatusMessage = reason;
            Raise();
            return false;
        }

        Document document = _session.Document;
        var target = (TextBlock)document.FindBlock(targetBlockId).Block;
        var children = new List<IDocumentCommand> { new SetLinkNextCommand(source.Id, targetBlockId) };

        if (!string.Equals(target.StoryRef, source.StoryRef, StringComparison.Ordinal))
        {
            string orphan = target.StoryRef;
            children.Add(new SetStoryRefCommand(targetBlockId, source.StoryRef));
            if (!AnyOtherBlockUsesStory(document, orphan, [targetBlockId]))
            {
                children.Add(new RemoveStoryCommand(orphan));
            }
        }

        _session.Execute(new CompositeCommand(
            "Link frames",
            new ChangeScope(ChangeKind.PageStructure, BlockId: source.Id, StoryId: source.StoryRef),
            children));

        IsLinkModeActive = false;
        LinkTargetBlockId = null;
        StatusMessage = "The text now continues into the next frame.";
        Raise();
        return true;
    }

    /// <summary>
    /// Detaches the selected frame's continuation. The detached frame gets a NEW empty story —
    /// without that it would become a second head on the same story (docs/M5-spec.md §8.1).
    /// </summary>
    public bool Unlink()
    {
        if (SelectedTextBlock() is not { LinkNext: { } nextId } source)
        {
            StatusMessage = "This frame does not continue into another frame.";
            Raise();
            return false;
        }

        Document document = _session.Document;
        string storyId = NextId("story", id => document.Stories.Any(s => s.Id == id));
        var story = new Story { Id = storyId };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = DefaultParagraphStyleRef(document),
            Runs = [new StoryRun { Text = "" }],
        });

        _session.Execute(new CompositeCommand(
            "Unlink frames",
            new ChangeScope(ChangeKind.PageStructure, BlockId: source.Id, StoryId: source.StoryRef),
            [
                new SetLinkNextCommand(source.Id, null),
                new AddStoryCommand(story),
                new SetStoryRefCommand(nextId, storyId),
            ]));
        StatusMessage = "The frames are no longer connected.";
        Raise();
        return true;
    }

    private bool CanLinkTo(string targetBlockId, out string? reason)
    {
        reason = null;
        if (SelectedTextBlock() is not { } source)
        {
            reason = "Pick a text frame first.";
            return false;
        }

        if (string.Equals(targetBlockId, source.Id, StringComparison.Ordinal))
        {
            reason = "A frame cannot continue into itself.";
            return false;
        }

        Document document = _session.Document;
        if (document.Pages.SelectMany(p => p.Blocks).FirstOrDefault(b => b.Id == targetBlockId) is not TextBlock target)
        {
            reason = "Text can only continue into another text frame.";
            return false;
        }

        if (FindPredecessor(document, targetBlockId) is not null)
        {
            reason = "That frame already continues another frame.";
            return false;
        }

        // Walking forward from the target must never arrive back at the source.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (TextBlock? walk = target; walk is not null && seen.Add(walk.Id);)
        {
            if (string.Equals(walk.Id, source.Id, StringComparison.Ordinal))
            {
                reason = "Those frames would form a loop.";
                return false;
            }

            walk = walk.LinkNext is { } id
                ? document.Pages.SelectMany(p => p.Blocks).OfType<TextBlock>().FirstOrDefault(b => b.Id == id)
                : null;
        }

        if (!string.Equals(target.StoryRef, source.StoryRef, StringComparison.Ordinal)
            && !IsStoryEmpty(document, target.StoryRef))
        {
            reason = "That frame already has text in it. Empty it first, or pick another frame.";
            return false;
        }

        return true;
    }

    // ---- Overlay ----------------------------------------------------------------------------

    /// <summary>
    /// Everything the canvas needs to draw on top of one page: selection, handles, snap guides,
    /// overset and link badges. Built on the UI thread and handed to the render thread as data
    /// (docs/M5-spec.md §10).
    /// </summary>
    public FrameOverlay BuildOverlay(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _session.Document.Pages.Count)
        {
            return FrameOverlay.Empty;
        }

        Page page = _session.Document.Pages[pageIndex];
        RectPt? selected = _selectedBlockId is { } id && SelectedPageIndex == pageIndex ? SelectedRect : null;

        var overset = new List<RectPt>();
        foreach (string blockId in _layout.GetOversetTailBlockIds())
        {
            if (page.Blocks.Any(b => b.Id == blockId))
            {
                overset.Add(_layout.GetEffectiveRect(blockId));
            }
        }

        var linked = new List<RectPt>();
        foreach (TextBlock block in page.Blocks.OfType<TextBlock>())
        {
            if (block.LinkNext is not null)
            {
                linked.Add(_layout.GetEffectiveRect(block.Id));
            }
        }

        IReadOnlyList<RectPt> candidates = IsLinkModeActive
            ? [.. GetLinkCandidates(pageIndex).Select(_layout.GetEffectiveRect)]
            : [];
        RectPt? linkTarget = IsLinkModeActive
            && LinkTargetBlockId is { } targetId
            && page.Blocks.Any(b => b.Id == targetId)
                ? _layout.GetEffectiveRect(targetId)
                : null;

        // M92: the other chosen frames, so a multi-selection is visible. Off-page ids cannot occur
        // — AddToSelection refuses to cross a page — but the filter is kept because SelectAll drops
        // them silently, and an overlay drawn from a stale list is exactly the kind of confident
        // wrong answer this app keeps finding.
        var alsoSelected = new List<RectPt>();
        foreach (string companionId in _alsoSelected)
        {
            if (page.Blocks.Any(b => b.Id == companionId))
            {
                alsoSelected.Add(IsDragging && _dragPreviews.TryGetValue(companionId, out RectPt preview)
                    ? preview
                    : _layout.GetEffectiveRect(companionId));
            }
        }

        return new FrameOverlay(
            selected, !IsDragging, [.. _snapGuides], overset, linked, candidates, linkTarget,
            alsoSelected);
    }

    // ---- Internals ---------------------------------------------------------------------------

    private const string OversetMessage =
        "There is more writing than fits — 'Make the rest fit' (Ctrl+Shift+M) will flow it, "
        + "or make this frame bigger.";

    private void OnDocumentChanged()
    {
        // M21: and it can remove one of the others just as easily. A stale id in the multi-selection
        // would make "line up" throw while asking the layout for a rectangle that is not there.
        if (_alsoSelected.Count > 0)
        {
            _alsoSelected.RemoveAll(also =>
                !_session.Document.Pages.Any(p => p.Blocks.Any(b => b.Id == also)));
        }

        // Undo can remove the selected block out from under us.
        if (_selectedBlockId is { } id
            && !_session.Document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)))
        {
            // Drop the drag preview too: leaving it installed would resurrect the block at the
            // dragged rect if a redo brings it back.
            if (IsDragging)
            {
                _layout.SetGeometryPreview(id, null);
            }

            // With others still chosen, one of them takes over rather than the whole selection
            // vanishing because the block that happened to be primary was the one deleted.
            _selectedBlockId = null;
            if (_alsoSelected.Count > 0)
            {
                _selectedBlockId = _alsoSelected[0];
                _alsoSelected.RemoveAt(0);
            }

            _dragHandle = FrameHandle.None;
            _snapGuides.Clear();
            IsLinkModeActive = false;
            LinkTargetBlockId = null;
        }

        StatusMessage = _selectedBlockId is { } selected && IsOverset(selected) ? OversetMessage : StatusMessage;
        Raise();
    }

    private bool IsOverset(string blockId) =>
        _layout.GetOversetTailBlockIds().Contains(blockId, StringComparer.Ordinal);

    private TextBlock? SelectedTextBlock() =>
        _selectedBlockId is { } id
            ? _session.Document.Pages.SelectMany(p => p.Blocks).OfType<TextBlock>().FirstOrDefault(b => b.Id == id)
            : null;

    private List<Block> PageBlocksInZOrder(int pageIndex) =>
        pageIndex < 0 || pageIndex >= _session.Document.Pages.Count
            ? []
            : [.. _session.Document.Pages[pageIndex].Blocks.OrderBy(b => b.ZOrder)];

    private bool TryBuildSnapContext(out SnapContext context)
    {
        context = null!;
        int pageIndex = SelectedPageIndex;
        if (pageIndex < 0)
        {
            return false;
        }

        Page page = _session.Document.Pages[pageIndex];
        PageMaster master = _session.Document.GetMaster(page.MasterRef);
        context = new SnapContext(
            master.Size,
            master.MarginLeftPt,
            master.MarginTopPt,
            master.MarginRightPt,
            master.MarginBottomPt,
            [.. page.Blocks.Where(b => b.Id != _selectedBlockId).Select(b => b.FrameRect)]);
        return true;
    }

    private static TextBlock? FindPredecessor(Document document, string blockId) =>
        document.Pages.SelectMany(p => p.Blocks).OfType<TextBlock>()
            .FirstOrDefault(b => string.Equals(b.LinkNext, blockId, StringComparison.Ordinal));

    /// <summary>
    /// Whether any block that is STAYING still shows this story. A story is dropped only when
    /// nothing will be left to draw it — and "nothing" has to account for the whole batch, or
    /// deleting two frames of one linked article would keep the story alive on the strength of a
    /// frame that is going in the same undo step.
    /// </summary>
    private static bool AnyOtherBlockUsesStory(
        Document document, string storyId, IReadOnlyList<string> exceptBlockIds) =>
        document.Pages.SelectMany(p => p.Blocks).OfType<TextBlock>()
            .Any(b => !exceptBlockIds.Contains(b.Id, StringComparer.Ordinal)
                && string.Equals(b.StoryRef, storyId, StringComparison.Ordinal));

    private static bool IsStoryEmpty(Document document, string storyId) =>
        document.Stories.FirstOrDefault(s => s.Id == storyId) is not { } story
        || story.Paragraphs.All(p => p.Runs.All(r => r.Text.Length == 0));

    private static string DefaultParagraphStyleRef(Document document) =>
        document.StyleSheet.ParagraphStyles.Any(s => s.Name == "body")
            ? "body"
            : document.StyleSheet.ParagraphStyles[0].Name;

    /// <summary>Deterministic ids (no clock, no Guid) so tests and snapshots stay stable.</summary>
    private static string NextId(string prefix, Func<string, bool> taken)
    {
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix}-{i}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
