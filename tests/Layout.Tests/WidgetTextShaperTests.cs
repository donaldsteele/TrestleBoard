using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// The M7 seam (docs/M7-spec.md §4.4): widgets measure and shape ONLY through this, so widget text
/// and body text agree glyph for glyph. If these drift, a table's columns and the prose beside them
/// stop sharing a rhythm.
/// </summary>
public sealed class WidgetTextShaperTests
{
    private const float Eps = 1e-3f;

    private static WidgetTextShaper Shaper() => new(TestData.Store.Value);

    [Fact]
    public void MeasuredWidthMatchesWhatTheEngineLaysOut()
    {
        const string text = "Worshipful Master";
        CharacterStyle style = TestData.Body();

        // Laid out in a frame wide enough to hold it on one line, the engine's advance is the
        // shaper's measurement — same fonts, same features, same scale.
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(text, new FrameRect(0, 0, 400, 100)));
        PositionedGlyphRun run = Assert.Single(Assert.Single(Assert.Single(result.Frames).Lines).Segments.Single().Runs);

        Assert.Equal(run.AdvanceWidthPt, Shaper().MeasureWidthPt(text, style), Eps);
    }

    [Fact]
    public void LineHeightUsesTheEnginesOwnFormula()
    {
        CharacterStyle style = TestData.Body();
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request("Placeholder", new FrameRect(0, 0, 400, 100)));
        LineBox line = Assert.Single(Assert.Single(result.Frames).Lines);

        // The paragraph fixture uses 1.2 line spacing; the shaper applies the same multiplier to
        // the same ascent+descent+leading sum, so the two must agree exactly.
        WidgetLineMetrics metrics = Shaper().GetLineMetrics(style, lineSpacing: 1.2f);
        Assert.Equal(line.LineHeight, metrics.LineHeightPt, Eps);
        Assert.Equal(line.MaxAscentPt, metrics.AscentPt, Eps);
        Assert.Equal(line.MaxDescentPt, metrics.DescentPt, Eps);
    }

    [Fact]
    public void ShapedRunCarriesTheSameGlyphsAtTheSamePens()
    {
        const string text = "Senior Warden";
        LayoutResult result = TestData.Engine().Layout(
            TestData.Request(text, new FrameRect(0, 0, 400, 100)));
        PositionedGlyphRun engineRun = Assert.Single(Assert.Single(Assert.Single(result.Frames).Lines).Segments.Single().Runs);

        WidgetTextItem item = Shaper().ShapeRun(text, TestData.Body(), 0f, 0f);
        PositionedGlyphRun widgetRun = Assert.Single(item.Runs);

        Assert.Equal(engineRun.Glyphs, widgetRun.Glyphs);
        Assert.Equal(engineRun.GlyphPenXPt.Count, widgetRun.GlyphPenXPt.Count);
        for (int i = 0; i < engineRun.GlyphPenXPt.Count; i++)
        {
            Assert.Equal(engineRun.GlyphPenXPt[i], widgetRun.GlyphPenXPt[i], Eps);
        }
    }

    /// <summary>
    /// Widget runs are marked "not story text" so the caret and hit-test machinery can never mistake
    /// one for editable content (docs/M7-spec.md §4.4, §11).
    /// </summary>
    [Fact]
    public void WidgetRunsAreNotStoryText()
    {
        PositionedGlyphRun run = Assert.Single(Shaper().ShapeRun("Treasurer", TestData.Body(), 0f, 0f).Runs);

        Assert.Equal(-1, run.Source.ParagraphIndex);
    }

    [Fact]
    public void AlignmentPlacesTheRunInsideTheGivenInterval()
    {
        WidgetTextShaper shaper = Shaper();
        CharacterStyle style = TestData.Body();
        const string text = "555-0100";
        float width = shaper.MeasureWidthPt(text, style);

        Assert.Equal(100f, Origin(shaper.ShapeAligned(text, style, 100f, 300f, 0f, TextAlign.Left)), Eps);
        Assert.Equal(300f - width, Origin(shaper.ShapeAligned(text, style, 100f, 300f, 0f, TextAlign.Right)), Eps);
        Assert.Equal(
            100f + ((200f - width) / 2f),
            Origin(shaper.ShapeAligned(text, style, 100f, 300f, 0f, TextAlign.Center)),
            Eps);
    }

    [Fact]
    public void WrapBreaksWhereTheEngineBreaks()
    {
        CharacterStyle style = TestData.Body();
        var frame = new FrameRect(0, 0, 200, 700);
        LayoutResult result = TestData.Engine().Layout(TestData.Request(TestData.FictionalProse, frame));
        List<string> engineLines = TestData.AllLines(result)
            .Select(l => TestData.FictionalProse[l.Source.StartChar..l.Source.EndChar].TrimEnd())
            .ToList();

        IReadOnlyList<string> widgetLines = Shaper().WrapToWidth(TestData.FictionalProse, style, 200f, 200f);

        Assert.Equal(engineLines, widgetLines);
    }

    [Fact]
    public void HangingIndentNarrowsEveryLineButTheFirst()
    {
        IReadOnlyList<string> lines = Shaper().WrapToWidth(
            TestData.FictionalProse, TestData.Body(), firstLineWidthPt: 200f, restWidthPt: 140f);

        Assert.True(lines.Count > 2);
        WidgetTextShaper shaper = Shaper();
        Assert.True(shaper.MeasureWidthPt(lines[0], TestData.Body()) <= 200f + Eps);
        for (int i = 1; i < lines.Count - 1; i++)
        {
            Assert.True(
                shaper.MeasureWidthPt(lines[i], TestData.Body()) <= 140f + Eps,
                $"line {i} overflowed the hanging indent");
        }
    }

    [Fact]
    public void EmptyTextProducesNothingToDraw()
    {
        Assert.Empty(Shaper().ShapeRun("", TestData.Body(), 0f, 0f).Runs);
        Assert.Empty(Shaper().WrapToWidth("", TestData.Body(), 100f, 100f));
        Assert.Equal(0f, Shaper().MeasureWidthPt("", TestData.Body()));
    }

    private static float Origin(WidgetTextItem item) => item.Runs[0].OriginX;
}
