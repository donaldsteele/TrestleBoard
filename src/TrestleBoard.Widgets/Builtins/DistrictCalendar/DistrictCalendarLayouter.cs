using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.DistrictCalendar;

/// <summary>
/// Prints the meeting-days table, the dated events, or both, depending on <see cref="DistrictCalendarData.Mode"/>
/// (docs/M7-spec.md §9.4). The table is sorted into recurrence order; the events are not sorted at
/// all, because a free-text date cannot be ordered reliably.
/// </summary>
public sealed class DistrictCalendarLayouter : IWidgetLayouter
{
    /// <summary>The gap between the table and the events heading in "Both" mode — the spec's own
    /// figure, distinct from <see cref="WidgetLayoutHelpers.GroupGapPt"/>.</summary>
    private const float BothModeGapPt = 8f;

    /// <summary>Gap between one event and the next.</summary>
    private const float EventGapPt = WidgetLayoutHelpers.GroupGapPt;

    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (DistrictCalendarData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        bool showTable = data.Mode is DistrictCalendarMode.MeetingDays or DistrictCalendarMode.Both;
        bool showEvents = data.Mode is DistrictCalendarMode.Events or DistrictCalendarMode.Both;

        float y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);
        if (data.Heading.Length > 0)
        {
            y += WidgetLayoutHelpers.GroupGapPt;
        }

        if (showTable)
        {
            y = LayoutTable(builder, shaper, style, context.WidthPt, data, y);
        }

        if (showTable && showEvents)
        {
            y += BothModeGapPt;
        }

        if (showEvents)
        {
            y = LayoutEvents(builder, shaper, style, context.WidthPt, data, y);
        }

        builder.Grow(y);

        bool isEmpty = data.Mode switch
        {
            DistrictCalendarMode.MeetingDays => data.Lodges.Count == 0,
            DistrictCalendarMode.Events => data.Events.Count == 0,
            _ => data.Lodges.Count == 0 && data.Events.Count == 0,
        };
        return builder.Build(isEmpty, "District calendar — not filled in yet.");
    }

    /// <summary>
    /// The two-column table: lodge left, meeting day right, sorted by
    /// <c>(MeetingRule.Ordinal, MeetingRule.Day, LodgeName ordinal)</c> — exactly the order the
    /// printed district table uses. An unparseable rule sorts last and prints as typed.
    /// </summary>
    private static float LayoutTable(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        WidgetStyleContext style,
        float widthPt,
        DistrictCalendarData data,
        float topPt)
    {
        float y = builder.Heading(shaper, data.LodgesHeading, style.Heading, style.LineSpacing, topPt);

        List<SortableLodge> ordered = Order(data.Lodges);
        var displayTexts = new List<string>(ordered.Count);
        foreach (SortableLodge lodge in ordered)
        {
            displayTexts.Add(lodge.Parsed ? lodge.Rule.ToString() : lodge.Entry.MeetingRule);
        }

        float dayWidth = WidgetLayoutHelpers.MaxWidth(shaper, displayTexts, style.Emphasis);
        IReadOnlyList<TableColumn> columns = TableLayouter.Columns(0f, widthPt, [null, dayWidth]);

        var rows = new List<IReadOnlyList<TableCell>>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            rows.Add(
            [
                new TableCell(ordered[i].Entry.LodgeName, style.Body, TextAlign.Left),
                new TableCell(displayTexts[i], style.Emphasis, TextAlign.Right),
            ]);
        }

        y = TableLayouter.Rows(builder, shaper, rows, columns, style.LineSpacing, y, style.RuleWidthPt, style.RuleArgb);
        builder.Grow(y);
        return y;
    }

    /// <summary>
    /// One hanging-indent row per event, in STORED order. Row text omits missing parts and their
    /// separators (<see cref="WidgetLayoutHelpers.JoinPresent"/>); the times, when present, follow on
    /// a continuation line in <see cref="WidgetStyleContext.Small"/>.
    /// </summary>
    private static float LayoutEvents(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        WidgetStyleContext style,
        float widthPt,
        DistrictCalendarData data,
        float topPt)
    {
        float y = builder.Heading(shaper, data.EventsHeading, style.Heading, style.LineSpacing, topPt);

        for (int i = 0; i < data.Events.Count; i++)
        {
            DistrictEventEntry entry = data.Events[i];
            string rowText = WidgetLayoutHelpers.JoinPresent(" — ", entry.DateText, entry.HostText, entry.Description);
            if (rowText.Length == 0)
            {
                continue;
            }

            // Measured from what is ACTUALLY on the row. JoinPresent omits the " — " separator when
            // the date is blank, but this measured it regardless — so a date-less event's
            // continuation lines were indented past a separator that was never printed, and hung in
            // mid-air under nothing (review §14.2).
            string prefix = string.IsNullOrWhiteSpace(entry.DateText)
                ? string.Empty
                : entry.DateText + " — ";
            float hangingIndentPt = prefix.Length == 0
                ? 0f
                : Math.Min(shaper.MeasureWidthPt(prefix, style.Body), widthPt * 0.4f);
            y = WidgetLayoutHelpers.WrappedParagraph(
                builder, shaper, rowText, style.Body, style.LineSpacing, 0f, widthPt, y, hangingIndentPt);

            if (!string.IsNullOrWhiteSpace(entry.TimesText))
            {
                y = WidgetLayoutHelpers.WrappedParagraph(
                    builder, shaper, entry.TimesText, style.Small, style.LineSpacing,
                    hangingIndentPt, widthPt, y, 0f);
            }

            if (i < data.Events.Count - 1)
            {
                y += EventGapPt;
            }
        }

        builder.Grow(y);
        return y;
    }

    /// <summary>
    /// Sorts by <c>(parsed?, Ordinal, Day, LodgeName ordinal)</c> — an explicit total order with an
    /// ordinal tiebreak, as determinism requires (docs/M7-spec.md §4.5). Rules that fail to parse
    /// form their own trailing bucket, still ordered deterministically by lodge name.
    /// </summary>
    private static List<SortableLodge> Order(List<DistrictLodgeEntry> lodges)
    {
        var withRule = new List<SortableLodge>(lodges.Count);
        foreach (DistrictLodgeEntry entry in lodges)
        {
            bool parsed = MeetingRule.TryParse(entry.MeetingRule, out MeetingRule rule);
            withRule.Add(new SortableLodge(entry, parsed, rule));
        }

        withRule.Sort(CompareLodges);
        return withRule;
    }

    private static int CompareLodges(SortableLodge a, SortableLodge b)
    {
        if (a.Parsed != b.Parsed)
        {
            // Parsed rules sort before unparseable ones — "sorts LAST" (§9.4).
            return a.Parsed ? -1 : 1;
        }

        if (a.Parsed)
        {
            int byOrdinal = ((int)a.Rule.Ordinal).CompareTo((int)b.Rule.Ordinal);
            if (byOrdinal != 0)
            {
                return byOrdinal;
            }

            int byDay = ((int)a.Rule.Day).CompareTo((int)b.Rule.Day);
            if (byDay != 0)
            {
                return byDay;
            }
        }

        return string.CompareOrdinal(a.Entry.LodgeName, b.Entry.LodgeName);
    }

    private readonly record struct SortableLodge(DistrictLodgeEntry Entry, bool Parsed, MeetingRule Rule);
}
