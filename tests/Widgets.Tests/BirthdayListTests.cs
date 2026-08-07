using System.Globalization;
using System.Linq;
using System.Text.Json;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Proves docs/M7-spec.md §9.2: the canonical "Name … M/D" row, sorted by (Month, Day, Name) in the
/// layouter and never in the data, the MonthDay wizard binding, and the codec. All names are
/// fictional (PLAN.md §0), drawn from <see cref="WidgetTestData"/>.
/// </summary>
public sealed class BirthdayListTests
{
    // A width wide enough that none of this file's short fictional names wraps, so a row is always
    // exactly two WidgetTextItems (name, date) sharing one baseline.
    private const float WideWidthPt = 300f;

    [Fact]
    public void LayoutSortsByMonthDayThenNameOrderRecoveredFromDrawListBaselines()
    {
        var definition = new BirthdayListDefinition();
        var data = new BirthdayListData
        {
            Entries =
            {
                new BirthdayEntry { Name = "A. Placeholder", Month = 12, Day = 25 },
                new BirthdayEntry { Name = "F. Nobody", Month = 1, Day = 1 },
                new BirthdayEntry { Name = "D. Fictional", Month = 7, Day = 4 },
            },
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(definition, data, WideWidthPt);
        WidgetStyleContext style = WidgetTestData.CreateStyle(definition.StyleDefaults);
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();

        // Each name has a distinct measured width, which is what lets a row be identified without
        // the draw list carrying any text (docs/M7-spec.md §4.3 — glyphs only, never strings).
        float nobodyWidth = shaper.MeasureWidthPt("F. Nobody", style.Body);
        float fictionalWidth = shaper.MeasureWidthPt("D. Fictional", style.Body);
        float placeholderWidth = shaper.MeasureWidthPt("A. Placeholder", style.Body);

        List<WidgetTextItem> textItems = [.. drawList.Items.OfType<WidgetTextItem>()];
        List<float> rowBaselines =
            [.. textItems.Select(i => i.Runs[0].BaselineY).Distinct().OrderBy(b => b).Skip(1)]; // skip the heading

        Assert.Equal(3, rowBaselines.Count);

        var nameWidthByRow = new float[3];
        for (int row = 0; row < 3; row++)
        {
            float baseline = rowBaselines[row];
            List<WidgetTextItem> onBaseline = [.. textItems.Where(i => i.Runs[0].BaselineY == baseline)];
            Assert.Equal(2, onBaseline.Count); // name + date, one line, no leaders
            nameWidthByRow[row] = onBaseline.Max(i => i.Runs[0].AdvanceWidthPt);
        }

        // 1/1, then 7/4, then 12/25 — the acceptance order (docs/M7-spec.md §9.2), read off the name
        // column's width per row rather than off the stored (unsorted) list.
        Assert.Equal((double)nobodyWidth, nameWidthByRow[0], 3);
        Assert.Equal((double)fictionalWidth, nameWidthByRow[1], 3);
        Assert.Equal((double)placeholderWidth, nameWidthByRow[2], 3);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12, 25)]
    [InlineData(7, 4)]
    [InlineData(1, 31)]
    [InlineData(9, 9)]
    public void FormatDateMatchesCanonicalPatternNoLeadingZeros(int month, int day)
    {
        // WizardValidators.FormatMonthDay is exactly what the layouter prints for a date
        // (BirthdayListLayouter.FormatDate delegates to it) — one canonical format, everywhere.
        string formatted = WizardValidators.FormatMonthDay(month, day);

        Assert.Matches(@"^\d{1,2}/\d{1,2}$", formatted);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{month}/{day}"), formatted);
    }

