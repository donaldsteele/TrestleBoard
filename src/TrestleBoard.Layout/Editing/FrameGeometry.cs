using TrestleBoard.Core.Model;

namespace TrestleBoard.Layout.Editing;

/// <summary>The eight resize grips plus the frame body (docs/M5-spec.md §2.1).</summary>
public enum FrameHandle
{
    None,
    Body,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// Pure selection-handle geometry: where the grips are, what a point grabs, and what a drag does
/// to the rect (docs/M5-spec.md §2). No Skia, no Avalonia — the app draws from these numbers and
/// the tests assert on them directly.
///
/// Everything is expressed in page points but sized on SCREEN: callers pass
/// <c>overlayScale = 1 / zoom</c>, so a handle stays 12 screen points at every zoom (PLAN.md §6).
/// </summary>
public static class FrameGeometry
{
    /// <summary>Drawn handle square, screen points (PLAN.md §6: 12pt visual / 24pt hit).</summary>
    public const float HandleVisualPt = 12f;

    /// <summary>Handle hit square, screen points — deliberately double the drawn size.</summary>
    public const float HandleHitPt = 24f;

    /// <summary>Band straddling a text frame's border that grabs the frame instead of the caret.</summary>
    public const float EdgeBandPt = 4f;

    /// <summary>A frame may never be resized smaller than this (docs/M5-spec.md §2.4).</summary>
    public const float MinFrameSizePt = 12f;

    /// <summary>Corners first: at heavy zoom-out the hit squares overlap and a corner must win.</summary>
    public static readonly IReadOnlyList<FrameHandle> HandleOrder =
    [
        FrameHandle.TopLeft,
        FrameHandle.TopRight,
        FrameHandle.BottomRight,
        FrameHandle.BottomLeft,
        FrameHandle.Top,
        FrameHandle.Right,
        FrameHandle.Bottom,
        FrameHandle.Left,
    ];

    /// <summary>Center point of one handle on the frame's border.</summary>
    public static (float X, float Y) HandleCenter(RectPt rect, FrameHandle handle)
    {
        float midX = rect.X + (rect.Width / 2f);
        float midY = rect.Y + (rect.Height / 2f);
        return handle switch
        {
            FrameHandle.TopLeft => (rect.X, rect.Y),
            FrameHandle.Top => (midX, rect.Y),
            FrameHandle.TopRight => (rect.Right, rect.Y),
            FrameHandle.Right => (rect.Right, midY),
            FrameHandle.BottomRight => (rect.Right, rect.Bottom),
            FrameHandle.Bottom => (midX, rect.Bottom),
            FrameHandle.BottomLeft => (rect.X, rect.Bottom),
            FrameHandle.Left => (rect.X, midY),
            _ => (midX, midY),
        };
    }

    /// <summary>The drawn square for a handle, in page points at the given overlay scale.</summary>
    public static RectPt HandleVisualRect(RectPt rect, FrameHandle handle, float overlayScale) =>
        SquareAt(HandleCenter(rect, handle), HandleVisualPt * overlayScale);

    /// <summary>The (larger) hit square for a handle, in page points at the given overlay scale.</summary>
    public static RectPt HandleHitRect(RectPt rect, FrameHandle handle, float overlayScale) =>
        SquareAt(HandleCenter(rect, handle), HandleHitPt * overlayScale);

