using Avalonia.Headless;
using Avalonia.Input;
using SkiaSharp;
using TrestleBoard.Core.Model;
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
