using System.Text.Json;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

/// <summary>Shared shape for commands that replace a block's frame rect (move/resize).</summary>
public abstract class BlockRectCommand(string blockId, RectPt newRect) : IDocumentCommand
{
    private RectPt _oldRect;

    public string BlockId { get; } = blockId;

    public RectPt NewRect { get; private set; } = newRect;

    public abstract string Description { get; }

    public ChangeScope Scope => new(ChangeKind.BlockGeometry, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        _oldRect = block.FrameRect;
        block.FrameRect = NewRect;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        block.FrameRect = _oldRect;
    }

    public bool TryMerge(IDocumentCommand newer)
    {
        // Arrow-nudge bursts merge; the drag interaction already commits once (PLAN.md §4).
        if (newer.GetType() == GetType() && newer is BlockRectCommand n && n.BlockId == BlockId)
        {
            NewRect = n.NewRect;
            return true;
        }

        return false;
    }
}

public sealed class MoveBlockCommand(string blockId, RectPt newRect) : BlockRectCommand(blockId, newRect)
{
    public override string Description => "Move block";
}

public sealed class ResizeBlockCommand(string blockId, RectPt newRect) : BlockRectCommand(blockId, newRect)
{
    public override string Description => "Resize block";
}

public sealed class SetWrapModeCommand(string blockId, WrapMode wrapMode, float wrapMarginPt) : IDocumentCommand
{
    private WrapMode _oldMode;
    private float _oldMargin;

    public string BlockId { get; } = blockId;

    public WrapMode WrapMode { get; } = wrapMode;

    public float WrapMarginPt { get; } = wrapMarginPt;

    public string Description => "Change text wrap";

    public ChangeScope Scope => new(ChangeKind.BlockGeometry, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        _oldMode = block.WrapMode;
        _oldMargin = block.WrapMarginPt;
        block.WrapMode = WrapMode;
        block.WrapMarginPt = WrapMarginPt;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        block.WrapMode = _oldMode;
        block.WrapMarginPt = _oldMargin;
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class AddBlockCommand(string pageId, Block block) : IDocumentCommand
{
    public string PageId { get; } = pageId;

    public Block Block { get; } = block;

    public string Description => "Add block";

    public ChangeScope Scope => new(ChangeKind.PageStructure, PageId: PageId, BlockId: Block.Id);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.GetPage(PageId).Blocks.Add(Block);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.GetPage(PageId).Blocks.Remove(Block);
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class RemoveBlockCommand(string pageId, string blockId) : IDocumentCommand
{
    private Block? _removed;
    private int _index;

    public string PageId { get; } = pageId;

    public string BlockId { get; } = blockId;

    public string Description => "Delete block";

    public ChangeScope Scope => new(ChangeKind.PageStructure, PageId: PageId, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Page page = document.GetPage(PageId);
        _index = page.Blocks.FindIndex(b => b.Id == BlockId);
        if (_index < 0)
        {
            throw new KeyNotFoundException($"Block not found on page {PageId}: {BlockId}");
        }

        _removed = page.Blocks[_index];
        page.Blocks.RemoveAt(_index);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.GetPage(PageId).Blocks.Insert(_index, _removed ?? throw new InvalidOperationException("Revert before Apply."));
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class SetImageRecipeCommand(string blockId, ImageRecipe newRecipe) : IDocumentCommand
{
    private ImageRecipe? _old;

    public string BlockId { get; } = blockId;

    public ImageRecipe NewRecipe { get; private set; } = newRecipe;

    public string Description => "Adjust photo";

    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        if (block is not ImageFrame frame)
        {
            throw new InvalidOperationException($"Block {BlockId} is not an image frame.");
        }

        _old = frame.Recipe;
        frame.Recipe = NewRecipe.Clone();
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        ((ImageFrame)block).Recipe = _old ?? throw new InvalidOperationException("Revert before Apply.");
    }

    public bool TryMerge(IDocumentCommand newer)
    {
        // Slider drags produce bursts; one Undo should revert the whole adjustment.
        if (newer is SetImageRecipeCommand n && n.BlockId == BlockId)
        {
            NewRecipe = n.NewRecipe;
            return true;
        }

        return false;
    }
}

public sealed class SetWidgetDataCommand(string blockId, JsonElement? newData, int newDataVersion) : IDocumentCommand
{
    private JsonElement? _oldData;
    private int _oldVersion;

    public string BlockId { get; } = blockId;

    public JsonElement? NewData { get; } = newData;

    public int NewDataVersion { get; } = newDataVersion;

    public string Description => "Edit widget";

    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        if (block is not WidgetBlock widget)
        {
            throw new InvalidOperationException($"Block {BlockId} is not a widget.");
        }

        _oldData = widget.Data;
        _oldVersion = widget.DataVersion;
        widget.Data = NewData;
        widget.DataVersion = NewDataVersion;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        var widget = (WidgetBlock)block;
        widget.Data = _oldData;
        widget.DataVersion = _oldVersion;
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}
