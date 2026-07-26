using System.Text.Json;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.CoverBanner;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Proves docs/M7-spec.md §9.6's acceptance detail: the rule/date split round-trips for M9, the
/// times line's three-way sentence has no stray separator, nothing is case-transformed, the whole
/// banner is centred, the seed copy at insert never invents a date or a time, and a long lodge name
/// wraps but never past two lines. All fixtures are fictional (PLAN.md §0).
/// </summary>
public sealed class CoverBannerTests
{
    private const float Epsilon = 0.02f;

    private static CoverBannerData FullyPopulated() => new()
    {
        LodgeName = WidgetTestData.LodgeName,
        HeadingText = "STATED COMMUNICATION",
        MeetingRule = "1st Tuesday",
        MeetingDateText = "July 7th",
        DinnerTimeText = "6:30",
        WorkTimeText = "7:30",
    };

    [Fact]
    public void MeetingRuleSurvivesWriteAndReadAndParses()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = FullyPopulated();

        JsonElement written = definition.WriteData(data);
        bool ok = definition.TryReadData(written, definition.CurrentDataVersion, out object typedData);

        Assert.True(ok);
        var roundTripped = (CoverBannerData)typedData;
        Assert.Equal("1st Tuesday", roundTripped.MeetingRule);

        Assert.True(MeetingRule.TryParse(roundTripped.MeetingRule, out MeetingRule rule));
        Assert.Equal(WeekOrdinal.First, rule.Ordinal);
        Assert.Equal(DayOfWeek.Tuesday, rule.Day);
    }

    [Fact]
    public void BlankTimesProduceExactlyOneFewerTextRunThanFilledTimes()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData filled = FullyPopulated();
        CoverBannerData blank = FullyPopulated();
        blank.DinnerTimeText = null;
        blank.WorkTimeText = null;

        WidgetDrawList filledList = WidgetTestData.LayOut(definition, filled, definition.DefaultSizePt.Width);
        WidgetDrawList blankList = WidgetTestData.LayOut(definition, blank, definition.DefaultSizePt.Width);

        int filledRuns = filledList.Items.Count(i => i is WidgetTextItem);
        int blankRuns = blankList.Items.Count(i => i is WidgetTextItem);

        Assert.Equal(filledRuns - 1, blankRuns);
    }

    [Fact]
    public void OnlyDinnerPrintsItsOwnSentenceWithNoStraySeparator()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = FullyPopulated();
        data.WorkTimeText = null;

        WidgetDrawList list = WidgetTestData.LayOut(definition, data, definition.DefaultSizePt.Width);

        AssertLastTextRunReads(list, "Dinner at 6:30", WidgetStyleDefaults.Standard.Body);
    }

    [Fact]
    public void OnlyWorkPrintsItsOwnSentenceWithNoStraySeparator()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = FullyPopulated();
        data.DinnerTimeText = null;

        WidgetDrawList list = WidgetTestData.LayOut(definition, data, definition.DefaultSizePt.Width);

        AssertLastTextRunReads(list, "Lodge opens at 7:30", WidgetStyleDefaults.Standard.Body);
    }

    [Fact]
    public void HeadingTextPrintsVerbatimNeverCaseTransformed()
    {
        var definition = new CoverBannerDefinition();
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        CharacterStyle headingStyle = WidgetStyleDefaults.Standard.Heading;

        CoverBannerData allCaps = FullyPopulated();
        allCaps.HeadingText = "STATED COMMUNICATION";
        CoverBannerData mixedCase = FullyPopulated();
        mixedCase.HeadingText = "Called Meeting";

        WidgetDrawList allCapsList = WidgetTestData.LayOut(definition, allCaps, definition.DefaultSizePt.Width);
        WidgetDrawList mixedCaseList = WidgetTestData.LayOut(definition, mixedCase, definition.DefaultSizePt.Width);

        Assert.True(ContainsRunShapedAs(allCapsList, shaper, "STATED COMMUNICATION", headingStyle));
        Assert.True(ContainsRunShapedAs(mixedCaseList, shaper, "Called Meeting", headingStyle));

        // Neither is what an unwanted case normalisation would have produced.
        Assert.False(ContainsRunShapedAs(allCapsList, shaper, "stated communication", headingStyle));
        Assert.False(ContainsRunShapedAs(mixedCaseList, shaper, "CALLED MEETING", headingStyle));
    }

    [Fact]
    public void AllTextIsCentredOnTheFrame()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = FullyPopulated();
        float widthPt = definition.DefaultSizePt.Width;

        WidgetDrawList list = WidgetTestData.LayOut(definition, data, widthPt);
        float expectedCentre = widthPt / 2f;

        foreach (WidgetDrawItem item in list.Items)
        {
            if (item is not WidgetTextItem textItem)
            {
                continue;
            }

            foreach (PositionedGlyphRun run in textItem.Runs)
            {
                float centre = run.OriginX + (run.AdvanceWidthPt / 2f);
                Assert.True(
                    Math.Abs(centre - expectedCentre) < Epsilon,
                    $"Run centred at {centre}, expected {expectedCentre}.");
            }
        }
    }

    [Fact]
    public void CreateEmptyCopiesSeedLodgeAndRuleLeavingDateAndTimesBlank()
    {
        var definition = new CoverBannerDefinition();

        CoverBannerData data = definition.CreateEmpty(WidgetTestData.Seed);

        Assert.Equal(WidgetTestData.Seed.LodgeName, data.LodgeName);
        Assert.Equal(WidgetTestData.Seed.MeetingRule, data.MeetingRule);
        Assert.Equal("STATED COMMUNICATION", data.HeadingText);
        Assert.Equal("", data.MeetingDateText);
        Assert.Null(data.DinnerTimeText);
        Assert.Null(data.WorkTimeText);
    }

    [Fact]
    public void EmptySeedDataReportsIsEmpty()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = definition.CreateEmpty(WidgetTestData.Seed);
        data.LodgeName = "";
        data.MeetingRule = "";

        WidgetDrawList list = WidgetTestData.LayOut(definition, data, definition.DefaultSizePt.Width);

        Assert.True(list.IsEmpty);
        Assert.Equal("Cover heading — not filled in yet.", list.EmptyPromptText);
    }

    [Fact]
    public void LongLodgeNameWrapsToAtMostTwoLines()
    {
        var definition = new CoverBannerDefinition();
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        CharacterStyle displayStyle = WidgetStyleDefaults.Standard.Display;
        const string longName =
            "Placeholder Lodge Number One Of The Grand Fictional Jurisdiction Of Somewhere Very Far Away And Nowhere Real At All";

        CoverBannerData data = new()
        {
            LodgeName = longName,
            HeadingText = "",
            MeetingRule = "",
            MeetingDateText = "",
        };
        float widthPt = definition.DefaultSizePt.Width;

        // The fixture must genuinely need more than two lines, or capping at two proves nothing.
        IReadOnlyList<string> uncapped = shaper.WrapToWidth(longName, displayStyle, widthPt, widthPt);
        Assert.True(uncapped.Count > 2, "Fixture is not long enough to exercise the two-line cap.");

        WidgetDrawList list = WidgetTestData.LayOut(definition, data, widthPt);
        int textItems = list.Items.Count(i => i is WidgetTextItem);

        Assert.Equal(2, textItems);
    }

    [Fact]
    public void CodecRoundTripsFullyPopulatedData()
    {
        var definition = new CoverBannerDefinition();
        CoverBannerData data = FullyPopulated();

        JsonElement written = definition.WriteData(data);
        bool ok = definition.TryReadData(written, definition.CurrentDataVersion, out object typedData);

        Assert.True(ok);
        var roundTripped = (CoverBannerData)typedData;
        Assert.Equal(data.LodgeName, roundTripped.LodgeName);
        Assert.Equal(data.HeadingText, roundTripped.HeadingText);
        Assert.Equal(data.MeetingRule, roundTripped.MeetingRule);
        Assert.Equal(data.MeetingDateText, roundTripped.MeetingDateText);
        Assert.Equal(data.DinnerTimeText, roundTripped.DinnerTimeText);
        Assert.Equal(data.WorkTimeText, roundTripped.WorkTimeText);
    }

    [Fact]
    public void UnknownPropertySurvivesIntoExtraPropertiesAndIsReemitted()
    {
        var definition = new CoverBannerDefinition();
        const string json = """
            {
              "lodgeName": "Placeholder Lodge No. 000",
              "headingText": "STATED COMMUNICATION",
              "meetingRule": "1st Tuesday",
              "meetingDateText": "July 7th",
              "futureField": "kept-for-a-newer-build"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement element = document.RootElement.Clone();

        bool ok = definition.TryReadData(element, definition.CurrentDataVersion, out object typedData);

        Assert.True(ok);
        var data = (CoverBannerData)typedData;
        Assert.NotNull(data.ExtraProperties);
        Assert.True(data.ExtraProperties!.TryGetValue("futureField", out JsonElement futureField));
        Assert.Equal("kept-for-a-newer-build", futureField.GetString());

        JsonElement written = definition.WriteData(data);
        Assert.True(written.TryGetProperty("futureField", out JsonElement roundTrippedField));
        Assert.Equal("kept-for-a-newer-build", roundTrippedField.GetString());
    }

    /// <summary>True when some run in the list shapes to exactly <paramref name="text"/> in <paramref name="style"/>.</summary>
    private static bool ContainsRunShapedAs(WidgetDrawList list, WidgetTextShaper shaper, string text, CharacterStyle style)
    {
        IReadOnlyList<ushort> expected = ShapeGlyphs(shaper, text, style);
        return list.Items.OfType<WidgetTextItem>()
            .SelectMany(item => item.Runs)
            .Any(run => run.Glyphs.SequenceEqual(expected));
    }

    /// <summary>Asserts the LAST text item in the list shapes to exactly <paramref name="expected"/>.</summary>
    private static void AssertLastTextRunReads(WidgetDrawList list, string expected, CharacterStyle style)
    {
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        PositionedGlyphRun run = list.Items.OfType<WidgetTextItem>().Last().Runs[0];
        Assert.Equal(ShapeGlyphs(shaper, expected, style), run.Glyphs);
    }

    private static IReadOnlyList<ushort> ShapeGlyphs(WidgetTextShaper shaper, string text, CharacterStyle style) =>
        shaper.ShapeRun(text, style, 0f, 0f).Runs[0].Glyphs;
}
