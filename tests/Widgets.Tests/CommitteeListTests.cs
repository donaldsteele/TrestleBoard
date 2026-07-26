using System.Text.Json;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Input;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.CommitteeList;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// Proves docs/M7-spec.md §9.3: the hanging indent, the members-one-per-line binding, the wizard's
/// reorder support, the Note tail, the codec round trip and the empty state. All names are fictional
/// (PLAN.md §0); committee names ("Building", "Ritual", "Traveling Brothers") are structure, not people.
/// </summary>
public sealed class CommitteeListTests
{
    private static readonly CommitteeListDefinition Definition = new();

    /// <summary>Same formula the layouter uses (docs/M7-spec.md §9.3) — computed independently here
    /// so the test does not simply echo the production code's own answer back at itself.</summary>
    private static float ExpectedHangingIndent(WidgetTextShaper shaper, WidgetStyleContext style, string name, float widthPt) =>
        Math.Min(shaper.MeasureWidthPt(name + ": ", style.Emphasis), widthPt * 0.40f);

    /// <summary>Glyph-for-glyph comparison against an independently shaped run — the only way to
    /// check "what text did this run actually print" without decoding glyph ids back to characters.</summary>
    private static void AssertRunText(WidgetTextShaper shaper, CharacterStyle style, string expectedText, PositionedGlyphRun actual)
    {
        WidgetTextItem expected = shaper.ShapeRun(expectedText, style, 0f, 0f);
        Assert.Equal(expected.Runs[0].Glyphs, actual.Glyphs);
    }

    [Fact]
    public void WrappingCommitteeHasHangingIndentOnEveryContinuationLine()
    {
        const string name = "Traveling Brothers";
        const float widthPt = 400f;
        var data = new CommitteeListData
        {
            Heading = "",
            NoticeText = null,
            Committees = [new CommitteeEntry { Name = name, Members = [.. WidgetTestData.Names] }],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, widthPt);
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        WidgetStyleContext style = WidgetTestData.CreateStyle(Definition.StyleDefaults);

        List<WidgetTextItem> textItems = [.. drawList.Items.OfType<WidgetTextItem>()];
        Assert.True(textItems.Count >= 3, "Twelve long names at 400pt must wrap across several lines.");

        Assert.Equal(0f, textItems[0].Runs[0].OriginX);

        float expectedIndent = ExpectedHangingIndent(shaper, style, name, widthPt);
        for (int i = 1; i < textItems.Count; i++)
        {
            Assert.Equal(expectedIndent, textItems[i].Runs[0].OriginX);
        }
    }

    [Fact]
    public void CommitteeWithNoMembersPrintsNameAndColonOnly()
    {
        var data = new CommitteeListData
        {
            Heading = "",
            NoticeText = null,
            Committees = [new CommitteeEntry { Name = "Ritual", Members = [], Note = null }],
        };

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, data, Definition.DefaultSizePt.Width);
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        WidgetStyleContext style = WidgetTestData.CreateStyle(Definition.StyleDefaults);

        List<WidgetTextItem> textItems = [.. drawList.Items.OfType<WidgetTextItem>()];
        WidgetTextItem only = Assert.Single(textItems);
        PositionedGlyphRun run = Assert.Single(only.Runs);
        Assert.Equal(0f, run.OriginX);

