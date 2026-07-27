using System.Globalization;
using System.Text;

namespace TrestleBoard.Roster.Tables;

/// <summary>
/// A file the user picked could not be read, with a sentence explaining what to do about it. Every
/// message on this exception is addressed to the person holding the spreadsheet, never to a
/// programmer (PLAN.md §6).
/// </summary>
public sealed class TableReadException : Exception
{
    public TableReadException(string message)
        : base(message)
    {
    }

    public TableReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TableReadException()
        : base("This file could not be read.")
    {
    }
}

/// <summary>One sheet, as a rectangle of text. Nothing here knows what a member is.</summary>
public sealed record TableSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;

    public int ColumnCount => Rows.Count == 0 ? 0 : Rows.Max(r => r.Count);

    /// <summary>The value at a cell, or an empty string — ragged rows are normal in real files.</summary>
    public string Cell(int row, int column) =>
        row >= 0 && row < Rows.Count && column >= 0 && column < Rows[row].Count
            ? Rows[row][column]
            : string.Empty;

    /// <summary>The first few values in a column, which is what lets the user recognise a column
    /// by its data rather than by decoding its header (PLAN.md §11 M12, screen 4).</summary>
    public IReadOnlyList<string> Sample(int column, int firstRow, int count) =>
        Enumerable.Range(firstRow, Math.Max(0, count))
            .Select(r => Cell(r, column))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(count)
            .ToList();
}

/// <summary>
/// A spreadsheet or CSV file the user chose, read into plain text and nothing more (PLAN.md §11 M12).
///
/// Everything downstream — header guessing, column mapping, merging — works on this, so a CSV and an
/// XLSX are literally the same problem after this point. That is what makes the acceptance criterion
/// "import the same list as CSV and as XLSX, get identical results" a property of the design rather
/// than a coincidence.
/// </summary>
public sealed record TableWorkbook(string SourcePath, IReadOnlyList<TableSheet> Sheets)
{
    /// <summary>"A", "B", … "AA". What the mapping screen shows beside each column's header.</summary>
    public static string ColumnLetter(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var text = new StringBuilder();
        for (int n = index; ; n = n / 26 - 1)
        {
            text.Insert(0, (char)('A' + (n % 26)));
            if (n < 26)
            {
                break;
            }
        }

        return text.ToString();
    }

    /// <summary>The invariant text for a number read out of a spreadsheet cell.</summary>
    internal static string Number(double value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
