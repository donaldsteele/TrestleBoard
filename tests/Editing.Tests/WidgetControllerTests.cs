using System.Text.Json;
using TrestleBoard.Core.Commands;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.Editing.Tests;

/// <summary>
/// Inserting and re-editing widgets (docs/M7-spec.md §7/§8): geometry, undo granularity,
/// fit-to-contents, and the refusal paths for a widget a later version wrote.
/// </summary>
public sealed class WidgetControllerTests
{
    private static readonly WidgetLayoutProvider Provider = WidgetLayoutProvider.CreateDefault();

    [Fact]
    public void InsertLandsInsideTheMarginsWithWrapTurnedOn()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");

        Assert.Equal("widget-1", blockId);
        var block = (WidgetBlock)harness.Session.Document.FindBlock(blockId).Block;
        PageMaster master = harness.Session.Document.GetMaster("master-1");

        // Text wraps around every widget by default — that is PLAN.md §5's promise.
        Assert.Equal(WrapMode.Rectangle, block.WrapMode);
        Assert.Equal(6f, block.WrapMarginPt);
        Assert.True(block.FrameRect.X >= master.MarginLeftPt);
        Assert.True(block.FrameRect.Y >= master.MarginTopPt);
        Assert.True(block.FrameRect.Right <= master.Size.Width - master.MarginRightPt + 0.01f);
    }

    [Fact]
    public void SuccessiveInsertsCascadeAndGetTheirOwnIds()
    {
        using var harness = new WidgetHarness();
        string first = harness.Widgets.InsertWidget(0, "officersTable");
        string second = harness.Widgets.InsertWidget(0, "birthdayList");

        Assert.Equal("widget-1", first);
        Assert.Equal("widget-2", second);
        RectPt a = harness.Rect(first);
        RectPt b = harness.Rect(second);
        Assert.Equal(18f, b.X - a.X, 3);
        Assert.Equal(18f, b.Y - a.Y, 3);
    }

    [Fact]
    public void ANewWidgetCarriesNoPeople()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");

        var block = (WidgetBlock)harness.Session.Document.FindBlock(blockId).Block;
        string json = block.Data!.Value.GetRawText();
        Assert.Contains("Worshipful Master", json, StringComparison.Ordinal);
        Assert.DoesNotContain("555-", json, StringComparison.Ordinal);

        // Every name is present but blank: the twelve offices are structure, the people are not.
        Assert.DoesNotContain("Placeholder", json, StringComparison.Ordinal);
        Assert.Equal(12, json.Split("\"name\": \"\"", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void InsertIsOneUndoStepAndTakesTheWidgetBackOffThePage()
    {
        using var harness = new WidgetHarness();
        harness.Widgets.InsertWidget(0, "officersTable");

        Assert.Equal("Add lodge officers", harness.Session.UndoDescription);
        harness.Session.Undo();
        Assert.DoesNotContain(harness.Session.Document.Pages[0].Blocks, b => b is WidgetBlock);
    }

    [Fact]
    public void OneWizardRunIsOneUndoStepThatAlsoCarriesTheResize()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        float before = harness.Rect(blockId).Height;

        (JsonElement data, int version) = WidgetHarness.FilledOfficers();
        Assert.True(harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers"));

        Assert.Equal("Edit lodge officers", harness.Session.UndoDescription);
        Assert.NotEqual(before, harness.Rect(blockId).Height);

        // One Ctrl+Z restores BOTH the old data and the old height.
        harness.Session.Undo();
        Assert.Equal(before, harness.Rect(blockId).Height, 3);
        Assert.Equal("Add lodge officers", harness.Session.UndoDescription);
    }

    [Fact]
    public void AnEditThatDoesNotChangeTheHeightCarriesNoResize()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        (JsonElement data, int version) = WidgetHarness.FilledOfficers();
        harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers");
        float fitted = harness.Rect(blockId).Height;

        // Same payload again: nothing moves, so nothing but the data command is issued.
        Assert.True(harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers"));
        Assert.Equal(fitted, harness.Rect(blockId).Height, 3);
    }

    [Fact]
    public void FitToContentsIsOneResizeAndOnlyWhenSomethingMoved()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        (JsonElement data, int version) = WidgetHarness.FilledOfficers();
        harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers");

        // Already fitted by the commit, so there is nothing left to do.
        Assert.False(harness.Widgets.FitToContents(blockId));

        harness.Session.Execute(new ResizeBlockCommand(
            blockId, harness.Rect(blockId) with { Height = 40f }));
        Assert.True(harness.Widgets.IsOverflowing(blockId));

        Assert.True(harness.Widgets.FitToContents(blockId));
        Assert.False(harness.Widgets.IsOverflowing(blockId));
    }

    [Fact]
    public void AWidgetFromANewerVersionIsMovableButNotEditable()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        var block = (WidgetBlock)harness.Session.Document.FindBlock(blockId).Block;
        block.DataVersion = 99;

        Assert.True(harness.Widgets.IsWidget(blockId));
        Assert.False(harness.Widgets.CanEdit(blockId));

        (JsonElement data, int version) = WidgetHarness.FilledOfficers();
        int undoDepthBefore = harness.Session.UndoDescription?.Length ?? 0;
        Assert.False(harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers"));
        Assert.Equal("Add lodge officers", harness.Session.UndoDescription);
        Assert.Equal(WidgetController.NewerVersionMessage, harness.Widgets.StatusMessage);
        Assert.True(undoDepthBefore > 0);

        // It is still a rectangle like any other: the frame editor moves and deletes it.
        harness.Frames.Select(blockId);
        Assert.True(harness.Frames.Nudge(10f, 0f));
        Assert.NotEqual(0f, harness.Rect(blockId).X - block.FrameRect.X + 1f);
    }

    [Fact]
    public void AnUnknownWidgetTypeIsNotEditableEither()
    {
        using var harness = new WidgetHarness();
        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        var block = (WidgetBlock)harness.Session.Document.FindBlock(blockId).Block;
        block.WidgetType = "memorialPanel";

        Assert.True(harness.Widgets.IsWidget(blockId));
        Assert.False(harness.Widgets.CanEdit(blockId));
        Assert.False(harness.Widgets.FitToContents(blockId));
    }

    [Fact]
    public void BodyTextWrapsAroundAnInsertedWidget()
    {
        using var harness = new WidgetHarness(withProse: true);
        int before = harness.LinesPushedAside();

        string blockId = harness.Widgets.InsertWidget(0, "officersTable");
        (JsonElement data, int version) = WidgetHarness.FilledOfficers();
        harness.Widgets.ApplyWidgetData(blockId, data, version, "Edit lodge officers");

        Assert.True(
            harness.LinesPushedAside() > before,
            "at least one line should have split around the widget");
    }

    private sealed class WidgetHarness : IDisposable
    {
        private readonly EditorTestHarness _inner;

        public WidgetHarness(bool withProse = false)
        {
            _inner = new EditorTestHarness(
                withProse
                    ? string.Concat(Enumerable.Repeat(
                        "The Placeholder Lodge meets on the appointed evening and the brothers gather early. ", 12))
                    : "",
                withExclusion: false,
                widgets: Provider);
            Widgets = new WidgetController(_inner.Session, _inner.Source, Provider);
        }

        public WidgetController Widgets { get; }

        public Core.Commands.DocumentSession Session => _inner.Session;

        public FrameEditorController Frames => _inner.Frames;

        public RectPt Rect(string blockId) => Session.Document.FindBlock(blockId).Block.FrameRect;

        public static (JsonElement Data, int Version) FilledOfficers()
        {
            var definition = new OfficersTableDefinition();
            OfficersTableData data = definition.CreateEmpty(new WidgetSeed("", 1, 2026, ""));
            for (int i = 0; i < data.Officers.Count; i++)
            {
                data.Officers[i].Name = $"{(char)('A' + i)}. Placeholder";
                data.Officers[i].Phone = $"555-01{i:00}";
            }

            return (definition.WriteData(data), definition.CurrentDataVersion);
        }

        /// <summary>
        /// Lines the widget pushed aside: a line whose text no longer starts at the frame's left
        /// edge is one that flowed around the block.
        /// </summary>
        public int LinesPushedAside()
        {
            Assert.True(_inner.Source.TryGetFrameLayout(EditorTestHarness.BlockId, out Layout.FrameLayout? layout));
            return layout!.Lines.Count(
                l => l.Segments.Count > 1 || (l.Segments.Count == 1 && l.Segments[0].XRange.Left > 55f));
        }

        public void Dispose() => _inner.Dispose();
    }
}
