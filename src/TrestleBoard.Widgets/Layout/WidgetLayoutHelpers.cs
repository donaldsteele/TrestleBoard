using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets.Layout;

/// <summary>
/// Small shared pieces every widget layouter needs (docs/M7-spec.md §12.1). Frozen during the
/// parallel widget phase so six agents never edit the same file.
/// </summary>
public static class WidgetLayoutHelpers
{
    /// <summary>Gap between logical groups, in points.</summary>
    public const float GroupGapPt = 6f;

    /// <summary>Gutter between table columns, in points.</summary>
    public const float ColumnGapPt = 8f;

    /// <summary>
    /// Draws one wrapped paragraph and returns the y below it. Continuation lines start at
    /// <paramref name="hangingIndentPt"/> — the hanging-indent shape shared by committee rows and
    /// district events.
    /// </summary>
    public static float WrappedParagraph(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        string text,
        CharacterStyle style,
        float lineSpacing,
        float leftPt,
        float rightPt,
        float topPt,
        float hangingIndentPt = 0f)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shaper);
        if (string.IsNullOrEmpty(text))
        {
            return topPt;
        }

        WidgetLineMetrics metrics = shaper.GetLineMetrics(style, lineSpacing);
        IReadOnlyList<string> lines = shaper.WrapToWidth(
            text, style, rightPt - leftPt, rightPt - leftPt - hangingIndentPt);

        float y = topPt;
        for (int i = 0; i < lines.Count; i++)
        {
            float x = leftPt + (i == 0 ? 0f : hangingIndentPt);
            builder.TextLine(
                shaper.ShapeRun(lines[i], style, x, y + metrics.AscentPt),
                y + metrics.LineHeightPt);
            y += metrics.LineHeightPt;
        }

        return y;
    }

    /// <summary>Widest measured string, or 0 for an empty set. The column-sizing primitive.</summary>
    public static float MaxWidth(
        WidgetTextShaper shaper,
        IEnumerable<string> values,
        CharacterStyle style)
    {
        ArgumentNullException.ThrowIfNull(shaper);
        ArgumentNullException.ThrowIfNull(values);
        float widest = 0f;
        foreach (string value in values)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            float width = shaper.MeasureWidthPt(value, style);
            if (width > widest)
            {
                widest = width;
            }
        }

        return widest;
    }

    /// <summary>Joins the non-blank parts with a separator — omitted parts take their separator with them.</summary>
    public static string JoinPresent(string separator, params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
