using System.Text.Json;
using TrestleBoard.Layout;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// The officers table is the acceptance widget (PLAN.md §11-M7): its wizard is the one the headless
/// shell test drives end to end. Fixtures are fictional throughout (PLAN.md §0).
/// </summary>
public sealed class OfficersTableTests
{
    private static readonly OfficersTableDefinition Definition = new();

    [Fact]
    public void NewTableCarriesTheTwelvePositionsAndNoPeople()
    {
        OfficersTableData data = Definition.CreateEmpty(WidgetTestData.Seed);

        Assert.Equal(OfficersTableData.StandardPositions, data.Officers.Select(o => o.Position));
        Assert.All(data.Officers, o => Assert.Equal("", o.Name));
        Assert.All(data.Officers, o => Assert.Null(o.Phone));
    }

    [Fact]
    public void EveryRowIsPrintedIncludingVacancies()
    {
        OfficersTableData data = Filled(blankNames: [5, 6], phones: true);
        WidgetDrawList list = WidgetTestData.LayOut(Definition, data, 240f);

        List<string> lines = TextOrder(list);
        Assert.Contains("Worshipful Master", lines);
        Assert.Equal(2, lines.Count(l => l == data.VacantText));

        // Twelve rows, each closed by a hairline, plus the rule under the heading.
        Assert.Equal(13, list.Items.OfType<WidgetRuleItem>().Count());
    }

    [Fact]
    public void RowsPrintInStandardOrderNoMatterHowTheyWereStored()
    {
        OfficersTableData data = Filled(blankNames: [], phones: false);
        data.Officers.Reverse();

        List<string> lines = TextOrder(WidgetTestData.LayOut(Definition, data, 240f));
        int master = lines.IndexOf("Worshipful Master");
        int marshall = lines.IndexOf("Marshall");
        Assert.True(master >= 0 && marshall > master, "the twelve offices must print in their fixed order");
    }

    [Fact]
    public void ThePhoneColumnCollapsesWhenNobodyHasAPhone()
    {
        WidgetDrawList withPhones = WidgetTestData.LayOut(Definition, Filled([], phones: true), 240f);
        WidgetDrawList without = WidgetTestData.LayOut(Definition, Filled([], phones: false), 240f);

        Assert.Contains(WidgetTestData.Phones[0], TextOrder(withPhones));
        Assert.DoesNotContain(TextOrder(without), l => l.Contains("555-", StringComparison.Ordinal));

        // The phone column is right-aligned to the widget edge, so it is the only thing that can
        // reach it; with the column gone, nothing does.
        Assert.True(RightmostTextEdge(withPhones) > 235f, "phones right-align to the widget edge");
        Assert.True(RightmostTextEdge(without) < 235f, "with no phone column nothing reaches the edge");
    }

    [Fact]
    public void AnUnknownOfficePrintsAfterTheTwelve()
    {
        OfficersTableData data = Filled([], phones: false);
        data.Officers.Add(new OfficerEntry { Position = "Historian", Name = WidgetTestData.Names[0] });

        List<string> lines = TextOrder(WidgetTestData.LayOut(Definition, data, 240f));
        Assert.True(lines.IndexOf("Historian") > lines.IndexOf("Marshall"));
    }

    [Fact]
    public void TheWizardWalksOneOfficePerScreenAndEndsInReview()
    {
        WizardSession session = NewSession();

        // One heading screen, twelve office screens, one review screen.
        Assert.Equal(14, session.ScreenCount);
        Assert.True(session.TryGoNext());
        Assert.Equal("Worshipful Master", session.ScreenTitle);

        for (int i = 0; i < 12; i++)
        {
            session.SetValue("name", WidgetTestData.Names[i]);
            session.SetValue("phone", WidgetTestData.Phones[i]);
            Assert.True(session.TryGoNext());
        }

        Assert.True(session.IsReviewScreen);
        Assert.Equal(13, session.ReviewLines.Count);
        Assert.True(session.TryCommit(out JsonElement data, out int version, out _));
        Assert.Equal(Definition.CurrentDataVersion, version);
        Assert.Equal(12, data.GetProperty("officers").GetArrayLength());
    }

    [Fact]
    public void AVacantOfficeIsAllowedToBeBlank()
    {
        WizardSession session = NewSession();
        session.TryGoNext();

        // Both fields optional: an unfilled office must not block the wizard.
        Assert.True(session.TryGoNext());
        Assert.Empty(session.Errors);
    }

