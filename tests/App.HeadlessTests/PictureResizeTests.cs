using System;
using System.Linq;
using System.Threading.Tasks;
using TrestleBoard.Core.Model;
using TrestleBoard.Editing.Actions;
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
///
/// <para><b>Rewritten for M72, and only half of it survives as written.</b> The three emblem tests
/// here were about a picture frame, because an emblem was one; it is a <c>VectorBlock</c> now. One
/// of the two mechanisms behind M69's bug has therefore gone away entirely — a drawing is scaled to
/// fit its frame and cannot be cropped by it, so <c>AnEmblemIsNeverCroppedToItsFrame</c> is no
/// longer a claim about <c>ImageFit</c> but about geometry, and it is asserted by rendering rather
/// than by reading a property that no longer exists.</para>
///
/// <para>The other mechanism is <b>deliberately kept</b>: the corner aspect-lock still applies to a
/// drawing. It no longer has to — nothing gets cropped either way — but the gesture is the point.
/// A square and compasses squashed out of shape by a stray corner drag is a mutilated symbol, and
/// M72 grants the lock to drawings on purpose rather than letting it lapse with the mechanism that
/// first motivated it. A photograph keeps everything it had.</para>
/// </summary>
public sealed class PictureResizeTests
{
    /// <summary>
    /// The reported case, still driven by the emblem it was reported with: drag a corner and the
    /// frame keeps its shape. M72 grants the aspect-lock to a drawing deliberately (see the class
    /// comment), so this is the test that would notice if the new block type had quietly been left
    /// out of the rule.
    /// </summary>
    [Fact]
    public async Task DraggingADrawingCornerKeepsItsShape()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "square-and-compasses";
                await window.InsertEmblemAsync();

                VectorBlock picture = Drawings(window).Last();
                RectPt before = picture.FrameRect;
                float aspectBefore = before.Width / before.Height;

                window.FramesForTest!.Select(picture.Id);
                window.FramesForTest.TryBeginDrag(before.Right - 1f, before.Bottom - 1f, 1f);

                // A deliberately lopsided pull: far more sideways than down. Before the fix this
                // widened the frame alone and the emblem lost its top and bottom.
                window.FramesForTest.DragTo(before.Right + 120f, before.Bottom + 8f, snap: false);
                window.FramesForTest.EndDrag(commit: true);

                RectPt after = Drawings(window).Last().FrameRect;
                Assert.Equal(aspectBefore, after.Width / after.Height, 2);
                Assert.True(after.Width > before.Width, "the drag did not resize anything");

                window.Close();
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An emblem is a whole thing. Even reshaped by a side handle — which still reshapes, on
    /// purpose — it shows all of itself rather than being cropped to the frame.
    ///
    /// <para>M65 answered this with <c>ImageFit.Contain</c> and this test read that property back.
    /// A drawing has no fit mode and no crop: it is scaled to fit and centred, and there is no
    /// property by which it could be cropped, so reading one back would be asserting nothing. The
    /// claim is made in the two places it can now be made honestly — here, that widening the frame
    /// leaves the drawing's own shape untouched and that the app refuses to offer cropping it at
    /// all with a reason; and in <c>Rendering.SnapshotTests/VectorArtTests</c>, which looks at the
    /// pixels of a drawing in a frame far wider than itself.</para>
    /// </summary>
    [Fact]
    public async Task ADrawingKeepsItsOwnShapeAndIsNeverOfferedACrop()
    {
        await HeadlessSession.DispatchAsync(
            async () =>
            {
                var window = new MainWindow();
                window.OpenSample();

                window.EmblemAnswerForTest = "square-and-compasses-g";
                await window.InsertEmblemAsync();

                VectorBlock drawing = Drawings(window).Last();
                RectPt frame = drawing.FrameRect;
                double shape = drawing.AspectRatio;

                window.FramesForTest!.Select(drawing.Id);
                window.FramesForTest.TryBeginDrag(frame.Right - 1f, frame.Y + (frame.Height / 2f), 1f);
                window.FramesForTest.DragTo(
                    frame.Right + frame.Width, frame.Y + (frame.Height / 2f), snap: false);
                window.FramesForTest.EndDrag(commit: true);

                VectorBlock after = Drawings(window).Last();
                Assert.True(after.FrameRect.Width > frame.Width * 1.5f, "the frame was not widened");
                Assert.Equal(shape, after.AspectRatio, 6);

                // And the commands that would crop or reframe it are refused by name, each with a
                // sentence — M11 refuses an empty reason at construction, so this is checking that
                // somebody decided, not that something happened to be off.
                ActionContext context = window.CurrentActionContext;
                Assert.Equal(SelectionKind.Drawing, context.Selection);
                foreach (string id in new[]
                    { ActionId.FixPhoto, ActionId.AdjustPhoto, ActionId.PositionPicture, ActionId.ReplacePicture })
                {
                    ActionAvailability refusal = ActionCatalog.Evaluate(id, context);
                    Assert.False(refusal.IsAvailable, $"{id} should not be offered on a drawing");
                    Assert.False(string.IsNullOrWhiteSpace(refusal.Reason));
                }

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
    /// A side handle still reshapes a frame — shown here on a drawing, where it is the only handle
    /// that changes the frame's shape at all. On a photograph it is the only direct way to crop by
    /// hand, and losing it there would be the worse loss; that is held by the fit-mode test above.
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

                VectorBlock picture = Drawings(window).Last();
                RectPt before = picture.FrameRect;

                window.FramesForTest!.Select(picture.Id);
                window.FramesForTest.TryBeginDrag(before.Right - 1f, before.Y + (before.Height / 2f), 1f);
                window.FramesForTest.DragTo(before.Right + 100f, before.Y + (before.Height / 2f), snap: false);
                window.FramesForTest.EndDrag(commit: true);

                RectPt after = Drawings(window).Last().FrameRect;
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

    private static System.Collections.Generic.List<VectorBlock> Drawings(MainWindow window) =>
        [.. window.PackageForTest!.Document.Pages.SelectMany(p => p.Blocks).OfType<VectorBlock>()];
}
