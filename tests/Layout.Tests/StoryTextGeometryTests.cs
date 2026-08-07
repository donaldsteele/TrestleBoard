using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Editing;
using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>M4 geometry acceptance (docs/M4-spec.md §8.2): hit-test round-trips, the
/// caret-never-inside-an-exclusion grid property, segment-aware navigation, selection rects.</summary>
public sealed class StoryTextGeometryTests
{
    private const string StoryId = "story-1";

    private static readonly FrameRect Frame = new(40, 40, 440, 640);

    private static readonly ExclusionRect[] Exclusions =
    [
        new(new FrameRect(200, 120, 340, 230), WrapMargin: 8f, ZOrder: 1),
        new(new FrameRect(40, 330, 160, 440), WrapMargin: 8f, ZOrder: 2),
    ];

    private static (StoryTextGeometry Geometry, LayoutResult Layout, LayoutRequest Request) BuildWrapFixture(
        string? secondParagraph = null)
    {
        ParagraphStyle style = TestData.Para();
        var paragraphs = new List<LayoutParagraph>
        {
            new(style, [new LayoutRun(TestData.FictionalProse, style.DefaultRun)]),
        };
        if (secondParagraph is not null)
        {
            paragraphs.Add(new LayoutParagraph(style, secondParagraph.Length == 0
                ? []
                : [new LayoutRun(secondParagraph, style.DefaultRun)]));
        }

        var request = new LayoutRequest(
            new LayoutStory(StoryId, paragraphs),
            [new LayoutFrame(Frame, Exclusions)]);
        LayoutResult layout = TestData.Engine().Layout(request);
        return (new StoryTextGeometry(request, layout), layout, request);
    }

    private static (StoryTextGeometry Geometry, LayoutResult Layout) BuildTwoFrameFixture()
    {
        ParagraphStyle style = TestData.Para();
        string text = TestData.FictionalProse + " " + TestData.FictionalProse;
        var request = new LayoutRequest(
            new LayoutStory(StoryId, [new(style, [new LayoutRun(text, style.DefaultRun)])]),
            [
                new LayoutFrame(new FrameRect(40, 40, 440, 200), []),
                new LayoutFrame(new FrameRect(90, 40, 490, 400), []),
            ]);
        LayoutResult layout = TestData.Engine().Layout(request);
        return (new StoryTextGeometry(request, layout), layout);
    }

    [Fact]
    public void CaretNeverLandsInsideAnExclusion()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();
        Assert.False(layout.IsOverset);

