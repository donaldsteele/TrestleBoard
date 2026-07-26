using TrestleBoard.Core.Model;

namespace TrestleBoard.Layout.Editing;

public enum SnapAxis
{
    X,
    Y,
}

/// <summary>A line a dragged frame can stick to. Lower <see cref="Priority"/> wins ties.</summary>
public sealed record SnapTarget(SnapAxis Axis, float PositionPt, int Priority);

/// <summary>A snap that actually fired — the overlay draws it across the page.</summary>
public sealed record SnapGuide(SnapAxis Axis, float PositionPt, float FromPt, float ToPt);

/// <summary>Everything on the page a drag can snap to (docs/M5-spec.md §3.1).</summary>
public sealed record SnapContext(
    SizePt PageSize,
    float MarginLeftPt,
    float MarginTopPt,
    float MarginRightPt,
    float MarginBottomPt,
    IReadOnlyList<RectPt> SiblingRects);

public sealed record SnapResult(RectPt Rect, IReadOnlyList<SnapGuide> Guides);

/// <summary>
/// Snapping for move and resize drags (docs/M5-spec.md §3). X and Y resolve independently; per
/// axis the smallest delta wins and a margin beats a sibling edge on a tie. Callers pass a
/// screen-constant threshold (<c>6 / zoom</c>) and skip this entirely for keyboard nudge, which
/// must move exactly the distance asked for.
/// </summary>
public static class SnapEngine
{
    /// <summary>Snap radius in SCREEN points; callers divide by zoom.</summary>
    public const float DefaultThresholdPt = 6f;

    public static IReadOnlyList<SnapTarget> BuildTargets(SnapContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var targets = new List<SnapTarget>
        {
            // Priority 0 — the page's own text area. This is where content belongs.
            new(SnapAxis.X, context.MarginLeftPt, 0),
            new(SnapAxis.X, context.PageSize.Width - context.MarginRightPt, 0),
            new(SnapAxis.Y, context.MarginTopPt, 0),
            new(SnapAxis.Y, context.PageSize.Height - context.MarginBottomPt, 0),

            // Priority 1 — trim edges and page centers.
            new(SnapAxis.X, 0f, 1),
            new(SnapAxis.X, context.PageSize.Width, 1),
            new(SnapAxis.X, context.PageSize.Width / 2f, 1),
            new(SnapAxis.Y, 0f, 1),
            new(SnapAxis.Y, context.PageSize.Height, 1),
            new(SnapAxis.Y, context.PageSize.Height / 2f, 1),
        };

        // Priority 2 — the other blocks on this page: edges and centers.
        foreach (RectPt sibling in context.SiblingRects)
        {
            targets.Add(new SnapTarget(SnapAxis.X, sibling.X, 2));
            targets.Add(new SnapTarget(SnapAxis.X, sibling.X + (sibling.Width / 2f), 2));
            targets.Add(new SnapTarget(SnapAxis.X, sibling.Right, 2));
            targets.Add(new SnapTarget(SnapAxis.Y, sibling.Y, 2));
            targets.Add(new SnapTarget(SnapAxis.Y, sibling.Y + (sibling.Height / 2f), 2));
            targets.Add(new SnapTarget(SnapAxis.Y, sibling.Bottom, 2));
        }

        return targets;
    }

    /// <summary>
    /// Nudges <paramref name="rect"/> onto the nearest targets. A move drag offers the frame's
    /// left/center/right (and top/middle/bottom); a resize drag offers only the edges the handle
    /// actually moves, so the opposite edge never wanders.
    /// </summary>
    public static SnapResult Snap(
        RectPt rect,
        FrameHandle handle,
        SnapContext context,
        float thresholdPt = DefaultThresholdPt)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (thresholdPt <= 0f)
        {
            return new SnapResult(rect, []);
        }

        IReadOnlyList<SnapTarget> targets = BuildTargets(context);
        (float? dx, SnapTarget? xTarget) = BestDelta(SnapAxis.X, MovingEdges(rect, handle, SnapAxis.X), targets, thresholdPt);
        (float? dy, SnapTarget? yTarget) = BestDelta(SnapAxis.Y, MovingEdges(rect, handle, SnapAxis.Y), targets, thresholdPt);

        RectPt snapped = handle is FrameHandle.Body or FrameHandle.None
            ? FrameGeometry.Translate(rect, dx ?? 0f, dy ?? 0f)
            : FrameGeometry.Resize(rect, handle, dx ?? 0f, dy ?? 0f);

        var guides = new List<SnapGuide>(2);
        if (xTarget is not null)
        {
            guides.Add(new SnapGuide(SnapAxis.X, xTarget.PositionPt, 0f, context.PageSize.Height));
        }

        if (yTarget is not null)
        {
            guides.Add(new SnapGuide(SnapAxis.Y, yTarget.PositionPt, 0f, context.PageSize.Width));
        }

        return new SnapResult(snapped, guides);
    }

    private static List<float> MovingEdges(RectPt rect, FrameHandle handle, SnapAxis axis)
    {
        if (handle is FrameHandle.Body or FrameHandle.None)
        {
            return axis == SnapAxis.X
                ? [rect.X, rect.X + (rect.Width / 2f), rect.Right]
                : [rect.Y, rect.Y + (rect.Height / 2f), rect.Bottom];
        }

        var edges = new List<float>(1);
        if (axis == SnapAxis.X)
        {
            if (FrameGeometry.MovesLeftEdge(handle))
            {
                edges.Add(rect.X);
            }
            else if (FrameGeometry.MovesRightEdge(handle))
            {
                edges.Add(rect.Right);
            }
        }
        else
        {
            if (FrameGeometry.MovesTopEdge(handle))
            {
                edges.Add(rect.Y);
            }
            else if (FrameGeometry.MovesBottomEdge(handle))
            {
                edges.Add(rect.Bottom);
            }
        }

        return edges;
    }

    private static (float? Delta, SnapTarget? Target) BestDelta(
        SnapAxis axis,
        IReadOnlyList<float> edges,
        IReadOnlyList<SnapTarget> targets,
        float thresholdPt)
    {
        float? bestDelta = null;
        SnapTarget? best = null;
        foreach (SnapTarget target in targets)
        {
            if (target.Axis != axis)
            {
                continue;
            }

            foreach (float edge in edges)
            {
                float delta = target.PositionPt - edge;
                if (Math.Abs(delta) > thresholdPt)
                {
                    continue;
                }

                bool better = best is null
                    || Math.Abs(delta) < Math.Abs(bestDelta!.Value) - 0.0001f
                    || (Math.Abs(Math.Abs(delta) - Math.Abs(bestDelta!.Value)) <= 0.0001f
                        && target.Priority < best.Priority);
                if (better)
                {
                    bestDelta = delta;
                    best = target;
                }
            }
        }

        return (bestDelta, best);
    }
}
