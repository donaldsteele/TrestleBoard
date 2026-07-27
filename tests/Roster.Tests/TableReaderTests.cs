using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// Reading the file the committee already keeps (PLAN.md §11 M12, "known import hazards"). Each
/// hazard listed there is a fixture here, because each of them is something a real spreadsheet does
/// and none of them is something a hand-written example would have shown us.
///
/// The fixtures are the only roster-shaped files in the repository (PLAN.md §0 rule 5) and every
/// person in them is fictional.
/// </summary>
public sealed class TableReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void AHundredPeopleReadBackAsAHundredRowsAndSixColumns()
    {
        TableWorkbook workbook = TableFileReader.Read(Fixture("members-100.csv"));

        TableSheet sheet = Assert.Single(workbook.Sheets);
        Assert.Equal(101, sheet.RowCount);
        Assert.Equal(6, sheet.ColumnCount);
        Assert.Equal("Name", sheet.Cell(0, 0));
        Assert.Equal("Aaron Placeholder", sheet.Cell(1, 0));
        Assert.Equal("555-0101", sheet.Cell(1, 2));
    }

    /// <summary>
    /// The BOM, CRLF, an embedded newline inside quotes, a comma inside quotes, doubled quotes, and
    /// Excel's ="…" text idiom — all in one file, because that is how they arrive.
    /// </summary>
    [Fact]
    public void TheAwkwardCsvIsReadTheWayAPersonWouldReadIt()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("members-hazards.csv")).Sheets[0];

        Assert.Equal("Name", sheet.Cell(0, 0));
        Assert.Equal("Placeholder, Aaron", sheet.Cell(1, 0));

        // Not ="555-0100": the equals sign is the spreadsheet saying "this is text".
        Assert.Equal("555-0100", sheet.Cell(1, 2));
        Assert.Equal("Likes commas, semicolons; and quotes", sheet.Cell(1, 4));

        Assert.Equal("Bertram Sample", sheet.Cell(2, 0));
        Assert.Contains("\r\n", sheet.Cell(2, 4), StringComparison.Ordinal);

        Assert.Equal("He said \"hello\" once", sheet.Cell(3, 4));
        Assert.Equal("Dorian Fictitious", sheet.Cell(4, 0));
    }

    /// <summary>European Excel writes semicolons, and a file read with the wrong delimiter is one column wide.</summary>
    [Fact]
    public void ASemicolonFileIsNotReadAsOneEnormousColumn()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("members-semicolon.csv")).Sheets[0];

        Assert.Equal(4, sheet.ColumnCount);
        Assert.Equal("Aaron Placeholder", sheet.Cell(1, 0));
        Assert.Equal("aaron@example.invalid", sheet.Cell(1, 3));
    }

    [Fact]
    public void AFileWithNoHeaderRowAndNoFinalNewlineStillReads()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("members-headerless.csv")).Sheets[0];

        Assert.Equal(2, sheet.RowCount);
        Assert.Equal("Bertram Sample", sheet.Cell(1, 0));
    }

    /// <summary>
    /// The spreadsheet fixture is produced by LibreOffice, not by our own writer — round-tripping
    /// our writer would prove nothing about the reader (PLAN.md §11 M12, testing).
    /// </summary>
    [Fact]
    public void TheSpreadsheetAndTheCsvOfTheSameListAgree()
    {
        TableSheet csv = TableFileReader.Read(Fixture("members-100.csv")).Sheets[0];
        TableSheet xlsx = TableFileReader.Read(Fixture("members-100.xlsx")).Sheets[0];

        Assert.Equal(csv.RowCount, xlsx.RowCount);
        for (int row = 1; row < csv.RowCount; row++)
        {
            Assert.Equal(csv.Cell(row, 0), xlsx.Cell(row, 0));
            Assert.Equal(csv.Cell(row, 3), xlsx.Cell(row, 3));

            // The birthday column may arrive as text from one and as a date cell from the other;
            // what has to agree is the birthday, which is the whole point.
            Assert.True(FieldValues.TryReadBirthday(csv.Cell(row, 1), out int csvMonth, out int csvDay));
            Assert.True(FieldValues.TryReadBirthday(xlsx.Cell(row, 1), out int xlsxMonth, out int xlsxDay));
            Assert.Equal((csvMonth, csvDay), (xlsxMonth, xlsxDay));
        }
    }

    /// <summary>
    /// The single most predictable failure in this milestone: a Birthday column of real date cells.
    /// The fixture is hand-written OOXML in the shape Excel writes — shared strings, and numeric
    /// cells carrying a date number format.
    /// </summary>
    [Fact]
    public void DateCellsBecomeDatesRatherThanFiveDigitNumbers()
    {
        TableSheet sheet = TableFileReader.Read(Fixture("excel-dates.xlsx")).Sheets[0];

        Assert.Equal("Members", sheet.Name);
        Assert.Equal("Birthday", sheet.Cell(0, 1));
        Assert.Equal("2023-07-16", sheet.Cell(1, 1));
        Assert.Equal("1969-01-01", sheet.Cell(2, 1));
        Assert.Equal("555-0100", sheet.Cell(1, 2));
    }

    [Fact]
    public void AnOldSpreadsheetIsRefusedInWordsTheUserCanActOn()
    {
        TableReadException error = Assert.Throws<TableReadException>(
            () => TableFileReader.Read(Fixture("old-format.xls")));

        Assert.Equal(XlsxTableReader.OldFormatMessage, error.Message);
        Assert.Contains("Save As", error.Message, StringComparison.Ordinal);
    }

    /// <summary>An encrypted .xlsx is an OLE compound file too — same bytes, different sentence.</summary>
    [Fact]
    public void APasswordProtectedSpreadsheetSaysSoRatherThanFailingObscurely()
    {
        TableReadException error = Assert.Throws<TableReadException>(
            () => TableFileReader.Read(Fixture("password-protected.xlsx")));

        Assert.Equal(XlsxTableReader.ProtectedMessage, error.Message);
    }

    [Fact]
    public void AFileThatIsNotAListAtAllIsRefusedPlainly()
    {
        string path = Path.Combine(Path.GetTempPath(), "tb-not-a-list.docx");
        File.WriteAllText(path, "not a list");
        try
        {
            TableReadException error = Assert.Throws<TableReadException>(() => TableFileReader.Read(path));
            Assert.Contains(".xlsx", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    public void ColumnsAreNamedTheWayASpreadsheetNamesThem(int index, string expected) =>
        Assert.Equal(expected, TableWorkbook.ColumnLetter(index));
}
