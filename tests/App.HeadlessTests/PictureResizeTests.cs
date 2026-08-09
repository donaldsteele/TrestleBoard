using System;
using System.Linq;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Emblems;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Resizing a picture scales it; it does not crop it (M69, reported 2026-08-09 with a screenshot of
/// a square and compasses cut off flat across the bottom).
///
/// <para>Two independent things had to be true for that bug, and both are fixed here: a corner drag
/// changed the frame's <b>aspect</b>, and <c>ImageFit.Cover</c> answers a changed aspect by cropping
/// the source. The geometry is held by <c>Layout.Tests/ResizeKeepingAspectTests</c>; this is the
/// half that goes through the real controller and the real insert path.</para>
/// </summary>
public sealed class PictureResizeTests
{
    /// <summary>
    /// The reported case: drag a corner of an emblem and the frame keeps its shape, so `Cover` has
    /// no changed aspect to crop against.
    /// </summary>
    [Fact]
    public async Task DraggingAPictureCornerKeepsItsShape()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "square-and-compasses";
                await window.InsertEmblemAsync();

                ImageFrame picture = Pictures(window).Last();
                RectPt before = picture.FrameRect;
                float aspectBefore = before.Width / before.Height;

                window.FramesForTest!.Select(picture.Id);
                window.FramesForTest.TryBeginDrag(before.Right - 1f, before.Bottom - 1f, 1f);

                // A deliberately lopsided pull: far more sideways than down. Before the fix this
                // widened the frame alone and the emblem lost its top and bottom.
                window.FramesForTest.DragTo(before.Right + 120f, before.Bottom + 8f, snap: false);
                window.FramesForTest.EndDrag(commit: true);

                RectPt after = Pictures(window).Last().FrameRect;
                Assert.Equal(aspectBefore, after.Width / after.Height, 2);
                Assert.True(after.Width > before.Width, "the drag did not resize anything");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An emblem is a whole thing. Even reshaped by a side handle — which still reshapes, on
    /// purpose — it must show all of itself rather than being cropped to the frame.
    /// </summary>
    [Fact]
    public async Task AnEmblemIsNeverCroppedToItsFrame()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "square-and-compasses-g";
                await window.InsertEmblemAsync();

                Assert.Equal(ImageFit.Contain, Pictures(window).Last().Fit);
                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A photograph keeps `Cover`: reshaping its frame is how somebody chooses what shows, and M22
    /// exists to position that crop. The fix must not take that away.
    /// </summary>
    [Fact]
    public async Task APhotographStillFillsItsFrame()
    {
        await HeadlessSession.DispatchAsync(
            () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                ImageFrame existing = Pictures(window).First();
                Assert.Equal(ImageFit.Cover, existing.Fit);

                window.Close();
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A side handle still reshapes a picture frame. Losing that would remove the only direct way
    /// to crop a photograph by hand.
    /// </summary>
    [Fact]
    public async Task ASideHandleStillReshapesAPicture()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "gavel";
                await window.InsertEmblemAsync();

                ImageFrame picture = Pictures(window).Last();
                RectPt before = picture.FrameRect;

                window.FramesForTest!.Select(picture.Id);
                window.FramesForTest.TryBeginDrag(before.Right - 1f, before.Y + (before.Height / 2f), 1f);
                window.FramesForTest.DragTo(before.Right + 100f, before.Y + (before.Height / 2f), snap: false);
                window.FramesForTest.EndDrag(commit: true);

                RectPt after = Pictures(window).Last().FrameRect;
                Assert.True(after.Width > before.Width);
                Assert.Equal(before.Height, after.Height, 2);

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>A text frame is unaffected: reshaping it reflows the words, which is the point.</summary>
    [Fact]
    public async Task ATextFrameStillReshapesFromItsCorner()
    {
        await HeadlessSession.Instance.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.FramesForTest!.AddTextFrame(0);
            RectPt before = window.SourceForTest!.GetEffectiveRect(blockId);

            window.FramesForTest.Select(blockId);
            window.FramesForTest.TryBeginDrag(before.Right - 1f, before.Bottom - 1f, 1f);
            window.FramesForTest.DragTo(before.Right + 120f, before.Bottom + 8f, snap: false);
            window.FramesForTest.EndDrag(commit: true);

            RectPt after = window.SourceForTest.GetEffectiveRect(blockId);
            Assert.True(
                Math.Abs((after.Width / after.Height) - (before.Width / before.Height)) > 0.05f,
                "a text frame should have been reshaped, not scaled");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static System.Collections.Generic.List<ImageFrame> Pictures(MainWindow window) =>
        [.. window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<ImageFrame>()];
}
