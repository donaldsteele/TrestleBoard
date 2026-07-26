using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The stated-communication recurrence rule (docs/M7-spec.md §0.3). It lives in Core because M9's
/// start-from-last-month recomputes dates from it and must not reach into the widget layer.
/// </summary>
public sealed class MeetingRuleTests
{
    [Theory]
    [InlineData("1st Tuesday", WeekOrdinal.First, DayOfWeek.Tuesday)]
    [InlineData("first tuesday", WeekOrdinal.First, DayOfWeek.Tuesday)]
    [InlineData("2ND MONDAY", WeekOrdinal.Second, DayOfWeek.Monday)]
    [InlineData("3rd Thursday", WeekOrdinal.Third, DayOfWeek.Thursday)]
    [InlineData("4th  Saturday", WeekOrdinal.Fourth, DayOfWeek.Saturday)]
    [InlineData("Last Friday", WeekOrdinal.Last, DayOfWeek.Friday)]
    public void ParsesTheFormsAnEditorActuallyTypes(string text, WeekOrdinal ordinal, DayOfWeek day)
    {
        Assert.True(MeetingRule.TryParse(text, out MeetingRule rule));
        Assert.Equal(new MeetingRule(ordinal, day), rule);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tuesday")]
    [InlineData("5th Tuesday")]
    [InlineData("1st Blursday")]
    [InlineData("every other Tuesday")]
    public void RefusesAnythingElseWithoutThrowing(string? text)
    {
        Assert.False(MeetingRule.TryParse(text, out _));
    }

    [Theory]
    [InlineData(WeekOrdinal.First, DayOfWeek.Tuesday, "1st Tuesday")]
    [InlineData(WeekOrdinal.Second, DayOfWeek.Monday, "2nd Monday")]
    [InlineData(WeekOrdinal.Third, DayOfWeek.Thursday, "3rd Thursday")]
    [InlineData(WeekOrdinal.Fourth, DayOfWeek.Sunday, "4th Sunday")]
    [InlineData(WeekOrdinal.Last, DayOfWeek.Friday, "Last Friday")]
    public void FormatsCanonically(WeekOrdinal ordinal, DayOfWeek day, string expected)
    {
        Assert.Equal(expected, new MeetingRule(ordinal, day).ToString());
    }

    [Fact]
    public void ParsingIsTheInverseOfFormatting()
    {
        foreach (WeekOrdinal ordinal in Enum.GetValues<WeekOrdinal>())
        {
            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            {
                var rule = new MeetingRule(ordinal, day);
                Assert.True(MeetingRule.TryParse(rule.ToString(), out MeetingRule parsed));
                Assert.Equal(rule, parsed);
            }
        }
    }

    [Theory]
    // July 2026 starts on a Wednesday.
    [InlineData(WeekOrdinal.First, DayOfWeek.Tuesday, 2026, 7, 7)]
    [InlineData(WeekOrdinal.First, DayOfWeek.Wednesday, 2026, 7, 1)]
    [InlineData(WeekOrdinal.Second, DayOfWeek.Tuesday, 2026, 7, 14)]
    [InlineData(WeekOrdinal.Last, DayOfWeek.Friday, 2026, 7, 31)]
    // February 2026 has exactly four Sundays, so "4th" and "last" coincide.
    [InlineData(WeekOrdinal.Fourth, DayOfWeek.Sunday, 2026, 2, 22)]
    [InlineData(WeekOrdinal.Last, DayOfWeek.Sunday, 2026, 2, 22)]
    public void ResolvesTheDateM9WillPrint(
        WeekOrdinal ordinal, DayOfWeek day, int year, int month, int expectedDay)
    {
        DateOnly date = new MeetingRule(ordinal, day).ResolveDate(year, month);

        Assert.Equal(new DateOnly(year, month, expectedDay), date);
        Assert.Equal(day, date.DayOfWeek);
    }

    [Fact]
    public void RejectsAMonthThatDoesNotExist()
    {
        var rule = new MeetingRule(WeekOrdinal.First, DayOfWeek.Tuesday);

        Assert.Throws<ArgumentOutOfRangeException>(() => rule.ResolveDate(2026, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rule.ResolveDate(2026, 13));
    }
}
