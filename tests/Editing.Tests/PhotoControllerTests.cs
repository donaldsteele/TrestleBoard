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
