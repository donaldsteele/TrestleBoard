using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.SimpleList;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// M60's seventh widget: a list of the user's own (PLAN.md §11 M60).
///
/// <para>Its layout is held by golden assertions on the draw list rather than by a pixel baseline.
/// A baseline would have to be baked on three operating systems before it could fail honestly, and
/// what matters here is the geometry — how many columns, where the rule goes, that nothing is
/// dropped — which is identical on every OS and is what <c>SnapshotInfra</c>'s own note calls the
/// real cross-OS guarantee.</para>
/// </summary>
public sealed class SimpleListTests
{
    private static readonly SimpleListDefinition Definition = new();

    private static SimpleListData Schedule() => new()
    {
        Heading = "Degree schedule",
        FirstColumn = "Date",
        SecondColumn = "Degree",
        Rows =
        [
            new SimpleListRow { First = "14 September", Second = "Entered Apprentice" },
            new SimpleListRow { First = "12 October", Second = "Fellowcraft" },
        ],
    };

    // ---- how many columns there are --------------------------------------------------------

    [Fact]
    public void AListWithOneHeadingHasOneColumn() =>
        Assert.Equal(1, new SimpleListData { FirstColumn = "What" }.ColumnCount);

    [Fact]
    public void AListWithTwoHeadingsHasTwoColumns() => Assert.Equal(2, Schedule().ColumnCount);

    [Fact]
    public void AListWithThreeHeadingsHasThree()
    {
        SimpleListData data = Schedule();
        data.ThirdColumn = "Who is working it";

        Assert.Equal(3, data.ColumnCount);
    }

    /// <summary>
    /// The count is derived from the headings rather than stored, so the two can never disagree —
    /// and a stored count that disagreed with its headings would print the wrong table.
    /// </summary>
    [Fact]
    public void ClearingTheSecondHeadingClearsTheSecondColumn()
    {
        SimpleListData data = Schedule();
        data.SecondColumn = "   ";

        Assert.Equal(1, data.ColumnCount);
        Assert.Single(data.ColumnHeadings());
    }

    /// <summary>Three boxes and no way to ask for a fourth: the cap is on the question.</summary>
    [Fact]
    public void ThereIsNoWayToAskForAFourthColumn()
    {
        var everything = new SimpleListData
        {
            FirstColumn = "One",
            SecondColumn = "Two",
            ThirdColumn = "Three",
        };

        Assert.Equal(3, everything.ColumnCount);
        Assert.Equal(3, everything.ColumnHeadings().Count);
        Assert.Equal(3, new SimpleListRow { First = "a", Second = "b", Third = "c" }.Cells(99).Count);
    }

    // ---- rows -------------------------------------------------------------------------------

    [Fact]
    public void ABlankRowIsAStrayEnterRatherThanAnEntry()
    {
        SimpleListData data = Schedule();
        data.Rows.Add(new SimpleListRow());
        data.Rows.Add(new SimpleListRow { First = "  ", Second = "\t" });

        Assert.Equal(2, data.FilledRows().Count);
    }

    [Fact]
    public void ARowKeepsOnlyAsManyCellsAsThereAreColumns()
    {
        var row = new SimpleListRow { First = "14 September", Second = "Entered Apprentice", Third = "ignored" };

        Assert.Equal(["14 September"], row.Cells(1));
        Assert.Equal(["14 September", "Entered Apprentice"], row.Cells(2));
    }

    // ---- the wizard --------------------------------------------------------------------------

    /// <summary>
    /// PLAN.md's guarantee: one question at a time, and the three-column cap is what keeps it true.
    /// A step with more than three fields throws at construction, so this also proves the wizard
    /// could not have been built with a fourth column.
    /// </summary>
    [Fact]
    public void TheWizardAsksTheRowsOneAtATime()
    {
        WizardSession session = WizardSession.Create(
            Definition, existingData: null, dataVersion: 1, seed: new WidgetSeed("", 9, 2026, ""));

        Assert.Contains("one at a time", session.IntroText, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.ScreenCount >= 3, "name, columns, rows and a review at least.");
    }

