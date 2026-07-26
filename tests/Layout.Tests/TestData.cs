using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Tests;

/// <summary>Shared fixtures. All text is fictional (PLAN.md §0) — no real names or numbers.</summary>
internal static class TestData
{
    public static readonly Lazy<FontStore> Store = new(BundledFonts.CreateDefaultStore);

    public const string FictionalProse =
        "The Placeholder Lodge convenes on the appointed evening of every month. "
        + "Brothers gather early for fellowship, share a simple meal together, and then "
        + "proceed to the reading of the minutes. Visitors are always welcome to attend "
        + "the open portions of the program and to enjoy the refreshments afterward.";

    public static CharacterStyle Body(float sizePt = 12f) =>
        new(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal, sizePt, 0xFF000000);

    public static ParagraphStyle Para(
        TextAlign align = TextAlign.Left,
        float lineSpacing = 1.2f,
        float spaceBefore = 0f,
        float spaceAfter = 0f,
        float indent = 0f,
        float sizePt = 12f) =>
        new(lineSpacing, spaceBefore, spaceAfter, indent, align, Body(sizePt));

    public static LayoutRequest Request(
        string text,
        FrameRect frame,
        IReadOnlyList<ExclusionRect>? exclusions = null,
        ParagraphStyle? style = null)
    {
        ParagraphStyle p = style ?? Para();
        var story = new LayoutStory(
            "story-1",
            [new LayoutParagraph(p, [new LayoutRun(text, p.DefaultRun)])]);
        return new LayoutRequest(story, [new LayoutFrame(frame, exclusions ?? [])]);
    }

    public static TextLayoutEngine Engine() => new(Store.Value);

    public static IEnumerable<LineBox> AllLines(LayoutResult result) =>
        result.Frames.SelectMany(f => f.Lines);

    /// <summary>Absolute x position of every glyph origin on a line.</summary>
    public static IEnumerable<float> GlyphXs(LineBox line) =>
        line.Segments
            .SelectMany(s => s.Runs)
            .SelectMany(r => r.GlyphOffsets.Select(o => r.OriginX + o.X));
}
