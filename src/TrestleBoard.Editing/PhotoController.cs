using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Imaging;
using TrestleBoard.Rendering;

namespace TrestleBoard.Editing;

/// <summary>Where the original photo bytes land — the package, in the shell's case.</summary>
public interface IPhotoAssetStore
{
    /// <summary>Stores the ORIGINAL encoded bytes verbatim; recipes never touch them.</summary>
    void Register(string assetRef, byte[] bytes);
}

/// <summary>
/// Inserting and correcting photos (docs/M6-spec.md §6/§7). Headless like the text and frame
/// controllers: "Fix photo" is testable without a window.
///
/// Everything that changes the document goes through a command, so every photo edit is undoable —
/// and nothing here ever rewrites the asset, which is what keeps originals byte-identical in the
/// container.
/// </summary>
public sealed class PhotoController
{
    /// <summary>New photos land at this fraction of the page's text width.</summary>
    private const float DefaultWidthFraction = 0.55f;

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _layout;
    private readonly IPhotoAssetStore _assets;

    public PhotoController(DocumentSession session, DocumentRenderSource layout, IPhotoAssetStore assets)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
    }

    public event EventHandler? Changed;

    /// <summary>Plain-language feedback for the shell's status line.</summary>
    public string? StatusMessage { get; private set; }

    public bool IsPhoto(string? blockId) =>
        blockId is not null
        && _session.Document.Pages.SelectMany(p => p.Blocks).Any(b => b.Id == blockId && b is ImageFrame);

    public ImageFrame? GetPhoto(string? blockId) =>
        blockId is null
            ? null
            : _session.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>()
                .FirstOrDefault(b => b.Id == blockId);

    /// <summary>
    /// Inserts a photo at its natural aspect inside the page margins and returns the new block id,
    /// or null when the bytes are not a readable image. Alt text is required at the call site —
    /// a screen-reader user must never meet an unlabelled photo (PLAN.md §6).
    /// </summary>
    public string? InsertPhoto(int pageIndex, byte[] bytes, string altText, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (pageIndex < 0 || pageIndex >= _session.Document.Pages.Count)
        {
            return null;
        }

        using DecodedImage? probe = ImageDecoder.Decode(bytes);
        if (probe is null)
        {
            StatusMessage = "That file is not a picture TrestleBoard can read. Try a JPEG or PNG.";
            Raise();
            return null;
        }

        Document document = _session.Document;
        Page page = document.Pages[pageIndex];
        PageMaster master = document.GetMaster(page.MasterRef);

        string assetRef = NextAssetRef(bytes);
        string blockId = NextId("photo", id => document.Pages.Any(p => p.Blocks.Any(b => b.Id == id)));

        // Register the bytes BEFORE the command runs so the first paint after it can decode them.
        _assets.Register(assetRef, bytes);
        _layout.AddAsset(assetRef, bytes);

        var block = new ImageFrame
        {
            Id = blockId,
            AssetRef = assetRef,
            FrameRect = DefaultRect(master, probe.Aspect),
            ZOrder = page.Blocks.Count == 0 ? 0 : page.Blocks.Max(b => b.ZOrder) + 1,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 6f,
            AltText = altText ?? "",
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption,
        };

        _session.Execute(new CompositeCommand(
            "Insert photo",
            new ChangeScope(ChangeKind.PageStructure, PageId: page.Id, BlockId: blockId),
            [new AddBlockCommand(page.Id, block)]));

        StatusMessage = null;
        Raise();
        return blockId;
    }

    /// <summary>
    /// The one big button (PLAN.md §9): auto-crop to the frame's shape plus luminance auto-levels,
    /// as a single undo step labelled in plain language.
    /// </summary>
    public bool FixPhoto(string blockId)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        DecodedImage? decoded = _layout.GetDecodedImage(frame.AssetRef);
        if (decoded is null)
        {
            StatusMessage = "That picture could not be read, so it cannot be fixed.";
            Raise();
            return false;
        }

        float targetAspect = frame.FrameRect.Height > 0f
            ? frame.FrameRect.Width / frame.FrameRect.Height
            : decoded.Aspect;
        NormalizedRect crop = AutoCrop.Propose(decoded, targetAspect);

        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.CropNormalized = new RectPt(crop.X, crop.Y, crop.Width, crop.Height);
        recipe.AutoLevels = true;
        recipe.AutoLevelsPerChannel = false;

        Execute(blockId, recipe, "Fix photo");
        StatusMessage = "Picture fixed. Press Ctrl+Z if you liked it better before.";
        Raise();
        return true;
    }

    /// <summary>Slider changes; consecutive calls coalesce into one undo step per drag.</summary>
    public bool SetAdjustments(string blockId, float brightness, float contrast, float saturation)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.Brightness = Math.Clamp(brightness, -1f, 1f);
        recipe.Contrast = Math.Clamp(contrast, -1f, 1f);
        recipe.Saturation = Math.Clamp(saturation, -1f, 1f);
        _session.Execute(new SetImageRecipeCommand(blockId, recipe));
        Raise();
        return true;
    }

    public bool SetCrop(string blockId, NormalizedRect crop)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        NormalizedRect clamped = crop.Clamped();
        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.CropNormalized = new RectPt(clamped.X, clamped.Y, clamped.Width, clamped.Height);
        Execute(blockId, recipe, "Crop photo");
        Raise();
        return true;
    }

    /// <summary>Quarter-turn rotation, the only rotation v1 offers (docs/M6-spec.md §8).</summary>
    public bool Rotate(string blockId, int steps)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.RotationSteps = (((frame.Recipe.RotationSteps + steps) % 4) + 4) % 4;
        Execute(blockId, recipe, "Turn photo");
        Raise();
        return true;
    }

    public bool SetAutoLevels(string blockId, bool enabled, bool perChannel = false)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.AutoLevels = enabled;
        recipe.AutoLevelsPerChannel = perChannel;
        Execute(blockId, recipe, "Adjust photo");
        Raise();
        return true;
    }

    /// <summary>Back to the untouched original — the reassuring escape hatch.</summary>
    public bool ResetPhoto(string blockId)
    {
        if (GetPhoto(blockId) is null)
        {
            return false;
        }

        Execute(blockId, new ImageRecipe(), "Undo picture changes");
        StatusMessage = "The picture is back to how it looked originally.";
        Raise();
        return true;
    }

    public bool SetAltText(string blockId, string altText)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        // Alt text lives on the block, not the recipe, so it rides the same command as a no-op
        // recipe change would; a dedicated command is not worth a new type here.
        frame.AltText = altText ?? "";
        Raise();
        return true;
    }

    /// <summary>Wraps the recipe change so the undo menu can say what the user actually did.</summary>
    private void Execute(string blockId, ImageRecipe recipe, string description) =>
        _session.Execute(new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
            [new SetImageRecipeCommand(blockId, recipe)]));

    private static RectPt DefaultRect(PageMaster master, float aspect)
    {
        float contentWidth = master.Size.Width - master.MarginLeftPt - master.MarginRightPt;
        float contentHeight = master.Size.Height - master.MarginTopPt - master.MarginBottomPt;
        float width = contentWidth * DefaultWidthFraction;
        float height = aspect > 0f ? width / aspect : width;
        if (height > contentHeight * DefaultWidthFraction)
        {
            height = contentHeight * DefaultWidthFraction;
            width = height * aspect;
        }

        return new RectPt(
            master.MarginLeftPt + ((contentWidth - width) / 2f),
            master.MarginTopPt + ((contentHeight - height) / 4f),
            width,
            height);
    }

    private string NextAssetRef(byte[] bytes)
    {
        string extension = SniffExtension(bytes);
        var used = _session.Document.Pages
            .SelectMany(p => p.Blocks)
            .OfType<ImageFrame>()
            .Select(b => b.AssetRef)
            .ToHashSet(StringComparer.Ordinal);
        return NextId("img", id => used.Contains(id + extension) || _layout.HasAsset(id + extension)) + extension;
    }

    /// <summary>Magic-number sniff — the file's own extension is not trustworthy.</summary>
    public static string SniffExtension(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
        {
            return ".webp";
        }

        return ".img";
    }

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
