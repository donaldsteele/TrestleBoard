using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Documents;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Editing;

/// <summary>Result of a page-point hit test. IsInsideContent is false when the point was
/// clamped from a gap, a margin, or an exclusion band (docs/M4-spec.md §1).</summary>
public readonly record struct TextHit(
    string StoryId,
    int FrameIndex,
    int LineIndex,
    int SegmentIndex,
    CaretPosition Caret,
    bool IsInsideContent);

/// <summary>Caret rectangle for rendering: x plus the ascent+descent box of its line
/// (docs/M4-spec.md §2.3 — the caret matches the glyphs, not the leading).</summary>
public readonly record struct CaretGeometry(
    int FrameIndex,
    string? PageId,
    string? BlockId,
    float XPt,
    float TopPt,
    float BaselineYPt,
    float HeightPt,
    int LineIndex,
    int SegmentIndex)
{
    public float BottomPt => TopPt + HeightPt;
}

/// <summary>One selection band on one line segment, in page points.</summary>
public readonly record struct SelectionRect(
    int FrameIndex,
    string? PageId,
    string? BlockId,
    float LeftPt,
    float TopPt,
    float RightPt,
    float BottomPt);

/// <summary>Sticky x for Up/Down, stored relative to the frame's left edge so the goal
/// survives a jump into a linked frame on another page (docs/M4-spec.md §4.2).</summary>
public readonly record struct CaretXGoal(float OffsetFromFrameLeftPt);

public sealed record SelectionGeometryOptions
{
    /// <summary>Visible width given to a selected empty paragraph / empty line.</summary>
    public float EmptyLineWidthPt { get; init; } = 3f;
}

/// <summary>
/// The M4 geometry service (docs/M4-spec.md §1–§4): page point → text position, caret
/// position → caret rectangle, range → selection rects, and segment-aware vertical motion.
/// Built once per relayout over a story chain's LayoutResult; all tables are precomputed.
/// </summary>
public sealed class StoryTextGeometry
{
    private readonly LayoutResult _layout;
    private readonly IReadOnlyList<FramePlacement>? _placements;
    private readonly List<FrameRect> _frameRects;
    private readonly string[] _paragraphTexts;
    private readonly List<VisualLine> _visualLines = [];

    public StoryTextGeometry(StoryLayoutPlan plan, LayoutResult layout)
        : this(plan is null ? throw new ArgumentNullException(nameof(plan)) : plan.Request, layout, plan.Placements)
    {
    }

    public StoryTextGeometry(
        LayoutRequest request,
        LayoutResult layout,
        IReadOnlyList<FramePlacement>? placements = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _placements = placements;
        _frameRects = request.Frames.Select(f => f.Rect).ToList();
        StoryId = request.Story.StoryId;
        _paragraphTexts = request.Story.Paragraphs
            .Select(p => string.Concat(p.Runs.Select(r => r.Text)))
            .ToArray();

        foreach (FrameLayout frame in layout.Frames)
        {
            for (int lineIdx = 0; lineIdx < frame.Lines.Count; lineIdx++)
            {
                _visualLines.Add(new VisualLine(frame.FrameIndex, lineIdx, frame.Lines[lineIdx]));
            }
        }
    }

    public string StoryId { get; }

    public int FrameCount => _frameRects.Count;

    public int VisualLineCount => _visualLines.Count;

    // ---- Hit testing (docs/M4-spec.md §1.4) -------------------------------------------------

    public bool TryHitTest(int frameIndex, float xPt, float yPt, out TextHit hit)
    {
        hit = default;
        List<int> frameLineIndices = [];
        for (int i = 0; i < _visualLines.Count; i++)
        {
            if (_visualLines[i].FrameIndex == frameIndex)
            {
                frameLineIndices.Add(i);
            }
        }

        if (frameLineIndices.Count == 0)
        {
            // Frame with no lines: the story position the frame would have started at.
            (int para, int offset) = FirstPositionOfFrame(frameIndex);
            hit = new TextHit(
                StoryId, frameIndex, LineIndex: -1, SegmentIndex: -1,
                CaretPosition.Leading(new TextPosition(StoryId, para, offset)),
                IsInsideContent: false);
            return true;
        }

        bool insideBand = false;
        int chosen = frameLineIndices[0];
        LineBox first = _visualLines[frameLineIndices[0]].Line;
        LineBox last = _visualLines[frameLineIndices[^1]].Line;
        if (yPt < first.BandTop)
        {
            chosen = frameLineIndices[0];
        }
        else if (yPt >= last.BandBottom)
        {
            chosen = frameLineIndices[^1];
        }
        else
        {
            float bestDistance = float.MaxValue;
            foreach (int i in frameLineIndices)
            {
                LineBox line = _visualLines[i].Line;
                if (yPt >= line.BandTop && yPt < line.BandBottom)
                {
                    chosen = i;
                    insideBand = true;
                    break;
                }

                float distance = yPt < line.BandTop ? line.BandTop - yPt : yPt - line.BandBottom;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    chosen = i;
                }
            }
        }

