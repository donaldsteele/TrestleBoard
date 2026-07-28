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
    public override string Description => "Move frame";
}

public sealed class ResizeBlockCommand(string blockId, RectPt newRect) : BlockRectCommand(blockId, newRect)
{
    public override string Description => "Resize frame";
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

/// <summary>
/// Puts different bytes in a picture frame that is already on the page (PLAN.md §11 M18) — the
/// command behind "Put a picture here…" and "Swap this picture…".
///
/// <para>Geometry is deliberately untouched: the frame the template drew is the frame the designer
/// wanted, and a swap that resized it would undo their work every time somebody changed their mind
/// about a photograph. The recipe IS reset, because a crop chosen for one photograph means nothing
/// on the next one.</para>
///
/// <para>One command, so one Ctrl+Z takes the whole swap back — bytes, description and caption
/// together — and the old asset stays in the container, which is what makes that undo lossless.</para>
/// </summary>
public sealed class ReplaceImageCommand(string blockId, string assetRef, string altText, string? caption)
    : IDocumentCommand
{
    private string? _oldAssetRef;
    private ImageRecipe? _oldRecipe;
    private string? _oldAltText;
    private string? _oldCaption;

    public string BlockId { get; } = blockId;

    public string AssetRef { get; } = assetRef;

    public string AltText { get; } = altText;

    public string? Caption { get; } = caption;

    public string Description => "Change the picture";

    /// <summary>
    /// Geometry, not content: from M18 a caption prints under the frame, so the space the picture
    /// takes from the text around it depends on what the caption says.
    /// </summary>
    public ChangeScope Scope => new(ChangeKind.BlockGeometry, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImageFrame frame = FindImageFrame(document, BlockId);
        _oldAssetRef = frame.AssetRef;
        _oldRecipe = frame.Recipe;
        _oldAltText = frame.AltText;
        _oldCaption = frame.Caption;

        frame.AssetRef = AssetRef;
        frame.Recipe = new ImageRecipe();
        frame.AltText = AltText;
        frame.Caption = Caption;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImageFrame frame = FindImageFrame(document, BlockId);
        frame.AssetRef = _oldAssetRef ?? throw new InvalidOperationException("Revert before Apply.");
        frame.Recipe = _oldRecipe ?? new ImageRecipe();
        frame.AltText = _oldAltText ?? "";
        frame.Caption = _oldCaption;
    }

    public bool TryMerge(IDocumentCommand newer) => false;

    internal static ImageFrame FindImageFrame(Document document, string blockId)
    {
        (_, Block block) = document.FindBlock(blockId);
        return block as ImageFrame
            ?? throw new InvalidOperationException($"Block {blockId} is not an image frame.");
    }
}

/// <summary>
/// The words that belong to a picture: what a screen reader says about it, and what prints under it
/// (PLAN.md §11 M18).
///
/// <para>Both live on the block rather than in a story, so before M18 the only code that set them
/// wrote straight to the model and never reached the undo stack. That is the defect this type
/// exists to close: a description typed by mistake was, until now, unrecoverable.</para>
/// </summary>
public sealed class SetPictureWordsCommand : IDocumentCommand
{
    private readonly bool _setAltText;
    private readonly bool _setCaption;
    private string? _oldAltText;
    private string? _oldCaption;
    private bool _applied;

    private SetPictureWordsCommand(string blockId, string? altText, string? caption, bool setAltText, bool setCaption)
    {
        BlockId = blockId;
        AltText = altText;
        Caption = caption;
        _setAltText = setAltText;
        _setCaption = setCaption;
    }

    public string BlockId { get; }

    public string? AltText { get; }

    public string? Caption { get; }

    public string Description => _setAltText ? "Describe the picture" : "Change the caption";

    /// <summary>
    /// A caption prints under the frame and therefore changes what the text around it has to flow
    /// past; a description is spoken, never drawn, so it moves nothing.
    /// </summary>
    public ChangeScope Scope => new(
        _setCaption ? ChangeKind.BlockGeometry : ChangeKind.BlockContent,
        BlockId: BlockId);

    public static SetPictureWordsCommand ForAltText(string blockId, string altText) =>
        new(blockId, altText, null, setAltText: true, setCaption: false);

    public static SetPictureWordsCommand ForCaption(string blockId, string? caption) =>
        new(blockId, null, caption, setAltText: false, setCaption: true);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImageFrame frame = ReplaceImageCommand.FindImageFrame(document, BlockId);
        _oldAltText = frame.AltText;
        _oldCaption = frame.Caption;
        _applied = true;

        if (_setAltText)
        {
            frame.AltText = AltText ?? "";
        }

        if (_setCaption)
        {
            frame.Caption = Caption;
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!_applied)
        {
            throw new InvalidOperationException("Revert before Apply.");
        }

        ImageFrame frame = ReplaceImageCommand.FindImageFrame(document, BlockId);
        frame.AltText = _oldAltText ?? "";
        frame.Caption = _oldCaption;
    }

    /// <summary>
    /// Typing is not merged here. The dialogs behind these two commands hand over one finished
    /// sentence when the user presses the button, so there is no burst to coalesce — and merging
    /// would silently join two deliberate edits into one undo step.
    /// </summary>
    public bool TryMerge(IDocumentCommand newer) => false;
}
