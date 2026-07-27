using System.Globalization;
using ClosedXML.Excel;

namespace TrestleBoard.Roster;

/// <summary>
/// "Save as a spreadsheet…" (PLAN.md §11 M12).
///
/// Two decisions carry this whole file:
///
/// <b>Every cell is written as text.</b> A real date cell forces a year onto a birthday that has
/// none and shows it as 7/4/1900; a numeric phone number becomes 8.03555E+09 and loses its leading
/// digits' meaning entirely. Both are things the user would have to undo by hand, in a file they
/// only opened to check a phone number.
///
/// <b>The ID column is written.</b> That is what makes export → edit in Excel → re-import lossless
/// even when a name changed in between, and export-then-edit is the workflow a lodge secretary will
/// actually use. It is also why the importer recognises "TrestleBoard ID" wherever it sits, and
/// never offers it as one of the user's own fields.
///
/// Export writes only to a path the user browsed to (PLAN.md §0 rule 5). There is no default
/// location beside the repository or the newsletter.
/// </summary>
public static class RosterExport
{
    public const string SheetName = "People";

    internal static readonly string[] Headers =
    [
        "TrestleBoard ID", "Name", "Birthday", "Phone", "Email", "Office", "Raised or initiated",
        "Date", "Active",
    ];

    /// <summary>"Lodge-address-book-2026-07-27.xlsx" — dated, because a lodge keeps the old ones.</summary>
    public static string SuggestedFileName(DateTimeOffset today) =>
        "Lodge-address-book-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xlsx";

    public static void Save(RosterBook book, string path)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var workbook = new XLWorkbook();
        IXLWorksheet sheet = workbook.AddWorksheet(SheetName);

        for (int column = 0; column < Headers.Length; column++)
        {
            IXLCell cell = sheet.Cell(1, column + 1);
            cell.SetValue(Headers[column]);
            cell.Style.Font.Bold = true;
        }

        int row = 2;
        foreach (Member member in book.InListOrder())
        {
            Write(sheet, row, 1, member.Id);
            Write(sheet, row, 2, member.DisplayName);
            Write(sheet, row, 3, member.BirthdayText);
            Write(sheet, row, 4, member.Phone ?? string.Empty);
            Write(sheet, row, 5, member.Email ?? string.Empty);
            Write(sheet, row, 6, member.Office ?? string.Empty);
            Write(sheet, row, 7, DegreeKindText(member.DegreeKind));
            Write(sheet, row, 8, member.DegreeDate ?? string.Empty);
            Write(sheet, row, 9, member.IsActive ? "Yes" : "No");
            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    /// <summary>
    /// One cell, forced to text. <c>SetValue(string)</c> alone is not enough — a string that parses
    /// as a number or a date is converted on the way in unless the cell says otherwise, which is
    /// exactly the case for every birthday and most phone numbers.
    /// </summary>
    private static void Write(IXLWorksheet sheet, int row, int column, string value)
    {
        IXLCell cell = sheet.Cell(row, column);
        cell.Style.NumberFormat.Format = "@";
        cell.SetValue(XLCellValue.FromObject(value, CultureInfo.InvariantCulture));
    }

    private static string DegreeKindText(string? kind) => kind switch
    {
        DegreeKind.Raised => "Raised",
        DegreeKind.Initiated => "Initiated",
        _ => string.Empty,
    };
}
