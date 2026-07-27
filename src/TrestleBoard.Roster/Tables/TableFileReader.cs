namespace TrestleBoard.Roster.Tables;

/// <summary>
/// The one entry point the import flow uses: hand it a path, get a workbook or a sentence saying why
/// not (PLAN.md §11 M12).
/// </summary>
public static class TableFileReader
{
    /// <summary>What the file picker offers, newest formats first.</summary>
    public static IReadOnlyList<string> ReadableExtensions => [".xlsx", ".xlsm", ".csv", ".txt", ".tsv"];

    public static TableWorkbook Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new TableReadException(
                "TrestleBoard could not find that file. It may have been moved or renamed since you "
                + "chose it.");
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            // .xlsm is accepted: a macro workbook's data reads exactly like any other, and refusing
            // it would mean telling someone their own file is the wrong sort of file.
            ".xlsx" or ".xlsm" => XlsxTableReader.Read(path),
            ".xls" => throw new TableReadException(XlsxTableReader.OldFormatMessage),
            ".csv" or ".txt" or ".tsv" => CsvTableReader.Read(path),
            _ => throw new TableReadException(
                "TrestleBoard can read spreadsheets saved as .xlsx and lists saved as .csv. "
                + "Open your list in Excel and choose File, then Save As, then one of those."),
        };
    }
}
