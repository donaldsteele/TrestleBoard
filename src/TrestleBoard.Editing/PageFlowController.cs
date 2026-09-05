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

    /// <summary>
    /// Copies a whole page, with everything on it, and puts the copy after it (PLAN.md §11 M100).
    ///
    /// <para><b>Why a whole page and not four duplicates.</b> A trestle board repeats its own shape:
    /// a photo page this month is a photo page next month with different photographs in it, and the
    /// committee's way of making the second one was to build it again frame by frame. `item.duplicate`
    /// has copied ONE thing since M81; nothing has ever copied a page.</para>
    ///
    /// <para>Each block is copied by <see cref="BlockCopier"/>, so every rule that milestone settled
    /// applies unchanged: a piece of writing gets a story of its own holding the same words, a
    /// picture shares its bytes, and a block kind nobody wired up is refused rather than
    /// half-copied.</para>
    ///
    /// <para><b>Links are dropped, deliberately.</b> `BlockCopier` breaks `LinkNext` because a copy
    /// continues nothing — two frames claiming to continue one story is not a thing the flow model
    /// can mean. So the copied page holds the same words, standing on their own.</para>
    /// </summary>
    /// <returns>The new page's id, or null when the page does not exist.</returns>
    public string? DuplicatePage(int pageIndex)
    {
        Document document = _session.Document;
        if (pageIndex < 0 || pageIndex >= document.Pages.Count)
        {
            return null;
        }

        Page source = document.Pages[pageIndex];
        var page = new Page { Id = NextPageId(document), MasterRef = source.MasterRef };

        var taken = new HashSet<string>(StringComparer.Ordinal);
        var children = new List<IDocumentCommand> { new AddPageCommand(page, pageIndex + 1) };

        foreach (Block block in source.Blocks.OrderBy(b => b.ZOrder))
        {
            string copyId = NextId("copy", id => taken.Contains(id)
                || document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));
            string? storyId = block is TextBlock
                ? NextId("story", id => taken.Contains(id) || document.Stories.Any(s => s.Id == id))
                : null;

            if (BlockCopier.Copy(block, copyId, storyId) is not { } copy)
            {
                continue;
            }

            taken.Add(copyId);

            if (block is TextBlock text && storyId is not null)
            {
                taken.Add(storyId);
                var story = new Story { Id = storyId };
                if (document.TryGetStory(text.StoryRef, out Story? from))
                {
                    story.Paragraphs.AddRange(from.Paragraphs.Select(BlockCopier.CopyParagraph));
                }

                children.Add(new AddStoryCommand(story));
            }

            // Same place, same stacking: a copied page must look identical to the one it came from,
            // which is the entire reason for asking for one.
            copy.FrameRect = block.FrameRect;
            copy.ZOrder = block.ZOrder;
            copy.Locked = block.Locked;
            children.Add(new AddBlockCommand(page.Id, copy));
        }

        _session.Execute(new CompositeCommand(
            "Make another page like this",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id),
            children));

        StatusMessage = null;
        Raise();
        return page.Id;
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

    // ---- The paper itself (PLAN.md §11 M97) ---------------------------------------------------

    /// <summary>The papers a lodge newsletter is printed on, in the order they are offered.</summary>
    public static readonly IReadOnlyList<(string Name, SizePt Size)> Papers =
    [
        ("Letter (8.5 by 11 inches)", new SizePt(612f, 792f)),
        ("A4 (210 by 297 mm)", new SizePt(595.28f, 841.89f)),
        ("Legal (8.5 by 14 inches)", new SizePt(612f, 1008f)),
    ];

    /// <summary>The size and margins every page is currently using.</summary>
    public (SizePt Size, float Left, float Top, float Right, float Bottom) PageSetup
    {
        get
        {
            PageMaster master = _session.Document.PageMasters[0];
            return (master.Size, master.MarginLeftPt, master.MarginTopPt,
                master.MarginRightPt, master.MarginBottomPt);
        }
    }

    /// <summary>
    /// Changes the paper and the margins for the whole newsletter (M97).
    ///
    /// <para><b>Nothing is moved to fit.</b> Making the paper smaller can leave a frame hanging off
    /// the edge, and this deliberately does not shuffle the committee's layout to prevent it: a
    /// dozen frames quietly moving is a bigger surprise than one that needs dragging, and the shell
    /// says how many are off so the choice to undo is an informed one.</para>
    /// </summary>
    /// <returns>False when the paper is already exactly that.</returns>
    public bool SetPageSetup(SizePt size, float leftPt, float topPt, float rightPt, float bottomPt)
    {
        (SizePt current, float left, float top, float right, float bottom) = PageSetup;
        if (Near(current.Width, size.Width) && Near(current.Height, size.Height)
            && Near(left, leftPt) && Near(top, topPt)
            && Near(right, rightPt) && Near(bottom, bottomPt))
        {
            StatusMessage = null;
            Raise();
            return false;
        }

        _session.Execute(new SetPageSetupCommand(size, leftPt, topPt, rightPt, bottomPt));
        StatusMessage = null;
        Raise();
        return true;
    }

    /// <summary>
    /// How many blocks now hang off the paper — asked AFTER a change so the shell can say so.
    /// Zero on every newsletter that has not been made smaller.
    /// </summary>
    public int BlocksOffThePaper()
    {
        Document document = _session.Document;
        int count = 0;
        foreach (Page page in document.Pages)
        {
            PageMaster master = document.GetMaster(page.MasterRef);
            foreach (Block block in page.Blocks)
            {
                RectPt rect = block.FrameRect;
                if (rect.X < -0.001f || rect.Y < -0.001f
                    || rect.X + rect.Width > master.Size.Width + 0.001f
                    || rect.Y + rect.Height > master.Size.Height + 0.001f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.01f;

    // ---- auto-flow ---------------------------------------------------------------------------

    /// <summary>True when this frame's story has run out of room and can be flowed onward.</summary>
    public bool CanAutoFlow(string? blockId) =>
        blockId is not null
        && _session.Document.TryFindBlock(blockId, out _, out Block? block)
        && block is TextBlock
        && _layout.GetOversetTailBlockIds().Contains(TailOf(blockId), StringComparer.Ordinal);

    /// <summary>
    /// M71: the first story in the newsletter that has run out of room, given as the FIRST frame of
    /// its chain and the page that frame is on. This is what "Make the writing fit" acts on when the
    /// user pressed it from the "what's next" card with nothing chosen — the card is only drawn in
    /// that state, so without this the command could never have run. The head is returned rather
    /// than the overflowing tail because that is the frame auto-flow measures from, and it is the
    /// one the user thinks of as "the writing".
    /// </summary>
    public (string BlockId, int PageIndex)? FirstOversetChainHead
    {
        get
        {
            Document document = _session.Document;
            for (int i = 0; i < document.Pages.Count; i++)
            {
                foreach (TextBlock frame in document.Pages[i].Blocks.OfType<TextBlock>())
                {
                    if (!CanAutoFlow(frame.Id))
                    {
                        continue;
                    }

                    string head = HeadOf(frame.Id);
                    for (int page = 0; page < document.Pages.Count; page++)
                    {
                        if (document.Pages[page].Blocks.Any(b => string.Equals(b.Id, head, StringComparison.Ordinal)))
                        {
                            return (head, page);
                        }
                    }

                    return (head, i);
                }
            }

            return null;
        }
    }

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

    /// <summary>
    /// Deterministic ids (no clock, no Guid), minted against a running set so a batch cannot claim
    /// one twice — the M91 defect, which produced several blocks all answering to one id.
    /// </summary>
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
