using System.Text.Json;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.EventCard;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Proves docs/M7-spec.md §9.5's acceptance detail and the wizard, headlessly. All fixture data is
/// fictional (PLAN.md §0): "Placeholder Lodge Picnic", "Sample Lodge 000".
/// </summary>
public sealed class EventCardTests
{
    private const float WidthPt = 240f;

    private static readonly EventCardDefinition Definition = new();

    private static EventCardData FullCard(bool showBorder = true) => new()
    {
        Title = "Placeholder Lodge Picnic",
        WhenText = "Saturday, 6:00 pm",
        WhereText = "Sample Lodge 000 hall",
        BodyText = "Bring a dish to share. Family and friends welcome.",
        ShowBorder = showBorder,
    };

    private static EventCardData CardWithoutWhenWhere(bool showBorder = true) => new()
    {
        Title = "Placeholder Lodge Picnic",
        BodyText = "Bring a dish to share. Family and friends welcome.",
        ShowBorder = showBorder,
    };

    [Fact]
    public void DrawListContainsExactlyOneFillItem()
    {
        WidgetDrawList list = WidgetTestData.LayOut(Definition, FullCard(), WidthPt);
        Assert.Single(list.Items.OfType<WidgetFillItem>());
    }

    [Fact]
    public void BorderAddsExactlyFourRulesRegardlessOfWhenWhere()
    {
        WidgetDrawList bordered = WidgetTestData.LayOut(Definition, FullCard(showBorder: true), WidthPt);
        WidgetDrawList borderless = WidgetTestData.LayOut(Definition, FullCard(showBorder: false), WidthPt);

        int borderedRules = bordered.Items.OfType<WidgetRuleItem>().Count();
        int borderlessRules = borderless.Items.OfType<WidgetRuleItem>().Count();

        Assert.Equal(4, borderedRules - borderlessRules);
    }

    [Fact]
    public void BlankWhenAndWhereYieldsAShorterCardWithNoEmptyTextRun()
    {
        WidgetDrawList blankList = WidgetTestData.LayOut(Definition, CardWithoutWhenWhere(), WidthPt);
        WidgetDrawList filledList = WidgetTestData.LayOut(Definition, FullCard(), WidthPt);

        Assert.True(blankList.HeightPt < filledList.HeightPt);
        Assert.All(blankList.Items.OfType<WidgetTextItem>(), item => Assert.NotEmpty(item.Runs));
        Assert.All(filledList.Items.OfType<WidgetTextItem>(), item => Assert.NotEmpty(item.Runs));
    }

    [Fact]
    public void ContentIsInsetByThePadding()
    {
        // WidgetStyleDefaults.Standard.PaddingPt is 0, so the layouter falls back to its own default
        // of 10pt (docs/M7-spec.md §9.5, rule 3) — that fallback is what this test is really pinning.
        const float DefaultPaddingPt = 10f;
        WidgetDrawList list = WidgetTestData.LayOut(Definition, FullCard(), WidthPt);

        foreach (WidgetTextItem item in list.Items.OfType<WidgetTextItem>())
        {
            foreach (PositionedGlyphRun run in item.Runs)
            {
                Assert.True(run.OriginX >= DefaultPaddingPt - 0.01f);
            }
        }
    }

    [Fact]
    public void LongBodyWrapsIntoMultipleLinesWithIncreasingBaselines()
    {
        var data = new EventCardData
        {
            BodyText =
                "This announcement is written long enough that it must wrap across several lines " +
                "once it is laid out at a narrow card width, so the test can see more than one text " +
                "run come out the other end.",
        };

        WidgetDrawList list = WidgetTestData.LayOut(Definition, data, 160f);

        var textItems = list.Items.OfType<WidgetTextItem>().ToList();
        Assert.True(textItems.Count >= 2, "The body should have wrapped into at least two lines.");

        float previousBaseline = float.NegativeInfinity;
        foreach (WidgetTextItem item in textItems)
        {
            float baseline = item.Runs[0].BaselineY;
            Assert.True(baseline > previousBaseline);
            previousBaseline = baseline;
        }
    }

    [Fact]
    public void CodecRoundTripsIncludingBoolAndPreservesUnknownProperties()
    {
        EventCardData original = FullCard(showBorder: false);
        JsonElement written = Definition.WriteData(original);

        Assert.True(Definition.TryReadData(written, Definition.CurrentDataVersion, out object typedObj));
        var roundTripped = Assert.IsType<EventCardData>(typedObj);
        Assert.Equal(original.Title, roundTripped.Title);
        Assert.Equal(original.WhenText, roundTripped.WhenText);
        Assert.Equal(original.WhereText, roundTripped.WhereText);
        Assert.Equal(original.BodyText, roundTripped.BodyText);
        Assert.False(roundTripped.ShowBorder);

        // A property a future build wrote must survive a read this build does not fully understand,
        // and must be re-emitted when the widget is next saved (docs/M7-spec.md §1.2, rule 5).
        const string json = """
            {
                "title": "Placeholder Lodge Picnic",
                "whenText": "Saturday, 6:00 pm",
                "whereText": "Sample Lodge 000 hall",
                "bodyText": "Bring a dish to share.",
                "showBorder": true,
                "futureField": "something a newer build added"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement withUnknown = document.RootElement.Clone();

        Assert.True(
            Definition.TryReadData(withUnknown, Definition.CurrentDataVersion, out object typedWithUnknown));
        var withUnknownData = Assert.IsType<EventCardData>(typedWithUnknown);
        Assert.NotNull(withUnknownData.ExtraProperties);
        Assert.True(withUnknownData.ExtraProperties!.ContainsKey("futureField"));

        JsonElement reEmitted = Definition.WriteData(withUnknownData);
        Assert.Contains("futureField", reEmitted.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateEmptyReportsEmptyButATitleOnlyCardDoesNot()
    {
        EventCardData empty = Definition.CreateEmpty(WidgetTestData.Seed);
        Assert.Equal("", empty.Title);
        Assert.Equal("", empty.BodyText);
        Assert.True(empty.ShowBorder);

        WidgetDrawList emptyList = WidgetTestData.LayOut(Definition, empty, WidthPt);
        Assert.True(emptyList.IsEmpty);
        Assert.Equal("Announcement — not filled in yet.", emptyList.EmptyPromptText);

        var titleOnly = new EventCardData { Title = "Placeholder Lodge Picnic" };
        WidgetDrawList titleOnlyList = WidgetTestData.LayOut(Definition, titleOnly, WidthPt);
        Assert.False(titleOnlyList.IsEmpty);
    }

    [Fact]
    public void WizardRequiresTitleButWhenAndWhereAreOptional()
    {
        WizardSession session = WizardSession.Create(
            Definition, existingData: null, Definition.CurrentDataVersion, WidgetTestData.Seed);

        // Screen 0: the Title field is required.
        session.SetValue("title", "");
        Assert.False(session.TryGoNext());
        Assert.Single(session.Errors);
        Assert.Equal(WizardValidators.Required("Title"), session.Errors[0].Message);

        session.SetValue("title", "Placeholder Lodge Picnic");
        Assert.True(session.TryGoNext());

        // Screen 1: When and Where are both optional — leaving them blank must not block navigation.
        session.SetValue("when", "");
        session.SetValue("where", "");
        Assert.True(session.TryGoNext());
    }
}
