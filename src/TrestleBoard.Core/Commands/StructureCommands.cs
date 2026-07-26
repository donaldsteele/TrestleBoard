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
