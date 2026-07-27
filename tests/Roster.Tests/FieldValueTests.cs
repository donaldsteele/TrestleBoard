using TrestleBoard.Roster.Import;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// Turning a cell into a birthday (PLAN.md §11 M12). The date-serial table is the one PLAN.md asks
/// for by name, including the two rows either side of Excel's 1900 leap-year mistake — serial 60 is
/// a day that never happened, and every serial below it is one out from the true count.
/// </summary>
public sealed class FieldValueTests
{
    [Theory]
    [InlineData(1, "1900-01-01")]
    [InlineData(59, "1900-02-28")]
    [InlineData(60, "1900-02-28")]
    [InlineData(61, "1900-03-01")]
    [InlineData(25204, "1969-01-01")]
    [InlineData(45123, "2023-07-16")]
    [InlineData(2958465, "9999-12-31")]
    public void ExcelSerialsBecomeTheDatesTheSpreadsheetShows(double serial, string expected) =>
        Assert.Equal(expected, FieldValues.FromExcelSerial(serial).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("7/4", 7, 4)]
    [InlineData("7-4", 7, 4)]
    [InlineData("12/25", 12, 25)]
    [InlineData("7/4/1968", 7, 4)]
    [InlineData("1968-07-04", 7, 4)]
    [InlineData("July 4", 7, 4)]
    [InlineData("Jul 4", 7, 4)]
    [InlineData("4 July", 7, 4)]
    [InlineData(" 3/14 ", 3, 14)]
    [InlineData("45123", 7, 16)]
    public void ABirthdayIsReadFromEveryShapeARealListUses(string text, int month, int day)
    {
        Assert.True(FieldValues.TryReadBirthday(text, out int readMonth, out int readDay));
        Assert.Equal((month, day), (readMonth, readDay));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("sometime in July")]
    [InlineData("13/40")]
    public void SomethingThatIsNotABirthdayIsNotGuessedAt(string? text) =>
        Assert.False(FieldValues.TryReadBirthday(text, out _, out _));

    [Fact]
    public void APhoneNumberIsKeptExactlyAsItWasWritten()
    {
        Assert.Equal("(803) 555-0100", FieldValues.ReadPhone(" (803) 555-0100 "));
        Assert.Equal("555-0100", FieldValues.ReadPhone("555-0100"));
        Assert.Null(FieldValues.ReadPhone("   "));
    }

    /// <summary>A phone number a spreadsheet turned into a number comes back readable, not as 8.0355E+09.</summary>
    [Fact]
    public void APhoneNumberInScientificNotationIsPutBackTogether() =>
        Assert.Equal("8035550100", FieldValues.ReadPhone("8.0355501E+09"));

    [Theory]
    [InlineData("Raised", "raised")]
    [InlineData("raised MM", "raised")]
    [InlineData("Initiated", "initiated")]
    [InlineData("EA initiated", "initiated")]
    [InlineData("something else", null)]
    [InlineData("", null)]
    public void TheDegreeKindIsReadGenerouslyBecauseLodgesWriteItDifferently(string text, string? expected) =>
        Assert.Equal(expected, FieldValues.ReadDegreeKind(text));

    [Fact]
    public void ADegreeDateIsStoredUnambiguously()
    {
        Assert.Equal("1995-05-01", FieldValues.ReadDate("5/1/1995"));
        Assert.Equal("2023-07-16", FieldValues.ReadDate("45123"));

        // Not recognised is not the same as not wanted: the user typed it, so it is kept verbatim.
        Assert.Equal("some time in 1995", FieldValues.ReadDate("some time in 1995"));
        Assert.Null(FieldValues.ReadDate(" "));
    }
}