        // Exactly "Ritual:" — never "Ritual: " with a dangling space, never a placeholder member.
        AssertRunText(shaper, style.Emphasis, "Ritual:", run);
    }

    [Fact]
    public void MembersBindingRoundTripsOnePerLine()
    {
        var data = new CommitteeListData();
        var listStep = (IWizardStep)Definition.Wizard.Steps[1];
        int rowIndex = listStep.AddRow(data);
        Assert.True(rowIndex >= 0);

        const string threeNames = "A. Placeholder\nB. Sample\nC. Example";
        listStep.SetValue(data, "members", rowIndex, threeNames);

        Assert.Equal(3, data.Committees[0].Members.Count);
        Assert.Equal(["A. Placeholder", "B. Sample", "C. Example"], data.Committees[0].Members);
        Assert.Equal(threeNames, listStep.GetValue(data, "members", rowIndex));
    }

    [Fact]
    public void MoveRowReordersCommitteesAndAllowReorderIsTrue()
    {
        var data = new CommitteeListData();
        var listStep = (IWizardStep)Definition.Wizard.Steps[1];
        var reorderableStep = (IWizardListStep)listStep;
        Assert.True(reorderableStep.AllowReorder);

        int first = listStep.AddRow(data);
        listStep.SetValue(data, "name", first, "Building");
        int second = listStep.AddRow(data);
        listStep.SetValue(data, "name", second, "Ritual");

        Assert.True(listStep.MoveRow(data, 0, 1));
        Assert.Equal("Ritual", data.Committees[0].Name);
        Assert.Equal("Building", data.Committees[1].Name);
    }

    [Fact]
    public void NoteAppearsAtEndOfRowWhenPresentAndContributesNothingWhenAbsent()
    {
        WidgetTextShaper shaper = WidgetTestData.CreateShaper();
        WidgetStyleContext style = WidgetTestData.CreateStyle(Definition.StyleDefaults);
        const float widthPt = 500f; // wide enough that neither case wraps.

        var withoutNote = new CommitteeListData
        {
            Heading = "",
            NoticeText = null,
            Committees = [new CommitteeEntry { Name = "Building", Members = ["A. Placeholder", "B. Sample"], Note = null }],
        };
        WidgetTextItem restWithout = Assert.Single(
            WidgetTestData.LayOut(Definition, withoutNote, widthPt).Items.OfType<WidgetTextItem>().Skip(1));
        AssertRunText(shaper, style.Body, "A. Placeholder, B. Sample", restWithout.Runs[0]);

        var withNote = new CommitteeListData
        {
            Heading = "",
            NoticeText = null,
            Committees = [new CommitteeEntry { Name = "Building", Members = ["A. Placeholder", "B. Sample"], Note = "and team" }],
        };
        WidgetTextItem restWith = Assert.Single(
            WidgetTestData.LayOut(Definition, withNote, widthPt).Items.OfType<WidgetTextItem>().Skip(1));
        AssertRunText(shaper, style.Body, "A. Placeholder, B. Sample, and team", restWith.Runs[0]);
    }

    [Fact]
    public void CodecRoundTripsFullyPopulatedDataAndPreservesUnknownProperties()
    {
        var data = new CommitteeListData
        {
            Heading = "Committees",
            NoticeText = "Please notify the Worshipful Master to be added or removed.",
            Committees =
            [
                new CommitteeEntry { Name = "Building", Members = ["A. Placeholder", "B. Sample"], Note = "and team" },
                new CommitteeEntry { Name = "Ritual", Members = [], Note = null },
            ],
        };

        JsonElement written = Definition.WriteData(data);
        Assert.True(Definition.TryReadData(written, Definition.CurrentDataVersion, out object typedObj));
        var roundTripped = Assert.IsType<CommitteeListData>(typedObj);

        Assert.Equal(data.Heading, roundTripped.Heading);
        Assert.Equal(data.NoticeText, roundTripped.NoticeText);
        Assert.Equal(data.Committees.Count, roundTripped.Committees.Count);
        for (int i = 0; i < data.Committees.Count; i++)
        {
            Assert.Equal(data.Committees[i].Name, roundTripped.Committees[i].Name);
            Assert.Equal(data.Committees[i].Members, roundTripped.Committees[i].Members);
            Assert.Equal(data.Committees[i].Note, roundTripped.Committees[i].Note);
        }

        const string jsonWithUnknownProperties = """
            {
              "heading": "Committees",
              "committees": [
                { "name": "Ritual", "members": ["A. Placeholder"], "futureNote": "future-value" }
              ],
              "futureField": "future-top"
            }
            """;
        using JsonDocument document = JsonDocument.Parse(jsonWithUnknownProperties);
        JsonElement root = document.RootElement.Clone();

        Assert.True(Definition.TryReadData(root, Definition.CurrentDataVersion, out object typedWithExtra));
        var withExtra = Assert.IsType<CommitteeListData>(typedWithExtra);
        Assert.NotNull(withExtra.ExtraProperties);
        Assert.True(withExtra.ExtraProperties!.ContainsKey("futureField"));
        Assert.NotNull(withExtra.Committees[0].ExtraProperties);
        Assert.True(withExtra.Committees[0].ExtraProperties!.ContainsKey("futureNote"));

        string rewritten = Definition.WriteData(withExtra).GetRawText();
        Assert.Contains("futureField", rewritten, StringComparison.Ordinal);
        Assert.Contains("futureNote", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateEmptyHasZeroCommitteesAndDrawListReportsIsEmpty()
    {
        CommitteeListData empty = Definition.CreateEmpty(WidgetTestData.Seed);
        Assert.Empty(empty.Committees);
        Assert.Equal("Committees", empty.Heading);
        Assert.Equal("Please notify the Worshipful Master to be added or removed.", empty.NoticeText);

        WidgetDrawList drawList = WidgetTestData.LayOut(Definition, empty, Definition.DefaultSizePt.Width);
        Assert.True(drawList.IsEmpty);
        Assert.Equal("Committees — not filled in yet.", drawList.EmptyPromptText);
    }
}
