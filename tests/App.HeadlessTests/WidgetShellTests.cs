using System.Text.Json;
using Avalonia.Headless;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Wizards;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// THE M7 acceptance run (PLAN.md §11-M7), driven through the real shell: the Officers wizard end
/// to end produces a styled table on the page with body text wrapped around it; re-editing
/// pre-fills; the data survives save and reload; one wizard run is one undo step.
/// Every value fictional (PLAN.md §0).
/// </summary>
public sealed class WidgetShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task TheOfficersWizardRunsEndToEndAndLandsAStyledTableOnThePage()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            WizardSession wizard = NewWizard(window, blockId);

            // One heading screen, twelve office screens, one review screen.
            Assert.Equal(14, wizard.ScreenCount);
            Assert.True(wizard.TryGoNext());
            Assert.Equal("Worshipful Master", wizard.ScreenTitle);

            // A mistyped phone number stops the user where they are, in plain language.
            wizard.SetValue("phone", "call the lodge");
            Assert.False(wizard.TryGoNext());
            Assert.Equal(
                "Phone numbers look like 555-0100. Please check this one.",
                wizard.Errors[0].Message);
            wizard.SetValue("phone", "555-0100");

            for (int i = 0; i < 12; i++)
            {
                // Two offices are left vacant, and one brother has no phone.
                if (i is not (5 or 6))
                {
                    wizard.SetValue("name", $"{(char)('A' + i)}. Placeholder");
                    wizard.SetValue("phone", i == 9 ? "" : $"555-01{i:00}");
                }

                Assert.True(wizard.TryGoNext());
            }

            Assert.True(wizard.IsReviewScreen);
            Assert.Equal(13, wizard.ReviewLines.Count);
            Assert.True(wizard.TryCommit(out JsonElement data, out int version, out _));
            Assert.True(window.WidgetsForTest.ApplyWidgetData(blockId, data, version, wizard.UndoLabel));

            // The table is on the page, in the printed order, with the vacancies kept.
            Assert.True(window.SourceForTest!.TryGetWidgetDrawList(blockId, out var drawList));
            List<string> lines = TextInOrder(drawList!);
            Assert.Equal("Worshipful Master", lines.First(l => l.Contains("Worshipful", StringComparison.Ordinal)));
            Assert.Equal(2, lines.Count(l => l == "(vacant)"));
            Assert.Equal(12, lines.Count(l => OfficersTableData.StandardPositions.Contains(l)));

            // Twelve row rules plus the one under the heading, and their colour and weight come from
            // the widget's resolved style rather than being hardcoded in the layouter.
            var rules = drawList!.Items.OfType<Layout.Widgets.WidgetRuleItem>().ToList();
            Assert.Equal(13, rules.Count);
            Assert.All(rules, r => Assert.Equal(WidgetStyleDefaults.Standard.RuleArgb, r.ColorArgb));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BodyTextWrapsAroundTheTableAndUnwrapsWhenItGoes()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            string textBlockId = FirstTextBlockId(window);
            int before = LinesPushedAside(window, textBlockId);

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            window.WidgetsForTest.ApplyWidgetData(blockId, FilledOfficers(), 1, "Edit lodge officers");

            // Park it over the right-hand side of the body frame so there is something to flow past.
            RectPt text = window.SourceForTest!.GetEffectiveRect(textBlockId);
            window.SessionForTest!.Execute(new Core.Commands.MoveBlockCommand(
                blockId,
                window.SourceForTest.GetEffectiveRect(blockId) with
                {
                    X = text.Right - 200f,
                    Y = text.Y + 40f,
                }));

            int wrapped = LinesPushedAside(window, textBlockId);
            Assert.True(wrapped > before, "the body text should have flowed around the table");

            // And the wrap is caused by THIS block: take it away and the lines come back.
            window.FramesForTest!.Select(blockId);
            window.FramesForTest.DeleteSelected();
            Assert.Equal(before, LinesPushedAside(window, textBlockId));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReEditingPrefillsEveryAnswerIncludingTheBlanks()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            window.WidgetsForTest.ApplyWidgetData(blockId, FilledOfficers(), 1, "Edit lodge officers");

            WizardSession reopened = NewWizard(window, blockId);
            Assert.False(reopened.IsDirty);
            reopened.TryGoNext();

            for (int i = 0; i < 12; i++)
            {
                Assert.Equal(i is 5 or 6 ? "" : $"{(char)('A' + i)}. Placeholder", reopened.GetValue("name"));
                Assert.Equal(i is 5 or 6 or 9 ? "" : $"555-01{i:00}", reopened.GetValue("phone"));
                reopened.TryGoNext();
            }

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WidgetDataSurvivesSaveAndReload()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            window.WidgetsForTest.ApplyWidgetData(blockId, FilledOfficers(), 1, "Edit lodge officers");

            var definition = new OfficersTableDefinition();
            var before = (WidgetBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
            Assert.True(definition.TryReadData(before.Data, before.DataVersion, out object original));
            string beforeJson = definition.WriteData(original).GetRawText();

            using var buffer = new MemoryStream();
            TboardContainer.Save(window.PackageForTest!, buffer);
            buffer.Position = 0;
            TboardPackage reloaded = TboardContainer.Load(buffer);

            var after = (WidgetBlock)reloaded.Document.FindBlock(blockId).Block;
            Assert.Equal("officersTable", after.WidgetType);
            Assert.Equal(before.DataVersion, after.DataVersion);

            // The container re-indents the payload to sit inside document.json, so the comparison is
            // on the decoded value, not the raw text (docs/M7-spec.md §1.2).
            Assert.True(definition.TryReadData(after.Data, after.DataVersion, out object typed));
            Assert.Equal(beforeJson, definition.WriteData(typed).GetRawText());
            var data = (OfficersTableData)typed;
            Assert.Equal(12, data.Officers.Count);
            Assert.Equal("A. Placeholder", data.Officers[0].Name);
            Assert.Equal("", data.Officers[5].Name);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OneWizardRunIsOneUndoStep()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            string blockId = window.WidgetsForTest!.InsertWidget(0, "officersTable");
            window.WidgetsForTest.ApplyWidgetData(blockId, FilledOfficers(), 1, "Edit lodge officers");

            Assert.Equal("Edit lodge officers", window.SessionForTest!.UndoDescription);

            // First undo puts the empty table back; the second takes it off the page.
            window.SessionForTest.Undo();
            var block = (WidgetBlock)window.SessionForTest.Document.FindBlock(blockId).Block;
            Assert.DoesNotContain("Placeholder", block.Data!.Value.GetRawText(), StringComparison.Ordinal);
            Assert.Equal("Add lodge officers", window.SessionForTest.UndoDescription);

            window.SessionForTest.Undo();
            Assert.DoesNotContain(window.SessionForTest.Document.Pages[0].Blocks, b => b.Id == blockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EveryWidgetIsReachableFromTheInsertMenu()
    {
        await Session.Dispatch(() =>
        {
            // Deliberately NOT shown: these assertions are on the document and the controllers, and
            // a shown window leaves a queued menu-measure that the headless session's teardown runs
            // against an already-disposed font manager.
            var window = new MainWindow();
            window.OpenSample();

            // Six widgets, six menu items — a keyboard user never has to go through a picker.
            Assert.Equal(6, window.WidgetProviderForTest.ListWidgets().Count);
            foreach (var info in window.WidgetProviderForTest.ListWidgets())
            {
                string id = window.WidgetsForTest!.InsertWidget(0, info.TypeId);
                Assert.True(window.WidgetsForTest.CanEdit(id));
                Assert.Equal(WrapMode.Rectangle, window.SessionForTest!.Document.FindBlock(id).Block.WrapMode);
            }

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// Drains the queued layout pass BEFORE the window goes away. Toggling menu item enablement
    /// dirties the menu's text; if that measure is still queued when the session tears the
    /// dispatcher down, it runs against a disposed font manager and takes the whole run with it.
    /// </summary>
    private static void Settle(MainWindow window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static WizardSession NewWizard(MainWindow window, string blockId)
    {
        Assert.True(window.WidgetProviderForTest.Registry.TryGet(
            window.WidgetsForTest!.GetWidgetType(blockId), out IWidgetDefinition? definition));
        var block = (WidgetBlock)window.SessionForTest!.Document.FindBlock(blockId).Block;
        return WizardSession.Create(
            definition!,
            block.Data,
            block.DataVersion,
            Editing.WidgetController.SeedFrom(window.SessionForTest.Document));
    }

    private static JsonElement FilledOfficers()
    {
        var definition = new OfficersTableDefinition();
        OfficersTableData data = definition.CreateEmpty(new Layout.Widgets.WidgetSeed("", 1, 2026, ""));
        for (int i = 0; i < data.Officers.Count; i++)
        {
            if (i is 5 or 6)
            {
                continue;
            }

            data.Officers[i].Name = $"{(char)('A' + i)}. Placeholder";
            data.Officers[i].Phone = i == 9 ? null : $"555-01{i:00}";
        }

        return definition.WriteData(data);
    }

    private static string FirstTextBlockId(MainWindow window) =>
        window.SessionForTest!.Document.Pages[0].Blocks.OfType<TextBlock>().First().Id;

    private static int LinesPushedAside(MainWindow window, string textBlockId)
    {
        Assert.True(window.SourceForTest!.TryGetFrameLayout(textBlockId, out FrameLayout? layout));
        RectPt frame = window.SourceForTest.GetEffectiveRect(textBlockId);
        // A line the widget touched either split in two or lost width to it. XRange is the interval
        // the engine had available, not the text's own width, so a short last line does not count.
        return layout!.Lines.Count(
            l => l.Segments.Count > 1
                || (l.Segments.Count == 1 && l.Segments[0].XRange.Width < frame.Width - 1f));
    }

    private static List<string> TextInOrder(Layout.Widgets.WidgetDrawList list)
    {
        var runs = new List<(float Y, float X, string Text)>();
        foreach (Layout.Widgets.WidgetTextItem item in list.Items.OfType<Layout.Widgets.WidgetTextItem>())
        {
            foreach (PositionedGlyphRun run in item.Runs)
            {
                runs.Add((run.BaselineY, run.OriginX, item.Text));
            }
        }

        return runs.OrderBy(r => r.Y).ThenBy(r => r.X).Select(r => r.Text).ToList();
    }
}
