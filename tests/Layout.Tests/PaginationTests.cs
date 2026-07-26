using TrestleBoard.Layout.Input;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// M8 pagination behaviour in the engine (docs/M8-spec.md §1, §4): what happens where a story
/// crosses a frame boundary.
/// </summary>
public sealed class PaginationTests
{
    private const string TwoParagraphs =
        "The Placeholder Lodge gathers on the appointed evening and the brothers arrive early for supper.";

    private static LayoutRequest TwoFrames(string text, float firstHeight)
    {
        ParagraphStyle style = TestData.Para();
        var story = new LayoutStory(
            "story-1",
            [
                new LayoutParagraph(style, [new LayoutRun("A short opening line.", style.DefaultRun)]),
                new LayoutParagraph(style, [new LayoutRun(text, style.DefaultRun)]),
            ]);

        return new LayoutRequest(
            story,
            [
                new LayoutFrame(new FrameRect(0, 0, 220, firstHeight), []),
                new LayoutFrame(new FrameRect(300, 0, 520, 600), []),
            ]);
    }

    private static TextLayoutEngine Engine(int minLinesAtBreak) =>
        new(TestData.Store.Value, new LayoutOptions { MinLinesAtBreak = minLinesAtBreak });

    private static int LinesOfParagraph(FrameLayout frame, int paragraphIndex) =>
        frame.Lines.Count(l => l.ParagraphIndex == paragraphIndex);

    /// <summary>
    /// The frame is sized to hold the opening paragraph plus exactly one line of the second. That
    /// one line is an orphan and must travel with its paragraph.
    /// </summary>
    [Fact]
    public void ASingleStrandedLineIsPushedToTheNextFrame()
    {
        // Height chosen empirically to leave exactly one line of paragraph 1 behind.
        LayoutResult loose = Engine(0).Layout(TwoFrames(TwoParagraphs, FirstFrameHeightForOneOrphan()));
        Assert.Equal(1, LinesOfParagraph(loose.Frames[0], 1));

        LayoutResult tight = Engine(2).Layout(TwoFrames(TwoParagraphs, FirstFrameHeightForOneOrphan()));
        Assert.Equal(0, LinesOfParagraph(tight.Frames[0], 1));

        // Nothing is lost: the pushed line reappears at the top of the next frame.
        Assert.Equal(
            LinesOfParagraph(loose.Frames[1], 1) + 1,
            LinesOfParagraph(tight.Frames[1], 1));
        Assert.False(tight.IsOverset);
    }

    [Fact]
    public void TurningItOffReproducesThePreM8Behaviour()
    {
        var request = TwoFrames(TwoParagraphs, FirstFrameHeightForOneOrphan());

        LayoutResult off = Engine(0).Layout(request);
        LayoutResult one = Engine(1).Layout(request);

        // minLines 0 and 1 both mean "a single line behind is fine".
        Assert.Equal(LinesOfParagraph(off.Frames[0], 1), LinesOfParagraph(one.Frames[0], 1));
    }

    /// <summary>
    /// The last frame has nowhere to push to, so pushing would only manufacture overset — the text
    /// would vanish off the end of the story rather than move down a page.
    /// </summary>
    [Fact]
    public void TheLastFrameKeepsItsStrandedLineRatherThanLosingIt()
    {
        ParagraphStyle style = TestData.Para();
        var story = new LayoutStory(
            "story-1",
            [
                new LayoutParagraph(style, [new LayoutRun("A short opening line.", style.DefaultRun)]),
                new LayoutParagraph(style, [new LayoutRun(TwoParagraphs, style.DefaultRun)]),
            ]);
        var request = new LayoutRequest(
            story,
            [new LayoutFrame(new FrameRect(0, 0, 220, FirstFrameHeightForOneOrphan()), [])]);

        LayoutResult result = Engine(2).Layout(request);

        Assert.Equal(1, LinesOfParagraph(result.Frames[0], 1));
        Assert.True(result.IsOverset);
    }

    /// <summary>Pushing must never empty a frame — that just moves the problem down the chain.</summary>
    [Fact]
    public void AFrameHoldingOnlyTheStrandedLinesKeepsThem()
    {
        ParagraphStyle style = TestData.Para();
        var story = new LayoutStory(
            "story-1",
            [new LayoutParagraph(style, [new LayoutRun(TwoParagraphs, style.DefaultRun)])]);
        var request = new LayoutRequest(
            story,
            [
                new LayoutFrame(new FrameRect(0, 0, 220, 20), []),
                new LayoutFrame(new FrameRect(300, 0, 520, 600), []),
            ]);

        LayoutResult result = Engine(2).Layout(request);

        Assert.NotEmpty(result.Frames[0].Lines);
    }

    // ---- cross-page chains ---------------------------------------------------------------------

    /// <summary>
    /// A chain's frames each carry their OWN exclusions — a photo on one page must not push text
    /// aside on another (docs/M8-spec.md §1).
    /// </summary>
    [Fact]
    public void EachFrameInAChainUsesItsOwnExclusions()
    {
        ParagraphStyle style = TestData.Para();
        var story = new LayoutStory(
            "story-1",
            [new LayoutParagraph(style, [new LayoutRun(TestData.FictionalProse, style.DefaultRun)])]);

        var exclusion = new ExclusionRect(new FrameRect(0, 0, 120, 200), 6f, 5);
        var request = new LayoutRequest(
            story,
            [
                new LayoutFrame(new FrameRect(0, 0, 300, 120), [exclusion]),
                new LayoutFrame(new FrameRect(0, 0, 300, 600), []),
            ]);

        LayoutResult result = Engine(0).Layout(request);

        // The first frame's lines start clear of the exclusion; the second frame's do not.
        Assert.All(
            result.Frames[0].Lines,
            l => Assert.True(l.Segments[0].XRange.Left >= 126f - 0.01f));
        Assert.Contains(result.Frames[1].Lines, l => l.Segments[0].XRange.Left < 1f);
    }

    /// <summary>
    /// The height at which the first frame holds the opening paragraph plus exactly one line of the
    /// second — derived, not guessed, so a font-metric change cannot quietly defeat the test.
    /// </summary>
    private static float FirstFrameHeightForOneOrphan()
    {
        LayoutResult probe = Engine(0).Layout(TwoFrames(TwoParagraphs, 600f));
        FrameLayout frame = probe.Frames[0];
        LineBox secondLineOfParagraph1 = frame.Lines.First(l => l.ParagraphIndex == 1);
        return secondLineOfParagraph1.BandBottom + 1f;
    }
}