    [Fact]
    public void AMistypedPhoneNumberIsRefusedInPlainLanguage()
    {
        WizardSession session = NewSession();
        session.TryGoNext();
        session.SetValue("phone", "not a phone");

        Assert.False(session.TryGoNext());
        Assert.Equal(
            "Phone numbers look like 555-0100. Please check this one.",
            session.Errors[0].Message);
    }

    [Fact]
    public void ShowAllRowsCollapsesTheTwelveScreensIntoOne()
    {
        WizardSession session = NewSession();
        session.TryGoNext();
        session.ShowAllRows();

        // Heading, one list screen, review.
        Assert.Equal(3, session.ScreenCount);
        Assert.Equal(12, session.RowCount);
    }

    [Fact]
    public void ReEditingPrefillsFromTheStoredPayload()
    {
        OfficersTableData data = Filled([], phones: true);
        JsonElement stored = Definition.WriteData(data);

        WizardSession session = WizardSession.Create(Definition, stored, 1, WidgetTestData.Seed);
        Assert.False(session.IsDirty);
        session.TryGoNext();
        Assert.Equal(WidgetTestData.Names[0], session.GetValue("name"));
        Assert.Equal(WidgetTestData.Phones[0], session.GetValue("phone"));
    }

    [Fact]
    public void PayloadsRoundTripAndKeepPropertiesThisBuildDoesNotKnow()
    {
        OfficersTableData data = Filled([], phones: true);
        JsonElement written = Definition.WriteData(data);

        using JsonDocument doc = JsonDocument.Parse(
            written.GetRawText().Replace("\"heading\"", "\"futureFlag\": true, \"heading\"", StringComparison.Ordinal));
        JsonElement withUnknown = doc.RootElement.Clone();

        Assert.True(Definition.TryReadData(withUnknown, 1, out object typed));
        var read = (OfficersTableData)typed;
        Assert.Equal(data.Officers.Count, read.Officers.Count);
        Assert.Equal(WidgetTestData.Names[0], read.Officers[0].Name);
        Assert.True(read.ExtraProperties!.ContainsKey("futureFlag"));
        Assert.Contains("futureFlag", Definition.WriteData(read).GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void APayloadFromANewerVersionIsRefusedRatherThanGuessedAt()
    {
        JsonElement written = Definition.WriteData(Filled([], phones: false));

        Assert.False(Definition.TryReadData(written, Definition.CurrentDataVersion + 1, out _));
    }

    [Fact]
    public void AnEmptyTableAsksToBeFilledIn()
    {
        WidgetDrawList list = WidgetTestData.LayOut(
            Definition, Definition.CreateEmpty(WidgetTestData.Seed), 240f);

        Assert.True(list.IsEmpty);
        Assert.Equal("Lodge officers — not filled in yet.", list.EmptyPromptText);
    }

    private static WizardSession NewSession() =>
        WizardSession.Create(Definition, null, 1, WidgetTestData.Seed);

    private static OfficersTableData Filled(IReadOnlyList<int> blankNames, bool phones)
    {
        OfficersTableData data = Definition.CreateEmpty(WidgetTestData.Seed);
        for (int i = 0; i < data.Officers.Count; i++)
        {
            if (blankNames.Contains(i))
            {
                continue;
            }

            data.Officers[i].Name = WidgetTestData.Names[i];
            if (phones)
            {
                data.Officers[i].Phone = WidgetTestData.Phones[i];
            }
        }

        return data;
    }

    /// <summary>Rendered strings in painting order — the draw list carries glyphs, not text, so the
    /// order is recovered from the runs' baselines and origins.</summary>
    private static List<string> TextOrder(WidgetDrawList list)
    {
        var runs = new List<(float Y, float X, string Text)>();
        foreach (WidgetTextItem item in list.Items.OfType<WidgetTextItem>())
        {
            foreach (PositionedGlyphRun run in item.Runs)
            {
                runs.Add((run.BaselineY, run.OriginX, item.Text));
            }
        }

        return runs.OrderBy(r => r.Y).ThenBy(r => r.X).Select(r => r.Text).ToList();
    }

    private static float RightmostTextEdge(WidgetDrawList list) =>
        list.Items.OfType<WidgetTextItem>()
            .SelectMany(i => i.Runs)
            .Max(r => r.OriginX + r.AdvanceWidthPt);
}
