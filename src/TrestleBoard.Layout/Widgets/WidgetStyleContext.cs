using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Widgets;

/// <summary>
/// Resolved appearance handed to a widget layouter (docs/M7-spec.md §9). Built by
/// <c>Rendering.WidgetStyleResolver</c> from the document's theme + table/frame styles layered over
/// the widget's own defaults, so a layouter never hardcodes a font family or a colour.
/// </summary>
public sealed record WidgetStyleContext(
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
    float PaddingPt,
    IReadOnlyDictionary<string, uint> ColorTokens)
{
    /// <summary>Theme token lookup with an explicit fallback — tokens are optional by design.</summary>
    public uint Color(string token, uint fallback) =>
        ColorTokens.TryGetValue(token, out uint value) ? value : fallback;
}
