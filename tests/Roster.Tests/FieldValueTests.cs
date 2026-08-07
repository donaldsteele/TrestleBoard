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

        // A bare year is how a lodge records a degree whose exact day is lost. It used to be read
        // as Excel serial 1968 and stored as a day in 1905; now it is kept as written, because
        // "1968-01-01" would invent a day nobody claimed (review §14.2).
        Assert.Equal("1968", FieldValues.ReadDate("1968"));
    }

    /// <summary>
    /// Review §14.2: the serial floor said 61 in its own documentation and 1 in the code, so every
    /// small number in a birthday column was read as a date. "7" became 7 January and a stray year
    /// "1968" became 21 May 1905 — and because <see cref="ColumnGuesser"/> asks this parser whether
    /// a column looks like birthdays, a column of ages or dues scored as one and pushed the real
    /// birthday column out of the mapping.
    /// </summary>
    [Theory]
    [InlineData("7")]
    [InlineData("12")]
    [InlineData("42")]
    [InlineData("60")]
    [InlineData("1968")]
    [InlineData("2026")]
    public void ASmallBareNumberIsNotABirthday(string text)
    {
        Assert.False(FieldValues.TryReadBirthday(text, out int month, out int day));
        Assert.Equal(0, month);
        Assert.Equal(0, day);
    }

    /// <summary>A real serial still reads as one — the floor is 1900-03-01, not "no serials".</summary>
    [Fact]
    public void ARealSpreadsheetSerialStillReads()
    {
        Assert.True(FieldValues.TryReadBirthday("45123", out int month, out int day));
        Assert.Equal(7, month);
        Assert.Equal(16, day);
    }

    /// <summary>
    /// Review §14.2: "February 29" is somebody's real birthday. The year-less formats were parsed
    /// against the CURRENT year, so in three years out of four the row silently failed to parse and
    /// was dropped — a bug that fixed itself for one year in four, which is the worst way for one
    /// to behave. They are now read against a leap year.
    /// </summary>
    [Theory]
    [InlineData("February 29")]
    [InlineData("Feb 29")]
    [InlineData("29 February")]
    [InlineData("2/29")]
    public void TheTwentyNinthOfFebruaryIsABirthdayInEveryYear(string text)
    {
        Assert.True(FieldValues.TryReadBirthday(text, out int month, out int day));
        Assert.Equal(2, month);
        Assert.Equal(29, day);
    }
}
