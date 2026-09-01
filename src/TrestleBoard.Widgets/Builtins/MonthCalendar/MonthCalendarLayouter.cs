using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets.Builtins.MonthCalendar;

/// <summary>
/// The month drawn as a grid (PLAN.md §11 M84).
///
/// <para><b>A minimum cell height, and it is not a nicety.</b> A seven-by-five grid squeezed into a
/// quarter page gives cells a quarter of an inch tall, which nobody in this newsletter's readership
/// can read. The widget asks for the height it needs and lets the frame grow — "Make it fit what is
/// in it" is one press away, and a calendar too small to read is worse than no calendar.</para>
///
/// <para><b>Sunday first.</b> Every wall calendar in an American lodge hall starts on Sunday, and a
/// grid that starts on Monday is a grid the committee will misread once and distrust thereafter.</para>
///
/// <para>Pure, like every layouter: no clock and no culture. The month comes from the data, the day
/// names are invariant, and the same document draws the same grid on all three operating
/// systems.</para>
/// </summary>
public sealed class MonthCalendarLayouter : IWidgetLayouter
{
    /// <summary>Days across. Sunday through Saturday.</summary>
    private const int DaysAcross = 7;

    /// <summary>
    /// The shortest a cell may be. Big enough for the day number and one short line under it at
    /// the sizes this app's styles use.
    /// </summary>
    private const float MinimumCellHeightPt = 34f;

    /// <summary>Room inside a cell, so nothing touches a rule.</summary>
    private const float CellPaddingPt = 3f;

    /// <summary>Sunday first — see the class remarks.</summary>
    private static readonly string[] DayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (MonthCalendarData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = 0f;
        if (!string.IsNullOrWhiteSpace(data.Heading))
        {
            y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);
        }

        if (!data.KnowsTheMonth)
        {
            // A grid for no month is a grid of empty boxes. The prompt says what to do instead,
            // and the empty-widget machinery already knows how to show it on screen and never in
            // print (docs/M7-spec.md §8.4).
            builder.Grow(y + MinimumCellHeightPt);
            return builder.Build(
                isEmpty: true,
                "This calendar does not know which month it is for yet. "
                + "Use “Which issue is this?” to say.");
        }

        float columnWidth = context.WidthPt / DaysAcross;
        float dayNameHeight = (style.Small.SizePt * style.LineSpacing) + (CellPaddingPt * 2f);

        // The day names, centred over their columns.
        for (int i = 0; i < DaysAcross; i++)
        {
            float left = i * columnWidth;
            builder.Text(shaper.ShapeAligned(
                ShortDayName(DayNames[i], columnWidth, style, shaper),
                style.Emphasis,
                left + CellPaddingPt,
                left + columnWidth - CellPaddingPt,
                y + CellPaddingPt + style.Small.SizePt,
                TextAlign.Center));
        }

        y += dayNameHeight;
        builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
        y += style.RuleWidthPt;

        var first = new DateOnly(data.Year, data.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(data.Year, data.Month);
        int leadingBlanks = (int)first.DayOfWeek;
        int weeks = (int)Math.Ceiling((leadingBlanks + daysInMonth) / (double)DaysAcross);

        Dictionary<int, List<string>> byDay = EntriesByDay(data);
        float gridTop = y;

        for (int week = 0; week < weeks; week++)
        {
            float rowTop = y;
            float rowHeight = MinimumCellHeightPt;

            for (int column = 0; column < DaysAcross; column++)
            {
                int day = (week * DaysAcross) + column - leadingBlanks + 1;
                if (day < 1 || day > daysInMonth)
                {
                    continue;
                }

                float left = column * columnWidth;
                float right = left + columnWidth;

                builder.Text(shaper.ShapeRun(
                    day.ToString(CultureInfo.InvariantCulture),
                    style.Emphasis,
                    left + CellPaddingPt,
                    rowTop + CellPaddingPt + style.Emphasis.SizePt));

                float lineY = rowTop + CellPaddingPt + (style.Emphasis.SizePt * style.LineSpacing);
                foreach (string what in byDay.TryGetValue(day, out List<string>? entries) ? entries : [])
                {
                    foreach (string line in shaper.WrapToWidth(
                        what, style.Small, columnWidth - (CellPaddingPt * 2f), columnWidth - (CellPaddingPt * 2f)))
                    {
                        lineY += style.Small.SizePt * style.LineSpacing;
                        builder.Text(shaper.ShapeRun(line, style.Small, left + CellPaddingPt, lineY));
                    }
                }

                rowHeight = Math.Max(rowHeight, lineY - rowTop + CellPaddingPt);
            }

            y = rowTop + rowHeight;
            builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt;
        }

        // The seven verticals, drawn once over the whole grid rather than per cell: a rule per cell
        // is the same line drawn five times, and the overlaps show at low zoom.
        for (int i = 1; i < DaysAcross; i++)
        {
            builder.VRule(i * columnWidth, gridTop, y, style.RuleWidthPt, style.RuleArgb);
        }

        builder.Grow(y);
        return builder.Build(
            byDay.Count == 0,
            $"{MonthName(data.Month)} {data.Year} — nothing has been put on the calendar yet.");
    }

    /// <summary>
    /// Everything happening on each day, the stated communication included.
    ///
    /// <para>The meeting is worked out from the lodge's own recurrence rule and the month, never
    /// stored as a date — which is what lets a newsletter carried forward to next month move the
    /// meeting to next month's Tuesday without anybody touching it.</para>
    /// </summary>
    public static Dictionary<int, List<string>> EntriesForTest(MonthCalendarData data) => EntriesByDay(data);

    private static Dictionary<int, List<string>> EntriesByDay(MonthCalendarData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var byDay = new Dictionary<int, List<string>>();

        if (data.KnowsTheMonth && MeetingRule.TryParse(data.MeetingRule, out MeetingRule rule))
        {
            DateOnly meeting = rule.ResolveDate(data.Year, data.Month);
            Add(byDay, meeting.Day, "Stated meeting");
        }

        foreach (CalendarEntry entry in data.FilledEntries())
        {
            if (data.KnowsTheMonth && entry.Day >= 1 && entry.Day <= DateTime.DaysInMonth(data.Year, data.Month))
            {
                Add(byDay, entry.Day, entry.What.Trim());
            }
        }

        return byDay;
    }

    private static void Add(Dictionary<int, List<string>> byDay, int day, string what)
    {
        if (!byDay.TryGetValue(day, out List<string>? entries))
        {
            entries = [];
            byDay[day] = entries;
        }

        entries.Add(what);
    }

    /// <summary>
    /// The longest form of a day name that fits its column: "Wednesday", then "Wed", then "W".
    /// A heading clipped mid-word is a heading that has told the reader nothing.
    /// </summary>
    private static string ShortDayName(
        string full, float columnWidth, WidgetStyleContext style, WidgetTextShaper shaper)
    {
        float room = columnWidth - (CellPaddingPt * 2f);
        foreach (string candidate in new[] { full, full[..3], full[..1] })
        {
            if (shaper.MeasureWidthPt(candidate, style.Emphasis) <= room)
            {
                return candidate;
            }
        }

        return full[..1];
    }

    /// <summary>Invariant, like every month name this app prints: the newsletter is in English.</summary>
    internal static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
            : "this month";
}
