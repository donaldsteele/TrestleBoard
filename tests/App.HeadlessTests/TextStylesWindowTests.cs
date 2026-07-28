using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M20's gate (PLAN.md §12 item 17): the font window never discards a pending choice without
/// applying it or saying why, its category headings are not choices, two kinds of writing can be
/// changed in one visit, and "just here" is its own window with its own truthful warning.
/// <para>
/// M14's engine semantics are untouched by M20, which is why <see cref="TextStylesShellTests"/>
/// re-runs unchanged beside this file — that suite IS the proof.
/// </para>
/// </summary>
public sealed class TextStylesWindowTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task TheRolesAreListedInTheOrderAPageIsReadIn()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            List<string> roles = [.. sheet.RoleNamesForTest];

            // Declared order, not the alphabet of a name the user is never shown.
            List<string> declared = [.. StyleLabels.DeclaredOrder.Where(roles.Contains)];
            Assert.Equal(declared, roles.Where(declared.Contains).ToList());
            Assert.True(
                roles.IndexOf("display") < roles.IndexOf("body"),
                "the cover title must come before the body text");
            Assert.True(
                roles.IndexOf("heading") < roles.IndexOf("body"),
                "headings must come before the words under them");

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ACategoryHeadingCannotBeSelectedAndKeepsThePendingChoice()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.SelectFontForTest("Lora");
            Assert.Equal("Lora", sheet.SelectedFamilyForTest);
            Assert.True(sheet.HasPendingChangeForTest);

            Assert.NotEmpty(sheet.HeaderIndexesForTest);
            int header = sheet.HeaderIndexesForTest[0];
            sheet.SelectRowForTest(header);

            // The heading refuses the selection and hands it to a family — and, crucially, the
            // choice the user had already made is still there. Before M20 it was silently gone.
            Assert.NotEqual(header, sheet.SelectedRowIndexForTest);
            Assert.NotNull(sheet.SelectedFamilyForTest);
            Assert.True(sheet.HasPendingChangeForTest);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EveryHeadingsWordsSurviveOnTheFamilyRowsAutomationName()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();

            // The grouping is not lost by making the heading unselectable: a screen reader hears
            // it on each row, which is M14 §13's grouped-list question answered by test.
            int firstFamily = sheet.FontRowLabelsForTest
                .Select((_, i) => i)
                .First(i => !sheet.HeaderIndexesForTest.Contains(i));
            string name = sheet.AutomationNameForRowForTest(firstFamily) ?? string.Empty;
            Assert.Contains("Serif", name, StringComparison.OrdinalIgnoreCase);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApplyWithNothingPendingSaysSoInsteadOfDoingNothing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            var applied = new List<TextStyleChoice>();
            sheet.Applied += (_, choice) => applied.Add(choice);

            Assert.False(sheet.HasPendingChangeForTest);
            sheet.ApplyForTest();

            Assert.Empty(applied);
            Assert.Contains("Nothing to change yet", sheet.StatusForTest, StringComparison.Ordinal);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoKindsOfWritingCanBeChangedInOneVisit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.Applied += (_, choice) => window.ApplyTextStyleChoice(choice);

            sheet.SelectRoleForTest("body");
            sheet.SelectFontForTest("Lora");
            sheet.ApplyForTest();
            Assert.Equal("Lora", styles.GetCharacterStyle("body").FontFamily);

            // The window is still open, so the second role is one visit and not a second trip.
            sheet.SelectRoleForTest("heading");
            sheet.SelectFontForTest("Lato");
            sheet.ApplyForTest();
            Assert.Equal("Lato", styles.GetCharacterStyle("heading").FontFamily);

            // Two changes, two undo steps, in the order they were made.
            window.SessionForTest.Undo();
            Assert.NotEqual("Lato", styles.GetCharacterStyle("heading").FontFamily);
            Assert.Equal("Lora", styles.GetCharacterStyle("body").FontFamily);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SwitchingRolesAppliesThePendingChangeAndSaysWhose()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.Applied += (_, choice) => window.ApplyTextStyleChoice(choice);

            sheet.SelectRoleForTest("body");
            sheet.SelectFontForTest("Lora");
            Assert.True(sheet.HasPendingChangeForTest);

            sheet.SelectRoleForTest("caption");

            Assert.Equal("Lora", styles.GetCharacterStyle("body").FontFamily);
            Assert.Contains("was applied before moving on", sheet.StatusForTest, StringComparison.Ordinal);
            Assert.Contains("Body text", sheet.StatusForTest, StringComparison.Ordinal);
            Assert.False(sheet.HasPendingChangeForTest);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClosingTheWindowAppliesAPendingChangeRatherThanLosingIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.Applied += (_, choice) => window.ApplyTextStyleChoice(choice);
            sheet.SelectRoleForTest("body");
            sheet.SelectFontForTest("Lora");

            // The title-bar X. It says nothing, so it must not mean "throw it away".
            sheet.CloseWithoutAnsweringForTest();

            Assert.Equal("Lora", styles.GetCharacterStyle("body").FontFamily);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancelIsTheOnlyPathThatChangesNothing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;
            string before = styles.GetCharacterStyle("body").FontFamily;

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.Applied += (_, choice) => window.ApplyTextStyleChoice(choice);
            sheet.SelectRoleForTest("body");
            sheet.SelectFontForTest("Lora");

            sheet.CancelForTest();

            Assert.Equal(before, styles.GetCharacterStyle("body").FontFamily);
            Assert.Null(sheet.Result);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JustHereShowsNoOtherRolesAndOnlyTheSelectionScopedWarning()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow picker = window.BuildJustHereWindowForTest();

            Assert.False(picker.ShowsRoleListForTest);
            Assert.Equal("Use a different font just here", picker.Title);
            Assert.Contains(
                "only the writing you have selected", picker.ReflowWarningForTest, StringComparison.Ordinal);
            Assert.DoesNotContain("different number of pages", picker.ReflowWarningForTest, StringComparison.Ordinal);

            // One entry, and it exists only to measure the pending change against — the user is
            // never offered a kind of writing this window is not going to change.
            Assert.Single(picker.RoleNamesForTest);

            picker.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task JustHereAppliesToTheHighlightedWordsAndCloses()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            Assert.True(window.EditorForTest!.TryBeginAt(0, 70f, 300f)
                        || window.EditorForTest.TryBeginAt(0, 70f, 200f)
                        || window.EditorForTest.TryBeginAt(0, 70f, 400f),
                "no text was found to click into on page one");
            window.EditorForTest.SelectAll();

            TextStylesWindow picker = window.BuildJustHereWindowForTest();
            var applied = new List<TextStyleChoice>();
            picker.Applied += (_, choice) => applied.Add(choice);

            picker.SelectFontForTest("Lora");
            picker.ApplyForTest();

            TextStyleChoice choice = Assert.Single(applied);
            Assert.Equal("Lora", choice.FontFamily);
            Assert.NotNull(picker.Result);
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheSecondAskForTheSamePreviewComesOutOfTheCache()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            int hitsBefore = sheet.PreviewCacheHitsForTest;
            int missesBefore = sheet.PreviewCacheMissesForTest;

            // Up a rung and back down: the size the preview lands on is one it has already drawn,
            // so the round trip costs exactly one new rasterisation, not two.
            sheet.StepSizeForTest(+1);
            sheet.StepSizeForTest(-1);

            Assert.True(sheet.PreviewCacheHitsForTest > hitsBefore,
                "the preview was re-rasterised for a family and size it had already drawn");
            Assert.Equal(missesBefore + 1, sheet.PreviewCacheMissesForTest);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AFamilyThisBuildDoesNotHaveShowsItselfInsteadOfBeingSilentlyAbsent()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            // Give a role a family this build does not bundle, exactly as an older document might.
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;
            styles.GetCharacterStyle("body").FontFamily = "Woodcut Antiqua";

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.SelectRoleForTest("body");

            Assert.Contains(
                sheet.FontRowLabelsForTest,
                label => label.Contains("Woodcut Antiqua", StringComparison.Ordinal)
                         && label.Contains("does not have it", StringComparison.Ordinal));

            // It is a statement, not a choice: it is not one of the families on offer.
            Assert.DoesNotContain("Woodcut Antiqua", sheet.VisibleFontsForTest);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheSizeStepperSaysWhenTheLadderRunsOutRatherThanStoppingInSilence()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            sheet.SelectRoleForTest("body");

            for (int i = 0; i < 40; i++)
            {
                sheet.StepSizeForTest(+1);
            }

            Assert.Contains("as large as TrestleBoard goes", sheet.StatusForTest, StringComparison.Ordinal);

            sheet.CancelForTest();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
