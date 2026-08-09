using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Editing;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// Dragging a picture's corner scales it instead of reshaping it (M69, reported 2026-08-09).
///
/// <para><b>The bug.</b> A corner drag moved both edges independently, so the frame's aspect
/// changed — and reshaping a picture frame does not reshape the picture. <c>ImageFit.Cover</c>
/// crops the source to the frame's new aspect, so making an emblem bigger by its corner cut the
/// bottom off the square and compasses. M22's "Position the picture" could only pan the crop
/// window, never widen it, so there was no easy way back.</para>
/// </summary>
public sealed class ResizeKeepingAspectTests
{
    private static readonly RectPt Square = new(100, 100, 200, 200);
    private static readonly RectPt Wide = new(100, 100, 300, 100);   // 3:1

    [Theory]
    [InlineData(FrameHandle.BottomRight)]
    [InlineData(FrameHandle.TopLeft)]
    [InlineData(FrameHandle.TopRight)]
    [InlineData(FrameHandle.BottomLeft)]
    public void EveryCornerKeepsTheShape(FrameHandle handle)
    {
        RectPt after = FrameGeometry.ResizeKeepingAspect(Wide, handle, 60, -25, aspect: 3f);

        Assert.Equal(3f, after.Width / after.Height, 3);
    }

    /// <summary>The corner opposite the hand stays put, so the frame grows away from the pointer.</summary>
    [Theory]
    [InlineData(FrameHandle.BottomRight, 100f, 100f)]   // top-left anchored
    [InlineData(FrameHandle.TopLeft, 300f, 300f)]       // bottom-right anchored
    [InlineData(FrameHandle.TopRight, 100f, 300f)]      // bottom-left anchored
    [InlineData(FrameHandle.BottomLeft, 300f, 100f)]    // top-right anchored
    public void TheOppositeCornerStaysPut(FrameHandle handle, float anchorX, float anchorY)
    {
        RectPt after = FrameGeometry.ResizeKeepingAspect(Square, handle, 40, 40, aspect: 1f);

        bool xHeld = Math.Abs(after.X - anchorX) < 0.01f || Math.Abs(after.Right - anchorX) < 0.01f;
        bool yHeld = Math.Abs(after.Y - anchorY) < 0.01f || Math.Abs(after.Bottom - anchorY) < 0.01f;
        Assert.True(xHeld && yHeld, $"anchor ({anchorX},{anchorY}) moved: {after}");
    }

    /// <summary>
    /// A diagonal drag follows whichever edge the pointer moved further, so the frame tracks the
    /// hand rather than snapping to whichever axis the code happens to read first.
    /// </summary>
    [Fact]
    public void ADiagonalDragFollowsTheLongerPull()
    {
        RectPt mostlySideways = FrameGeometry.ResizeKeepingAspect(Square, FrameHandle.BottomRight, 100, 10, 1f);
        RectPt mostlyDown = FrameGeometry.ResizeKeepingAspect(Square, FrameHandle.BottomRight, 10, 100, 1f);

        Assert.Equal(300f, mostlySideways.Width, 1);
        Assert.Equal(300f, mostlyDown.Height, 1);
    }

    [Fact]
    public void ShrinkingKeepsTheShapeToo()
    {
        RectPt after = FrameGeometry.ResizeKeepingAspect(Wide, FrameHandle.BottomRight, -150, 0, 3f);

        Assert.Equal(3f, after.Width / after.Height, 3);
        Assert.True(after.Width < Wide.Width);
    }

    /// <summary>A frame cannot be dragged below the minimum, and it does not go inside out doing it.</summary>
    [Fact]
    public void ItStopsAtTheMinimumRatherThanFlipping()
    {
        RectPt after = FrameGeometry.ResizeKeepingAspect(Square, FrameHandle.BottomRight, -10_000, -10_000, 1f);

        Assert.True(after.Width > 0 && after.Height > 0);
        Assert.True(after.Width >= FrameGeometry.MinFrameSizePt - 0.01f);
        Assert.Equal(1f, after.Width / after.Height, 3);
    }

    /// <summary>
    /// **The side handles are deliberately untouched.** Reshaping the frame is how somebody chooses
    /// a different crop, and taking that away would remove the only direct way to do it. A corner
    /// scales; an edge reshapes.
    /// </summary>
    [Theory]
    [InlineData(FrameHandle.Right)]
    [InlineData(FrameHandle.Bottom)]
    [InlineData(FrameHandle.Left)]
    [InlineData(FrameHandle.Top)]
    public void AnEdgeStillReshapes(FrameHandle handle)
    {
        RectPt keeping = FrameGeometry.ResizeKeepingAspect(Square, handle, 60, 60, aspect: 1f);
        RectPt plain = FrameGeometry.Resize(Square, handle, 60, 60);

        Assert.Equal(plain.Width, keeping.Width, 3);
        Assert.Equal(plain.Height, keeping.Height, 3);
    }

    /// <summary>A degenerate aspect falls back rather than dividing by zero.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-2f)]
    public void ANonsenseAspectFallsBackToTheOrdinaryResize(float aspect)
    {
        RectPt keeping = FrameGeometry.ResizeKeepingAspect(Square, FrameHandle.BottomRight, 30, 10, aspect);
        RectPt plain = FrameGeometry.Resize(Square, FrameHandle.BottomRight, 30, 10);

        Assert.Equal(plain.Width, keeping.Width, 3);
        Assert.Equal(plain.Height, keeping.Height, 3);
    }
}
