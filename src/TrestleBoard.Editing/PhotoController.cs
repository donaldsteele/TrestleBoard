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
    /// A picture frame with nothing in it yet (PLAN.md §11 M18). The photo template ships three of
    /// these and, until M18, no command could fill one — the frame pointed at an asset the package
    /// did not contain, and it rendered as a grey rectangle forever.
    /// </summary>
    public bool IsPlaceholder(string? blockId) =>
        GetPhoto(blockId) is { } frame && FrameIsEmpty(frame);

    /// <summary>True when any page still shows an unfilled picture frame — the "what's next" flag.</summary>
    public bool HasPlaceholder =>
        _session.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>().Any(FrameIsEmpty);

    /// <summary>
    /// The first unfilled picture frame and the page it sits on, or null. The shell uses it to
    /// answer the "what's next" card with nothing selected: it turns to that page, chooses the
    /// frame, and then asks for a file — the same two-way reachability M13 gave the birthday sync.
    /// </summary>
    public (string BlockId, int PageIndex)? FirstPlaceholder
    {
        get
        {
            for (int i = 0; i < _session.Document.Pages.Count; i++)
            {
                foreach (ImageFrame frame in _session.Document.Pages[i].Blocks.OfType<ImageFrame>())
                {
                    if (FrameIsEmpty(frame))
                    {
                        return (frame.Id, i);
                    }
                }
            }

            return null;
        }
    }

    private bool FrameIsEmpty(ImageFrame frame) => _layout.GetDecodedImage(frame.AssetRef) is null;

    /// <summary>
    /// Inserts a photo at its natural aspect inside the page margins and returns the new block id,
    /// or null when the bytes are not a readable image. Alt text is required at the call site —
    /// a screen-reader user must never meet an unlabelled photo (PLAN.md §6).
    /// </summary>
    /// <param name="centre">
    /// M18: where the user put it. A drop lands where it was dropped rather than in the middle of
    /// the page — the position was already in the drag event and was thrown away. Null keeps the
    /// M6 placement, which is what every keyboard path still uses: there is no pointer to ask.
    /// </param>
    public string? InsertPhoto(
        int pageIndex, byte[] bytes, string altText, string? caption = null, (float X, float Y)? centre = null)
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

        RectPt rect = DefaultRect(master, probe.Aspect);
        if (centre is { } point)
        {
            rect = CentreOn(rect, point.X, point.Y, master.Size);
        }

        var block = new ImageFrame
        {
            Id = blockId,
            AssetRef = assetRef,
            FrameRect = rect,
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
    /// Puts a picture into a frame that is already on the page, or swaps the one that is there
    /// (PLAN.md §11 M18). Returns false when the frame is not a picture or the bytes are unreadable.
    ///
    /// <para>The original encoded bytes are stored verbatim, exactly as on the insert path — a swap
    /// never re-encodes, so §12 item 7's "re-crop months later with no loss" guarantee applies to a
    /// swapped-in photograph as much as to an inserted one. The frame's rectangle is not touched,
    /// and the whole swap is one undo step.</para>
    /// </summary>
    public bool ReplacePhoto(string blockId, byte[] bytes, string altText, string? caption = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        using DecodedImage? probe = ImageDecoder.Decode(bytes);
        if (probe is null)
        {
            StatusMessage = "That file is not a picture TrestleBoard can read. Try a JPEG or PNG.";
            Raise();
            return false;
        }

        bool wasEmpty = FrameIsEmpty(frame);
        string assetRef = NextAssetRef(bytes);

        // Register BEFORE the command runs so the first paint after it can decode the new bytes.
        _assets.Register(assetRef, bytes);
        _layout.AddAsset(assetRef, bytes);

        _session.Execute(new CompositeCommand(
            wasEmpty ? "Put a picture here" : "Swap this picture",
            new ChangeScope(ChangeKind.BlockGeometry, BlockId: blockId),
            [new ReplaceImageCommand(blockId, assetRef, altText ?? "", string.IsNullOrWhiteSpace(caption) ? null : caption)]));

        StatusMessage = wasEmpty
            ? "The picture is on the page."
            : "The picture was swapped. Press Ctrl+Z to put the old one back.";
        Raise();
        return true;
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

    /// <summary>
    /// Keeps only part of the picture (docs/M6-spec.md §8). From M18 this is what "Trim the edges…"
    /// drives, which is how it stopped being a method nobody called.
    ///
    /// <para>Like the sliders, a burst of these coalesces into ONE undo step: the four edge controls
    /// are dragged, and an undo stack with a step per pixel of drag is an undo stack nobody can use.
    /// That is why the bare <see cref="SetImageRecipeCommand"/> is executed here rather than the
    /// labelled composite the one-shot photo edits use.</para>
    /// </summary>
    public bool SetCrop(string blockId, NormalizedRect crop)
    {
        if (GetPhoto(blockId) is not { } frame)
        {
            return false;
        }

        NormalizedRect clamped = crop.Clamped();
        ImageRecipe recipe = frame.Recipe.Clone();
        recipe.CropNormalized = new RectPt(clamped.X, clamped.Y, clamped.Width, clamped.Height);
        _session.Execute(new SetImageRecipeCommand(blockId, recipe));
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

    /// <summary>
    /// What a screen reader says instead of showing the picture (PLAN.md §6).
    ///
    /// <para>Until M18 this wrote straight to the block and never reached <c>DocumentSession</c>, so
    /// a description typed by mistake could not be taken back — the one photo edit in the app that
    /// was not undoable. It goes through a command now like everything else.</para>
    /// </summary>
    public bool SetAltText(string blockId, string altText)
    {
        if (GetPhoto(blockId) is null)
        {
            return false;
        }

        _session.Execute(SetPictureWordsCommand.ForAltText(blockId, altText ?? ""));
        StatusMessage = "The description is saved. Press Ctrl+Z to take it back.";
        Raise();
        return true;
    }

    /// <summary>The words printed under the picture; null or blank removes the caption (M18).</summary>
    public bool SetCaption(string blockId, string? caption)
    {
        if (GetPhoto(blockId) is null)
        {
            return false;
        }

        _session.Execute(SetPictureWordsCommand.ForCaption(
            blockId,
            string.IsNullOrWhiteSpace(caption) ? null : caption.Trim()));
        Raise();
        return true;
    }

    /// <summary>Wraps the recipe change so the undo menu can say what the user actually did.</summary>
    private void Execute(string blockId, ImageRecipe recipe, string description) =>
        _session.Execute(new CompositeCommand(
            description,
            new ChangeScope(ChangeKind.BlockContent, BlockId: blockId),
            [new SetImageRecipeCommand(blockId, recipe)]));

    /// <summary>
    /// The same rectangle, centred on a point and pushed back onto the page. Clamping rather than
    /// refusing: a photograph dropped near the edge belongs near the edge, and half of it hanging
    /// off the paper is not what anybody meant by that.
    /// </summary>
    private static RectPt CentreOn(RectPt rect, float x, float y, SizePt page)
    {
        float left = Math.Clamp(x - (rect.Width / 2f), 0f, Math.Max(0f, page.Width - rect.Width));
        float top = Math.Clamp(y - (rect.Height / 2f), 0f, Math.Max(0f, page.Height - rect.Height));
        return new RectPt(left, top, rect.Width, rect.Height);
    }

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