    [Fact]
    public void LayoutTwentyRowFixtureMeasuresAHeightFitToContentsCanUse()
    {
        var definition = new BirthdayListDefinition();
        var data = new BirthdayListData();
        for (int i = 0; i < 20; i++)
        {
            data.Entries.Add(new BirthdayEntry
            {
                Name = WidgetTestData.Names[i % WidgetTestData.Names.Count],
                Month = (i % 12) + 1,
                Day = ((i * 7) % 28) + 1,
            });
        }

        WidgetDrawList drawList = WidgetTestData.LayOut(definition, data, definition.DefaultSizePt.Width);

        // The list is designed for 6-20 rows; a long one simply reports a taller natural height and
        // "Fit to contents" grows the box (docs/M7-spec.md §4.6). What must NOT happen is the type
        // shrinking to fit, so the height is expected to scale with the row count.
        Assert.True(drawList.HeightPt > 200f, $"20 rows measured only {drawList.HeightPt}pt");
        Assert.True(drawList.HeightPt < 340f, $"20 rows measured {drawList.HeightPt}pt, which is too loose");
    }

    [Fact]
    public void LayoutDoesNotMutateTheStoredEntryOrder()
    {
        var definition = new BirthdayListDefinition();
        var first = new BirthdayEntry { Name = "A. Placeholder", Month = 12, Day = 25 };
        var second = new BirthdayEntry { Name = "F. Nobody", Month = 1, Day = 1 };
        var data = new BirthdayListData { Entries = { first, second } };

        WidgetTestData.LayOut(definition, data, WideWidthPt);

        // The layouter sorted internally, but data.Entries — what re-editing sees — is untouched.
        Assert.Same(first, data.Entries[0]);
        Assert.Same(second, data.Entries[1]);
        Assert.Equal(12, data.Entries[0].Month);
        Assert.Equal(25, data.Entries[0].Day);
    }

    [Fact]
    public void WizardDateFieldRoundTripsThroughSetValueGetValueAndThePoco()
    {
        var definition = new BirthdayListDefinition();
        WizardSession session =
            WizardSession.Create(definition, null, definition.CurrentDataVersion, WidgetTestData.Seed);
        Assert.True(session.TryGoNext()); // heading step -> the birthdays list step

        int row = session.AddRow();
        session.SetValue("name", "A. Placeholder", row);
        session.SetValue("date", "7/4", row);

        Assert.Equal("7/4", session.GetValue("date", row));

        Assert.True(session.TryCommit(out JsonElement written, out int version, out _));
        Assert.True(definition.TryReadData(written, version, out object typedObj));
        var committed = Assert.IsType<BirthdayListData>(typedObj);

        Assert.Equal(7, committed.Entries[0].Month);
        Assert.Equal(4, committed.Entries[0].Day);
    }

    [Fact]
    public void WizardMalformedDateIsRefusedWithTheExactMonthDaySentence()
    {
        var definition = new BirthdayListDefinition();
        WizardSession session =
            WizardSession.Create(definition, null, definition.CurrentDataVersion, WidgetTestData.Seed);
        Assert.True(session.TryGoNext()); // heading step -> the birthdays list step

        int row = session.AddRow();
        session.SetValue("name", "B. Sample", row);
        session.SetValue("date", "13/45", row); // two numbers, but out of range

        Assert.False(session.TryGoNext());
        string expected = WizardValidators.MonthDay("13/45")
            ?? throw new InvalidOperationException("Expected 13/45 to fail MonthDay validation.");

        WizardFieldError error = Assert.Single(session.Errors);
        Assert.Equal("date", error.FieldKey);
        Assert.Equal(expected, error.Message);
    }