        for (float y = Frame.Top; y <= Frame.Bottom; y += 4f)
        {
            for (float x = Frame.Left; x <= Frame.Right; x += 4f)
            {
                Assert.True(geometry.TryHitTest(0, x, y, out TextHit hit));
                Assert.True(geometry.TryGetCaretGeometry(hit.Caret, out CaretGeometry caret));

                foreach (ExclusionRect exclusion in Exclusions)
                {
                    float left = exclusion.Rect.Left - exclusion.WrapMargin;
                    float top = exclusion.Rect.Top - exclusion.WrapMargin;
                    float right = exclusion.Rect.Right + exclusion.WrapMargin;
                    float bottom = exclusion.Rect.Bottom + exclusion.WrapMargin;
                    bool intersects = caret.XPt > left && caret.XPt < right
                        && caret.BottomPt > top && caret.TopPt < bottom;
                    Assert.False(intersects,
                        $"caret at ({caret.XPt:F1},{caret.TopPt:F1}) from hit ({x},{y}) intersects exclusion");
                }
            }
        }
    }

    [Fact]
    public void HitTestRoundTripsEveryOffset()
    {
        (StoryTextGeometry geometry, _, _) = BuildWrapFixture();
        for (int offset = 0; offset <= TestData.FictionalProse.Length; offset++)
        {
            // An offset inside a ligature cluster is not caret-addressable (spec §1.4 step 5);
            // the round-trip contract is idempotence of the RESOLVED position.
            var caret = new CaretPosition(new TextPosition(StoryId, 0, offset), TextAffinity.Leading);
            Assert.True(geometry.TryGetCaretGeometry(caret, out CaretGeometry g));
            Assert.True(geometry.TryHitTest(0, g.XPt + 0.1f, g.BaselineYPt, out TextHit hit));

            int resolved = hit.Caret.Offset;
            var resolvedCaret = new CaretPosition(new TextPosition(StoryId, 0, resolved), hit.Caret.Affinity);
            Assert.True(geometry.TryGetCaretGeometry(resolvedCaret, out CaretGeometry g2));
            Assert.True(geometry.TryHitTest(0, g2.XPt + 0.1f, g2.BaselineYPt, out TextHit hit2));
            Assert.Equal(resolved, hit2.Caret.Offset);
            // The resolved offset never drifts more than one ligature width from the request.
            Assert.InRange(resolved, offset - 2, offset + 2);
        }
    }

    [Fact]
    public void XIncreasesWithOffsetWithinASegment()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();
        foreach (LineBox line in layout.Frames[0].Lines)
        {
            foreach (LineSegment segment in line.Segments.Where(s => s.Runs.Count > 0))
            {
                int start = segment.Runs[0].Source.StartChar;
                int end = segment.Runs[^1].Source.EndChar;
                float lastX = float.MinValue;
                for (int offset = start; offset <= end; offset++)
                {
                    var caret = new CaretPosition(
                        new TextPosition(StoryId, 0, offset),
                        offset == end ? TextAffinity.Trailing : TextAffinity.Leading);
                    if (!geometry.TryGetCaretGeometry(caret, out CaretGeometry g)
                        || g.BaselineYPt != line.BaselineY)
                    {
                        continue;
                    }

                    Assert.True(g.XPt >= lastX - 0.001f, $"x regressed at offset {offset}");
                    lastX = g.XPt;
                }
            }
        }
    }

    [Fact]
    public void ArrowRightCrossesSegmentBoundaries()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();

        // Find a line split by the first exclusion: two segments.
        LineBox split = layout.Frames[0].Lines.First(l => l.Segments.Count == 2);
        int leftEnd = split.Segments[0].Runs[^1].Source.EndChar;
        int rightStart = split.Segments[1].Runs[0].Source.StartChar;
        Assert.Equal(leftEnd, rightStart); // contiguous text across the photo

        // Trailing caret at the left segment's end draws in the left segment...
        Assert.True(geometry.TryGetCaretGeometry(
            CaretPosition.Trailing(new TextPosition(StoryId, 0, leftEnd)), out CaretGeometry trailing));
        Assert.Equal(0, trailing.SegmentIndex);
        // ...and the Leading caret at the same offset draws at the right segment's start.
        Assert.True(geometry.TryGetCaretGeometry(
            CaretPosition.Leading(new TextPosition(StoryId, 0, rightStart)), out CaretGeometry leading));
        Assert.Equal(1, leading.SegmentIndex);
        Assert.True(leading.XPt > trailing.XPt);
    }

    [Fact]
    public void VerticalMotionSkipsExclusionsAndPreservesXGoal()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();

        // Start above the first exclusion at an x inside its horizontal span.
        float insideExclusionX = 270f;
        LineBox firstLine = layout.Frames[0].Lines[0];
        Assert.True(geometry.TryHitTest(0, insideExclusionX, firstLine.BaselineY, out TextHit start));

        CaretPosition current = start.Caret;
        CaretXGoal? goal = null;
        CaretXGoal? firstGoal = null;
        for (int step = 0; step < layout.Frames[0].Lines.Count - 1; step++)
        {
            Assert.True(geometry.TryMoveVertical(current, +1, goal, out CaretPosition next, out CaretXGoal newGoal));
            goal = newGoal;
            firstGoal ??= newGoal;
            Assert.True(geometry.TryGetCaretGeometry(next, out CaretGeometry g));
            foreach (ExclusionRect exclusion in Exclusions)
            {
                float left = exclusion.Rect.Left - exclusion.WrapMargin;
                float right = exclusion.Rect.Right + exclusion.WrapMargin;
                float top = exclusion.Rect.Top - exclusion.WrapMargin;
                float bottom = exclusion.Rect.Bottom + exclusion.WrapMargin;
                bool inside = g.XPt > left && g.XPt < right && g.BottomPt > top && g.TopPt < bottom;
                Assert.False(inside, $"vertical motion step {step} landed inside an exclusion");
            }

            current = next;
        }

        // The goal derives from the snapped caret x on the first move and is then preserved
        // VERBATIM across every subsequent step (docs/M4-spec.md §4.2 step 2).
        Assert.NotNull(goal);
        Assert.Equal(firstGoal!.Value.OffsetFromFrameLeftPt, goal.Value.OffsetFromFrameLeftPt);
        Assert.Equal(insideExclusionX - Frame.Left, goal.Value.OffsetFromFrameLeftPt, 6f);
    }

    [Fact]
    public void VerticalMotionCrossesFramesKeepingFrameRelativeColumn()
    {
        (StoryTextGeometry geometry, LayoutResult layout) = BuildTwoFrameFixture();
        Assert.True(layout.Frames[1].Lines.Count > 0, "story must spill into frame 2");

        LineBox lastInFirst = layout.Frames[0].Lines[^1];
        float columnX = 120f; // frame-relative 80pt
        Assert.True(geometry.TryHitTest(0, columnX, lastInFirst.BaselineY, out TextHit start));
        Assert.True(geometry.TryMoveVertical(start.Caret, +1, null, out CaretPosition next, out CaretXGoal goal));
        Assert.True(geometry.TryGetCaretGeometry(next, out CaretGeometry g));
        Assert.Equal(1, g.FrameIndex);
        // Frame 2 is offset +50pt; the caret keeps the frame-relative column, not the page x.
        Assert.Equal(90f + goal.OffsetFromFrameLeftPt, g.XPt, 6f);
    }

    [Fact]
    public void EmptyParagraphHasCaretGeometry()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture(secondParagraph: "");
        LineBox emptyLine = layout.Frames[0].Lines.First(l => l.ParagraphIndex == 1);
        Assert.Empty(emptyLine.Segments[0].Runs);

        Assert.True(geometry.TryGetCaretGeometry(
            CaretPosition.Leading(new TextPosition(StoryId, 1, 0)), out CaretGeometry g));
        Assert.Equal(emptyLine.BaselineY, g.BaselineYPt);
        Assert.True(g.XPt >= emptyLine.Segments[0].XRange.Left);
    }

    [Fact]
    public void HomeEndUseVisualLineNotSegment()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();
        LineBox split = layout.Frames[0].Lines.First(l => l.Segments.Count == 2);
        int midOffset = split.Segments[1].Runs[0].Source.StartChar + 1;

        Assert.True(geometry.TryGetLineBounds(
            CaretPosition.Leading(new TextPosition(StoryId, 0, midOffset)),
            out CaretPosition lineStart, out CaretPosition lineEnd));
        Assert.Equal(split.Source.StartChar, lineStart.Offset);
        Assert.Equal(split.Source.EndChar, lineEnd.Offset);
        Assert.Equal(TextAffinity.Leading, lineStart.Affinity);
        Assert.Equal(TextAffinity.Trailing, lineEnd.Affinity);
    }

    [Fact]
    public void SelectionRectsSplitAroundExclusionAndStayClamped()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();
        LineBox split = layout.Frames[0].Lines.First(l => l.Segments.Count == 2);

        var range = new TextRange(
            new TextPosition(StoryId, 0, split.Source.StartChar),
            new TextPosition(StoryId, 0, split.Source.EndChar));
        IReadOnlyList<SelectionRect> rects = geometry.GetSelectionRects(range);

        List<SelectionRect> onSplitLine = rects.Where(r => r.TopPt == split.BandTop).ToList();
        Assert.Equal(2, onSplitLine.Count);
        for (int s = 0; s < 2; s++)
        {
            Assert.True(onSplitLine[s].LeftPt >= split.Segments[s].XRange.Left - 0.001f);
            Assert.True(onSplitLine[s].RightPt <= split.Segments[s].XRange.Right + 0.001f);
        }
    }

    [Fact]
    public void MultiParagraphSelectionCoversInteriorLines()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture(secondParagraph: TestData.FictionalProse);
        var range = new TextRange(
            new TextPosition(StoryId, 0, 5),
            new TextPosition(StoryId, 1, 10));
        IReadOnlyList<SelectionRect> rects = geometry.GetSelectionRects(range);
        Assert.True(rects.Count >= layout.Frames[0].Lines.Count(l => l.ParagraphIndex == 0));
        Assert.All(rects, r => Assert.True(r.RightPt > r.LeftPt));
    }

    [Fact]
    public void OversetCaretReturnsFalseWithoutThrowing()
    {
        ParagraphStyle style = TestData.Para();
        var request = new LayoutRequest(
            new LayoutStory(StoryId, [new(style, [new LayoutRun(TestData.FictionalProse, style.DefaultRun)])]),
            [new LayoutFrame(new FrameRect(40, 40, 240, 80), [])]);
        LayoutResult layout = TestData.Engine().Layout(request);
        Assert.True(layout.IsOverset);

        var geometry = new StoryTextGeometry(request, layout);
        var pastEnd = CaretPosition.Leading(new TextPosition(StoryId, 0, TestData.FictionalProse.Length));
        Assert.False(geometry.TryGetCaretGeometry(pastEnd, out _));
    }

    [Fact]
    public void GapHitsClampToNearestContent()
    {
        (StoryTextGeometry geometry, LayoutResult layout, _) = BuildWrapFixture();
        LineBox firstLine = layout.Frames[0].Lines[0];

        // Above the frame content → first line.
        Assert.True(geometry.TryHitTest(0, 200f, Frame.Top - 20f, out TextHit above));
        Assert.False(above.IsInsideContent);
        Assert.Equal(0, above.LineIndex);

        // Below everything → last line.
        Assert.True(geometry.TryHitTest(0, 200f, Frame.Bottom + 50f, out TextHit below));
        Assert.False(below.IsInsideContent);

        // Left margin → line start, Leading.
        Assert.True(geometry.TryHitTest(0, Frame.Left - 30f, firstLine.BaselineY, out TextHit left));
        Assert.Equal(TextAffinity.Leading, left.Caret.Affinity);
        Assert.Equal(firstLine.Source.StartChar, left.Caret.Offset);
    }
    /// <summary>
    /// Review §14.2: a run's source span covers its last CLUSTER, not one past that cluster's
    /// first character.
    ///
    /// <para>A cluster is not a character. Shaping "affix coffin fi" through the bundled body font
    /// with standard ligatures on — the default — produces clusters 0,1,4,5,6,7,8,11,12,13: the
    /// "ffi" at 1 is ONE glyph covering three characters, and the trailing "fi" at 13 is one glyph
    /// covering two. So `clusters[^1] + 1` gave 14 for a fifteen-character paragraph, and
    /// everything built on that span inherited the error — SegmentSpan stopped short, and XToOffset
    /// used it as the last cluster's exclusive end, so clicking the right half of a trailing
    /// ligature put the caret inside it.</para>
    ///
    /// <para>The text ends in a ligature deliberately. An earlier version of this test ended in
    /// "coffin", whose last cluster is the plain "n" — and it passed against the unfixed engine,
    /// which is the whole reason the ending matters.</para>
    /// </summary>
    [Fact]
    public void ARunEndingInALigatureSpansTheWholeOfIt()
    {
        const string text = "affix coffin fi";

        ParagraphStyle style = TestData.Para();
        var request = new LayoutRequest(
            new LayoutStory(StoryId, [new(style, [new LayoutRun(text, style.DefaultRun)])]),
            [new LayoutFrame(new FrameRect(40, 40, 440, 400), [])]);
        LayoutResult layout = TestData.Engine().Layout(request);

        int furthest = 0;
        foreach (FrameLayout frame in layout.Frames)
        {
            foreach (LineBox line in frame.Lines)
            {
                foreach (LineSegment segment in line.Segments)
                {
                    foreach (PositionedGlyphRun run in segment.Runs)
                    {
                        furthest = Math.Max(furthest, run.Source.EndChar);

                        // No span may claim more than the paragraph holds, either.
                        Assert.True(
                            run.Source.EndChar <= text.Length,
                            $"a run ends at {run.Source.EndChar}, past the {text.Length}-character paragraph");
                        Assert.True(run.Source.EndChar > run.Source.StartChar);
                    }
                }
            }
        }

        // The trailing "fi" is one glyph covering two characters; the span has to reach both.
        Assert.Equal(text.Length, furthest);
    }



}
