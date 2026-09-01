using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// M83: two columns.
///
/// <para><c>ColumnCount</c> has been in the document model and in this engine's own input since M1,
/// documented as "1 in M1 (multi-column deferred)", and it was always 1.</para>
/// </summary>
public sealed class TwoColumnTests
{
    /// <summary>
    /// <b>The single-column path must be arithmetically identical to what it was.</b> This is the
    /// property that lets a milestone touching the layout engine move no baseline at all: a frame
    /// with one column is laid out over its own rectangle, by the loop that has always been there.
    /// </summary>
    [Fact]
    public void OneColumnIsTheFramesOwnRectangle()
    {
        var frame = new LayoutFrame(new FrameRect(72f, 72f, 540f, 700f), []);

        FrameRect only = Assert.Single(TextLayoutEngine.Columns(frame));

        Assert.Equal(frame.Rect, only);
    }

    /// <summary>
    /// Two columns split the frame evenly with one gutter between them, and neither reaches outside
    /// the frame.
    /// </summary>
    [Fact]
    public void TwoColumnsSplitTheFrameEvenlyWithOneGutter()
    {
        var frame = new LayoutFrame(new FrameRect(100f, 50f, 400f, 600f), [], ColumnCount: 2);

        IReadOnlyList<FrameRect> columns = TextLayoutEngine.Columns(frame);

        Assert.Equal(2, columns.Count);
        Assert.Equal(100f, columns[0].Left, 3);
        Assert.Equal(400f, columns[1].Right, 3);

        float width = columns[0].Right - columns[0].Left;
        Assert.Equal(width, columns[1].Right - columns[1].Left, 3);
        Assert.Equal(TextLayoutEngine.ColumnGutterPt, columns[1].Left - columns[0].Right, 3);

        // Both run the full height of the frame.
        Assert.All(columns, c =>
        {
            Assert.Equal(frame.Rect.Top, c.Top, 3);
            Assert.Equal(frame.Rect.Bottom, c.Bottom, 3);
        });
    }

    /// <summary>
    /// A frame too narrow to divide gets one column rather than two of nothing. Refusing to lay
    /// anything out would lose the user's article.
    /// </summary>
    [Fact]
    public void AFrameTooNarrowToDivideStaysOneColumn()
    {
        var frame = new LayoutFrame(new FrameRect(0f, 0f, TextLayoutEngine.ColumnGutterPt, 100f), [], ColumnCount: 2);

        Assert.Single(TextLayoutEngine.Columns(frame));
    }

    /// <summary>
    /// <b>The left column fills before the right one starts.</b> That is reading order, and it is
    /// what a screen reader announces: the lines come out of the engine in the order a person reads
    /// them, not in the order they appear down the page.
    /// </summary>
    [Fact]
    public void TheLeftColumnFillsBeforeTheRightOneStarts()
    {
        LayoutResult result = LayOut(columns: 2, sentences: 12);
        FrameLayout frame = Assert.Single(result.Frames);

        Assert.True(frame.Lines.Count > 4, "the fixture has to fill both columns or this proves nothing");

        // Every line's x range identifies its column. Walking the lines in the order the engine
        // emitted them, the column index must never go backwards.
        float boundary = Midpoint(frame);
        int column = 0;
        foreach (LineBox line in frame.Lines)
        {
            int thisColumn = line.Segments[0].XRange.Left < boundary ? 0 : 1;
            Assert.True(
                thisColumn >= column,
                "the engine emitted a left-column line after a right-column one, so a screen reader "
                    + "would read the columns out of order");
            column = thisColumn;
        }

        Assert.Equal(1, column);
    }

    /// <summary>
    /// A line never straddles the gutter: each one lies wholly inside one column, which is what
    /// makes two columns readable rather than one wide column with a stripe through it.
    /// </summary>
    [Fact]
    public void NoLineCrossesTheGutter()
    {
        LayoutResult result = LayOut(columns: 2, sentences: 12);
        FrameLayout frame = Assert.Single(result.Frames);
        float boundary = Midpoint(frame);

        foreach (LineBox line in frame.Lines)
        {
            foreach (LineSegment segment in line.Segments)
            {
                bool left = segment.XRange.Right <= boundary + 0.5f;
                bool right = segment.XRange.Left >= boundary - 0.5f;
                Assert.True(left || right, $"a line ran from {segment.XRange.Left} to {segment.XRange.Right}, across the gutter");
            }
        }
    }

    /// <summary>
    /// Two columns hold more of the article than one of the same size, which is the whole reason
    /// somebody asks for them.
    /// </summary>
    [Fact]
    public void TwoColumnsHoldMoreOfTheArticleThanOne()
    {
        int one = LayOut(columns: 1, sentences: 12).Frames[0].Lines.Count;
        int two = LayOut(columns: 2, sentences: 12).Frames[0].Lines.Count;

        Assert.True(two > one, $"one column laid out {one} lines and two laid out {two}");
    }

    private static float Midpoint(FrameLayout frame) =>
        (frame.Frame.Left + frame.Frame.Right) / 2f;

    private static LayoutResult LayOut(int columns, int sentences)
    {
        FontStore fonts = BundledFonts.CreateDefaultStore();
        var engine = new TextLayoutEngine(fonts);

        var style = new CharacterStyle(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal, 11f, 0xFF000000);
        var paragraph = new LayoutParagraph(
            new ParagraphStyle(
                LineSpacing: 1.2f,
                SpaceBeforePt: 0f,
                SpaceAfterPt: 6f,
                FirstLineIndentPt: 0f,
                Align: TextAlign.Left,
                DefaultRun: style),
            [new LayoutRun(
                string.Concat(Enumerable.Repeat(
                    "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ",
                    sentences)),
                style)]);

        return engine.Layout(new LayoutRequest(
            new LayoutStory("story-1", [paragraph]),
            [new LayoutFrame(new FrameRect(72f, 72f, 468f, 300f), [], columns)]));
    }
}
