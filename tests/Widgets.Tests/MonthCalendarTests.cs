using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.MonthCalendar;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// M84: this month's calendar — the eighth widget.
///
/// <para>The district calendar is a table, which is the right shape for six lodges and the wrong one
/// for one month. What a trestle board wants is the month laid out the way the calendar on the
/// lodge-hall wall is.</para>
/// </summary>
public sealed class MonthCalendarTests
{
    private static readonly MonthCalendarDefinition Definition = new();

    /// <summary>
    /// <b>The month is not asked for.</b> It comes from the issue, so a newsletter carried forward
    /// gets next month's grid with next month's Tuesday marked and nobody re-answers anything.
    /// </summary>
    [Fact]
    public void TheMonthComesFromTheIssueRatherThanBeingAskedFor()
    {
        MonthCalendarData data = Definition.CreateEmpty(
            new WidgetSeed("Indian Land Lodge 414", 9, 2026, "1st Tuesday"));

        Assert.Equal(9, data.Month);
        Assert.Equal(2026, data.Year);
        Assert.Equal("1st Tuesday", data.MeetingRule);
    }

    /// <summary>
    /// PLAN.md §0: structure only. A calendar arrives with no entries at all — the stated meeting is
    /// worked out, never stored, and nothing else is put there on anybody's behalf.
    /// </summary>
    [Fact]
    public void AFreshCalendarHoldsNobodysDetails()
    {
        MonthCalendarData data = Definition.CreateEmpty(
            new WidgetSeed("Indian Land Lodge 414", 9, 2026, "1st Tuesday"));

        Assert.Empty(data.Entries);
        Assert.Equal(string.Empty, data.Heading);
    }

    /// <summary>
    /// M75's hook. A calendar made in August and re-edited after the issue moved to September must
    /// draw September — the exact disagreement the design exists to prevent.
    /// </summary>
    [Fact]
    public void ReEditingPicksUpTheIssuesMonthAgain()
    {
        IWidgetDefinition definition = new MonthCalendarDefinition();
        var data = new MonthCalendarData { Month = 8, Year = 2026, MeetingRule = "1st Tuesday" };

        definition.SeedAnswersFromDocument(data, new WidgetSeed("Indian Land Lodge 414", 9, 2026, "1st Tuesday"));

        Assert.Equal(9, data.Month);
    }

    /// <summary>
    /// <b>The stated meeting is worked out, never stored.</b> September 2026's first Tuesday is the
    /// 1st; October's is the 6th. The same widget, carried forward, marks the right day in each.
    /// </summary>
    [Theory]
    [InlineData(9, 2026, 1)]
    [InlineData(10, 2026, 6)]
    [InlineData(2, 2027, 2)]
    public void TheStatedMeetingLandsOnTheRightDayForTheMonth(int month, int year, int expectedDay)
    {
        var data = new MonthCalendarData { Month = month, Year = year, MeetingRule = "1st Tuesday" };

        Dictionary<int, List<string>> byDay = MonthCalendarLayouter.EntriesForTest(data);

        Assert.Contains("Stated meeting", byDay[expectedDay]);
    }

    /// <summary>A lodge with no recurrence rule written down simply has no meeting marked.</summary>
    [Fact]
    public void NoMeetingRuleMeansNoMeetingMarked()
    {
        var data = new MonthCalendarData { Month = 9, Year = 2026, MeetingRule = "" };

        Assert.Empty(MonthCalendarLayouter.EntriesForTest(data));
    }

    /// <summary>
    /// A day outside the month is dropped rather than drawn somewhere wrong. A 31st typed into a
    /// September calendar is a mistake, and putting it on the 1st of October would be the app
    /// inventing an answer.
    /// </summary>
    [Fact]
    public void ADayThatIsNotInThisMonthIsLeftOut()
    {
        var data = new MonthCalendarData
        {
            Month = 9,
            Year = 2026,
            Entries = [new CalendarEntry { Day = 31, What = "Fish fry" }],
        };

        Assert.DoesNotContain(
            MonthCalendarLayouter.EntriesForTest(data),
            pair => pair.Value.Contains("Fish fry"));
    }

    /// <summary>Two things on one day both appear, in the order they were typed.</summary>
    [Fact]
    public void TwoThingsOnOneDayBothAppear()
    {
        var data = new MonthCalendarData
        {
            Month = 9,
            Year = 2026,
            MeetingRule = "1st Tuesday",
            Entries =
            [
                new CalendarEntry { Day = 1, What = "Supper at six" },
                new CalendarEntry { Day = 1, What = "Degree practice" },
            ],
        };

        Assert.Equal(
            ["Stated meeting", "Supper at six", "Degree practice"],
            MonthCalendarLayouter.EntriesForTest(data)[1]);
    }

    /// <summary>
    /// A widget that does not know its month says so rather than drawing a grid of empty boxes, and
    /// the prompt names the command that fixes it.
    /// </summary>
    [Fact]
    public void WithoutAMonthItSaysSoRatherThanDrawingEmptyBoxes()
    {
        var data = new MonthCalendarData();
        Assert.False(data.KnowsTheMonth);

        WidgetDrawList drawn = WidgetTestData.LayOut(Definition, data, widthPt: 468f);

        Assert.True(drawn.IsEmpty);
        Assert.Contains("which month", drawn.EmptyPromptText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>A minimum cell height, and it is not a nicety.</b> A seven-by-five grid squeezed small
    /// gives cells nobody in this readership can read, so the widget asks for the height it needs.
    /// </summary>
    [Fact]
    public void TheGridAsksForEnoughHeightToBeRead()
    {
        var data = new MonthCalendarData { Month = 9, Year = 2026, MeetingRule = "1st Tuesday" };

        WidgetDrawList drawn = WidgetTestData.LayOut(Definition, data, widthPt: 468f);

        // September 2026 starts on a Tuesday and has 30 days, so it needs five rows.
        Assert.True(
            drawn.HeightPt >= 5 * 34f,
            $"the grid asked for {drawn.HeightPt}pt, too little for five readable rows");
    }

    /// <summary>
    /// The same data draws the same grid every time — the determinism every layouter owes
    /// (docs/M7-spec.md §4.5). No clock and no culture reach this one.
    /// </summary>
    [Fact]
    public void TheSameMonthDrawsTheSameGridEveryTime()
    {
        var data = new MonthCalendarData
        {
            Month = 9,
            Year = 2026,
            MeetingRule = "1st Tuesday",
            Entries = [new CalendarEntry { Day = 14, What = "Fish fry, 6pm" }],
        };

        WidgetDrawList first = WidgetTestData.LayOut(Definition, data, widthPt: 468f);
        WidgetDrawList second = WidgetTestData.LayOut(Definition, data, widthPt: 468f);

        Assert.Equal(first.HeightPt, second.HeightPt);
        Assert.Equal(first.Items.Count, second.Items.Count);
    }
}
