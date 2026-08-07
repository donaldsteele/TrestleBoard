using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing;

/// <summary>
/// Page structure and auto-flow (docs/M8-spec.md §2/§3): adding, removing and reordering pages, and
/// "make the rest of this fit", which pours an overset story into as many new frames as it needs.
/// Headless like every other controller.
/// </summary>
public sealed class PageFlowController
{
    /// <summary>
    /// Most frames one auto-flow run will add. A story can be unflowable — a frame narrower than its
    /// longest word, an exclusion covering the whole body area — and without a cap the loop would add
    /// pages until the machine gave out.
    /// </summary>
    public const int MaxAutoFlowFrames = 8;

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _layout;

    public PageFlowController(DocumentSession session, DocumentRenderSource layout)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
    }

    public event EventHandler? Changed;

    /// <summary>Plain-language feedback for the shell's status line (PLAN.md §6).</summary>
    public string? StatusMessage { get; private set; }

    public int PageCount => _session.Document.Pages.Count;

    // ---- page structure ----------------------------------------------------------------------

    /// <summary>Adds an empty page after <paramref name="afterPageIndex"/> and returns its id.</summary>
    public string AddPage(int afterPageIndex)
    {
        Document document = _session.Document;
        int index = Math.Clamp(afterPageIndex + 1, 0, document.Pages.Count);
        var page = new Page
        {
            Id = NextPageId(document),
            MasterRef = document.Pages.Count > 0 ? document.Pages[^1].MasterRef : document.PageMasters[0].Id,
        };

        _session.Execute(new AddPageCommand(page, index));
        StatusMessage = null;
        Raise();
        return page.Id;
    }

    /// <summary>
    /// Removes a page and everything on it. A newsletter with one page keeps it: an empty document
    /// has nothing to show and nothing to undo into.
    /// </summary>
    public bool RemovePage(int pageIndex)
    {
        Document document = _session.Document;
        if (document.Pages.Count <= 1 || pageIndex < 0 || pageIndex >= document.Pages.Count)
        {
            StatusMessage = document.Pages.Count <= 1
                ? "This is the only page, so it cannot be removed. Add another page first."
                : null;
            Raise();
            return false;
        }

        Page page = document.Pages[pageIndex];
        bool breaksAChain = BreaksAStoryChain(document, page);

        _session.Execute(new RemovePageCommand(page.Id));
        StatusMessage = breaksAChain
            ? "That page carried the rest of a story. The text before it now shows a “does not fit” "
              + "mark — choose Object ▸ Make the rest fit, or press Ctrl+Z to put the page back."
            : null;
        Raise();
        return true;
    }

    public bool MovePage(int fromIndex, int toIndex)
    {
        Document document = _session.Document;
        if (fromIndex == toIndex
            || fromIndex < 0 || fromIndex >= document.Pages.Count
            || toIndex < 0 || toIndex >= document.Pages.Count)
        {
            return false;
        }

        _session.Execute(new MovePageCommand(document.Pages[fromIndex].Id, toIndex));
        StatusMessage = null;
        Raise();
        return true;
    }

    // ---- auto-flow ---------------------------------------------------------------------------

    /// <summary>True when this frame's story has run out of room and can be flowed onward.</summary>
    public bool CanAutoFlow(string? blockId) =>
        blockId is not null
        && _session.Document.TryFindBlock(blockId, out _, out Block? block)
        && block is TextBlock
        && _layout.GetOversetTailBlockIds().Contains(TailOf(blockId), StringComparer.Ordinal);

    /// <summary>
    /// Pours the rest of an overset story into new frames, adding pages as needed. The WHOLE run is
    /// one undo step: "make the rest fit" is one thing the user did, however many pages it took.
    /// </summary>
    public bool AutoFlow(string blockId)
    {
        ArgumentNullException.ThrowIfNull(blockId);
        if (!CanAutoFlow(blockId))
        {
            StatusMessage = null;
            Raise();
            return false;
        }

        Document document = _session.Document;
        var children = new List<IDocumentCommand>();
        var plan = new List<(string PageId, TextBlock Frame, string PreviousFrameId)>();

        // The commands are built against a projection of the document rather than executed one at a
        // time, so a cap hit or a refusal leaves the document untouched.
        var projectedPages = document.Pages.Select(p => p.Id).ToList();
        string tailId = TailOf(blockId);
        int nextPageOrdinal = HighestOrdinal(document.Pages.Select(p => p.Id), "page") + 1;
        int nextFrameOrdinal = HighestOrdinal(
            document.Pages.SelectMany(p => p.Blocks).Select(b => b.Id), "frame") + 1;

        // Overflow is a property of the whole chain, so it is measured from the chain's HEAD even
        // when the user invoked this on a frame part-way down it.
        string headId = HeadOf(blockId);

        // The tail walks off the end of the document as soon as the first frame is planned, so its
        // page and master are tracked here rather than looked up — nothing has been executed yet.
        (Page firstTailPage, Block firstTailBlock) = document.FindBlock(tailId);
        string tailPageId = firstTailPage.Id;
        string masterRef = firstTailPage.MasterRef;
        string storyRef = ((TextBlock)firstTailBlock).StoryRef;

        int added = 0;
        while (added < MaxAutoFlowFrames)
        {
            int tailPageIndex = projectedPages.IndexOf(tailPageId);

            string targetPageId;
            if (tailPageIndex >= 0 && tailPageIndex + 1 < projectedPages.Count)
            {
                targetPageId = projectedPages[tailPageIndex + 1];
            }
            else
            {
                var page = new Page
                {
                    Id = $"page-{nextPageOrdinal++}",
                    MasterRef = masterRef,
                };
                children.Add(new AddPageCommand(page, projectedPages.Count));
                projectedPages.Add(page.Id);
                targetPageId = page.Id;
            }

            PageMaster master = document.GetMaster(masterRef);
            var frame = new TextBlock
            {
                Id = $"frame-{nextFrameOrdinal++}",
                StoryRef = storyRef,
                FrameRect = BodyArea(master),

                // Above whatever is already on the target page. ZOrder = 0 put a continuation
                // frame BENEATH existing blocks, so an article flowed onto a page that already had
                // a picture on it could be wrap-shadowed by that picture — text vanishing into a
                // frame the user cannot see it in. Every other insert path in the app uses
                // Max(ZOrder)+1; this one did not (review §14.2).
                ZOrder = NextZOrder(document, targetPageId),

                // A continuation of a story must not push its own text aside.
                WrapMode = WrapMode.None,
            };

            children.Add(new AddBlockCommand(targetPageId, frame));
            children.Add(new SetLinkNextCommand(tailId, frame.Id));
            plan.Add((targetPageId, frame, tailId));

            tailId = frame.Id;
            tailPageId = targetPageId;
            added++;

            if (!StillOverset(headId, plan))
            {
                break;
            }
        }

        if (children.Count == 0)
        {
            return false;
        }

        _session.Execute(new CompositeCommand(
            "Make the rest fit",
            new ChangeScope(ChangeKind.PageStructure, BlockId: blockId),
            children));

        bool stillOverset = _layout.GetOversetTailBlockIds().Contains(tailId, StringComparer.Ordinal);
        StatusMessage = stillOverset
            ? $"Added {added} more {(added == 1 ? "frame" : "frames")}, but the text still does not all "
              + "fit. The frame may be too narrow for the words in it."
            : null;
        Raise();
        return true;
    }

    /// <summary>
    /// First frame of the chain this block belongs to. Auto-flow may be invoked on a frame in the
    /// MIDDLE of a chain, and a speculative layout started from there would pour the whole story
    /// into a truncated frame list — predicting overflow that does not exist.
    /// </summary>
    private string HeadOf(string blockId)
    {
        Document document = _session.Document;
        string current = blockId;
        var visited = new HashSet<string>(StringComparer.Ordinal) { current };
        while (true)
        {
            string? previous = null;
            foreach (Page page in document.Pages)
            {
                foreach (Block block in page.Blocks)
                {
                    if (block is TextBlock { LinkNext: { } next } && next == current)
                    {
                        previous = block.Id;
                        break;
                    }
                }

                if (previous is not null)
                {
                    break;
                }
            }

            if (previous is null || !visited.Add(previous))
            {
                return current;
            }

            current = previous;
        }
    }

    /// <summary>Last frame of the chain this block belongs to.</summary>
    private string TailOf(string blockId)
    {
        Document document = _session.Document;
        string current = blockId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current)
            && document.TryFindBlock(current, out _, out Block? block)
            && block is TextBlock { LinkNext: { } next }
            && document.TryFindBlock(next, out _, out _))
        {
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Would the story still be overset with the frames planned so far? Measured by laying the story
    /// out against the planned geometry, WITHOUT touching the document.
    /// </summary>
    private bool StillOverset(
        string headBlockId,
        List<(string PageId, TextBlock Frame, string PreviousFrameId)> plan)
    {
        return _layout.WouldStillBeOverset(
            headBlockId,
            plan.Select(p => (p.PageId, (Block)p.Frame, p.PreviousFrameId)).ToList());
    }

    private static bool BreaksAStoryChain(Document document, Page page)
    {
        var doomed = new HashSet<string>(page.Blocks.Select(b => b.Id), StringComparer.Ordinal);
        foreach (Page other in document.Pages)
        {
            if (ReferenceEquals(other, page))
            {
                continue;
            }

            foreach (Block block in other.Blocks)
            {
                if (block is TextBlock { LinkNext: { } next } && doomed.Contains(next))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static RectPt BodyArea(PageMaster master) => new(
        master.MarginLeftPt,
        master.MarginTopPt,
        master.Size.Width - master.MarginLeftPt - master.MarginRightPt,
        master.Size.Height - master.MarginTopPt - master.MarginBottomPt);

    private static string NextPageId(Document document) =>
        $"page-{HighestOrdinal(document.Pages.Select(p => p.Id), "page") + 1}";

    /// <summary>
    /// Highest N across existing "prefix-N" ids, so new ids never collide with one the user already
    /// has — and stay deterministic (no clock, no Guid), the rule since M5.
    /// </summary>
    private static int HighestOrdinal(IEnumerable<string> ids, string prefix)
    {
        int highest = 0;
        foreach (string id in ids)
        {
            if (id.StartsWith(prefix + "-", StringComparison.Ordinal)
                && int.TryParse(
                    id.AsSpan(prefix.Length + 1),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int ordinal)
                && ordinal > highest)
            {
                highest = ordinal;
            }
        }

        return highest;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// One above everything already on the page, matching every other insert path in the app
    /// (FrameEditorController, PhotoController, WidgetController all do exactly this). Frames added
    /// during the same auto-flow are counted as they land, so a run of new pages does not give
    /// every continuation the same z-order.
    /// </summary>
    private static int NextZOrder(Document document, string pageId)
    {
        if (!document.Pages.Any(p => p.Id == pageId))
        {
            return 0;
        }

        Page page = document.GetPage(pageId);
        return page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1;
    }

}
