using System;
using System.Collections.Generic;
using System.Linq;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Layout;

namespace TrestleBoard.Widgets.Builtins.SimpleList;

/// <summary>
/// A list of the user's own, drawn as a table (PLAN.md §11 M60).
///
/// <para>Everything here goes through <see cref="TableLayouter"/> — the same columns, the same
/// row rules, the same wrapping the officers table and the district calendar use. A seventh widget
/// that drew its own table would be a seventh set of decisions about column gaps and rule weights,
/// and the page would show it.</para>
///
/// <para>Columns share the width evenly. The officers table measures its columns from its data
/// because it knows what is in them — names and telephone numbers, one much wider than the other.
/// This widget knows nothing about its own content, so it cannot measure with any authority, and an
/// even split is the honest answer rather than a guess dressed up as one.</para>
/// </summary>
public sealed class SimpleListLayouter : IWidgetLayouter
{
    public WidgetDrawList Layout(WidgetLayoutContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var data = (SimpleListData)context.Data;
        WidgetStyleContext style = context.Style;
        WidgetTextShaper shaper = context.Shaper;
        var builder = new WidgetDrawListBuilder(context.WidthPt);

        float y = 0f;
        if (!string.IsNullOrWhiteSpace(data.Heading))
        {
            y = builder.Heading(shaper, data.Heading, style.Heading, style.LineSpacing, 0f);
        }

        int columns = data.ColumnCount;
        IReadOnlyList<TableColumn> geometry = TableLayouter.Columns(
            0f, context.WidthPt, Enumerable.Repeat<float?>(null, columns).ToList());

        if (data.HasColumnHeadings)
        {
            IReadOnlyList<TableCell> header =
            [
                .. data.ColumnHeadings().Select(h => new TableCell(h, style.Emphasis, TextAlign.Left)),
            ];
            y = TableLayouter.Row(builder, shaper, header, geometry, style.LineSpacing, y);
            builder.HRule(y, 0f, context.WidthPt, style.RuleWidthPt, style.RuleArgb);
            y += style.RuleWidthPt;
        }

        IReadOnlyList<SimpleListRow> rows = data.FilledRows();
        if (rows.Count > 0)
        {
            IReadOnlyList<IReadOnlyList<TableCell>> cells =
            [
                .. rows.Select(r => (IReadOnlyList<TableCell>)
                    [.. r.Cells(columns).Select(c => new TableCell(c, style.Body, TextAlign.Left))]),
            ];

            // A hairline under every row but the last, the officers table's shape: the rule between
            // two rows helps the eye track across; a rule under the final row draws a box nobody
            // asked for.
            y = TableLayouter.Rows(
                builder, shaper, cells, geometry, style.LineSpacing, y, style.RuleWidthPt, style.RuleArgb);
        }

        builder.Grow(y);

        return builder.Build(
            rows.Count == 0,
            string.IsNullOrWhiteSpace(data.Heading)
                ? "A list of your own — not filled in yet."
                : $"{data.Heading} — not filled in yet.");
    }
}
