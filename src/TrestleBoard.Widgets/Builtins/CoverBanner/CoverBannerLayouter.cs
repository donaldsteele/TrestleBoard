using System.Globalization;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.CoverBanner;

/// <summary>
/// Five centred elements, top to bottom — lodge name, heading, a full-width rule, the meeting date,
/// and an optional times line (docs/M7-spec.md §9.6). Nothing here is left-aligned: a cover banner is
/// read as a single centred block, unlike every other widget's left-anchored text.
/// </summary>
public sealed class CoverBannerLayouter : IWidgetLayouter
{
    /// <summary>The lodge name is a headline, not a paragraph — capped at two lines, never more.</summary>
    private const int MaxLodgeNameLines = 2;

    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (CoverBannerData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = 0f;

        float afterLodge = CenteredWrapped(
            builder, shaper, data.LodgeName, style.Display, style.LineSpacing, context.WidthPt, y,
            MaxLodgeNameLines);
        y = Advance(y, afterLodge);

        float afterHeading = CenteredLine(
            builder, shaper, data.HeadingText, style.Heading, style.LineSpacing, context.WidthPt, y);
        y = Advance(y, afterHeading);

        // Unconditional (docs/M7-spec.md §9.6 step 3) — the rule separates the identity block above
        // from the meeting particulars below even when one side is sparse.
        builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
        y += style.RuleWidthPt + WidgetLayoutHelpers.GroupGapPt;

        float afterDate = CenteredLine(
            builder, shaper, data.MeetingDateText, style.Emphasis, style.LineSpacing, context.WidthPt, y);
        y = Advance(y, afterDate);

        string timesText = BuildTimesText(data);
        float afterTimes = CenteredLine(
            builder, shaper, timesText, style.Body, style.LineSpacing, context.WidthPt, y);
        y = Advance(y, afterTimes);

        builder.Grow(y);

        // Empty means "nothing to identify this issue by" — the lodge's name and the date it prints
        // (docs/M7-spec.md §9.6 step 6). The heading and times are decoration around those two facts.
        bool isEmpty = string.IsNullOrWhiteSpace(data.LodgeName) && string.IsNullOrWhiteSpace(data.MeetingDateText);
        return builder.Build(isEmpty, "Cover heading — not filled in yet.");
    }

    /// <summary>
    /// "Dinner at {Dinner} · Lodge opens at {Work}" when both are present, one clause alone when only
    /// one is, and "" (the whole line omitted) when neither is — the March/April issues carry no times
    /// at all (docs/M7-spec.md §9.6 step 5).
    /// </summary>
    private static string BuildTimesText(CoverBannerData data)
    {
        bool hasDinner = !string.IsNullOrWhiteSpace(data.DinnerTimeText);
        bool hasWork = !string.IsNullOrWhiteSpace(data.WorkTimeText);
        return (hasDinner, hasWork) switch
        {
            (true, true) => string.Create(
                CultureInfo.InvariantCulture,
                $"Dinner at {data.DinnerTimeText} · Lodge opens at {data.WorkTimeText}"),
            (true, false) => string.Create(CultureInfo.InvariantCulture, $"Dinner at {data.DinnerTimeText}"),
            (false, true) => string.Create(CultureInfo.InvariantCulture, $"Lodge opens at {data.WorkTimeText}"),
            _ => "",
        };
    }

    /// <summary>One centred line, or nothing at all when <paramref name="text"/> is blank.</summary>
    private static float CenteredLine(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        string text,
        CharacterStyle style,
        float lineSpacing,
        float widthPt,
        float topPt)
    {
        if (string.IsNullOrEmpty(text))
        {
            return topPt;
        }

        WidgetLineMetrics metrics = shaper.GetLineMetrics(style, lineSpacing);
        builder.TextLine(
            shaper.ShapeAligned(text, style, 0f, widthPt, topPt + metrics.AscentPt, TextAlign.Center),
            topPt + metrics.LineHeightPt);
        return topPt + metrics.LineHeightPt;
    }

    /// <summary>Wraps and centres each line, drawing at most <paramref name="maxLines"/> of them.</summary>
    private static float CenteredWrapped(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        string text,
        CharacterStyle style,
        float lineSpacing,
        float widthPt,
        float topPt,
        int maxLines)
    {
        if (string.IsNullOrEmpty(text))
        {
            return topPt;
        }

        WidgetLineMetrics metrics = shaper.GetLineMetrics(style, lineSpacing);
        IReadOnlyList<string> lines = shaper.WrapToWidth(text, style, widthPt, widthPt);
        int count = Math.Min(lines.Count, maxLines);

        float y = topPt;
        for (int i = 0; i < count; i++)
        {
            builder.TextLine(
                shaper.ShapeAligned(lines[i], style, 0f, widthPt, y + metrics.AscentPt, TextAlign.Center),
                y + metrics.LineHeightPt);
            y += metrics.LineHeightPt;
        }

        return y;
    }

    /// <summary>Adds the group gap after content that actually drew something; leaves the cursor put otherwise.</summary>
    private static float Advance(float previousY, float newY) =>
        newY > previousY ? newY + WidgetLayoutHelpers.GroupGapPt : previousY;
}
