using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>M5 handle geometry and snapping (docs/M5-spec.md §2, §3).</summary>
public sealed class FrameGeometryTests
{
    private static readonly RectPt Frame = new(100f, 200f, 180f, 120f);

    private static SnapContext Context(params RectPt[] siblings) =>
        new(new SizePt(612f, 792f), 54f, 54f, 54f, 54f, siblings);

    [Fact]
    public void HandleHitBoxIsTwiceTheVisualBoxAndScreenConstant()
    {
        RectPt visual = FrameGeometry.HandleVisualRect(Frame, FrameHandle.TopLeft, overlayScale: 1f);
        RectPt hit = FrameGeometry.HandleHitRect(Frame, FrameHandle.TopLeft, overlayScale: 1f);
        Assert.Equal(FrameGeometry.HandleVisualPt, visual.Width, 3);
        Assert.Equal(FrameGeometry.HandleHitPt, hit.Width, 3);

        // Zoomed out 2×, the same grip covers twice as many page points — identical on screen.
        RectPt zoomedOut = FrameGeometry.HandleHitRect(Frame, FrameHandle.TopLeft, overlayScale: 2f);
        Assert.Equal(FrameGeometry.HandleHitPt * 2f, zoomedOut.Width, 3);
    }

    [Fact]
    public void HandlesSitOnTheFrameBorder()
    {
        Assert.Equal((Frame.X, Frame.Y), FrameGeometry.HandleCenter(Frame, FrameHandle.TopLeft));
        Assert.Equal((Frame.Right, Frame.Bottom), FrameGeometry.HandleCenter(Frame, FrameHandle.BottomRight));
        Assert.Equal((Frame.X + 90f, Frame.Y), FrameGeometry.HandleCenter(Frame, FrameHandle.Top));
        Assert.Equal((Frame.X, Frame.Y + 60f), FrameGeometry.HandleCenter(Frame, FrameHandle.Left));
    }

    [Fact]
    public void CornerWinsWhenZoomedOutFarEnoughForHitBoxesToOverlap()
    {
        // A small frame at heavy zoom-out: the Right grip's hit box swallows both corners.
        var small = new RectPt(100f, 100f, 20f, 20f);
        FrameHandle grabbed = FrameGeometry.HitHandle(small, small.Right, small.Y + 10f, overlayScale: 4f);
        Assert.Contains(grabbed, new[] { FrameHandle.TopRight, FrameHandle.BottomRight });
    }

    [Fact]
    public void PointInsideTheBodyGrabsNoHandle() =>
        Assert.Equal(FrameHandle.None, FrameGeometry.HitHandle(Frame, 190f, 260f, overlayScale: 1f));

    [Fact]
    public void EdgeBandGrabsTheBorderButNotTheBody()
    {
        Assert.True(FrameGeometry.IsOnEdgeBand(Frame, Frame.X + 1f, 260f, overlayScale: 1f));
        Assert.True(FrameGeometry.IsOnEdgeBand(Frame, Frame.X - 3f, 260f, overlayScale: 1f));
        Assert.False(FrameGeometry.IsOnEdgeBand(Frame, 190f, 260f, overlayScale: 1f));
        Assert.False(FrameGeometry.IsOnEdgeBand(Frame, Frame.X - 20f, 260f, overlayScale: 1f));
    }

    [Fact]
    public void ResizeMovesOnlyTheEdgesTheHandleOwns()
    {
        RectPt resized = FrameGeometry.Resize(Frame, FrameHandle.TopLeft, -20f, -10f);
        Assert.Equal(Frame.X - 20f, resized.X, 3);
        Assert.Equal(Frame.Y - 10f, resized.Y, 3);
        Assert.Equal(Frame.Right, resized.Right, 3);
        Assert.Equal(Frame.Bottom, resized.Bottom, 3);

        RectPt bottomOnly = FrameGeometry.Resize(Frame, FrameHandle.Bottom, 50f, 30f);
        Assert.Equal(Frame.X, bottomOnly.X, 3);
        Assert.Equal(Frame.Right, bottomOnly.Right, 3);
        Assert.Equal(Frame.Bottom + 30f, bottomOnly.Bottom, 3);
    }

