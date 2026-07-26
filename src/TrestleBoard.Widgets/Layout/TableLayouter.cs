using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;

namespace TrestleBoard.Widgets.Layout;

/// <summary>One cell: its text, its style, and how it sits in its column.</summary>
public readonly record struct TableCell(string Text, CharacterStyle Style, TextAlign Align);

/// <summary>A column's resolved geometry.</summary>
public readonly record struct TableColumn(float LeftPt, float RightPt);

/// <summary>
/// Shared table drawing for OfficersTable and DistrictCalendar (docs/M7-spec.md §12.1). Columns are
/// measured from the data — nothing is truncated or ellipsised, because a printed newsletter must
/// not silently drop a brother's name; over-wide cells wrap inside their column.
/// </summary>
public static class TableLayouter
{
    /// <summary>
    /// Draws rows into pre-computed columns and returns the y below the last row. A hairline is
    /// drawn under every row when <paramref name="ruleWidthPt"/> is positive.
    /// </summary>
    public static float Rows(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        IReadOnlyList<IReadOnlyList<TableCell>> rows,
        IReadOnlyList<TableColumn> columns,
        float lineSpacing,
        float topPt,
        float ruleWidthPt,
        uint ruleArgb)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shaper);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(columns);

        float y = topPt;
        foreach (IReadOnlyList<TableCell> row in rows)
        {
            y = Row(builder, shaper, row, columns, lineSpacing, y);
            if (ruleWidthPt > 0f)
            {
                builder.HRule(y, columns[0].LeftPt, columns[^1].RightPt, ruleWidthPt, ruleArgb);
                y += ruleWidthPt;
            }
        }

        return y;
    }

    /// <summary>One row; the row's height is the tallest cell's, so a wrapped name keeps its rule aligned.</summary>
    public static float Row(
        WidgetDrawListBuilder builder,
        WidgetTextShaper shaper,
        IReadOnlyList<TableCell> cells,
        IReadOnlyList<TableColumn> columns,
        float lineSpacing,
        float topPt)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shaper);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(columns);

        float bottom = topPt;
        for (int i = 0; i < cells.Count && i < columns.Count; i++)
        {
            TableCell cell = cells[i];
            if (string.IsNullOrEmpty(cell.Text))
            {
                continue;
            }

            TableColumn column = columns[i];
            WidgetLineMetrics metrics = shaper.GetLineMetrics(cell.Style, lineSpacing);
            float available = column.RightPt - column.LeftPt;
            IReadOnlyList<string> lines = shaper.MeasureWidthPt(cell.Text, cell.Style) <= available
                ? [cell.Text]
                : shaper.WrapToWidth(cell.Text, cell.Style, available, available);

            float y = topPt;
            foreach (string line in lines)
            {
                builder.TextLine(
                    shaper.ShapeAligned(line, cell.Style, column.LeftPt, column.RightPt, y + metrics.AscentPt, cell.Align),
                    y + metrics.LineHeightPt);
                y += metrics.LineHeightPt;
            }

            if (y > bottom)
            {
                bottom = y;
            }
        }

        if (bottom <= topPt)
        {
            // An entirely blank row still occupies a line, so the printed table keeps its shape.
            bottom = topPt + shaper.GetLineMetrics(cells.Count > 0 ? cells[0].Style : default, lineSpacing).LineHeightPt;
        }

        builder.Grow(bottom);
        return bottom;
    }

    /// <summary>
    /// Lays out columns left to right: fixed-width columns take their measured width, and the one
    /// column with a null width absorbs the remainder.
    /// </summary>
    public static IReadOnlyList<TableColumn> Columns(float leftPt, float rightPt, IReadOnlyList<float?> widths)
    {
        ArgumentNullException.ThrowIfNull(widths);
        float fixedTotal = 0f;
        int flexible = 0;
        foreach (float? width in widths)
        {
            if (width is { } value)
            {
                fixedTotal += value;
            }
            else
            {
                flexible++;
            }
        }

        float gaps = WidgetLayoutHelpers.ColumnGapPt * Math.Max(0, widths.Count - 1);
        float remainder = Math.Max(0f, rightPt - leftPt - fixedTotal - gaps);
        float perFlexible = flexible > 0 ? remainder / flexible : 0f;

        var columns = new List<TableColumn>(widths.Count);
        float x = leftPt;
        for (int i = 0; i < widths.Count; i++)
        {
            float width = widths[i] ?? perFlexible;
            columns.Add(new TableColumn(x, x + width));
            x += width + WidgetLayoutHelpers.ColumnGapPt;
        }

        return columns;
    }
}