    /// <summary>
    /// Which handle a point grabs, or <see cref="FrameHandle.None"/>. Zoomed out far enough the
    /// hit squares overlap, so candidates rank corners before edge midpoints and then by distance
    /// to the grip — the grab always lands on the corner the user was aiming at.
    /// </summary>
    public static FrameHandle HitHandle(RectPt rect, float xPt, float yPt, float overlayScale)
    {
        FrameHandle best = FrameHandle.None;
        int bestRank = int.MaxValue;
        float bestDistance = float.MaxValue;
        foreach (FrameHandle handle in HandleOrder)
        {
            if (!Contains(HandleHitRect(rect, handle, overlayScale), xPt, yPt))
            {
                continue;
            }

            int rank = IsCorner(handle) ? 0 : 1;
            (float cx, float cy) = HandleCenter(rect, handle);
            float distance = ((cx - xPt) * (cx - xPt)) + ((cy - yPt) * (cy - yPt));
            if (rank < bestRank || (rank == bestRank && distance < bestDistance))
            {
                best = handle;
                bestRank = rank;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static bool IsCorner(FrameHandle handle) =>
        handle is FrameHandle.TopLeft or FrameHandle.TopRight
            or FrameHandle.BottomRight or FrameHandle.BottomLeft;

    /// <summary>
    /// True when the point sits in the band straddling the frame's border — the grab zone that
    /// moves a text frame instead of placing a caret (docs/M5-spec.md §1.1/§2.3).
    /// </summary>
    public static bool IsOnEdgeBand(RectPt rect, float xPt, float yPt, float overlayScale)
    {
        float band = EdgeBandPt * overlayScale;
        if (!Contains(Inflate(rect, band), xPt, yPt))
        {
            return false;
        }

        RectPt inner = Inflate(rect, -band);
        return inner.Width <= 0f || inner.Height <= 0f || !Contains(inner, xPt, yPt);
    }

    public static bool Contains(RectPt rect, float xPt, float yPt) =>
        xPt >= rect.X && xPt <= rect.Right && yPt >= rect.Y && yPt <= rect.Bottom;

    public static RectPt Inflate(RectPt rect, float byPt) =>
        new(rect.X - byPt, rect.Y - byPt, rect.Width + (2f * byPt), rect.Height + (2f * byPt));

    public static RectPt Translate(RectPt rect, float dxPt, float dyPt) =>
        new(rect.X + dxPt, rect.Y + dyPt, rect.Width, rect.Height);

    /// <summary>
    /// Applies a drag delta to the edges the handle owns. Edges never cross: each axis clamps at
    /// <see cref="MinFrameSizePt"/> rather than flipping the rect (docs/M5-spec.md §2.4).
    /// <see cref="FrameHandle.Body"/> translates.
    /// </summary>
    public static RectPt Resize(RectPt rect, FrameHandle handle, float dxPt, float dyPt)
    {
        if (handle is FrameHandle.Body)
        {
            return Translate(rect, dxPt, dyPt);
        }

        float left = rect.X;
        float top = rect.Y;
        float right = rect.Right;
        float bottom = rect.Bottom;

        if (MovesLeftEdge(handle))
        {
            left = Math.Min(rect.X + dxPt, right - MinFrameSizePt);
        }
        else if (MovesRightEdge(handle))
        {
            right = Math.Max(rect.Right + dxPt, left + MinFrameSizePt);
        }

        if (MovesTopEdge(handle))
        {
            top = Math.Min(rect.Y + dyPt, bottom - MinFrameSizePt);
        }
        else if (MovesBottomEdge(handle))
        {
            bottom = Math.Max(rect.Bottom + dyPt, top + MinFrameSizePt);
        }

        return new RectPt(left, top, right - left, bottom - top);
    }

    public static bool MovesLeftEdge(FrameHandle handle) =>
        handle is FrameHandle.TopLeft or FrameHandle.Left or FrameHandle.BottomLeft;

    public static bool MovesRightEdge(FrameHandle handle) =>
        handle is FrameHandle.TopRight or FrameHandle.Right or FrameHandle.BottomRight;

    public static bool MovesTopEdge(FrameHandle handle) =>
        handle is FrameHandle.TopLeft or FrameHandle.Top or FrameHandle.TopRight;

    public static bool MovesBottomEdge(FrameHandle handle) =>
        handle is FrameHandle.BottomLeft or FrameHandle.Bottom or FrameHandle.BottomRight;

    private static RectPt SquareAt((float X, float Y) center, float side) =>
        new(center.X - (side / 2f), center.Y - (side / 2f), side, side);
}
