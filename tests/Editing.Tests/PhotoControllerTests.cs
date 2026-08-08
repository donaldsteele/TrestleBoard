using SkiaSharp;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Imaging;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// M6 acceptance (PLAN.md §11-M6): dropping a photo auto-crops it to the frame, every edit is
/// undoable, and the ORIGINAL bytes survive a container round trip untouched.
/// </summary>
public sealed class PhotoControllerTests : IDisposable
{
    private static readonly Lazy<FontStore> Fonts = new(BundledFonts.CreateDefaultStore);

    private readonly DocumentSession _session;
    private readonly DocumentRenderSource _source;
    private readonly PhotoController _photos;
    private readonly RecordingAssetStore _assets = new();

    public PhotoControllerTests()
    {
        var doc = new Document();
        doc.StyleSheet.CharacterStyles.Add(new CharacterStyleDef
        {
            Name = "body",
            FontFamily = BundledFonts.BodyFamily,
            SizePt = 12f,
        });
        doc.StyleSheet.ParagraphStyles.Add(new ParagraphStyleDef { Name = "body", CharacterStyleRef = "body" });
        doc.PageMasters.Add(new PageMaster { Id = "master-1" });
        doc.Stories.Add(new Story
        {
            Id = "story-1",
            Paragraphs = { new StoryParagraph { ParagraphStyleRef = "body", Runs = [new StoryRun { Text = "Placeholder." }] } },
        });
        var page = new Page { Id = "page-1", MasterRef = "master-1" };
        page.Blocks.Add(new TextBlock
        {
            Id = "text-1",
            StoryRef = "story-1",
            FrameRect = new RectPt(54f, 54f, 504f, 600f),
            ZOrder = 1,
        });
        doc.Pages.Add(page);

        _session = new DocumentSession(doc);
        _source = DocumentRenderSource.CreateEditable(doc, new Dictionary<string, byte[]>(), Fonts.Value, _session);
        _photos = new PhotoController(_session, _source, _assets);
    }

    private sealed class RecordingAssetStore : IPhotoAssetStore
    {
        public Dictionary<string, byte[]> Assets { get; } = new(StringComparer.Ordinal);

        public void Register(string assetRef, byte[] bytes) => Assets[assetRef] = bytes;
    }

