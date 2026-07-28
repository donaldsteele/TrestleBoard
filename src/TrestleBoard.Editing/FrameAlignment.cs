using TrestleBoard.Core.Model;

namespace TrestleBoard.Editing;

/// <summary>Which edge (or middle) of the chosen frames is being lined up (PLAN.md §11 M21).</summary>
public enum FrameAlignmentKind
{
    Left,

    /// <summary>Centres agree side to side; the frames still sit at their own heights.</summary>
    CentreX,

    Right,

    Top,

    /// <summary>Middles agree top to bottom.</summary>
    MiddleY,

    Bottom,
}

/// <summary>
/// The arithmetic behind "line up" and "space out evenly" (PLAN.md §11 M21). Pure: rectangles in,
/// rectangles out, no document, no session, no undo. <see cref="FrameEditorController"/> turns the
/// answer into one <c>CompositeCommand</c>, which is what makes aligning three frames one undo step.
///
/// <para><b>Everything is measured against the chosen frames' own bounding box</b>, never against
/// the page and never against one "key" frame. Aligning left therefore moves every frame to the
/// leftmost edge already in the selection, so nothing ever jumps to a margin the user was not
/// looking at, and aligning twice changes nothing the second time.</para>
///
/// <para>Nothing here resizes: a frame that is lined up is moved, so the words inside it keep the
/// width they were laid out for. That is why every command this produces is a
/// <c>MoveBlockCommand</c>.</para>
/// </summary>
public static class FrameAlignment
{
    /// <summary>
    /// The lined-up rectangles, in the same order they were given. A frame already in the right
    /// place comes back unchanged, so the caller can drop the no-op moves.
    /// </summary>
    public static IReadOnlyList<RectPt> Align(IReadOnlyList<RectPt> rects, FrameAlignmentKind kind)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (rects.Count == 0)
        {
            return [];
        }

        float left = rects.Min(r => r.X);
        float right = rects.Max(r => r.X + r.Width);
        float top = rects.Min(r => r.Y);
        float bottom = rects.Max(r => r.Y + r.Height);
        float centreX = (left + right) / 2f;
        float middleY = (top + bottom) / 2f;

        var moved = new List<RectPt>(rects.Count);
        foreach (RectPt rect in rects)
        {
            moved.Add(kind switch
            {
                FrameAlignmentKind.Left => rect with { X = left },
                FrameAlignmentKind.CentreX => rect with { X = centreX - (rect.Width / 2f) },
                FrameAlignmentKind.Right => rect with { X = right - rect.Width },
                FrameAlignmentKind.Top => rect with { Y = top },
                FrameAlignmentKind.MiddleY => rect with { Y = middleY - (rect.Height / 2f) },
                FrameAlignmentKind.Bottom => rect with { Y = bottom - rect.Height },
                _ => rect,
            });
        }

        return moved;
    }

    /// <summary>
    /// Equal GAPS between neighbours, not equal centres: with frames of different sizes, equal
    /// centres leaves gaps that visibly are not equal, which is the thing the user was asking for.
    /// The two outermost frames do not move — they are the ends of the run being shared out — so
    /// three frames or more are needed for this to mean anything.
    /// </summary>
    public static IReadOnlyList<RectPt> Distribute(IReadOnlyList<RectPt> rects, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(rects);
        if (rects.Count < 3)
        {
            return [.. rects];
        }

        // Sorted for the arithmetic, then put back in the caller's order: the answer must line up
        // with the block ids it was asked about.
        List<int> order = [.. Enumerable.Range(0, rects.Count)];
        order.Sort((a, b) => horizontal
            ? rects[a].X.CompareTo(rects[b].X)
            : rects[a].Y.CompareTo(rects[b].Y));

        float start = horizontal ? rects[order[0]].X : rects[order[0]].Y;
        RectPt last = rects[order[^1]];
        float end = horizontal ? last.X + last.Width : last.Y + last.Height;
        float occupied = order.Sum(i => horizontal ? rects[i].Width : rects[i].Height);
        float gap = (end - start - occupied) / (rects.Count - 1);

        var result = new RectPt[rects.Count];
        float cursor = start;
        foreach (int index in order)
        {
            RectPt rect = rects[index];
            result[index] = horizontal ? rect with { X = cursor } : rect with { Y = cursor };
            cursor += (horizontal ? rect.Width : rect.Height) + gap;
        }

        return result;
    }

    /// <summary>What the undo menu says afterwards — plain language, never a class name.</summary>
    public static string Describe(FrameAlignmentKind kind) => kind switch
    {
        FrameAlignmentKind.Left => "Line up the left edges",
        FrameAlignmentKind.CentreX => "Line up the centres",
        FrameAlignmentKind.Right => "Line up the right edges",
        FrameAlignmentKind.Top => "Line up the top edges",
        FrameAlignmentKind.MiddleY => "Line up the middles",
        _ => "Line up the bottom edges",
    };
}