    [Fact]
    public void CodecRoundTripsFullyPopulatedData()
    {
        var definition = new BirthdayListDefinition();
        var original = new BirthdayListData
        {
            Heading = "Birthdays",
            Entries =
            {
                new BirthdayEntry { Name = "A. Placeholder", Month = 3, Day = 15 },
                new BirthdayEntry { Name = "F. Nobody", Month = 11, Day = 2 },
            },
            ClosingNote = "Miss anyone? Let us know.",
        };

        JsonElement written = definition.WriteData(original);
        Assert.True(definition.TryReadData(written, definition.CurrentDataVersion, out object typedObj));
        var roundTripped = Assert.IsType<BirthdayListData>(typedObj);

        Assert.Equal(original.Heading, roundTripped.Heading);
        Assert.Equal(original.ClosingNote, roundTripped.ClosingNote);
        Assert.Equal(original.Entries.Count, roundTripped.Entries.Count);
        for (int i = 0; i < original.Entries.Count; i++)
        {
            Assert.Equal(original.Entries[i].Name, roundTripped.Entries[i].Name);
            Assert.Equal(original.Entries[i].Month, roundTripped.Entries[i].Month);
            Assert.Equal(original.Entries[i].Day, roundTripped.Entries[i].Day);
        }
    }

    [Fact]
    public void CodecUnknownPropertiesSurviveInExtraPropertiesAndAreReEmitted()
    {
        var definition = new BirthdayListDefinition();
        const string json = """
            {
              "heading": "Birthdays",
              "entries": [
                { "name": "A. Placeholder", "month": 3, "day": 15, "futureEntryFlag": true }
              ],
              "closingNote": "Miss anyone? Let us know.",
              "futureListFlag": "unknown-value"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement element = document.RootElement.Clone();

        Assert.True(definition.TryReadData(element, definition.CurrentDataVersion, out object typedObj));
        var typed = Assert.IsType<BirthdayListData>(typedObj);

        Assert.NotNull(typed.ExtraProperties);
        Assert.True(typed.ExtraProperties!.ContainsKey("futureListFlag"));
        Assert.NotNull(typed.Entries[0].ExtraProperties);
        Assert.True(typed.Entries[0].ExtraProperties!.ContainsKey("futureEntryFlag"));

        JsonElement written = definition.WriteData(typed);
        string rawText = written.GetRawText();
        Assert.Contains("futureListFlag", rawText, StringComparison.Ordinal);
        Assert.Contains("futureEntryFlag", rawText, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateEmptyHasZeroEntriesAndDrawListReportsEmpty()
    {
        var definition = new BirthdayListDefinition();
        BirthdayListData empty = definition.CreateEmpty(WidgetTestData.Seed);
        Assert.Empty(empty.Entries);

        WidgetDrawList drawList = WidgetTestData.LayOut(definition, empty, definition.DefaultSizePt.Width);
        Assert.True(drawList.IsEmpty);
        Assert.Equal("Birthdays — not filled in yet.", drawList.EmptyPromptText);
    }

    /// <summary>
    /// Review §14.2: a day that does not exist in its month is not a birthday. The check was a flat
    /// "1 to 31", so 30 February and 31 April were accepted, stored, and printed as somebody's real
    /// birthday on a page that goes out to the whole lodge.
    /// </summary>
    [Theory]
    [InlineData("2/30")]
    [InlineData("2/31")]
    [InlineData("4/31")]
    [InlineData("6/31")]
    [InlineData("9/31")]
    [InlineData("11/31")]
    public void ADayThatDoesNotExistInItsMonthIsRefused(string value) =>
        Assert.False(WizardValidators.TryParseMonthDay(value, out _, out _));

    /// <summary>
    /// And the days that do exist are still accepted — including 29 February, which is somebody's
    /// actual birthday and which a month-and-day with no year behind it cannot rule out.
    /// </summary>
    [Theory]
    [InlineData("2/29", 2, 29)]
    [InlineData("1/31", 1, 31)]
    [InlineData("4/30", 4, 30)]
    [InlineData("12/25", 12, 25)]
    public void TheDaysThatDoExistAreStillAccepted(string value, int month, int day)
    {
        Assert.True(WizardValidators.TryParseMonthDay(value, out int m, out int d));
        Assert.Equal(month, m);
        Assert.Equal(day, d);
    }
}
