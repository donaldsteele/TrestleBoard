using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.EventCard;

/// <summary>
/// A filled, optionally-bordered box: title, then an optional when/where line pair under a rule,
/// then the announcement itself (docs/M7-spec.md §9.5). The fill and border must span the card's
/// full measured height, which is itself a function of the content drawn on top of them — so this
/// layouter measures the content once in a throwaway builder, then re-draws it into the real one
/// after the background is already in place (painter's order: <c>WidgetDrawListRenderer</c> paints
/// items in list order, so the fill/border must be added first).
/// </summary>
public sealed class EventCardLayouter : IWidgetLayouter
{
    /// <summary>Used when the style's own padding is unset (docs/M7-spec.md §9.5, rule 3).</summary>
    private const float DefaultPaddingPt = 10f;

    /// <summary>Used when the style's own stroke width is unset (rule 2).</summary>
    private const float DefaultStrokeWidthPt = 1f;

    /// <summary>Parchment fallback fill, used when neither the style nor a "widgetAccent" theme token
    /// supplies one.</summary>
    private const uint DefaultFillArgb = 0xFFF4F1E8;

    /// <summary>Dark-brown fallback border, used when the style supplies none.</summary>
    private const uint DefaultStrokeArgb = 0xFF6B5B3E;

    private const string AccentColorToken = "widgetAccent";

    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (EventCardData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        float widthPt = context.WidthPt;

        float paddingPt = style.PaddingPt > 0f ? style.PaddingPt : DefaultPaddingPt;
        float strokeWidthPt = style.StrokeWidthPt > 0f ? style.StrokeWidthPt : DefaultStrokeWidthPt;
        uint fillArgb = style.FillArgb ?? style.Color(AccentColorToken, DefaultFillArgb);
        uint strokeArgb = style.StrokeArgb ?? DefaultStrokeArgb;

        float left = paddingPt;
        float right = widthPt - paddingPt;

        // The background is sized from the MEASURED content height, which is not known until the
        // content is laid out — and the renderer paints in list order. So the content goes down
        // first and the background slides underneath it, rather than shaping every string twice.
        var builder = new WidgetDrawListBuilder(widthPt);
        float cardHeightPt = LayoutContent(builder, shaper, data, style, left, right, paddingPt) + paddingPt;

        if (data.ShowBorder)
        {
            builder.PrependBorder(0f, 0f, widthPt, cardHeightPt, strokeWidthPt, strokeArgb);
        }

        builder.PrependFill(0f, 0f, widthPt, cardHeightPt, fillArgb);
        builder.Grow(cardHeightPt);

        bool isEmpty = string.IsNullOrWhiteSpace(data.Title) && string.IsNullOrWhiteSpace(data.BodyText);
        return builder.Build(isEmpty, "Announcement — not filled in yet.");
    }

    /// <summary>
    /// Title, then when/where (each entirely omitted — no blank line — when blank), then a rule under
    /// them when either is present, then the body. Returns the y just below the last thing drawn (the
    /// caller adds the bottom padding).
    /// </summary>
    private static float LayoutContent(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        EventCardData data,
        WidgetStyleContext style,
        float leftPt,
        float rightPt,
        float topPt)
    {
        bool hasTitle = !string.IsNullOrWhiteSpace(data.Title);
        bool hasWhen = !string.IsNullOrWhiteSpace(data.WhenText);
        bool hasWhere = !string.IsNullOrWhiteSpace(data.WhereText);
        bool hasWhenOrWhere = hasWhen || hasWhere;
        bool hasBody = !string.IsNullOrWhiteSpace(data.BodyText);

        float y = topPt;

        if (hasTitle)
        {
            y = CenteredLine(builder, shaper, data.Title, style.Heading, style.LineSpacing, leftPt, rightPt, y);
            if (hasWhenOrWhere || hasBody)
            {
                y += WidgetLayoutHelpers.GroupGapPt;
            }
        }

        if (hasWhen)
        {
            y = CenteredLine(builder, shaper, data.WhenText!, style.Emphasis, style.LineSpacing, leftPt, rightPt, y);
        }

        if (hasWhere)
        {
            y = CenteredLine(builder, shaper, data.WhereText!, style.Emphasis, style.LineSpacing, leftPt, rightPt, y);
        }

        if (hasWhenOrWhere)
        {
            builder.HRule(y, leftPt, rightPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt;
            if (hasBody)
            {
                y += WidgetLayoutHelpers.GroupGapPt;
            }
        }

        if (hasBody)
        {
            y = WidgetLayoutHelpers.WrappedParagraph(
                builder, shaper, data.BodyText, style.Body, style.LineSpacing, leftPt, rightPt, y);
        }

        return y;
    }

    /// <summary>One centred line, advancing past its own line height.</summary>
    private static float CenteredLine(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        string text,
        CharacterStyle style,
        float lineSpacing,
        float leftPt,
        float rightPt,
        float topPt)
    {
        WidgetLineMetrics metrics = shaper.GetLineMetrics(style, lineSpacing);
        builder.TextLine(
            shaper.ShapeAligned(text, style, leftPt, rightPt, topPt + metrics.AscentPt, TextAlign.Center),
            topPt + metrics.LineHeightPt);
        return topPt + metrics.LineHeightPt;
    }
}
