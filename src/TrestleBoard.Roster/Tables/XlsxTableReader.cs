using System.Globalization;
using ClosedXML.Excel;

namespace TrestleBoard.Roster.Tables;

/// <summary>
/// Reads a real spreadsheet (PLAN.md §1, §11 M12) — the file the committee already keeps, whatever
/// program made it.
///
/// The one thing this type exists to get right is <b>dates</b>. A "Birthday" column in a spreadsheet
/// is very often a real date cell, which stores 45123 and merely *shows* 7/4. Reading the displayed
/// text would depend on the reader's locale; reading the number would store 45123 as somebody's
/// birthday. So a date cell is converted here, once, to an unambiguous ISO string, and everything
/// downstream sees text.
/// </summary>
public static class XlsxTableReader
{
    /// <summary>The first eight bytes of an OLE compound file — an old .xls, or an encrypted .xlsx.</summary>
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public const string OldFormatMessage =
        "This is an older kind of spreadsheet. Open it in Excel and choose File, then Save As, then "
        + "Excel Workbook (.xlsx). Then try again.";

    public const string ProtectedMessage =
        "This spreadsheet is protected with a password. Open it in Excel, save a copy without the "
        + "password, and try again with that copy.";

    public static TableWorkbook Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        bool compound = LooksLikeCompoundFile(path);
        if (compound)
        {
            // Same bytes, two different things to tell the user: a genuinely old .xls needs saving
            // in the newer format, whereas an .xlsx in this shape is an encrypted one.
            throw new TableReadException(
                path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ? OldFormatMessage : ProtectedMessage);
        }

        if (path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
        {
            throw new TableReadException(OldFormatMessage);
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(path);
        }
        catch (Exception e) when (e is not TableReadException)
        {
            throw new TableReadException(
                "TrestleBoard could not read that spreadsheet. If it opens in Excel, try File, then "
                + "Save As, then Excel Workbook (.xlsx), and use that copy.",
                e);
        }

        using (workbook)
        {
            var sheets = new List<TableSheet>();
            foreach (IXLWorksheet sheet in workbook.Worksheets)
            {
                sheets.Add(new TableSheet(sheet.Name, ReadRows(sheet)));
            }

            if (sheets.Count == 0)
            {
                throw new TableReadException("That spreadsheet has no sheets in it.");
            }

            return new TableWorkbook(path, sheets);
        }
    }

    private static List<IReadOnlyList<string>> ReadRows(IXLWorksheet sheet)
    {
        var rows = new List<IReadOnlyList<string>>();
        IXLRange? used = sheet.RangeUsed();
        if (used is null)
        {
            return rows;
        }

        int lastColumn = used.LastColumn().ColumnNumber();
        int firstColumn = used.FirstColumn().ColumnNumber();
        int firstRow = used.FirstRow().RowNumber();
        int lastRow = used.LastRow().RowNumber();

        for (int r = firstRow; r <= lastRow; r++)
        {
            var values = new List<string>(lastColumn - firstColumn + 1);
            for (int c = firstColumn; c <= lastColumn; c++)
            {
                values.Add(CellText(sheet.Cell(r, c)));
            }

            rows.Add(values);
        }

        return rows;
    }

    /// <summary>
    /// One cell as text, with every path culture-invariant. Two machines reading the same file must
    /// see the same strings, exactly as the layout engine's determinism rule demands of everything
    /// else in this project (PLAN.md §3).
    /// </summary>
    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        switch (cell.DataType)
        {
            case XLDataType.DateTime:
                return cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            case XLDataType.Number:
                return TableWorkbook.Number(cell.GetDouble());

            case XLDataType.Boolean:
                return cell.GetBoolean() ? "true" : "false";

            default:
                try
                {
                    return cell.GetString().Trim();
                }
                catch (Exception e) when (e is FormatException or InvalidCastException)
                {
                    return string.Empty;
                }
        }
    }

    private static bool LooksLikeCompoundFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[CompoundFileSignature.Length];
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
                && head.SequenceEqual(CompoundFileSignature);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TableReadException(
                "TrestleBoard could not open that file. Check that it is not open in another "
                + "program, then try again.",
                e);
        }
    }
}
