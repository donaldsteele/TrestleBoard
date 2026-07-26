using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.OfficersTable;

/// <summary>
/// Three columns — position, name, phone — with the phone column collapsing when no office carries
/// one (docs/M7-spec.md §9.1). Vacancies keep their row: the printed table always has twelve.
/// </summary>
public sealed class OfficersTableLayouter : IWidgetLayouter
{
    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (OfficersTableData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);
        if (data.Heading.Length > 0)
        {
            builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt + WidgetLayoutHelpers.GroupGapPt;
        }

        List<OfficerEntry> ordered = Order(data);
        bool anyPhone = ordered.Any(o => !string.IsNullOrWhiteSpace(o.Phone));

        float positionWidth = Math.Min(
            WidgetLayoutHelpers.MaxWidth(shaper, ordered.Select(o => o.Position), style.Emphasis),
            context.WidthPt * 0.45f);
        float phoneWidth = anyPhone
            ? WidgetLayoutHelpers.MaxWidth(shaper, ordered.Select(o => o.Phone ?? ""), style.Body)
            : 0f;

        IReadOnlyList<float?> widths = anyPhone
            ? [positionWidth, null, phoneWidth]
            : [positionWidth, null];
        IReadOnlyList<TableColumn> columns = TableLayouter.Columns(0f, context.WidthPt, widths);

        var rows = new List<IReadOnlyList<TableCell>>(ordered.Count);
        foreach (OfficerEntry officer in ordered)
        {
            string name = string.IsNullOrWhiteSpace(officer.Name) ? data.VacantText : officer.Name;
            var cells = new List<TableCell>
            {
                new(officer.Position, style.Emphasis, TextAlign.Left),
                new(name, style.Body, TextAlign.Left),
            };
            if (anyPhone)
            {
                cells.Add(new TableCell(officer.Phone ?? "", style.Body, TextAlign.Right));
            }

            rows.Add(cells);
        }

        y = TableLayouter.Rows(
            builder, shaper, rows, columns, style.LineSpacing, y, style.RuleWidthPt, style.RuleArgb);
        builder.Grow(y);

        bool isEmpty = ordered.Count == 0 || ordered.All(o => string.IsNullOrWhiteSpace(o.Name));
        return builder.Build(isEmpty, "Lodge officers — not filled in yet.");
    }

    /// <summary>
    /// The twelve standard offices first, in their printed order, then anything else in stored order
    /// (a lodge that adds an office in a later version still prints).
    /// </summary>
    private static List<OfficerEntry> Order(OfficersTableData data)
    {
        var ordered = new List<OfficerEntry>(data.Officers.Count);
        var taken = new bool[data.Officers.Count];
        foreach (string position in OfficersTableData.StandardPositions)
        {
            int index = -1;
            for (int i = 0; i < data.Officers.Count; i++)
            {
                if (!taken[i] && string.Equals(data.Officers[i].Position, position, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                taken[index] = true;
                ordered.Add(data.Officers[index]);
            }
            else
            {
                ordered.Add(new OfficerEntry { Position = position });
            }
        }

        for (int i = 0; i < data.Officers.Count; i++)
        {
            if (!taken[i])
            {
                ordered.Add(data.Officers[i]);
            }
        }

        return ordered;
    }
}
