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

        _dragHandle = handle;
        _dragStartRect = rect;
        _previewRect = rect;
        _dragStartXPt = xPt;
        _dragStartYPt = yPt;
        _snapGuides.Clear();
        Raise();
        return true;
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

        RectPt candidate = FrameGeometry.Resize(
            _dragStartRect, _dragHandle, xPt - _dragStartXPt, yPt - _dragStartYPt);

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

        if (commit && finalRect != startRect)
        {
            _session.Execute(handle is FrameHandle.Body
                ? new MoveBlockCommand(blockId, finalRect)
                : new ResizeBlockCommand(blockId, finalRect));
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

    // ---- Keyboard equivalents (docs/M5-spec.md §9) -------------------------------------------

    /// <summary>Arrow-key move. Never snaps — a nudge must move exactly what was asked.</summary>
    public bool Nudge(float dxSteps, float dySteps, bool large = false)
    {
        if (_selectedBlockId is null || IsDragging || SelectedRect is not { } rect)
        {
            return false;
        }

        float step = large ? LargeNudgeStepPt : NudgeStepPt;
        _session.Execute(new MoveBlockCommand(
            _selectedBlockId, FrameGeometry.Translate(rect, dxSteps * step, dySteps * step)));
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

        float step = large ? LargeNudgeStepPt : NudgeStepPt;
        _session.Execute(new ResizeBlockCommand(
            _selectedBlockId,
            FrameGeometry.Resize(rect, FrameHandle.BottomRight, dxSteps * step, dySteps * step)));
        return true;
    }

    // ---- Structure ---------------------------------------------------------------------------

    /// <summary>Adds an empty text frame inside the page margins and selects it
    /// (docs/M5-spec.md §7). Returns the new block id.</summary>
    public string AddTextFrame(int pageIndex)
    {
        Document document = _session.Document;
        Page page = document.Pages[pageIndex];
        PageMaster master = document.GetMaster(page.MasterRef);

        string storyId = NextId("story", id => document.Stories.Any(s => s.Id == id));
        string blockId = NextId("frame", id => document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));

        var story = new Story { Id = storyId };
        story.Paragraphs.Add(new StoryParagraph
        {
            ParagraphStyleRef = DefaultParagraphStyleRef(document),
            Runs = [new StoryRun { Text = "" }],
        });

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
            "Add text frame",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: blockId),
            [new AddStoryCommand(story), new AddBlockCommand(page.Id, block)]));
        Select(blockId);
        return blockId;
    }

    /// <summary>Deletes the selected frame, detaching it from any chain first and dropping the
    /// story when nothing else shows it (docs/M5-spec.md §7).</summary>
    public bool DeleteSelected()
    {
        if (_selectedBlockId is not { } blockId)
        {
            return false;
        }

        CancelDragIfAny();
        Document document = _session.Document;
        (Page page, Block block) = document.FindBlock(blockId);
        var children = new List<IDocumentCommand>();

        // Deleting a frame out of the MIDDLE of a chain heals the chain across the gap: A→B→C
        // becomes A→C, and the article keeps flowing.
        //
        // Nulling the predecessor unconditionally, as this did, broke the one invariant the whole
        // linking model rests on — that a story has exactly one head. A→B→C with B deleted left A
        // and C both pointing at nothing and both still referencing the same story, so the article
        // was drawn TWICE, from its first paragraph, in two places on the page. It also silently
        // disabled the story-cleanup guard below for that story ever afterwards, since two blocks
        // genuinely did use it. Unlink() at the bottom of this file guards the same invariant from
        // the other direction, by minting a new story for the frame it detaches.
        string? healTo = block is TextBlock { LinkNext: { } continuation } ? continuation : null;

        if (FindPredecessor(document, blockId) is { } predecessor)
        {
            children.Add(new SetLinkNextCommand(predecessor.Id, healTo));
        }

        if (block is TextBlock { LinkNext: not null } textBlock)
        {
            children.Add(new SetLinkNextCommand(textBlock.Id, null));
        }

        children.Add(new RemoveBlockCommand(page.Id, blockId));

        if (block is TextBlock text && !AnyOtherBlockUsesStory(document, text.StoryRef, blockId))
        {
            children.Add(new RemoveStoryCommand(text.StoryRef));
        }

        _session.Execute(new CompositeCommand(
            "Delete frame",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: blockId),
            children));
        Select(null);
        return true;
    }

    /// <summary>Turns text wrap on or off for the selected block (docs/M5-spec.md §6).</summary>
    public bool ToggleWrap()
    {
        if (_selectedBlockId is not { } blockId)
        {
            return false;
        }

        (_, Block block) = _session.Document.FindBlock(blockId);
        bool turningOn = block.WrapMode is WrapMode.None;
        _session.Execute(new SetWrapModeCommand(
            blockId,
            turningOn ? WrapMode.Rectangle : WrapMode.None,
            turningOn ? (block.WrapMarginPt > 0f ? block.WrapMarginPt : DefaultWrapMarginPt) : block.WrapMarginPt));
        return true;
    }

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
        int index = ordered.FindIndex(b => b.Id == blockId);
        int target = delta switch
        {
            int.MaxValue => ordered.Count - 1,
            int.MinValue => 0,
            _ => index + delta,
        };
        target = Math.Clamp(target, 0, ordered.Count - 1);
        if (index < 0 || target == index)
        {
            return false;
        }

        Block moving = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(target, moving);

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
            if (!AnyOtherBlockUsesStory(document, orphan, targetBlockId))
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

        return new FrameOverlay(
            selected, !IsDragging, [.. _snapGuides], overset, linked, candidates, linkTarget);
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

    private static bool AnyOtherBlockUsesStory(Document document, string storyId, string exceptBlockId) =>
        document.Pages.SelectMany(p => p.Blocks).OfType<TextBlock>()
            .Any(b => b.Id != exceptBlockId && string.Equals(b.StoryRef, storyId, StringComparison.Ordinal));

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