    [Fact]
    public void ResizeClampsAtMinimumSizeInsteadOfFlipping()
    {
        RectPt crushed = FrameGeometry.Resize(Frame, FrameHandle.Right, -500f, 0f);
        Assert.Equal(FrameGeometry.MinFrameSizePt, crushed.Width, 3);
        Assert.Equal(Frame.X, crushed.X, 3);

        RectPt crushedTop = FrameGeometry.Resize(Frame, FrameHandle.Top, 0f, 500f);
        Assert.Equal(FrameGeometry.MinFrameSizePt, crushedTop.Height, 3);
        Assert.Equal(Frame.Bottom, crushedTop.Bottom, 3);
    }

    [Fact]
    public void BodyHandleTranslates()
    {
        RectPt moved = FrameGeometry.Resize(Frame, FrameHandle.Body, 12f, -7f);
        Assert.Equal(new RectPt(112f, 193f, 180f, 120f), moved);
    }

    // ---- Snapping ---------------------------------------------------------------------------

    [Fact]
    public void MoveSnapsTheNearestEdgeToThePageMargin()
    {
        var dragged = new RectPt(58f, 300f, 180f, 120f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Body, Context());

        Assert.Equal(54f, result.Rect.X, 3);
        Assert.Equal(300f, result.Rect.Y, 3);
        Assert.Equal(dragged.Width, result.Rect.Width, 3);
        SnapGuide guide = Assert.Single(result.Guides);
        Assert.Equal(SnapAxis.X, guide.Axis);
        Assert.Equal(54f, guide.PositionPt, 3);
    }

    [Fact]
    public void NothingSnapsBeyondTheThreshold()
    {
        var dragged = new RectPt(80f, 300f, 180f, 120f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Body, Context());
        Assert.Equal(dragged, result.Rect);
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void SiblingEdgesAreSnapTargets()
    {
        var sibling = new RectPt(300f, 400f, 100f, 80f);
        var dragged = new RectPt(297f, 600f, 60f, 40f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Body, Context(sibling));
        Assert.Equal(300f, result.Rect.X, 3);
    }

    [Fact]
    public void MarginBeatsASiblingEdgeAtEqualDistance()
    {
        // Sibling left edge at 50, margin at 54; a frame at 52 is 2pt from both.
        var sibling = new RectPt(50f, 400f, 100f, 80f);
        var dragged = new RectPt(52f, 600f, 60f, 40f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Body, Context(sibling));
        Assert.Equal(54f, result.Rect.X, 3);
    }

    [Fact]
    public void ResizeSnapsOnlyTheEdgeBeingDragged()
    {
        // Right edge lands 2pt shy of the right margin (612-54 = 558); left edge must not move.
        var dragged = new RectPt(100f, 300f, 456f, 120f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Right, Context());
        Assert.Equal(100f, result.Rect.X, 3);
        Assert.Equal(558f, result.Rect.Right, 3);
    }

    [Fact]
    public void ZeroThresholdDisablesSnapping()
    {
        var dragged = new RectPt(55f, 300f, 180f, 120f);
        SnapResult result = SnapEngine.Snap(dragged, FrameHandle.Body, Context(), thresholdPt: 0f);
        Assert.Equal(dragged, result.Rect);
        Assert.Empty(result.Guides);
    }

    [Fact]
    public void ThresholdIsScreenConstantSoZoomedOutDragsSnapFromFurtherAway()
    {
        var dragged = new RectPt(64f, 300f, 180f, 120f);
        Assert.Equal(dragged, SnapEngine.Snap(dragged, FrameHandle.Body, Context()).Rect);

        // Zoomed out 2× the same 6-screen-point radius covers 12 page points.
        SnapResult zoomedOut = SnapEngine.Snap(
            dragged, FrameHandle.Body, Context(), SnapEngine.DefaultThresholdPt * 2f);
        Assert.Equal(54f, zoomedOut.Rect.X, 3);
    }
}
