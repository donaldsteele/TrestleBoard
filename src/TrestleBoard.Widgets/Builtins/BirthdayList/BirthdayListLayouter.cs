using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.Widgets.Builtins.BirthdayList;

/// <summary>
/// A narrow single column: name left, "M/D" right-aligned, one line, no leaders — the one canonical
/// format that ends the per-issue drift the examples showed (docs/M7-spec.md §9.2). Sorting happens
/// here, never in the data, so the stored order is whatever the user typed and re-editing shows their
/// own order back.
/// </summary>
public sealed class BirthdayListLayouter : IWidgetLayouter
{
    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (BirthdayListData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);
        if (data.Heading.Length > 0)
        {
            builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt + WidgetLayoutHelpers.GroupGapPt;
        }

        List<BirthdayEntry> ordered = Order(data);
        if (ordered.Count > 0)
        {
            // The date column is fixed-width (the widest formatted date); the name column takes the
            // remainder and wraps — the date always stays on the row's first line (docs/M7-spec.md §9.2).
            float dateWidth = WidgetLayoutHelpers.MaxWidth(shaper, ordered.Select(FormatDate), style.Body);
            IReadOnlyList<float?> widths = [null, dateWidth];
            IReadOnlyList<TableColumn> columns = TableLayouter.Columns(0f, context.WidthPt, widths);

            var rows = new List<IReadOnlyList<TableCell>>(ordered.Count);
            foreach (BirthdayEntry entry in ordered)
            {
                rows.Add(
                [
                    new TableCell(entry.Name, style.Body, TextAlign.Left),
                    new TableCell(FormatDate(entry), style.Body, TextAlign.Right),
                ]);
            }

            // ruleWidthPt = 0: no separator between rows — the examples are a plain list, not a table.
            y = TableLayouter.Rows(builder, shaper, rows, columns, style.LineSpacing, y, 0f, style.RuleArgb);
        }

        if (!string.IsNullOrWhiteSpace(data.ClosingNote))
        {
            y = WidgetLayoutHelpers.WrappedParagraph(
                builder,
                shaper,
                data.ClosingNote,
                style.Small,
                style.LineSpacing,
                0f,
                context.WidthPt,
                y + WidgetLayoutHelpers.GroupGapPt);
        }

        builder.Grow(y);
        return builder.Build(ordered.Count == 0, "Birthdays — not filled in yet.");
    }

    /// <summary>Canonical "M/D", no leading zeros, invariant culture — the same helper the wizard's
    /// date field uses, so what is typed is exactly what prints.</summary>
    private static string FormatDate(BirthdayEntry entry) => WizardValidators.FormatMonthDay(entry.Month, entry.Day);

    /// <summary>Total order (Month, Day, Name ordinal) — an explicit tiebreak so the sort is
    /// deterministic across OSes (docs/M7-spec.md §4.5). The source list is never mutated.</summary>
    private static List<BirthdayEntry> Order(BirthdayListData data)
    {
        var ordered = new List<BirthdayEntry>(data.Entries);
        ordered.Sort(CompareEntries);
        return ordered;
    }

    private static int CompareEntries(BirthdayEntry a, BirthdayEntry b)
    {
        int month = a.Month.CompareTo(b.Month);
        if (month != 0)
        {
            return month;
        }

        int day = a.Day.CompareTo(b.Day);
        return day != 0 ? day : string.CompareOrdinal(a.Name, b.Name);
    }
}
