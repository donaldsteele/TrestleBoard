using Avalonia.Headless;
using Avalonia.Input;
using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M6 through the real shell (docs/M6-spec.md §7): a picture goes in with its description, the
/// one big Fix button works from the keyboard, and the original bytes sit untouched in the package.
/// </summary>
public sealed class PhotoShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>Synthetic photo — fictional pixels only (PLAN.md §0).</summary>
    private static byte[] PhotoBytes()
    {
        var info = new SKImageInfo(240, 160, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("raster surface");
        surface.Canvas.Clear(new SKColor(0xFF8A9AA8));
        using (var patch = new SKPaint { Color = new SKColor(0xFF3A4048) })
        {
            surface.Canvas.DrawRect(SKRect.Create(140, 30, 80, 100), patch);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task InsertingAPictureKeepsItsDescriptionAndOriginalBytes()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            byte[] bytes = PhotoBytes();
            string id = Assert.IsType<string>(
                window.PhotosForTest!.InsertPhoto(0, bytes, "Brothers at the picnic"));

            var frame = (ImageFrame)window.SessionForTest!.Document.FindBlock(id).Block;
            Assert.Equal("Brothers at the picnic", frame.AltText);
            Assert.Equal("Insert photo", window.SessionForTest.UndoDescription);

            // The package holds the ORIGINAL encoded file, byte for byte.
            Assert.Equal(bytes, window.PackageForTest!.Assets[frame.AssetRef]);

            window.SessionForTest.Undo();
            Assert.DoesNotContain(window.SessionForTest.Document.Pages[0].Blocks, b => b.Id == id);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FixPhotoRunsFromTheKeyboardOnTheSelectedPicture()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            string id = Assert.IsType<string>(
                window.PhotosForTest!.InsertPhoto(0, PhotoBytes(), "Placeholder picture"));
            window.FramesForTest!.Select(id);

            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);

            var frame = (ImageFrame)window.SessionForTest!.Document.FindBlock(id).Block;
            Assert.True(frame.Recipe.AutoLevels);
            Assert.NotNull(frame.Recipe.CropNormalized);
            Assert.Equal("Fix photo", window.SessionForTest.UndoDescription);
            Assert.Equal(
                "Picture fixed. Press Ctrl+Z if you liked it better before.",
                window.StatusLabelTextForTest);

            window.SessionForTest.Undo();
            var reverted = (ImageFrame)window.SessionForTest.Document.FindBlock(id).Block;
            Assert.False(reverted.Recipe.AutoLevels);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- M18: the photo template's frames can actually be filled ------------------------------

    /// <summary>
    /// The milestone in one test. "6-page with photos" ships three unfillable frames — there was no
    /// replace command at all before M18, so <c>DocumentRenderSource</c> drew grey rectangles for
    /// the life of the document. Now each of them offers the hint, takes a picture, and says so.
    /// </summary>
    [Fact]
    public async Task EveryPlaceholderInThePhotoTemplateCanBeFilledIn()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenTemplate("six-page-photos");

            (string BlockId, int PageIndex) first = Assert.IsType<(string, int)>(
                window.PhotosForTest!.FirstPlaceholder);
            Assert.Equal(3, first.PageIndex);                       // page 4, the first photo page
            Assert.Contains(
                window.SourceForTest!.GetPlaceholderPictureRects(first.PageIndex),
                p => p.BlockId == first.BlockId);

            byte[] bytes = PhotoBytes();
            Assert.True(window.PhotosForTest.ReplacePhoto(
                first.BlockId, bytes, "Brothers at the picnic", "Summer picnic"));

            var frame = (ImageFrame)window.SessionForTest!.Document.FindBlock(first.BlockId).Block;
            Assert.Equal(bytes, window.PackageForTest!.Assets[frame.AssetRef]);
            Assert.Equal("Summer picnic", frame.Caption);
            Assert.Equal("Put a picture here", window.SessionForTest.UndoDescription);

            // The frame stops being a placeholder, and stops being offered the hint.
            Assert.DoesNotContain(
                window.SourceForTest.GetPlaceholderPictureRects(first.PageIndex),
                p => p.BlockId == first.BlockId);

            // ...and the next empty frame moves up, so the "what's next" card keeps working.
            Assert.NotEqual(first, window.PhotosForTest.FirstPlaceholder);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Double-clicking a picture frame asks for a picture — the gesture M17 taught for the lists,
    /// routed through the runner so the availability rules apply exactly as from the menu.
    /// </summary>
    [Fact]
    public async Task DoubleClickingAPictureFrameAsksToFillIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenTemplate("six-page-photos");
            window.GoToPage(3);

            (string blockId, _) = Assert.IsType<(string, int)>(window.PhotosForTest!.FirstPlaceholder);
            Core.Model.RectPt rect = window.SourceForTest!.GetEffectiveRect(blockId);

            var invoked = new List<string>();
            window.ActionsForTest.InterceptorForTest = id =>
            {
                invoked.Add(id);
                return true;
            };

            Assert.True(window.CanvasForTest.TryActivatePictureAt(
                rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)));

            Assert.Equal(blockId, window.FramesForTest!.SelectedBlockId);
            Assert.Equal(["picture.replace"], invoked);

            window.ActionsForTest.InterceptorForTest = null;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Keyboard-only fillability: Ctrl+Shift+O reaches the same command.</summary>
    [Fact]
    public async Task TheKeyboardReachesTheReplaceCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenTemplate("six-page-photos");
            window.CanvasForTest.Focus();

            (string blockId, int pageIndex) = Assert.IsType<(string, int)>(
                window.PhotosForTest!.FirstPlaceholder);
            window.GoToPage(pageIndex);
            window.FramesForTest!.Select(blockId);

            var invoked = new List<string>();
            window.ActionsForTest.InterceptorForTest = id =>
            {
                invoked.Add(id);
                return true;
            };

            window.KeyPressQwerty(PhysicalKey.O, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(["picture.replace"], invoked);

            window.ActionsForTest.InterceptorForTest = null;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The picture commands that need a picture refuse an empty frame out loud, naming the way out.
    /// A grey rectangle cannot be brightened, and greying the button would say nothing about why.
    /// </summary>
    [Fact]
    public async Task AnEmptyFrameRefusesTheAdjustmentsInPlainLanguage()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenTemplate("six-page-photos");

            (string blockId, int pageIndex) = Assert.IsType<(string, int)>(
                window.PhotosForTest!.FirstPlaceholder);
            window.GoToPage(pageIndex);
            window.FramesForTest!.Select(blockId);
            window.RefreshActions();

            await window.ActionsForTest.RunAsync("picture.fix");

            Assert.Contains(
                "no picture in this frame",
                window.StatusLabelTextForTest ?? "",
                StringComparison.Ordinal);
            Assert.False(window.SessionForTest!.CanUndo);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M18: Ctrl+V outside a piece of writing is about pictures, so it stops being greyed with
    /// "click into some writing first" — and when there is nothing on the clipboard to put on the
    /// page it says so in a sentence rather than doing nothing at all.
    /// </summary>
    [Fact]
    public async Task PasteWithNothingSelectedIsAboutPicturesAndSaysSoWhenThereIsNone()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.RefreshActions();

            Assert.Equal(SelectionKind.None, window.CurrentActionContext.Selection);
            Assert.True(ActionCatalog.Evaluate(ActionId.Paste, window.CurrentActionContext).IsAvailable);

            await window.ActionsForTest.RunAsync(ActionId.Paste);

            Assert.Contains(
                "no picture to paste",
                window.StatusLabelTextForTest ?? "",
                StringComparison.Ordinal);
            Assert.False(window.SessionForTest!.CanUndo);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FixPhotoDoesNothingWhenTheSelectionIsNotAPicture()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();
            window.CanvasForTest.Focus();

            window.FramesForTest!.Select("block-body-1");
            Assert.False(window.PhotosForTest!.IsPhoto("block-body-1"));

            window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.False(window.SessionForTest!.CanUndo);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
