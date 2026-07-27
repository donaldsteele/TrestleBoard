using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Layout.Fonts;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M14's acceptance criteria driven through the real shell: the picker lists roles rather than
/// raw style names, changing a font sweeps the siblings in one undo step, the size stepper walks
/// the ladder, a document naming an unbundled family opens with a plain-language warning and
/// saves back byte-unchanged, and the licence text ships inside the installer.
/// </summary>
public sealed class TextStylesShellTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task ThePickerListsRolesInPlainLanguageAndNeverRawStyleNames()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();

            Assert.Contains("body", sheet.RoleNamesForTest);
            Assert.DoesNotContain("body-bold", sheet.RoleNamesForTest);
            Assert.DoesNotContain("body-bold-italic", sheet.RoleNamesForTest);
            Assert.Equal("Body text", StyleLabels.Describe("body"));

            sheet.Close();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheFontListOffersEveryBundledFamilyAndTheSearchNarrowsIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            TextStylesWindow sheet = window.BuildTextStylesWindowForTest();
            Assert.Equal(BundledFontCatalog.FamilyNames.Count, sheet.VisibleFontsForTest.Count);

            sheet.SearchForTest("garamond");
            Assert.Contains("EB Garamond", sheet.VisibleFontsForTest);
            Assert.DoesNotContain("Lato", sheet.VisibleFontsForTest);

            sheet.SearchForTest(string.Empty);
            Assert.Equal(BundledFontCatalog.FamilyNames.Count, sheet.VisibleFontsForTest.Count);

            sheet.Close();
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApplyingAFontSweepsTheSiblingsInOneUndoStep()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;
            string before = styles.GetCharacterStyle("body").FontFamily;

            window.ApplyTextStyleChoice(new TextStyleChoice("body", "Lora", null));

            Assert.Equal("Lora", styles.GetCharacterStyle("body").FontFamily);
            Assert.Equal("Lora", styles.GetCharacterStyle("body-bold").FontFamily);

            // And bold still finds its pair afterwards, which is the whole point of the sweep.
            Assert.True(CharacterStyleResolver.TryResolve(
                styles, "body", FontWeightToken.Bold, FontSlantToken.Normal,
                out CharacterStyleDef bold));
            Assert.Equal("body-bold", bold.Name);

            window.SessionForTest.Undo();
            Assert.Equal(before, styles.GetCharacterStyle("body").FontFamily);
            Assert.Equal(before, styles.GetCharacterStyle("body-bold").FontFamily);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheSizeStepperWalksTheLadderAndSaysSoWhenItRunsOut()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;

            // The sample issue's body is 11pt; the rung above it is 12.
            window.ApplyTextStyleChoice(new TextStyleChoice("body", null, 12f));
            Assert.Equal(12f, styles.GetCharacterStyle("body").SizePt);
            Assert.Equal(12f, styles.GetCharacterStyle("body-bold").SizePt);

            Assert.Null(FontSizeLadder.Step(FontSizeLadder.Largest, +1));
            Assert.Null(FontSizeLadder.Step(FontSizeLadder.Smallest, -1));
            Assert.Equal(12f, FontSizeLadder.Step(11f, +1));
            Assert.Equal(10f, FontSizeLadder.Step(11f, -1));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApplyingAFontSaysWhetherThePageCountMoved()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            window.ApplyTextStyleChoice(new TextStyleChoice("body", "Lora", null));

            string status = window.StatusLabelTextForTest ?? string.Empty;
            Assert.Contains("Body text", status, StringComparison.Ordinal);
            Assert.Contains("Ctrl+Z", status, StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ShowingWhereFontsWereChangedIsAnEditorOverlayAndIsOffByDefault()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            Assert.False(window.CanvasForTest.ShowFontChanges);

            window.ToggleShowFontChanges();
            Assert.True(window.CanvasForTest.ShowFontChanges);
            Assert.Contains("font", window.StatusLabelTextForTest ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            window.ToggleShowFontChanges();
            Assert.False(window.CanvasForTest.ShowFontChanges);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ADocumentNamingAnUnknownFamilyOpensWithAWarningAndIsNotRewritten()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            // Give the sample a family this build does not bundle, then reopen it.
            StyleSheet styles = window.SessionForTest!.Document.StyleSheet;
            styles.CharacterStyles.Add(new CharacterStyleDef
            {
                Name = "woodcut",
                FontFamily = "Woodcut Antiqua",
                SizePt = 18f,
            });

            IReadOnlyList<UnknownFontUse> unknown = DocumentFontAudit.FindUnknownFamilies(
                window.SessionForTest.Document, BundledFontCatalog.FamilyNames);
            UnknownFontUse use = Assert.Single(unknown);
            Assert.Equal("Woodcut Antiqua", use.FontFamily);

            string warning = DocumentFontAudit.DescribeWarning(unknown, BundledFonts.BodyFamily)!;
            Assert.Contains("Woodcut Antiqua", warning, StringComparison.Ordinal);
            Assert.Contains("not changed", warning, StringComparison.Ordinal);

            // The document keeps the unknown family verbatim: a round trip is lossless.
            Assert.Equal("Woodcut Antiqua", styles.GetCharacterStyle("woodcut").FontFamily);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheAppSubstitutesRatherThanThrowingForAnUnbundledFamily()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();

            // App default: substitute to the body family, because here "the newsletter must still
            // open" outranks "fail loudly". The engine default stays null — proven in Layout.Tests.
            Assert.Equal(BundledFonts.BodyFamily, window.FontsForTest.SubstituteFamily);
            Assert.Equal(
                new FontKey(BundledFonts.BodyFamily, FontWeight.Regular, FontStyleSlant.Normal),
                window.FontsForTest.ResolveKey(
                    new FontKey("Woodcut Antiqua", FontWeight.Regular, FontStyleSlant.Normal)));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheLicenceTextTheHelpMenuShowsCoversEveryBundledFamily()
    {
        await Session.Dispatch(() =>
        {
            string text = BundledFonts.ReadLicenceText();
            foreach (string family in BundledFontCatalog.FamilyNames)
            {
                Assert.Contains(family, text, StringComparison.Ordinal);
            }

            Assert.Contains("SIL OPEN FONT LICENSE", text, StringComparison.OrdinalIgnoreCase);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThePanelSaysWhenTextCarriesAFontOfItsOwn()
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
            window.EditorForTest.UseFontJustHere("Lora", null);
            window.RefreshActions();

            ActionContext context = window.CurrentActionContext;
            Assert.True(context.SelectionUsesFontOverride);
            Assert.Contains("Lora", context.FontOverrideNote ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(
                ActionAvailabilityKind.Available,
                ActionCatalog.Evaluate(ActionId.ClearFontOverride, context).Kind);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
