using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

/// <summary>
/// Sets a block's stacking order (docs/M5-spec.md §5). Z is not only paint order: a block wraps
/// text only in frames BELOW it, so this is a layout change, hence the BlockGeometry scope.
/// Bring-forward/send-backward renumber a whole page as one <see cref="CompositeCommand"/>.
/// </summary>
public sealed class SetZOrderCommand(string blockId, int zOrder) : IDocumentCommand
{
    private int _old;

    public string BlockId { get; } = blockId;

    public int ZOrder { get; } = zOrder;

    public string Description => "Change stacking order";

    public ChangeScope Scope => new(ChangeKind.BlockGeometry, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        _old = block.ZOrder;
        block.ZOrder = ZOrder;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        block.ZOrder = _old;
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Points a text frame at the next frame in its story chain, or detaches it (null).
/// Chain shape decides which frames a story flows through, so the scope is PageStructure.
/// The one-head-per-story invariant (docs/M5-spec.md §8.1) is the caller's to preserve — see
/// the link/unlink composites in TrestleBoard.Editing.
/// </summary>
public sealed class SetLinkNextCommand(string blockId, string? linkNext) : IDocumentCommand
{
    private string? _old;

    public string BlockId { get; } = blockId;

    public string? LinkNext { get; } = linkNext;

    public string Description => LinkNext is null ? "Unlink frames" : "Link frames";

    public ChangeScope Scope => new(ChangeKind.PageStructure, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TextBlock block = FindTextBlock(document, BlockId);
        _old = block.LinkNext;
        block.LinkNext = LinkNext;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        FindTextBlock(document, BlockId).LinkNext = _old;
    }

    public bool TryMerge(IDocumentCommand newer) => false;

    internal static TextBlock FindTextBlock(Document document, string blockId)
    {
        (_, Block block) = document.FindBlock(blockId);
        return block as TextBlock
            ?? throw new InvalidOperationException($"Block {blockId} is not a text frame.");
    }
}

/// <summary>
/// Repoints a text frame at another story — the second half of linking and unlinking
/// (docs/M5-spec.md §8.2/§8.3). Never destroys text on its own; story removal is a separate
/// command the composite adds only when the old story is unreferenced and empty.
/// </summary>
public sealed class SetStoryRefCommand(string blockId, string storyRef) : IDocumentCommand
{
    private string? _old;

    public string BlockId { get; } = blockId;

    public string StoryRef { get; } = storyRef;

    public string Description => "Change frame text";

    public ChangeScope Scope => new(ChangeKind.PageStructure, BlockId: BlockId, StoryId: StoryRef);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TextBlock block = SetLinkNextCommand.FindTextBlock(document, BlockId);
        _old = block.StoryRef;
        block.StoryRef = StoryRef;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SetLinkNextCommand.FindTextBlock(document, BlockId).StoryRef =
            _old ?? throw new InvalidOperationException("Revert before Apply.");
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}
