using System.Linq;
using System.Text.Json;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.DistrictCalendar;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Proves docs/M7-spec.md §9.4's acceptance detail: the table's recurrence sort (with its
/// unparseable-rule fallback), the events' stored-order printing, mode-driven section suppression,
/// the wizard's mode-driven step skipping (the only widget that exercises <c>IWizardStep.IsActive</c>),
/// and the codec's enum-as-string / extension-data round trip. All fixture data is fictional
/// (PLAN.md §0).
/// </summary>
public sealed class DistrictCalendarTests
{
    private static readonly DistrictCalendarDefinition Definition = new();
    private static readonly WidgetStyleContext Style = WidgetTestData.CreateStyle(Definition.StyleDefaults);
    private static readonly WidgetTextShaper Shaper = WidgetTestData.CreateShaper();

    // A generous width so the joined event lines in these tests never wrap mid-line — the tests care
    // about glyph-sequence identity, not wrapping.
    private const float WideWidthPt = 400f;

    [Fact]
    public void MeetingDaysTableSortsScrambledLodgesIntoRecurrenceOrder()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.MeetingDays,
            Lodges =
            [
                Lodge("Sample Lodge 000", "2nd Monday"),
                Lodge("Placeholder Lodge No. 000", "1st Tuesday"),
                Lodge("Example Lodge 222", "1st Monday"),
            ],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, Definition.DefaultSizePt.Width);

        AssertGlyphOrder(drawList, Style.Emphasis, "1st Monday", "1st Tuesday", "2nd Monday");
    }

    [Fact]
    public void MeetingDaysTableUnparseableRuleSortsLastAndPrintsVerbatim()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.MeetingDays,
            Lodges =
            [
                Lodge("Zed Lodge 999", "sometime soon"),
                Lodge("Alpha Lodge 111", "1st Tuesday"),
            ],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, Definition.DefaultSizePt.Width);

        // The valid rule prints normalised and first; the unparseable text prints exactly as typed,
        // last.
        AssertGlyphOrder(drawList, Style.Emphasis, "1st Tuesday", "sometime soon");
    }

    [Fact]
    public void EventsModeEmitsNoTableRulesAtAll()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.Events,
            Events = [Event("July 12", "Sample Lodge 000", "Masters and Wardens meeting")],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, WideWidthPt);

        Assert.Empty(drawList.Items.OfType<WidgetRuleItem>());
    }

    [Fact]
    public void MeetingDaysModeEmitsNoEventRows()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.MeetingDays,
            Lodges = [Lodge("Sample Lodge 000", "1st Tuesday")],
            // Leftover event data from a prior mode switch must not print while Mode = MeetingDays.
            Events = [Event("July 12", null, "Degree work")],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, WideWidthPt);

        Assert.False(ContainsRun(drawList, Style.Body, "July 12 — Degree work"));
    }

    [Fact]
    public void BothModeEmitsTableSortedAndEventsInEntryOrder()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.Both,
            Lodges =
            [
                Lodge("Beta Lodge 222", "2nd Monday"),
                Lodge("Alpha Lodge 111", "1st Monday"),
            ],
            Events =
            [
                Event("July 12", null, "Second event, chronologically earlier is irrelevant"),
                Event("March 1", null, "First event, entered second"),
            ],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, WideWidthPt);

        Assert.NotEmpty(drawList.Items.OfType<WidgetRuleItem>());
        AssertGlyphOrder(drawList, Style.Emphasis, "1st Monday", "2nd Monday");
        // Stored order (July then March), never re-sorted by date text.
        AssertGlyphOrder(
            drawList,
            Style.Body,
            "July 12 — Second event, chronologically earlier is irrelevant",
            "March 1 — First event, entered second");
    }

    [Fact]
    public void MeetingRuleNormalisesFreeTypedOrdinalAndDay()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.MeetingDays,
            Lodges = [Lodge("Sample Lodge 000", "first tuesday")],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, Definition.DefaultSizePt.Width);

        Assert.True(ContainsRun(drawList, Style.Emphasis, "1st Tuesday"));
        Assert.False(ContainsRun(drawList, Style.Emphasis, "first tuesday"));
    }

    [Fact]
    public void WizardSessionModeSwitchRecomputesActiveSteps()
    {
        WizardSession session = WizardSession.Create(
            Definition, existingData: null, Definition.CurrentDataVersion, WidgetTestData.Seed);

        // Default Mode = MeetingDays: the fields step, the lodges step and review — no events step.
        int beforeScreenCount = session.ScreenCount;

        session.SetValue("mode", "both");

        int afterScreenCount = session.ScreenCount;

        Assert.Equal(3, beforeScreenCount);
        Assert.Equal(4, afterScreenCount);
    }

    [Fact]
    public void EventWithNoHostTextRendersWithoutDoubledSeparator()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.Events,
            Events = [Event("July 12", null, "Degree work")],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, WideWidthPt);

        Assert.True(ContainsRun(drawList, Style.Body, "July 12 — Degree work"));
    }

    [Fact]
    public void CodecRoundTripsTheModeEnumAsACamelCaseString()
    {
        var data = new DistrictCalendarData
        {
            Mode = DistrictCalendarMode.Both,
            Lodges = [Lodge("Sample Lodge 000", "1st Tuesday")],
            Events = [Event("July 12", "Sample Lodge 000", "Degree work", "Meal 6:30, work 7:30")],
        };

        JsonElement written = Definition.WriteData(data);

        JsonElement modeElement = written.GetProperty("mode");
        Assert.Equal(JsonValueKind.String, modeElement.ValueKind);
        Assert.Equal("both", modeElement.GetString());

        Assert.True(Definition.TryReadData(written, Definition.CurrentDataVersion, out object typedObj));
        var roundTripped = (DistrictCalendarData)typedObj;
        Assert.Equal(DistrictCalendarMode.Both, roundTripped.Mode);
        Assert.Equal("Sample Lodge 000", roundTripped.Lodges[0].LodgeName);
        Assert.Equal("1st Tuesday", roundTripped.Lodges[0].MeetingRule);
        Assert.Equal("July 12", roundTripped.Events[0].DateText);
        Assert.Equal("Meal 6:30, work 7:30", roundTripped.Events[0].TimesText);
    }

    [Fact]
    public void CodecUnknownPropertiesSurviveIntoExtraPropertiesAndAreReEmitted()
    {
        const string json = """
            {
              "heading": "22nd District",
              "mode": "meetingDays",
              "lodgesHeading": "Meeting days",
              "lodges": [
                { "lodgeName": "Sample Lodge 000", "meetingRule": "1st Tuesday", "futureLodgeProp": "x" }
              ],
              "eventsHeading": "Coming up",
              "events": [],
              "futureProp": "future value"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement.Clone();

        Assert.True(Definition.TryReadData(root, Definition.CurrentDataVersion, out object typedObj));
        var typed = (DistrictCalendarData)typedObj;

        Assert.NotNull(typed.ExtraProperties);
        Assert.Equal("future value", typed.ExtraProperties!["futureProp"].GetString());
        Assert.NotNull(typed.Lodges[0].ExtraProperties);
        Assert.Equal("x", typed.Lodges[0].ExtraProperties!["futureLodgeProp"].GetString());

        JsonElement rewritten = Definition.WriteData(typed);
        Assert.True(rewritten.TryGetProperty("futureProp", out JsonElement futureProp));
        Assert.Equal("future value", futureProp.GetString());
        Assert.True(rewritten.GetProperty("lodges")[0].TryGetProperty("futureLodgeProp", out JsonElement futureLodgeProp));
        Assert.Equal("x", futureLodgeProp.GetString());
    }

    [Fact]
    public void CreateEmptyHasZeroLodgesAndZeroEventsAndReportsEmpty()
    {
        DistrictCalendarData empty = Definition.CreateEmpty(WidgetTestData.Seed);

        Assert.Empty(empty.Lodges);
        Assert.Empty(empty.Events);
        Assert.Equal(DistrictCalendarMode.MeetingDays, empty.Mode);

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, empty, Definition.DefaultSizePt.Width);
        Assert.True(drawList.IsEmpty);
    }

    private static DistrictLodgeEntry Lodge(string name, string rule) =>
        new() { LodgeName = name, MeetingRule = rule };

    private static DistrictEventEntry Event(string date, string? host, string description, string? times = null) =>
        new() { DateText = date, HostText = host, Description = description, TimesText = times };

    /// <summary>Shapes <paramref name="text"/> the same way the layouter would, for glyph comparison.</summary>
    private static IReadOnlyList<ushort> GlyphsOf(string text, CharacterStyle style) =>
        Shaper.ShapeRun(text, style, 0f, 0f).Runs[0].Glyphs;

    /// <summary>True when some text run in the draw list shapes to exactly <paramref name="text"/>.</summary>
    private static bool ContainsRun(WidgetDrawList drawList, CharacterStyle style, string text)
    {
        IReadOnlyList<ushort> expected = GlyphsOf(text, style);
        foreach (WidgetDrawItem item in drawList.Items)
        {
            if (item is not WidgetTextItem textItem)
            {
                continue;
            }

            foreach (PositionedGlyphRun run in textItem.Runs)
            {
                if (run.Glyphs.SequenceEqual(expected))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Asserts each of <paramref name="expectedTextsInOrder"/> appears as a run in the draw list, in
    /// that relative order — the "assert the text-run order" acceptance detail, independent of
    /// whatever headings or other runs sit around them.
    /// </summary>
    private static void AssertGlyphOrder(
        WidgetDrawList drawList, CharacterStyle style, params string[] expectedTextsInOrder)
    {
        List<IReadOnlyList<ushort>> expectedGlyphs = expectedTextsInOrder
            .Select(text => GlyphsOf(text, style))
            .ToList();

        int cursor = 0;
        foreach (WidgetDrawItem item in drawList.Items)
        {
            if (item is not WidgetTextItem textItem)
            {
                continue;
            }

            foreach (PositionedGlyphRun run in textItem.Runs)
            {
                if (cursor < expectedGlyphs.Count && run.Glyphs.SequenceEqual(expectedGlyphs[cursor]))
                {
                    cursor++;
                }
            }
        }

        Assert.Equal(expectedGlyphs.Count, cursor);
    }
}
