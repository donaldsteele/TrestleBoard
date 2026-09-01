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

/// <summary>
/// Splits a box of writing into two columns, or puts it back to one (M83).
///
/// <para>Two or one, and nothing else. Three columns in a letter-width frame gives about an inch
/// and a half of text each, which is a column of hyphens waiting to happen — and this app has no
/// hyphenation (PLAN.md §1 non-goals). Every column is another place for writing to hide, which is
/// why the answer is a toggle rather than a number.</para>
/// </summary>
public sealed class SetColumnCountCommand(string blockId, int columns) : IDocumentCommand
{
    private int _oldColumns;

    public string BlockId { get; } = blockId;

    public int Columns { get; } = Math.Clamp(columns, 1, 2);

    public string Description => Columns > 1 ? "Split into two columns" : "Back to one column";

    // Geometry rather than content: nothing about the words changed, only the shape they flow in.
    public ChangeScope Scope => new(ChangeKind.BlockGeometry, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var block = (TextBlock)document.FindBlock(BlockId).Block;
        _oldColumns = block.ColumnCount;
        block.ColumnCount = Columns;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ((TextBlock)document.FindBlock(BlockId).Block).ColumnCount = _oldColumns;
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Puts a border or shading on a frame, or takes it off (M79).
///
/// <para>Two facts, one command, one undo step: the style definition is added to the sheet if it is
/// not already there, and the block is pointed at it. Splitting them would mean an undo that left a
/// style behind, and a document accumulating definitions nothing refers to.</para>
///
/// <para>"Nothing at all" is stored as NO reference rather than as a style that draws nothing.
/// A newsletter that has never been given a border must be byte-identical to one whose border was
/// turned on and off again — which is the property that makes this milestone safe to try.</para>
/// </summary>
public sealed class SetFrameLookCommand(string blockId, bool border, bool shade) : IDocumentCommand
{
    private string? _oldStyleRef;
    private bool _addedStyle;

    public string BlockId { get; } = blockId;

    public bool Border { get; } = border;

    public bool Shade { get; } = shade;

    public string Description => (Border, Shade) switch
    {
        (true, true) => "Put a border round it and shade it",
        (true, false) => "Put a border round it",
        (false, true) => "Shade it",
        _ => "Take the border and shading off",
    };

    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);

        // Re-captured on every Apply, so redo is as correct as undo (see IDocumentCommand).
        _oldStyleRef = block.FrameStyleRef;
        _addedStyle = false;

        string? name = PageLooks.StyleNameFor(Border, Shade);
        block.FrameStyleRef = name;
        if (name is null || document.StyleSheet.FrameStyles.Exists(s => s.Name == name))
        {
            return;
        }

        document.StyleSheet.FrameStyles.Add(PageLooks.Define(name));
        _addedStyle = true;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        block.FrameStyleRef = _oldStyleRef;

        if (_addedStyle && PageLooks.StyleNameFor(Border, Shade) is { } name)
        {
            document.StyleSheet.FrameStyles.RemoveAll(s => s.Name == name);
        }
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}