        hit = ResolveHorizontal(chosen, xPt, insideBand);
        return true;
    }

    private TextHit ResolveHorizontal(int visualLineIndex, float xPt, bool insideBand)
    {
        VisualLine line = _visualLines[visualLineIndex];
        IReadOnlyList<LineSegment> segments = line.Line.Segments;

        int segmentIndex = -1;
        bool insideSegment = false;
        if (xPt < segments[0].XRange.Left)
        {
            segmentIndex = 0;
        }
        else if (xPt > segments[^1].XRange.Right)
        {
            segmentIndex = segments.Count - 1;
        }
        else
        {
            float bestDistance = float.MaxValue;
            for (int s = 0; s < segments.Count; s++)
            {
                FloatInterval range = segments[s].XRange;
                if (xPt >= range.Left && xPt <= range.Right)
                {
                    segmentIndex = s;
                    insideSegment = true;
                    break;
                }

                float distance = xPt < range.Left ? range.Left - xPt : xPt - range.Right;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    segmentIndex = s;
                }
            }
        }

        LineSegment segment = segments[segmentIndex];
        (int start, int end) = SegmentSpan(line.Line, segment);
        int offset;
        TextAffinity affinity;
        if (xPt < segment.XRange.Left && !insideSegment && segmentIndex == 0)
        {
            offset = start;
            affinity = TextAffinity.Leading;
        }
        else if (xPt > segment.XRange.Right && !insideSegment && segmentIndex == segments.Count - 1)
        {
            offset = end;
            affinity = TextAffinity.Trailing;
        }
        else if (!insideSegment)
        {
            // Between segments (inside an exclusion): nearest edge; tie already broke left.
            bool leftOfSegment = xPt < segment.XRange.Left;
            offset = leftOfSegment ? start : end;
            affinity = leftOfSegment ? TextAffinity.Leading : TextAffinity.Trailing;
        }
        else
        {
            offset = XToOffset(line.Line, segment, xPt, out affinity);
        }

        string text = _paragraphTexts[line.Line.ParagraphIndex];
        offset = StoryNavigator.SnapToGrapheme(text, offset);
        return new TextHit(
            StoryId,
            line.FrameIndex,
            line.LineIndexInFrame,
            segmentIndex,
            new CaretPosition(new TextPosition(StoryId, line.Line.ParagraphIndex, offset), affinity),
            insideBand && insideSegment);
    }

    // ---- Caret geometry (docs/M4-spec.md §2.3) ----------------------------------------------

    public bool TryGetCaretGeometry(CaretPosition caret, out CaretGeometry geometry)
    {
        geometry = default;
        if (!TryLocateVisualLine(caret, out int visualLineIndex))
        {
            return false;
        }

        VisualLine line = _visualLines[visualLineIndex];
        int segmentIndex = LocateSegment(line.Line, caret);
        LineSegment segment = line.Line.Segments[segmentIndex];
        float x = XForOffset(line.Line, segment, caret.Offset);
        // Caret clamp rule (spec §2.3 step 5): trailing whitespace / force-placed over-wide
        // tokens may extend past XRange into the inflated exclusion; the caret must not.
        x = Math.Clamp(x, segment.XRange.Left, segment.XRange.Right);

        (string? pageId, string? blockId) = Placement(line.FrameIndex);
        geometry = new CaretGeometry(
            line.FrameIndex,
            pageId,
            blockId,
            x,
            line.Line.BandTop,
            line.Line.BaselineY,
            line.Line.MaxAscentPt + line.Line.MaxDescentPt,
            line.LineIndexInFrame,
            segmentIndex);
        return true;
    }

    public bool TryGetVisualLineIndex(CaretPosition caret, out int visualLineIndex) =>
        TryLocateVisualLine(caret, out visualLineIndex);

    /// <summary>Start/end positions of the visual line containing the caret (Home/End).</summary>
    public bool TryGetLineBounds(CaretPosition caret, out CaretPosition lineStart, out CaretPosition lineEnd)
    {
        lineStart = default;
        lineEnd = default;
        if (!TryLocateVisualLine(caret, out int index))
        {
            return false;
        }

        LineBox line = _visualLines[index].Line;
        lineStart = CaretPosition.Leading(new TextPosition(StoryId, line.ParagraphIndex, line.Source.StartChar));
        lineEnd = CaretPosition.Trailing(new TextPosition(StoryId, line.ParagraphIndex, line.Source.EndChar));
        return true;
    }

    // ---- Vertical motion (docs/M4-spec.md §4.2) ---------------------------------------------

    public bool TryMoveVertical(
        CaretPosition from,
        int deltaLines,
        CaretXGoal? goal,
        out CaretPosition to,
        out CaretXGoal newGoal)
    {
        to = default;
        newGoal = default;
        if (!TryLocateVisualLine(from, out int index) || !TryGetCaretGeometry(from, out CaretGeometry geometry))
        {
            return false;
        }

        float goalX = goal?.OffsetFromFrameLeftPt ?? (geometry.XPt - _frameRects[geometry.FrameIndex].Left);
        newGoal = new CaretXGoal(goalX);

        int target = index + deltaLines;
        if (target < 0)
        {
            to = CaretPosition.Leading(new TextPosition(StoryId, 0, 0));
            return true;
        }

        if (target >= _visualLines.Count)
        {
            int lastParagraph = _paragraphTexts.Length - 1;
            to = CaretPosition.Trailing(new TextPosition(StoryId, lastParagraph, _paragraphTexts[lastParagraph].Length));
            return true;
        }

        VisualLine targetLine = _visualLines[target];
        float targetX = _frameRects[targetLine.FrameIndex].Left + goalX;
        TextHit hit = ResolveHorizontal(target, targetX, insideBand: true);
        to = hit.Caret;
        return true;
    }

    // ---- Selection geometry (docs/M4-spec.md §3.1) ------------------------------------------

    public IReadOnlyList<SelectionRect> GetSelectionRects(TextRange range, SelectionGeometryOptions? options = null)
    {
        options ??= new SelectionGeometryOptions();
        var rects = new List<SelectionRect>();
        if (range.IsEmpty)
        {
            return rects;
        }

        foreach (VisualLine line in _visualLines)
        {
            int p = line.Line.ParagraphIndex;
            if (p < range.Start.ParagraphIndex || p > range.End.ParagraphIndex)
            {
                continue;
            }

            int lineRangeStart = p == range.Start.ParagraphIndex ? range.Start.Offset : 0;
            int lineRangeEnd = p == range.End.ParagraphIndex ? range.End.Offset : int.MaxValue;
            bool interiorLine = p < range.End.ParagraphIndex
                || (p > range.Start.ParagraphIndex && p < range.End.ParagraphIndex);

            foreach (LineSegment segment in line.Line.Segments)
            {
                (int segStart, int segEnd) = SegmentSpan(line.Line, segment);
                int s = Math.Max(lineRangeStart, segStart);
                int e = Math.Min(lineRangeEnd, segEnd);
                if (e < s)
                {
                    continue;
                }

                float left = Math.Clamp(XForOffset(line.Line, segment, s), segment.XRange.Left, segment.XRange.Right);
                float right = Math.Clamp(XForOffset(line.Line, segment, e), segment.XRange.Left, segment.XRange.Right);
                bool paragraphBreakSelected = p < range.End.ParagraphIndex && segEnd == LineEndOfParagraph(line.Line);
                if (right - left < options.EmptyLineWidthPt && (interiorLine || paragraphBreakSelected || segStart == segEnd))
                {
                    right = Math.Min(left + options.EmptyLineWidthPt, segment.XRange.Right);
                }

                if (right <= left)
                {
                    continue;
                }

                (string? pageId, string? blockId) = Placement(line.FrameIndex);
                rects.Add(new SelectionRect(
                    line.FrameIndex, pageId, blockId,
                    left, line.Line.BandTop, right, line.Line.BandBottom));
            }
        }

        return rects;
    }

    // ---- Internals --------------------------------------------------------------------------

    private sealed record VisualLine(int FrameIndex, int LineIndexInFrame, LineBox Line);

    private (string? PageId, string? BlockId) Placement(int frameIndex) =>
        _placements is not null && frameIndex < _placements.Count
            ? (_placements[frameIndex].PageId, _placements[frameIndex].BlockId)
            : (null, null);

    private (int Paragraph, int Offset) FirstPositionOfFrame(int frameIndex)
    {
        for (int i = _visualLines.Count - 1; i >= 0; i--)
        {
            if (_visualLines[i].FrameIndex < frameIndex)
            {
                LineBox line = _visualLines[i].Line;
                return (line.ParagraphIndex, line.Source.EndChar);
            }
        }

        return (0, 0);
    }

    private static int LineEndOfParagraph(LineBox line) => line.Source.EndChar;

    /// <summary>Locates the visual line for a caret; at most two candidates exist (a shared
    /// line-boundary offset) and affinity picks one (spec §2.3 step 2).</summary>
    private bool TryLocateVisualLine(CaretPosition caret, out int visualLineIndex)
    {
        visualLineIndex = -1;
        int firstCandidate = -1;
        int lastCandidate = -1;
        for (int i = 0; i < _visualLines.Count; i++)
        {
            LineBox line = _visualLines[i].Line;
            if (line.ParagraphIndex != caret.ParagraphIndex)
            {
                continue;
            }

            if (caret.Offset >= line.Source.StartChar && caret.Offset <= line.Source.EndChar)
            {
                if (firstCandidate < 0)
                {
                    firstCandidate = i;
                }

                lastCandidate = i;
            }
        }

        if (firstCandidate < 0)
        {
            return false;
        }

        visualLineIndex = caret.Affinity == TextAffinity.Trailing ? firstCandidate : lastCandidate;
        return true;
    }

    private static int LocateSegment(LineBox line, CaretPosition caret)
    {
        int first = -1;
        int last = -1;
        for (int s = 0; s < line.Segments.Count; s++)
        {
            (int start, int end) = SegmentSpan(line, line.Segments[s]);
            if (caret.Offset >= start && caret.Offset <= end)
            {
                if (first < 0)
                {
                    first = s;
                }

                last = s;
            }
        }

        if (first < 0)
        {
            // Offset outside every segment span (only possible at line boundaries after
            // clamping); pin to the nearest end.
            return caret.Offset <= SegmentSpan(line, line.Segments[0]).Start ? 0 : line.Segments.Count - 1;
        }

        return caret.Affinity == TextAffinity.Trailing ? first : last;
    }

    private static (int Start, int End) SegmentSpan(LineBox line, LineSegment segment)
    {
        if (segment.Runs.Count == 0)
        {
            return (line.Source.StartChar, line.Source.EndChar);
        }

        return (segment.Runs[0].Source.StartChar, segment.Runs[^1].Source.EndChar);
    }

    /// <summary>Offset → x within a segment (spec §2.3 step 4). Not yet clamped.</summary>
    private static float XForOffset(LineBox line, LineSegment segment, int offset)
    {
        if (segment.Runs.Count == 0)
        {
            // Empty line: alignment-resolved zero-width content position (spec §2.3 step 7).
            return segment.Align switch
            {
                Input.TextAlign.Center => (segment.XRange.Left + segment.XRange.Right) / 2f,
                Input.TextAlign.Right => segment.XRange.Right,
                _ => segment.XRange.Left,
            };
        }

        foreach (PositionedGlyphRun run in segment.Runs)
        {
            if (offset >= run.Source.EndChar)
            {
                continue;
            }

            for (int i = 0; i < run.Clusters.Count; i++)
            {
                if (run.Clusters[i] >= offset)
                {
                    return run.OriginX + run.GlyphPenXPt[i];
                }
            }

            return run.OriginX + run.AdvanceWidthPt;
        }

        PositionedGlyphRun lastRun = segment.Runs[^1];
        return lastRun.OriginX + lastRun.AdvanceWidthPt;
    }

    /// <summary>x → offset within a segment via cluster midpoints (spec §1.4 steps 4–6).</summary>
    private static int XToOffset(LineBox line, LineSegment segment, float xPt, out TextAffinity affinity)
    {
        (int segStart, int segEnd) = SegmentSpan(line, segment);
        if (segment.Runs.Count == 0)
        {
            affinity = TextAffinity.Leading;
            return segStart;
        }

        PositionedGlyphRun? target = null;
        foreach (PositionedGlyphRun run in segment.Runs)
        {
            if (xPt < run.OriginX)
            {
                target = run;
                break;
            }

            if (xPt < run.OriginX + run.AdvanceWidthPt)
            {
                target = run;
                break;
            }
        }

        if (target is null)
        {
            affinity = TextAffinity.Trailing;
            return segEnd;
        }

        if (xPt <= target.OriginX)
        {
            affinity = TextAffinity.Leading;
            return target.Source.StartChar;
        }

        // Cluster table: distinct cluster starts in order with pen spans.
        float xLocal = xPt - target.OriginX;
        int count = target.Clusters.Count;
        int i = 0;
        while (i < count)
        {
            int clusterStart = target.Clusters[i];
            int j = i;
            while (j + 1 < count && target.Clusters[j + 1] == clusterStart)
            {
                j++;
            }

            int clusterEnd = j + 1 < count ? target.Clusters[j + 1] : target.Source.EndChar;
            float penLeft = target.GlyphPenXPt[i];
            float penRight = j + 1 < count ? target.GlyphPenXPt[j + 1] : target.AdvanceWidthPt;
            if (xLocal < penRight || j + 1 >= count)
            {
                int offset = xLocal < (penLeft + penRight) / 2f ? clusterStart : clusterEnd;
                affinity = offset == segEnd ? TextAffinity.Trailing : TextAffinity.Leading;
                return offset;
            }

            i = j + 1;
        }

        affinity = TextAffinity.Trailing;
        return segEnd;
    }
}