    [Fact]
    public void AnInsertedListIsEmptyAndSaysSo()
    {
        SimpleListData fresh = Definition.CreateEmpty(new WidgetSeed("Indian Land Lodge 414", 9, 2026, ""));

        Assert.Empty(fresh.Rows);
        Assert.Equal("", fresh.Heading);
        Assert.Equal("", fresh.FirstColumn);
    }

    // ---- the round trip ------------------------------------------------------------------------

    /// <summary>
    /// The format needed no migration for this widget, and this is why: the payload is opaque JSON
    /// under <c>WidgetBlock.Data</c>, so a new widget type is new data rather than a new format.
    /// </summary>
    [Fact]
    public void ThePayloadSurvivesBeingWrittenAndReadBack()
    {
        SimpleListData before = Schedule();

        JsonElement written = Definition.WriteData(before);
        Assert.True(Definition.TryReadData(written, 1, out object read));
        var after = (SimpleListData)read;

        Assert.Equal(before.Heading, after.Heading);
        Assert.Equal(before.ColumnCount, after.ColumnCount);
        Assert.Equal(
            before.Rows.Select(r => r.First + "|" + r.Second),
            after.Rows.Select(r => r.First + "|" + r.Second));
    }

    /// <summary>Gate 9's shape, applied here: writing what was read changes nothing.</summary>
    [Fact]
    public void WritingWhatWasReadChangesNothing()
    {
        JsonElement once = Definition.WriteData(Schedule());
        Assert.True(Definition.TryReadData(once, 1, out object read));
        JsonElement twice = Definition.WriteData((SimpleListData)read);

        Assert.Equal(once.GetRawText(), twice.GetRawText());
    }

    [Fact]
    public void AFieldFromANewerTrestleBoardIsKeptRatherThanDropped()
    {
        JsonElement newer = JsonDocument.Parse(
            """{"heading":"Degree schedule","firstColumn":"Date","somethingNewer":true,"rows":[]}""")
            .RootElement;

        Assert.True(Definition.TryReadData(newer, 1, out object read));
        JsonElement again = Definition.WriteData((SimpleListData)read);

        Assert.Contains("somethingNewer", again.GetRawText(), StringComparison.Ordinal);
    }

    // ---- the drawn table -------------------------------------------------------------------------

    [Fact]
    public void TheTableDrawsAHeaderRowAndOneRowPerEntry()
    {
        WidgetDrawList drawn = LayoutOf(Schedule());

        // Two heading cells, four body cells, and the heading above them all.
        Assert.Equal(7, drawn.Items.OfType<WidgetTextItem>().Count());
        Assert.False(drawn.IsEmpty);
    }

    [Fact]
    public void AnEmptyListPromptsOnScreenAndNamesItself()
    {
        WidgetDrawList drawn = LayoutOf(new SimpleListData { Heading = "Degree schedule", FirstColumn = "Date" });

        Assert.True(drawn.IsEmpty);
        Assert.Contains("Degree schedule", drawn.EmptyPromptText, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyUnnamedListStillSaysWhatItIs()
    {
        WidgetDrawList drawn = LayoutOf(new SimpleListData());

        Assert.True(drawn.IsEmpty);
        Assert.Contains("list of your own", drawn.EmptyPromptText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nothing is truncated: every cell's text reaches the draw list.</summary>
    [Fact]
    public void NoCellIsSilentlyDropped()
    {
        WidgetDrawList drawn = LayoutOf(Schedule());
        string all = string.Concat(drawn.Items.OfType<WidgetTextItem>().Select(t => t.Text));

        foreach (string expected in new[]
        {
            "Degree schedule", "Date", "Degree", "14 September", "Entered Apprentice",
            "12 October", "Fellowcraft",
        })
        {
            Assert.Contains(expected, all, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSameListDrawsTheSameThingTwice()
    {
        string first = Describe(LayoutOf(Schedule()));
        string second = Describe(LayoutOf(Schedule()));

        Assert.Equal(first, second);
    }

    private static WidgetDrawList LayoutOf(SimpleListData data) =>
        WidgetTestData.LayOut(Definition, data, widthPt: 320f);

    private static string Describe(WidgetDrawList drawn) =>
        string.Join(
            ";",
            drawn.Items.OfType<WidgetTextItem>().Select(t =>
                string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{t.Text}@{t.Runs.Count}")));
}
