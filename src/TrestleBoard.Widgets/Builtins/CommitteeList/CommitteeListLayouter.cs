using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.CommitteeList;

/// <summary>
/// A plain list, one row per committee: <c>"{Name}: {member, member, …}{, Note}"</c>, with the name
/// and colon in <see cref="WidgetStyleContext.Emphasis"/> and the rest in
/// <see cref="WidgetStyleContext.Body"/> (docs/M7-spec.md §9.3). Continuation lines hang at
/// <c>min(MeasureWidthPt(Name + ": "), 0.40 × WidthPt)</c> so a long roster reads like body prose,
/// not a ledger.
/// </summary>
public sealed class CommitteeListLayouter : IWidgetLayouter
{
    /// <summary>The hanging indent never exceeds this fraction of the widget's width, so one very
    /// long committee name cannot push every continuation line into the right margin.</summary>
    private const float MaxIndentFraction = 0.40f;

    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (CommitteeListData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);

        if (!string.IsNullOrEmpty(data.NoticeText))
        {
            y = WidgetLayoutHelpers.WrappedParagraph(
                builder, shaper, data.NoticeText, style.Small, style.LineSpacing, 0f, context.WidthPt, y);
        }

        if (data.Heading.Length > 0 || !string.IsNullOrEmpty(data.NoticeText))
        {
            builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt + WidgetLayoutHelpers.GroupGapPt;
        }

        List<CommitteeEntry> committees = data.Committees;
        for (int i = 0; i < committees.Count; i++)
        {
            y = CommitteeRow(builder, shaper, committees[i], style, context.WidthPt, y);
            if (i < committees.Count - 1)
            {
                y += WidgetLayoutHelpers.GroupGapPt;
            }
        }

        builder.Grow(y);

        return builder.Build(committees.Count == 0, "Committees — not filled in yet.");
    }

    /// <summary>
    /// One committee's row. The name-and-colon prefix is shaped as its own
    /// <see cref="WidgetStyleContext.Emphasis"/> run at x=0; the joined members/note text wraps in
    /// <see cref="WidgetStyleContext.Body"/> starting right after the prefix on its first line and at
    /// the (possibly capped) hanging indent on every line after.
    /// A committee with no members and no note prints the name and colon alone — never a trailing
    /// space, never a placeholder member.
    /// </summary>
    private static float CommitteeRow(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        CommitteeEntry entry,
        WidgetStyleContext style,
        float widthPt,
        float topPt)
    {
        string prefix = entry.Name + ": ";
        float prefixWidthPt = shaper.MeasureWidthPt(prefix, style.Emphasis);
        float hangingIndentPt = Math.Min(prefixWidthPt, widthPt * MaxIndentFraction);

        WidgetLineMetrics emphasisMetrics = shaper.GetLineMetrics(style.Emphasis, style.LineSpacing);
        WidgetLineMetrics bodyMetrics = shaper.GetLineMetrics(style.Body, style.LineSpacing);
        float ascentPt = Math.Max(emphasisMetrics.AscentPt, bodyMetrics.AscentPt);
        float lineHeightPt = Math.Max(emphasisMetrics.LineHeightPt, bodyMetrics.LineHeightPt);

        string rest = BuildRestText(entry);
        if (rest.Length == 0)
        {
            // No members, no note: the name and its colon, and nothing else — never a dangling space.
            builder.TextLine(
                shaper.ShapeRun(entry.Name + ":", style.Emphasis, 0f, topPt + ascentPt),
                topPt + lineHeightPt);
            return topPt + lineHeightPt;
        }

        IReadOnlyList<string> lines = shaper.WrapToWidth(
            rest, style.Body, widthPt - prefixWidthPt, widthPt - hangingIndentPt);

        float y = topPt;
        for (int i = 0; i < lines.Count; i++)
        {
            if (i == 0)
            {
                builder.Text(shaper.ShapeRun(prefix, style.Emphasis, 0f, y + ascentPt));
                builder.TextLine(
                    shaper.ShapeRun(lines[0], style.Body, prefixWidthPt, y + ascentPt), y + lineHeightPt);
            }
            else
            {
                builder.TextLine(
                    shaper.ShapeRun(lines[i], style.Body, hangingIndentPt, y + ascentPt), y + lineHeightPt);
            }

            y += lineHeightPt;
        }

        return y;
    }

    /// <summary>Members joined with ", ", then the note appended the same way — either half may be
    /// absent without leaving a stray separator (docs/M7-spec.md §9.3).</summary>
    private static string BuildRestText(CommitteeEntry entry)
    {
        string members = string.Join(", ", entry.Members.Where(m => !string.IsNullOrWhiteSpace(m)));
        return WidgetLayoutHelpers.JoinPresent(", ", members, entry.Note);
    }
}
