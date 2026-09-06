using TrestleBoard.Layout;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// M109: a paragraph pulled in from both sides.
///
/// <para>Unlike the first-line indent, which marks where a paragraph begins, these apply to EVERY
/// line — that is what sets a paragraph apart from the ones around it rather than merely starting
/// it.</para>
/// </summary>
public sealed class ParagraphIndentTests
{
    private const float FrameLeft = 72f;
    private const float FrameRight = 468f;
    private const float Indent = 24f;

    /// <summary>
    /// <b>Every line, not just the first.</b> A left indent applied only to the first line would be
    /// the first-line indent that already existed, under a new name.
    /// </summary>
    [Fact]
    public void EveryLineStartsFurtherInNotJustTheFirst()
    {
        LayoutResult plain = LayOut(left: 0f, right: 0f);
        LayoutResult pulled = LayOut(left: Indent, right: 0f);

        Assert.True(plain.Frames[0].Lines.Count > 1, "the fixture must wrap for this to mean anything");
        Assert.True(pulled.Frames[0].Lines.Count > 1);

        foreach (LineBox line in pulled.Frames[0].Lines)
        {
            Assert.Equal(FrameLeft + Indent, line.Segments[0].Runs[0].OriginX, 1);
        }
    }

    /// <summary>
    /// The right indent narrows the line as well, which is what makes it a block rather than a
    /// paragraph shoved sideways. Fewer words fit on a line, so more lines are used.
    /// </summary>
    [Fact]
    public void PullingItInFromBothSidesUsesMoreLines()
    {
        int plain = LayOut(left: 0f, right: 0f).Frames[0].Lines.Count;
        int pulled = LayOut(left: Indent, right: Indent).Frames[0].Lines.Count;

        Assert.True(pulled > plain, $"plain used {plain} lines and pulled-in used {pulled}");
    }

    /// <summary>
    /// No line runs past where the right indent puts the edge. This is the half a line-count
    /// assertion cannot see: more lines could equally mean bigger words.
    /// </summary>
    [Fact]
    public void NoLineRunsPastTheRightIndent()
    {
        LayoutResult pulled = LayOut(left: 0f, right: Indent);

        foreach (LineBox line in pulled.Frames[0].Lines)
        {
            foreach (LineSegment segment in line.Segments)
            {
                Assert.True(
                    segment.XRange.Right <= FrameRight - Indent + 0.01f,
                    $"a line reached {segment.XRange.Right} past {FrameRight - Indent}");
            }
        }
    }

    /// <summary>
    /// Indents that would leave nothing are ignored rather than obeyed. A paragraph that vanished
    /// because two numbers met in the middle would be a newsletter losing a paragraph silently,
    /// which is worse than one that ignores a setting somebody can see is not working.
    /// </summary>
    [Fact]
    public void IndentsWiderThanTheFrameAreIgnored()
    {
        LayoutResult absurd = LayOut(left: 400f, right: 400f);

        Assert.NotEmpty(absurd.Frames[0].Lines);
    }

    /// <summary>A paragraph with no indents lays out exactly as it did before this existed.</summary>
    [Fact]
    public void WithoutThemNothingChanges()
    {
        LayoutResult plain = LayOut(left: 0f, right: 0f);

        Assert.Equal(FrameLeft, plain.Frames[0].Lines[0].Segments[0].Runs[0].OriginX, 1);
    }

    private static LayoutResult LayOut(float left, float right)
    {
        FontStore fonts = BundledFonts.CreateDefaultStore();
        var engine = new TextLayoutEngine(fonts);

        var style = new CharacterStyle(
            BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal, 11f, 0xFF000000);

        var paragraph = new LayoutParagraph(
            new ParagraphStyle(
                LineSpacing: 1.2f,
                SpaceBeforePt: 0f,
                SpaceAfterPt: 6f,
                FirstLineIndentPt: 0f,
                Align: TextAlign.Left,
                DefaultRun: style,
                MarkerText: "",
                LeftIndentPt: left,
                RightIndentPt: right),
            [new LayoutRun(
                string.Concat(Enumerable.Repeat(
                    "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ",
                    6)),
                style)]);

        return engine.Layout(new LayoutRequest(
            new LayoutStory("story-1", [paragraph]),
            [new LayoutFrame(new FrameRect(FrameLeft, 72f, FrameRight, 400f), [])]));
    }
}