    /// <summary>A 3:2 synthetic photo — fictional pixels only (PLAN.md §0).</summary>
    private static byte[] PhotoBytes(int width = 300, int height = 200)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0x88, 0x92, 0x9C));
            using var detail = new SKPaint { Color = new SKColor(0x20, 0x20, 0x20) };
            for (int x = width / 2; x < width - 10; x += 6)
            {
                canvas.DrawRect(new SKRect(x, 20, x + 3, height - 20), detail);
            }
        }

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        bitmap.Dispose();
        return data.ToArray();
    }

    private string InsertSquareFramedPhoto()
    {
        string id = _photos.InsertPhoto(0, PhotoBytes(), "Placeholder picture")
            ?? throw new InvalidOperationException("insert failed");

        // Square the frame up so the auto-crop has real work to do.
        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 200f, 200f)));
        return id;
    }

    [Fact]
    public void InsertingAPhotoAddsOneUndoableBlockAndKeepsTheBytes()
    {
        byte[] bytes = PhotoBytes();
        string? id = _photos.InsertPhoto(0, bytes, "Brothers at the picnic", caption: "Summer picnic");

        Assert.NotNull(id);
        var frame = (ImageFrame)_session.Document.FindBlock(id!).Block;
        Assert.Equal("Brothers at the picnic", frame.AltText);
        Assert.Equal("Summer picnic", frame.Caption);
        Assert.Equal(WrapMode.Rectangle, frame.WrapMode);
        Assert.Equal("Insert photo", _session.UndoDescription);

        // The asset store got the ORIGINAL bytes, untouched.
        Assert.Equal(bytes, _assets.Assets[frame.AssetRef]);

        // Natural aspect preserved and placed inside the margins.
        Assert.InRange(frame.FrameRect.Width / frame.FrameRect.Height, 1.45f, 1.55f);
        Assert.True(frame.FrameRect.X >= 54f && frame.FrameRect.Right <= 558f);

        _session.Undo();
        Assert.Empty(_session.Document.Pages[0].Blocks.OfType<ImageFrame>());
    }

    [Fact]
    public void InsertingRubbishIsRefusedInPlainLanguage()
    {
        Assert.Null(_photos.InsertPhoto(0, [1, 2, 3, 4], "not a photo"));
        Assert.Equal("That file is not a picture TrestleBoard can read. Try a JPEG or PNG.", _photos.StatusMessage);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void FixPhotoCropsToTheFrameAspectAndTurnsOnAutoLevels()
    {
        string id = InsertSquareFramedPhoto();
        Assert.True(_photos.FixPhoto(id));

        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        RectPt crop = Assert.IsType<RectPt>(frame.Recipe.CropNormalized);

        // Frame is square, source is 3:2 → the crop must square the source up.
        float croppedAspect = (crop.Width * 300f) / (crop.Height * 200f);
        Assert.InRange(croppedAspect, 0.94f, 1.06f);
        Assert.True(frame.Recipe.AutoLevels);
        Assert.False(frame.Recipe.AutoLevelsPerChannel); // luminance by default: no tint shift
        Assert.Equal("Fix photo", _session.UndoDescription);
    }

    [Fact]
    public void FixPhotoIsOneUndoStepBackToTheUntouchedOriginal()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);

        _session.Undo();
        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Null(frame.Recipe.CropNormalized);
        Assert.False(frame.Recipe.AutoLevels);
    }

    [Fact]
    public void SliderDragsCoalesceIntoOneUndoStep()
    {
        string id = InsertSquareFramedPhoto();
        _session.Execute(new SetImageRecipeCommand(id, new ImageRecipe()));   // separate the burst

        for (int i = 1; i <= 8; i++)
        {
            _photos.SetAdjustments(id, i * 0.05f, 0f, 0f);
        }

        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Equal(0.4f, frame.Recipe.Brightness, 3);

        _session.Undo();
        Assert.Equal(0f, ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.Brightness, 3);
    }

    [Fact]
    public void RotateAndResetAreUndoableAndPlainlyLabelled()
    {
        string id = InsertSquareFramedPhoto();

        _photos.Rotate(id, 1);
        Assert.Equal(1, ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.RotationSteps);
        Assert.Equal("Turn photo", _session.UndoDescription);

        _photos.Rotate(id, 3);   // wraps back to 0
        Assert.Equal(0, ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.RotationSteps);

        _photos.SetAdjustments(id, 0.5f, 0.5f, 0.5f);
        Assert.True(_photos.ResetPhoto(id));
        ImageRecipe recipe = ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe;
        Assert.Equal(0f, recipe.Brightness, 3);
        Assert.Null(recipe.CropNormalized);
        Assert.Equal("Undo picture changes", _session.UndoDescription);
    }

    [Fact]
    public void EditingAPhotoNeverTouchesTheStoredOriginal()
    {
        string id = InsertSquareFramedPhoto();
        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        byte[] before = (byte[])_assets.Assets[frame.AssetRef].Clone();

        _photos.FixPhoto(id);
        _photos.SetAdjustments(id, 0.6f, -0.3f, 0.4f);
        _photos.Rotate(id, 2);

        Assert.Equal(before, _assets.Assets[frame.AssetRef]);
    }

    [Fact]
    public void OriginalsSurviveAContainerRoundTripByteForByte()
    {
        string id = InsertSquareFramedPhoto();
        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        _photos.FixPhoto(id);

        var package = new TboardPackage { Document = _session.Document };
        foreach ((string key, byte[] value) in _assets.Assets)
        {
            package.Assets[key] = value;
        }

        using var buffer = new MemoryStream();
        TboardContainer.Save(package, buffer);
        buffer.Position = 0;
        TboardPackage reloaded = TboardContainer.Load(buffer);

        Assert.Equal(_assets.Assets[frame.AssetRef], reloaded.Assets[frame.AssetRef]);

        // ...and the recipe travelled with the document, so the photo still looks fixed.
        var reloadedFrame = (ImageFrame)reloaded.Document.FindBlock(id).Block;
        Assert.True(reloadedFrame.Recipe.AutoLevels);
        Assert.NotNull(reloadedFrame.Recipe.CropNormalized);
    }

    [Fact]
    public void ThePageRendersTheRecipeNotTheRawPhoto()
    {
        string id = InsertSquareFramedPhoto();

        byte[] plain = RenderPage();
        _photos.SetAdjustments(id, 0.6f, 0f, 0f);
        byte[] brightened = RenderPage();

        Assert.NotEqual(plain, brightened);
    }

    // ---- M18: filling, swapping and labelling ---------------------------------------------------

    /// <summary>A frame pointing at bytes the package does not hold — how every photo template ships.</summary>
    private string AddPlaceholderFrame()
    {
        var frame = new ImageFrame
        {
            Id = "img-placeholder",
            AssetRef = "photo-placeholder-1.jpg",
            FrameRect = new RectPt(54f, 54f, 300f, 200f),
            ZOrder = 5,
            WrapMode = WrapMode.Rectangle,
            WrapMarginPt = 8f,
            AltText = "Write a description of this photo here…",
        };
        _session.Execute(new AddBlockCommand("page-1", frame));
        return frame.Id;
    }

    [Fact]
    public void ATemplatePlaceholderIsRecognisedAndCanBeFilledIn()
    {
        string id = AddPlaceholderFrame();
        Assert.True(_photos.IsPlaceholder(id));
        Assert.True(_photos.HasPlaceholder);
        Assert.Equal((id, 0), _photos.FirstPlaceholder);

        byte[] bytes = PhotoBytes();
        Assert.True(_photos.ReplacePhoto(id, bytes, "Brothers at the picnic", "Summer picnic"));

        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Equal("Brothers at the picnic", frame.AltText);
        Assert.Equal("Summer picnic", frame.Caption);
        Assert.Equal(bytes, _assets.Assets[frame.AssetRef]);
        Assert.False(_photos.IsPlaceholder(id));
        Assert.False(_photos.HasPlaceholder);
        Assert.Null(_photos.FirstPlaceholder);
        Assert.Equal("Put a picture here", _session.UndoDescription);
    }

    /// <summary>
    /// The acceptance sentence for M18: a swap is ONE undo step, and undoing it brings the previous
    /// picture back byte for byte — which is only true because the old asset stays in the container.
    /// </summary>
    [Fact]
    public void SwappingAPictureIsOneUndoStepBackToTheOldBytes()
    {
        string id = InsertSquareFramedPhoto();
        var before = (ImageFrame)_session.Document.FindBlock(id).Block;
        string firstAsset = before.AssetRef;
        byte[] firstBytes = _assets.Assets[firstAsset];
        RectPt geometry = before.FrameRect;
        _photos.FixPhoto(id);   // the picture has been worked on before the swap

        byte[] second = PhotoBytes(400, 400);
        Assert.True(_photos.ReplacePhoto(id, second, "A different photograph"));

        var after = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.NotEqual(firstAsset, after.AssetRef);
        Assert.Equal(second, _assets.Assets[after.AssetRef]);
        Assert.Equal(geometry, after.FrameRect);            // geometry is never touched by a swap
        Assert.Null(after.Recipe.CropNormalized);           // ...but the old crop is not kept
        Assert.Equal("Swap this picture", _session.UndoDescription);

        _session.Undo();
        var reverted = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Equal(firstAsset, reverted.AssetRef);
        Assert.Equal(firstBytes, _assets.Assets[reverted.AssetRef]);
        Assert.True(reverted.Recipe.AutoLevels);            // and the work done on it comes back
        Assert.Equal(geometry, reverted.FrameRect);
    }

    [Fact]
    public void ASwapNeverReEncodesTheBytesItWasGiven()
    {
        string id = InsertSquareFramedPhoto();
        byte[] second = (byte[])PhotoBytes(320, 240).Clone();
        _photos.ReplacePhoto(id, second, "A different photograph");

        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Equal(second, _assets.Assets[frame.AssetRef]);
        Assert.Equal(".png", System.IO.Path.GetExtension(frame.AssetRef));
    }

    [Fact]
    public void SwappingInRubbishIsRefusedAndChangesNothing()
    {
        string id = InsertSquareFramedPhoto();
        string assetBefore = ((ImageFrame)_session.Document.FindBlock(id).Block).AssetRef;

        Assert.False(_photos.ReplacePhoto(id, [1, 2, 3, 4], "not a photo"));
        Assert.Equal("That file is not a picture TrestleBoard can read. Try a JPEG or PNG.", _photos.StatusMessage);
        Assert.Equal(assetBefore, ((ImageFrame)_session.Document.FindBlock(id).Block).AssetRef);
    }

    /// <summary>
    /// The M18 defect this closes: <c>SetAltText</c> wrote straight to the block, so a description
    /// typed by mistake could not be taken back.
    /// </summary>
    [Fact]
    public void DescriptionAndCaptionEditsAreUndoable()
    {
        string id = InsertSquareFramedPhoto();

        Assert.True(_photos.SetAltText(id, "Brothers at the picnic"));
        Assert.Equal("Describe the picture", _session.UndoDescription);
        _session.Undo();
        Assert.Equal("Placeholder picture", ((ImageFrame)_session.Document.FindBlock(id).Block).AltText);

        Assert.True(_photos.SetCaption(id, "  A warm evening at the lodge.  "));
        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.Equal("A warm evening at the lodge.", frame.Caption);
        Assert.Equal("Change the caption", _session.UndoDescription);

        _session.Undo();
        Assert.Null(((ImageFrame)_session.Document.FindBlock(id).Block).Caption);

        // A blank caption removes it rather than printing an empty line.
        _photos.SetCaption(id, "Something");
        _photos.SetCaption(id, "   ");
        Assert.Null(((ImageFrame)_session.Document.FindBlock(id).Block).Caption);
    }

    /// <summary>
    /// M18: a dropped picture lands where it was dropped. The position was in the drag event all
    /// along and was thrown away, so every dropped photograph went to the same spot on the page.
    /// </summary>
    [Fact]
    public void APictureCanBeInsertedCentredOnAPoint()
    {
        string id = _photos.InsertPhoto(0, PhotoBytes(), "Dropped here", caption: null, centre: (200f, 300f))
            ?? throw new InvalidOperationException("insert failed");

        RectPt rect = _session.Document.FindBlock(id).Block.FrameRect;
        Assert.Equal(200f, rect.X + (rect.Width / 2f), 1);
        Assert.Equal(300f, rect.Y + (rect.Height / 2f), 1);
    }

    /// <summary>Dropped at the very corner of the sheet, it is pushed back on rather than half lost.</summary>
    [Fact]
    public void APictureDroppedAtThePageEdgeIsClampedOntoTheSheet()
    {
        string id = _photos.InsertPhoto(0, PhotoBytes(), "Dropped at the edge", caption: null, centre: (0f, 0f))
            ?? throw new InvalidOperationException("insert failed");

        RectPt rect = _session.Document.FindBlock(id).Block.FrameRect;
        SizePt page = _session.Document.GetMaster(_session.Document.Pages[0].MasterRef).Size;
        Assert.Equal(0f, rect.X, 3);
        Assert.Equal(0f, rect.Y, 3);
        Assert.True(rect.Right <= page.Width + 0.01f && rect.Bottom <= page.Height + 0.01f);

        // ...and with no point at all the M6 placement is untouched, which is what every keyboard
        // path still uses.
        string keyboard = _photos.InsertPhoto(0, PhotoBytes(), "Chosen from the menu")
            ?? throw new InvalidOperationException("insert failed");
        RectPt keyboardRect = _session.Document.FindBlock(keyboard).Block.FrameRect;
        Assert.True(keyboardRect.X >= 54f);
    }

    /// <summary>Trim drags are a burst like the sliders: one undo step for the whole adjustment.</summary>
    [Fact]
    public void TrimmingTheEdgesCoalescesAndClamps()
    {
        string id = InsertSquareFramedPhoto();
        _session.Execute(new SetImageRecipeCommand(id, new ImageRecipe()));   // separate the burst

        for (int i = 1; i <= 5; i++)
        {
            _photos.SetCrop(id, new NormalizedRect(i * 0.02f, 0f, 1f - (i * 0.02f), 1f));
        }

        RectPt crop = Assert.IsType<RectPt>(
            ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized);
        Assert.Equal(0.1f, crop.X, 3);

        _session.Undo();
        Assert.Null(((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized);
    }

    /// <summary>
    /// M22: Position keeps the crop's SIZE exactly as Fix Photo left it — only the centre moves.
    /// That is the whole point of splitting resize-the-frame from align-the-content.
    /// </summary>
    [Fact]
    public void PositionKeepsTheCommittedCropSizeAndOnlyMovesItsCentre()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);
        RectPt committed = Assert.IsType<RectPt>(
            ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized);

        NormalizedRect? proposal = _photos.ProposePosition(id, 0.8f, 0.8f);
        Assert.NotNull(proposal);
        Assert.Equal(committed.Width, proposal!.Value.Width, 3);
        Assert.Equal(committed.Height, proposal.Value.Height, 3);

        Assert.True(_photos.SetPosition(id, proposal.Value));
        RectPt moved = Assert.IsType<RectPt>(
            ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized);
        Assert.Equal(committed.Width, moved.Width, 3);
        Assert.Equal(committed.Height, moved.Height, 3);
        Assert.NotEqual(committed.X, moved.X, 3);
        Assert.Equal("Position photo", _session.UndoDescription);

        _session.Undo();
        Assert.Equal(committed, Assert.IsType<RectPt>(
            ((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized));
    }

    /// <summary>Before any crop is committed, Position proposes the same largest-inscribed-window
    /// shape Fix Photo would start from — locked to the frame's aspect, not resizable here.</summary>
    [Fact]
    public void PositionWithNoExistingCropProposesTheFrameAspectWindow()
    {
        string id = InsertSquareFramedPhoto();
        NormalizedRect? proposal = _photos.ProposePosition(id, 0.5f, 0.5f);
        Assert.NotNull(proposal);

        // Frame is square, source is 3:2 → the proposed window must be squared up too.
        float aspect = (proposal!.Value.Width * 300f) / (proposal.Value.Height * 200f);
        Assert.InRange(aspect, 0.94f, 1.06f);
    }

    [Fact]
    public void PositionIsRefusedForABlockThatIsNotAPhoto()
    {
        Assert.Null(_photos.ProposePosition("no-such-block", 0.5f, 0.5f));
        Assert.False(_photos.SetPosition("no-such-block", NormalizedRect.Full));
        Assert.Null(_photos.GetDecodedImage("no-such-block"));
    }

    /// <summary>M23: a crop that already matches its frame's aspect has nothing stale to say.</summary>
    [Fact]
    public void CropIsNotStaleImmediatelyAfterFixPhoto()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);

        Assert.False(_photos.CropIsStale(id));
        Assert.Null(_photos.CropStaleNote(id));
    }

    /// <summary>M23's whole point: a frame resize that changes shape enough is flagged, non-blockingly.</summary>
    [Fact]
    public void ResizingTheFrameAfterFixPhotoMakesTheCropStale()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);

        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 400f, 100f)));

        Assert.True(_photos.CropIsStale(id));
        Assert.NotNull(_photos.CropStaleNote(id));

        // Never a silent auto-recrop: the recipe TrestleBoard.Imaging never touches stays put.
        Assert.NotNull(((ImageFrame)_session.Document.FindBlock(id).Block).Recipe.CropNormalized);
    }

    /// <summary>Dismissing hides the notice, but only until the frame's shape changes again.</summary>
    [Fact]
    public void DismissingTheNoticeHidesItUntilTheFrameChangesShapeAgain()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);
        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 400f, 100f)));
        Assert.True(_photos.CropIsStale(id));

        Assert.True(_photos.DismissStaleCropNotice(id));
        Assert.False(_photos.CropIsStale(id));

        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 400f, 300f)));
        Assert.True(_photos.CropIsStale(id));
    }

    /// <summary>
    /// M43, review §14.3: "I have already seen this" outlives the session.
    ///
    /// <para>The dismissal was a dictionary in this controller, so it died when the app closed and
    /// the note came back on the next open — the app arguing with somebody who had already
    /// answered. It now lives in the recipe, which the document carries and the file keeps.</para>
    /// </summary>
    [Fact]
    public void DismissingTheNoticeIsRememberedInTheDocumentRatherThanTheSession()
    {
        string id = InsertSquareFramedPhoto();
        _photos.FixPhoto(id);
        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 400f, 100f)));
        Assert.True(_photos.DismissStaleCropNotice(id));

        // The document itself now holds the answer — which is what a save writes and an open reads.
        var frame = (ImageFrame)_session.Document.FindBlock(id).Block;
        Assert.NotNull(frame.Recipe.StretchNoticeDismissedAtAspect);
        Assert.Equal(4f, frame.Recipe.StretchNoticeDismissedAtAspect!.Value, 3);

        // And it is an ordinary recorded change, so it can be taken back like any other.
        Assert.True(_session.CanUndo);
        _session.Undo();
        Assert.True(_photos.CropIsStale(id));
    }

    /// <summary>No crop has ever been committed, so there is nothing for a resize to make stale.</summary>
    [Fact]
    public void CropIsNeverStaleBeforeAnyCropIsSet()
    {
        string id = InsertSquareFramedPhoto();
        _session.Execute(new ResizeBlockCommand(id, new RectPt(100f, 100f, 400f, 100f)));

        Assert.False(_photos.CropIsStale(id));
    }

    [Fact]
    public void CropIsStaleIsRefusedForABlockThatIsNotAPhoto()
    {
        Assert.False(_photos.CropIsStale("no-such-block"));
        Assert.Null(_photos.CropStaleNote("no-such-block"));
        Assert.False(_photos.DismissStaleCropNotice("no-such-block"));
    }

    private byte[] RenderPage()
    {
        var info = new SKImageInfo(306, 396, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        surface.Canvas.Scale(0.5f);
        _source.RenderPage(surface.Canvas, 0);
        surface.Canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose() => _source.Dispose();
}
