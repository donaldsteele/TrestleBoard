using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

public sealed class AddPageCommand(Page page, int index) : IDocumentCommand
{
    public Page Page { get; } = page;

    public int Index { get; } = index;

    public string Description => "Add page";

    public ChangeScope Scope => new(ChangeKind.PageStructure, PageId: Page.Id);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Pages.Insert(Index, Page);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Pages.RemoveAt(Index);
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Reorders pages (docs/M8-spec.md §3). Deliberately does NOT touch <c>linkNext</c>: a chain is a
/// list of block ids, so moving the page a frame sits on changes where it prints, not what
/// continues into what.
/// </summary>
public sealed class MovePageCommand(string pageId, int newIndex) : IDocumentCommand
{
    private int _oldIndex = -1;

    public string PageId { get; } = pageId;

    public int NewIndex { get; } = newIndex;

    public string Description => "Move page";

    public ChangeScope Scope => new(ChangeKind.PageStructure, PageId: PageId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _oldIndex = document.Pages.FindIndex(p => p.Id == PageId);
        if (_oldIndex < 0)
        {
            throw new KeyNotFoundException($"Page not found: {PageId}");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(NewIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(NewIndex, document.Pages.Count);

        Page page = document.Pages[_oldIndex];
        document.Pages.RemoveAt(_oldIndex);
        document.Pages.Insert(NewIndex, page);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        int current = document.Pages.FindIndex(p => p.Id == PageId);
        Page page = document.Pages[current];
        document.Pages.RemoveAt(current);
        document.Pages.Insert(_oldIndex, page);
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class RemovePageCommand(string pageId) : IDocumentCommand
{
    private Page? _removed;
    private int _index;

    public string PageId { get; } = pageId;

    public string Description => "Delete page";

    public ChangeScope Scope => new(ChangeKind.PageStructure, PageId: PageId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _index = document.Pages.FindIndex(p => p.Id == PageId);
        if (_index < 0)
        {
            throw new KeyNotFoundException($"Page not found: {PageId}");
        }

        _removed = document.Pages[_index];
        document.Pages.RemoveAt(_index);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Pages.Insert(_index, _removed ?? throw new InvalidOperationException("Revert before Apply."));
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class AddStoryCommand(Story story) : IDocumentCommand
{
    public Story Story { get; } = story;

    public string Description => "Add text";

    public ChangeScope Scope => new(ChangeKind.StoryStructure, StoryId: Story.Id);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Stories.Add(Story);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Stories.Remove(Story);
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class RemoveStoryCommand(string storyId) : IDocumentCommand
{
    private Story? _removed;
    private int _index;

    public string StoryId { get; } = storyId;

    public string Description => "Delete text";

    public ChangeScope Scope => new(ChangeKind.StoryStructure, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _index = document.Stories.FindIndex(s => s.Id == StoryId);
        if (_index < 0)
        {
            throw new KeyNotFoundException($"Story not found: {StoryId}");
        }

        _removed = document.Stories[_index];
        document.Stories.RemoveAt(_index);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Stories.Insert(_index, _removed ?? throw new InvalidOperationException("Revert before Apply."));
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Turns the page footer on or off for every page in the newsletter (M78).
///
/// <para>Every master at once, not the current page's: a newsletter whose cover had a footer and
/// whose inside pages did not would look like a mistake, and nobody asked for the two to differ.
/// Reverting puts each master back to what it individually was, so a document that arrived with a
/// mixture keeps its mixture on undo.</para>
/// </summary>
public sealed class ShowPageFooterCommand(bool show) : IDocumentCommand
{
    private Dictionary<string, bool>? _old;

    public bool Show { get; } = show;

    public string Description => Show ? "Show page numbers" : "Hide page numbers";

    public ChangeScope Scope => new(ChangeKind.PageStructure);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Re-captured on every Apply, so redo is as correct as undo (see IDocumentCommand).
        _old = document.PageMasters.ToDictionary(m => m.Id, m => m.ShowFooter, StringComparer.Ordinal);
        foreach (PageMaster master in document.PageMasters)
        {
            master.ShowFooter = Show;
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Dictionary<string, bool> old = _old ?? throw new InvalidOperationException("Revert before Apply.");
        foreach (PageMaster master in document.PageMasters)
        {
            if (old.TryGetValue(master.Id, out bool was))
            {
                master.ShowFooter = was;
            }
        }
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class SetMetadataCommand(DocumentMetadata newMetadata) : IDocumentCommand
{
    private DocumentMetadata? _old;

    public DocumentMetadata NewMetadata { get; } = newMetadata;

    public string Description => "Edit newsletter details";

    public ChangeScope Scope => new(ChangeKind.Metadata);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _old = document.Metadata;
        document.Metadata = NewMetadata;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Metadata = _old ?? throw new InvalidOperationException("Revert before Apply.");
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// The paper every page is printed on: its size and its four margins (PLAN.md §11 M97).
///
/// <para><b>Nothing in the application has ever written any of these five fields.</b>
/// <see cref="PageMaster.Size"/> and the four margins are read by the layout engine, the snap
/// engine, the renderer, frame placement, picture placement and widget placement — eight readers,
/// no writer. US Letter was hardwired by a property default, and the word "A4" did not appear
/// anywhere in the source.</para>
///
/// <para><b>Every master at once</b>, like <see cref="ShowPageFooterCommand"/> and for the same
/// reason: the paper is a fact about the newsletter, not about one page of it, and a document whose
/// pages were different sizes is not something this app can produce or the committee can print.</para>
/// </summary>
public sealed class SetPageSetupCommand(SizePt size, float leftPt, float topPt, float rightPt, float bottomPt)
    : IDocumentCommand
{
    private readonly List<(string Id, SizePt Size, float Left, float Top, float Right, float Bottom)> _before = [];

    public SizePt Size { get; } = size;

    public float LeftPt { get; } = leftPt;

    public float TopPt { get; } = topPt;

    public float RightPt { get; } = rightPt;

    public float BottomPt { get; } = bottomPt;

    public string Description => "Change the paper";

    /// <summary>
    /// Page structure: every frame on every page has to be laid out again, because the area they
    /// live in has changed shape.
    /// </summary>
    public ChangeScope Scope => new(ChangeKind.PageStructure);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _before.Clear();
        foreach (PageMaster master in document.PageMasters)
        {
            _before.Add((master.Id, master.Size, master.MarginLeftPt, master.MarginTopPt,
                master.MarginRightPt, master.MarginBottomPt));

            master.Size = Size;
            master.MarginLeftPt = LeftPt;
            master.MarginTopPt = TopPt;
            master.MarginRightPt = RightPt;
            master.MarginBottomPt = BottomPt;
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Each master back to what IT was, not to one answer for all of them — a document that
        // arrived with a mixture keeps its mixture on undo. The same rule ShowPageFooterCommand
        // follows, and the reason both capture per-master rather than one snapshot.
        foreach ((string id, SizePt size, float left, float top, float right, float bottom) in _before)
        {
            PageMaster? master = document.PageMasters.Find(m => m.Id == id);
            if (master is null)
            {
                continue;
            }

            master.Size = size;
            master.MarginLeftPt = left;
            master.MarginTopPt = top;
            master.MarginRightPt = right;
            master.MarginBottomPt = bottom;
        }

        _before.Clear();
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}
