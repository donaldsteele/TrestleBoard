using System.Globalization;

namespace TrestleBoard.Roster.Import;

/// <summary>
/// Turning what a cell says into what the address book stores (PLAN.md §11 M12).
///
/// The birthday parser is the sharp end of this milestone. PLAN.md names Excel date serials as the
/// single most predictable import failure, and it is worth being precise about why: a spreadsheet's
/// "Birthday" column is usually a real date cell, which stores the number of days since 1899-12-30
/// and merely *shows* 7/4. Export that to CSV in the wrong way and 45123 is what lands in the file.
/// Stored naively, the lodge's newsletter prints a number.
/// </summary>
public static class FieldValues
{
    /// <summary>
    /// Excel's own epoch, and its famous mistake: 1900 is treated as a leap year, so serial 60 is
    /// the day that never existed. Serials below it are one day out from the real count, which is
    /// why the epoch shifts rather than the arithmetic.
    /// </summary>
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private static readonly DateTime ExcelEpochBeforeTheLeapBug = new(1899, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>1900-03-01 onwards. Below 61 the sheet and the calendar disagree; above 2958465 it is not a date.</summary>
    private const double SmallestPlausibleSerial = 1;

    private const double LargestSerial = 2958465;

    private static readonly string[] BirthdayFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "M/d/yyyy", "M-d-yyyy", "M.d.yyyy",
        "MMMM d", "MMM d", "d MMMM", "d MMM", "MMMM d yyyy", "MMM d yyyy",
    ];

    /// <summary>
    /// Reads a birthday as month and day, from any of the shapes a real list uses: "7/4", "7/4/1968",
    /// "July 4", "1968-07-04", or the bare serial number "45123".
    /// </summary>
    public static bool TryReadBirthday(string? text, out int month, out int day)
    {
        month = 0;
        day = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim().Replace(",", string.Empty, StringComparison.Ordinal);

        // A bare number is an Excel serial. Nothing else in a birthday column is a plain number:
        // "7" alone is not a birthday, and this is exactly the failure PLAN.md flags.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
            && serial >= SmallestPlausibleSerial
            && serial <= LargestSerial
            && !value.Contains('/', StringComparison.Ordinal)
            && !value.Contains('-', StringComparison.Ordinal))
        {
            DateTime fromSerial = FromExcelSerial(serial);
            month = fromSerial.Month;
            day = fromSerial.Day;
            return true;
        }

        if (DateTime.TryParseExact(
                value,
                BirthdayFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            month = parsed.Month;
            day = parsed.Day;
            return true;
        }

        // "7/4" and "7-4": month and day with no year, the shape a birthday list is usually in.
        string[] parts = value.Split(['/', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int m)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int d)
            && m is >= 1 and <= 12
            && d is >= 1 and <= 31)
        {
            month = m;
            day = d;
            return true;
        }

        // A month name with a day, in either order: "July 4", "4 July".
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault,
                out DateTime loose)
            && loose.Year != 1)
        {
            month = loose.Month;
            day = loose.Day;
            return true;
        }

        return false;
    }

    /// <summary>The date a spreadsheet serial means, leap-year bug and all.</summary>
    public static DateTime FromExcelSerial(double serial)
    {
        double whole = Math.Floor(serial);
        return whole < 60
            ? ExcelEpochBeforeTheLeapBug.AddDays(whole)
            : ExcelEpoch.AddDays(whole);
    }

    /// <summary>
    /// A degree date, kept as an ISO string. Serials are handled here too — a "Raised" column is a
    /// real date far more often than a birthday column is.
    /// </summary>
    public static string? ReadDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string value = text.Trim();
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double serial)
            && serial is >= SmallestPlausibleSerial and <= LargestSerial)
        {
            return FromExcelSerial(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
        {
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Unrecognised, but not discarded: the user typed it, so it is kept as they wrote it.
        return value;
    }

    /// <summary>
    /// A phone number is kept as the user wrote it, with only surrounding whitespace and Excel's
    /// text markers removed. Reformatting somebody's phone number is not the app's business, and a
    /// number that reads differently from the sheet it came from is a number the user distrusts.
    /// </summary>
    public static string? ReadPhone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string value = text.Trim();

        // A phone number that arrived as a number, e.g. 8035550100 or 8.0355e+09. The second is
        // what a spreadsheet does to a phone number with no formatting, and is unrecoverable —
        // but the plain integer form is worth keeping readable.
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            && value.Contains('E', StringComparison.OrdinalIgnoreCase))
        {
            value = TableWorkbookNumber(number);
        }

        return value;
    }

    /// <summary>Trimmed and lower-cased, because that is what it is compared by (see the merge policy).</summary>
    public static string? ReadEmail(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// Which of the two degree kinds a column value names, or null. Deliberately generous: a lodge
    /// writes "Raised", "raised MM", "EA initiated" and everything between.
    /// </summary>
    public static string? ReadDegreeKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string value = text.Trim();
        if (value.Contains("rais", StringComparison.OrdinalIgnoreCase))
        {
            return DegreeKind.Raised;
        }

        return value.Contains("init", StringComparison.OrdinalIgnoreCase) ? DegreeKind.Initiated : null;
    }

    private static string TableWorkbookNumber(double value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);
}
