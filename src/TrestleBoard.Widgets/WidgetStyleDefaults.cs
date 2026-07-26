using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Widgets;

/// <summary>
/// A widget's own fallback appearance (docs/M7-spec.md §12.1). Document styles are layered OVER
/// these by <c>Rendering.WidgetStyleResolver</c>, so no widget ever writes into the document's
/// stylesheet — which is what lets six widgets be built in parallel without touching a shared file.
/// </summary>
public sealed record WidgetStyleDefaults(
    CharacterStyle Display,
    CharacterStyle Heading,
    CharacterStyle Body,
    CharacterStyle Emphasis,
    CharacterStyle Small,
    float LineSpacing,
    uint RuleArgb,
    float RuleWidthPt,
    uint? FillArgb,
    uint? StrokeArgb,
    float StrokeWidthPt,
    float PaddingPt)
{
    public const uint InkArgb = 0xFF1A1A1A;

    /// <summary>The house look: Cinzel for banners, Source Sans for headings, Source Serif for rows.</summary>
    public static WidgetStyleDefaults Standard { get; } = new(
        Display: new CharacterStyle(BundledFonts.DisplayFamily, FontWeight.Regular, FontStyleSlant.Normal, 20f, InkArgb),
        Heading: new CharacterStyle(BundledFonts.SansFamily, FontWeight.Bold, FontStyleSlant.Normal, 12f, InkArgb),
        Body: new CharacterStyle(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal, 10f, InkArgb),
        Emphasis: new CharacterStyle(BundledFonts.BodyFamily, FontWeight.Bold, FontStyleSlant.Normal, 10f, InkArgb),
        // Serif italic, not sans italic: BundledFonts ships no italic Source Sans face, and a
        // bundled-fonts-only engine has no system fallback to rescue it (PLAN.md §1).
        Small: new CharacterStyle(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Italic, 8.5f, InkArgb),
        LineSpacing: 1.15f,
        RuleArgb: 0xFF8A8A8A,
        RuleWidthPt: 0.5f,
        FillArgb: null,
        StrokeArgb: null,
        StrokeWidthPt: 0f,
        PaddingPt: 0f);
}
