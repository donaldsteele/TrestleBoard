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

    /// <summary>
    /// M72: the block whose WORDS are being changed, which from M72 is a picture or a drawing. The
    /// alt text and the caption are the two things a drawing shares with a photograph — everything
    /// else on <see cref="ImageFrame"/> is about pixels, and a drawing has none.
    /// </summary>
    internal static ICaptionedBlock FindCaptionedBlock(Document document, string blockId)
    {
        (_, Block block) = document.FindBlock(blockId);
        return block as ICaptionedBlock
            ?? throw new InvalidOperationException($"Block {blockId} has no caption or description.");
    }
}

/// <summary>
/// The words that belong to a picture: what a screen reader says about it, and what prints under it
/// (PLAN.md §11 M18). From M72 it acts on any <see cref="ICaptionedBlock"/>, so a drawing is
/// described and captioned by the same command and lands on the same undo stack.
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
        ICaptionedBlock frame = ReplaceImageCommand.FindCaptionedBlock(document, BlockId);
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

        ICaptionedBlock frame = ReplaceImageCommand.FindCaptionedBlock(document, BlockId);
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

/// <summary>
/// Keeps a block where it is, or lets it move again (M81).
///
/// <para>Position and size only — see <see cref="Block.Locked"/> for why that is the whole of it.
/// A one-field command with a plain description, so the Edit menu reads "Undo keeping it in
/// place" rather than "Undo block change".</para>
/// </summary>
public sealed class SetBlockLockedCommand(string blockId, bool locked) : IDocumentCommand
{
    private bool _wasLocked;

    public string BlockId { get; } = blockId;

    public bool Locked { get; } = locked;

    public string Description => Locked ? "Keep it in place" : "Let it move";

    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        (_, Block block) = document.FindBlock(BlockId);
        _wasLocked = block.Locked;
        block.Locked = Locked;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.FindBlock(BlockId).Block.Locked = _wasLocked;
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// The colours of a box or a line drawn on the page (PLAN.md §11 M95).
///
/// <para><b>Why a shape has its own command and a text frame does not.</b>
/// <see cref="ShapeBlock"/> carries <see cref="ShapeBlock.StrokeArgb"/>,
/// <see cref="ShapeBlock.StrokeWidthPt"/> and <see cref="ShapeBlock.FillArgb"/> on the block
/// itself, so any colour at all is already expressible and the renderer has always drawn it. A
/// text frame's look lives in a NAMED <c>FrameStyleDef</c> instead, of which the app mints exactly
/// three, and giving those arbitrary colours means minting derived styles — the machinery M86 is
/// bringing. This closes the half that needs nothing new.</para>
///
/// <para>Null stroke means no outline and null fill means see-through, which is exactly what the
/// renderer already does with them.</para>
/// </summary>
public sealed class SetShapeLookCommand(
    string blockId, uint? strokeArgb, float strokeWidthPt, uint? fillArgb) : IDocumentCommand
{
    private (uint? Stroke, float Width, uint? Fill)? _before;

    public string BlockId { get; } = blockId;

    public uint? StrokeArgb { get; } = strokeArgb;

    public float StrokeWidthPt { get; } = strokeWidthPt;

    public uint? FillArgb { get; } = fillArgb;

    public string Description => "Change its colours";

    /// <summary>
    /// Content, not geometry: a box that changes colour occupies exactly the same space, so nothing
    /// around it has to move and no story needs relaying out.
    /// </summary>
    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ShapeBlock shape = Find(document);
        _before = (shape.StrokeArgb, shape.StrokeWidthPt, shape.FillArgb);
        shape.StrokeArgb = StrokeArgb;
        shape.StrokeWidthPt = StrokeWidthPt;
        shape.FillArgb = FillArgb;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_before is not { } was)
        {
            throw new InvalidOperationException("Revert before Apply.");
        }

        ShapeBlock shape = Find(document);
        shape.StrokeArgb = was.Stroke;
        shape.StrokeWidthPt = was.Width;
        shape.FillArgb = was.Fill;
        _before = null;
    }

    public bool TryMerge(IDocumentCommand newer) => false;

    private ShapeBlock Find(Document document) =>
        document.FindBlock(BlockId).Block as ShapeBlock
            ?? throw new InvalidOperationException($"Not a box or a line: {BlockId}");
}

/// <summary>
/// What colour an emblem is drawn in (PLAN.md §11 M104).
///
/// <para><b>The last of the "written once and never changeable" fields.</b>
/// <see cref="VectorBlock.InkArgb"/> has been on the block since M65, is copied by
/// <c>BlockCopier</c>, and is what <c>DocumentRenderSource</c> paints every part with — and the
/// only thing that ever set it was the moment of insertion. An emblem put on the page in black
/// stayed black for the life of the newsletter.</para>
///
/// <para><b>One colour for the whole drawing, not one per part.</b> That is not a simplification
/// made here: a <see cref="VectorBlock"/> has a single ink and the renderer takes no other colour,
/// because a trestle board emblem is a line drawing meant to print in one colour on a page that is
/// usually black and white. Per-part colour would be a different feature and a different model.</para>
/// </summary>
public sealed class SetEmblemInkCommand(string blockId, uint inkArgb) : IDocumentCommand
{
    private uint? _before;

    public string BlockId { get; } = blockId;

    public uint InkArgb { get; } = inkArgb;

    public string Description => "Change what colour the emblem is";

    /// <summary>Content, not geometry: the drawing occupies exactly the same space either way.</summary>
    public ChangeScope Scope => new(ChangeKind.BlockContent, BlockId: BlockId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        VectorBlock vector = Find(document);
        _before = vector.InkArgb;
        vector.InkArgb = InkArgb;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_before is not { } was)
        {
            throw new InvalidOperationException("Revert before Apply.");
        }

        Find(document).InkArgb = was;
        _before = null;
    }

    /// <summary>
    /// No merging. Two recolourings of one emblem are two decisions somebody made, and folding them
    /// together would mean one Ctrl+Z jumping past a colour the user chose and looked at.
    /// </summary>
    public bool TryMerge(IDocumentCommand newer) => false;

    private VectorBlock Find(Document document) =>
        document.FindBlock(BlockId).Block as VectorBlock
            ?? throw new InvalidOperationException($"Not a drawing: {BlockId}");
}
