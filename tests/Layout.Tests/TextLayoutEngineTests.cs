using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Layout.Tests;

public sealed class TextLayoutEngineTests
{
    private const float Eps = 1e-3f;

    // ---- #1 single word --------------------------------------------------------------------

    [Fact]
    public void SingleWordProducesSingleLine()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request("Placeholder", new FrameRect(72, 72, 400, 300)));

        LineBox line = Assert.Single(TestData.AllLines(result));
        LineSegment segment = Assert.Single(line.Segments);
        PositionedGlyphRun run = Assert.Single(segment.Runs);
        Assert.True(run.Glyphs.Count > 0);
        Assert.True(run.AdvanceWidthPt > 0f);
        Assert.False(result.IsOverset);
    }

    // ---- #2 wrapping -----------------------------------------------------------------------

    [Fact]
    public void WrappingParagraphPartitionsTextAcrossContiguousLines()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(TestData.FictionalProse, new FrameRect(72, 72, 272, 700)));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.True(lines.Count > 3, $"expected wrapping, got {lines.Count} line(s)");
        Assert.Equal(0, lines[0].Source.StartChar);
        Assert.Equal(TestData.FictionalProse.Length, lines[^1].Source.EndChar);
        for (int i = 1; i < lines.Count; i++)
        {
            Assert.Equal(lines[i - 1].Source.EndChar, lines[i].Source.StartChar);
            Assert.True(lines[i].BaselineY > lines[i - 1].BaselineY);
            Assert.Equal(lines[i - 1].BandBottom, lines[i].BandTop, Eps);
        }

        // No glyph may start beyond the frame's right edge.
        foreach (LineBox line in lines)
        {
            Assert.All(TestData.GlyphXs(line), x => Assert.True(x < 272f + Eps));
        }
    }

    // ---- #3 mandatory break ----------------------------------------------------------------

    [Fact]
    public void EmbeddedNewlineForcesLineBreak()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request("Alpha beta\ngamma delta", new FrameRect(72, 72, 500, 300)));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(11, lines[0].Source.EndChar);
        Assert.Equal(11, lines[1].Source.StartChar);
    }

    // ---- #4 hyphen break -------------------------------------------------------------------

    [Fact]
    public void HyphenatedWordBreaksAfterHyphen()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request("well-known", new FrameRect(72, 72, 112, 300)));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(5, lines[0].Source.EndChar); // "well-" stays on the upper line
        Assert.Equal(5, lines[1].Source.StartChar);
    }

    // ---- #5 over-wide token ----------------------------------------------------------------

    [Fact]
    public void OverwideTokenIsForcePlacedAndOverflows()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request("Sesquipedalian floccinaucinihilipilification", new FrameRect(72, 72, 132, 300)));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.Equal(2, lines.Count);
        Assert.False(result.IsOverset);

        // The second token is far wider than the 60pt frame: its glyphs overflow the right edge.
        Assert.Contains(TestData.GlyphXs(lines[1]), x => x > 132f);
    }

    // ---- #6 indent + paragraph spacing -------------------------------------------------------

    [Fact]
    public void FirstLineIndentAndParagraphSpacingApply()
    {
        ParagraphStyle style = TestData.Para(spaceBefore: 6f, spaceAfter: 8f, indent: 18f);
        var story = new LayoutStory("story-1",
        [
            new LayoutParagraph(style, [new LayoutRun("First paragraph.", style.DefaultRun)]),
            new LayoutParagraph(style, [new LayoutRun("Second paragraph.", style.DefaultRun)]),
        ]);
        LayoutResult result = TestData.Engine().Layout(
            new LayoutRequest(story, [new LayoutFrame(new FrameRect(72, 72, 500, 400), [])]));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.Equal(2, lines.Count);

        // Indent: the first glyph of a paragraph-start line is inset by FirstLineIndentPt.
        Assert.Equal(72f + 18f, lines[0].Segments[0].Runs[0].OriginX, Eps);

        // Spacing: gap between baselines = lineHeight + spaceAfter(para1) + spaceBefore(para2).
        float expectedGap = lines[0].LineHeight + 8f + 6f;
        Assert.Equal(expectedGap, lines[1].BaselineY - lines[0].BaselineY, Eps);
    }

    // ---- #7 alignment ------------------------------------------------------------------------

    [Fact]
    public void CenterAndRightAlignmentPositionContent()
    {
        var frame = new FrameRect(72, 72, 372, 300);

        LayoutResult right = TestData.Engine().Layout(
            TestData.Request("Placeholder", frame, style: TestData.Para(TextAlign.Right)));
        PositionedGlyphRun rightRun = TestData.AllLines(right).Single().Segments[0].Runs[0];
        Assert.Equal(372f, rightRun.OriginX + rightRun.AdvanceWidthPt, Eps);

        LayoutResult center = TestData.Engine().Layout(
            TestData.Request("Placeholder", frame, style: TestData.Para(TextAlign.Center)));
        PositionedGlyphRun centerRun = TestData.AllLines(center).Single().Segments[0].Runs[0];
        Assert.Equal(72f + (300f - centerRun.AdvanceWidthPt) / 2f, centerRun.OriginX, Eps);
    }

    [Fact]
    public void TrailingWhitespaceHangsOutsideAlignment()
    {
        var frame = new FrameRect(72, 72, 372, 300);
        ParagraphStyle style = TestData.Para(TextAlign.Right);

        PositionedGlyphRun bare = TestData.AllLines(
            TestData.Engine().Layout(TestData.Request("Placeholder", frame, style: style)))
            .Single().Segments[0].Runs[0];
        PositionedGlyphRun trailing = TestData.AllLines(
            TestData.Engine().Layout(TestData.Request("Placeholder ", frame, style: style)))
            .Single().Segments[0].Runs[0];

        Assert.Equal(bare.OriginX, trailing.OriginX, Eps);
    }

    // ---- #8 single exclusion -----------------------------------------------------------------

    [Fact]
    public void SingleExclusionNarrowsOverlappingBandsOnly()
    {
        var frame = new FrameRect(72, 72, 400, 500);
        var exclusion = new ExclusionRect(new FrameRect(300, 72, 400, 160), WrapMargin: 6f, ZOrder: 1);
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(TestData.FictionalProse, frame, [exclusion]));

        List<LineBox> lines = TestData.AllLines(result).ToList();
        Assert.False(result.IsOverset);

        bool sawNarrowed = false;
        bool sawFullWidth = false;
        foreach (LineBox line in lines)
        {
            bool overlaps = line.BandTop < 166f && line.BandBottom > 66f;
            float maxX = TestData.GlyphXs(line).Max();
            if (overlaps)
            {
                sawNarrowed = true;
                Assert.True(maxX < 294f + Eps, $"glyph at x={maxX} intrudes into the exclusion");
            }
            else if (maxX > 294f)
            {
                sawFullWidth = true;
            }
        }

        Assert.True(sawNarrowed, "no line overlapped the exclusion band");
        Assert.True(sawFullWidth, "no line resumed full width below the exclusion");
    }

    // ---- #9 min-segment-width discard --------------------------------------------------------

    [Fact]
    public void SliverSegmentIsDiscarded()
    {
        var frame = new FrameRect(72, 72, 400, 500);

        // Inflated exclusion leaves a 12pt sliver on the right — under 4 average char widths.
        var exclusion = new ExclusionRect(new FrameRect(100, 72, 382, 200), WrapMargin: 6f, ZOrder: 1);
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(TestData.FictionalProse, frame, [exclusion]));

        foreach (LineBox line in TestData.AllLines(result))
        {
            if (line.BandTop < 206f && line.BandBottom > 66f)
            {
                LineSegment segment = Assert.Single(line.Segments);
                Assert.True(segment.XRange.Right <= 94f + Eps, "text must flow only left of the exclusion");
            }
        }
    }

    // ---- #10 THE acceptance fixture: text column with two exclusion rects ---------------------

    [Fact]
    public void TextWrapsAroundTwoExclusionRects()
    {
        var frame = new FrameRect(72, 72, 400, 640);
        var exclusionA = new ExclusionRect(new FrameRect(200, 150, 320, 250), WrapMargin: 6f, ZOrder: 1);
        var exclusionB = new ExclusionRect(new FrameRect(72, 350, 180, 450), WrapMargin: 6f, ZOrder: 2);
        string text = string.Join(" ", Enumerable.Repeat(TestData.FictionalProse, 4));
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(text, frame, [exclusionA, exclusionB]));

        Assert.False(result.IsOverset);
        List<LineBox> lines = TestData.AllLines(result).ToList();

        bool sawSplitBand = false;
        bool sawRightOnlyBand = false;
        bool sawFullWidthBelow = false;
        foreach (LineBox line in lines)
        {
            bool overlapsA = line.BandTop < 256f && line.BandBottom > 144f;
            bool overlapsB = line.BandTop < 456f && line.BandBottom > 344f;
            foreach (float x in TestData.GlyphXs(line))
            {
                if (overlapsA)
                {
                    Assert.False(x > 194f + Eps && x < 326f - Eps, $"glyph at x={x} inside exclusion A band");
                }

                if (overlapsB)
                {
                    Assert.False(x < 186f - Eps, $"glyph at x={x} inside exclusion B band");
                }
            }

            if (overlapsA && line.Segments.Count == 2)
            {
                sawSplitBand = true;
            }

            if (overlapsB && line.Segments.Count == 1 && line.Segments[0].XRange.Left >= 186f - Eps)
            {
                sawRightOnlyBand = true;
            }

            if (line.BandTop > 456f && TestData.GlyphXs(line).Max() > 326f
                && TestData.GlyphXs(line).Min() < 194f)
            {
                sawFullWidthBelow = true;
            }
        }

        Assert.True(sawSplitBand, "exclusion A should split at least one band into two segments");
        Assert.True(sawRightOnlyBand, "exclusion B should leave right-only bands");
        Assert.True(sawFullWidthBelow, "full-width flow should resume below both exclusions");
    }

    // ---- #11 spill + overset ------------------------------------------------------------------

    [Fact]
    public void StoryLongerThanFrameReportsOverset()
    {
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(TestData.FictionalProse, new FrameRect(72, 72, 272, 110)));

        Assert.True(result.IsOverset);
        Assert.NotNull(result.Overflow);
        Assert.Equal(0, result.Overflow.Value.ParagraphIndex);
        Assert.True(result.Overflow.Value.CharIndex > 0);
        Assert.Equal(0, result.Overflow.Value.LastFrameIndex);
    }

    [Fact]
    public void StorySpillsIntoLinkedFrame()
    {
        ParagraphStyle style = TestData.Para();
        var story = new LayoutStory("story-1",
            [new LayoutParagraph(style, [new LayoutRun(TestData.FictionalProse, style.DefaultRun)])]);
        var request = new LayoutRequest(story,
        [
            new LayoutFrame(new FrameRect(72, 72, 272, 110), []),
            new LayoutFrame(new FrameRect(300, 72, 500, 700), []),
        ]);
        LayoutResult result = TestData.Engine().Layout(request);

        Assert.False(result.IsOverset);
        Assert.True(result.Frames[0].Lines.Count > 0);
        Assert.True(result.Frames[1].Lines.Count > 0);

        LineBox lastInFirst = result.Frames[0].Lines[^1];
        LineBox firstInSecond = result.Frames[1].Lines[0];
        Assert.Equal(lastInFirst.Source.EndChar, firstInSecond.Source.StartChar);
        Assert.Equal(TestData.FictionalProse.Length, result.Frames[1].Lines[^1].Source.EndChar);
    }
}
