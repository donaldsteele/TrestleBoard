using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M76(d)(e), docs/M76-spec.md §2 and §9: the toolbar has to fit the window the application opens
/// for itself.
///
/// <para><b>What went wrong, and why it needed a test rather than a screenshot.</b>
/// <c>MainWindow.axaml</c> opens at 1280×860 and the toolbar was wider than that, so "Zoom in" was
/// clipped by the window edge and "Fit page" was off screen behind M69's deliberately-permanent
/// horizontal scroll track. M69 was working exactly as designed — it made that scrollbar permanent
/// so a control pushed out of reach would announce itself instead of being silently cut off — on a
/// case it was not looking at, because its own report was about 200% scale, where overflow is
/// expected and correct. At 100%, on the default size, a scrollbar on a toolbar is not a rescue: it
/// is the first thing a new user sees, and it says the program does not fit in its own window.</para>
///
/// <para><b>The second half of this test is not padding.</b> Deleting the four zoom controls
/// outright would pass the width assertion perfectly, so the same test that demands the bar fit also
/// demands that what left it is still in the window — in the canvas footer, pressable, named, and in
/// the tab order. A milestone that made the toolbar fit by making the application smaller would fail
/// here, which is the only thing that makes the first assertion mean what it says.</para>
/// </summary>
public sealed class ToolbarFitTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>The size the window chooses for itself in <c>MainWindow.axaml</c>.</summary>
    private const double DefaultWidth = 1280;

    private const double DefaultHeight = 860;

    /// <summary>
    /// The four controls M76(d) moved off the toolbar. A static field rather than an inline array
    /// literal because CA1861 is an error in this repository.
    /// </summary>
    private static readonly string[] MovedToTheFooter =
        ["ZoomOutButton", "ZoomLadderButton", "ZoomInButton", "FitButton"];

    [Fact]
    public async Task AtTheDefaultWindowSizeTheToolbarFitsAndTheZoomControlsAreInTheCanvasFooter()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenAtTheDefaultSize();

            // ---- Half one: the bar fits, so no horizontal scrollbar appears ----------------------
            //
            // Measured against the strip's own viewport rather than against the whole window,
            // because the strip is what scrolls: "Make the PDF" sits in a second column OUTSIDE it
            // (M76(e)), and the room that button takes is room the scrolling group genuinely does
            // not have.
            ScrollViewer strip = window.ToolbarStripForTest;
            double wanted = window.ToolbarStackForTest.DesiredSize.Width;
            double available = strip.Bounds.Width;

            Assert.True(available > 0, "the toolbar strip was never laid out — nothing was measured");
            Assert.True(
                wanted <= available,
                $"the toolbar wants {wanted:F0}px and has {available:F0}px at the window's own "
                    + $"default {DefaultWidth:F0}×{DefaultHeight:F0}, so it opens with a horizontal "
                    + "scrollbar and at least one control off screen (docs/M76-spec.md §2)");

            // ---- Half two: and the four that left are in the canvas footer -----------------------
            //
            // By name, because a name is what the markup and the code-behind agree on, and because
            // "it is somewhere in the window" is not the claim being made — the claim is that these
            // four are in the footer strip under the page, which is where this audience has looked
            // for them in every document application they have ever used.
            List<Button> footer =
                [.. window.CanvasFooterStackForTest.GetLogicalDescendants().OfType<Button>()];

            foreach (string name in MovedToTheFooter)
            {
                Assert.True(
                    footer.Exists(b => b.Name == name),
                    $"{name} is not in the canvas footer — the toolbar may fit because this control "
                        + "was deleted rather than moved (docs/M76-spec.md §9)");
            }

            // The zoom percentage travelled with them, and it is still the polite live region that
            // announces the new magnification (docs/M76-spec.md §7).
            TextBlock percentage = Assert.Single(
                window.CanvasFooterStackForTest.GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Name == "ZoomLabel");
            Assert.Equal(
                Avalonia.Automation.AutomationLiveSetting.Polite,
                Avalonia.Automation.AutomationProperties.GetLiveSetting(percentage));

            // …and it is a BUTTON now rather than dead text: it opens the ladder of magnifications
            // (docs/M76-spec.md §7). Reachable by keyboard, like everything else in PLAN.md §6.
            Button ladder = window.ZoomLadderButtonForTest;
            Assert.True(
                ladder.Focusable && ladder.IsTabStop,
                "the zoom percentage cannot be reached with the keyboard");

            // None of the four left the markup only to be re-parented back onto the bar.
            List<string?> stillOnTheBar =
                [.. strip.GetLogicalDescendants().OfType<Button>().Select(b => b.Name)];
            Assert.DoesNotContain("ZoomOutButton", stillOnTheBar);
            Assert.DoesNotContain("ZoomInButton", stillOnTheBar);
            Assert.DoesNotContain("FitButton", stillOnTheBar);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md §6's floors apply to the footer exactly as they apply to the toolbar: 16pt text,
    /// 44×44 hit targets, a name for every control, and the M37 affordance that says a button can be
    /// pressed. A control that got worse by changing which strip of chrome it stands on would be a
    /// regression bought with a layout fix, and that is not a trade this application makes.
    /// </summary>
    [Fact]
    public async Task EveryControlInTheCanvasFooterMeetsTheAccessibilityFloors()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenAtTheDefaultSize();

            var failures = new List<string>();
            int checkedCount = 0;

            foreach (Button button in
                     window.CanvasFooterStackForTest.GetLogicalDescendants().OfType<Button>())
            {
                // Framework internals are not ours to measure — the discriminator ActionSurfaceTests
                // uses: a button the app made has no templated parent.
                if (button.TemplatedParent is not null)
                {
                    continue;
                }

                checkedCount++;
                string who = button.Name ?? "(unnamed)";

                // The ARRANGED size, not the declared minimum: "Fit page" is wide because of its
                // words rather than because of a MinWidth, and what the user's finger has to hit is
                // the box that ended up on the screen.
                if (button.Bounds.Width < 44 || button.Bounds.Height < 44)
                {
                    failures.Add(
                        $"{who}: hit target {button.Bounds.Width:F0}×{button.Bounds.Height:F0}, floor is 44×44");
                }

                if (button.FontSize < 16)
                {
                    failures.Add($"{who}: {button.FontSize}pt, floor is 16pt");
                }

                if (string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(button)))
                {
                    failures.Add($"{who}: no AutomationProperties.Name");
                }

                // M37: the affordance that says "you can press this". DressToolbar applies it to the
                // footer as well as to the toolbar, and this is what proves the second half of it.
                if (!button.Classes.Contains("action"))
                {
                    failures.Add($"{who}: carries neither the action nor the primary treatment");
                }
            }

            Assert.True(checkedCount >= 4, $"only {checkedCount} footer buttons were found");
            Assert.True(
                failures.Count == 0,
                "the canvas footer breaks PLAN.md §6: " + string.Join("; ", failures));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// M76(e): the terminal goal of the whole product is on the toolbar, permanently, in the primary
    /// treatment, and saying the words the catalog uses for it.
    ///
    /// <para>"Make the PDF" is what the committee is here to do — PLAN.md §7 calls the PDF the file
    /// they email to the lodge — and until this milestone it appeared only inside the what's-next
    /// card, which scrolls, and only when that card decided to offer it. It is the only primary in
    /// this region, which is the whole of what makes the treatment mean anything.</para>
    /// </summary>
    [Fact]
    public async Task MakeThePdfIsOnTheToolbarInThePrimaryTreatmentAndIsTheOnlyOne()
    {
        await Session.Dispatch(() =>
        {
            MainWindow window = OpenAtTheDefaultSize();

            Button export = Assert.Single(
                window.ToolbarButtons,
                b => (b.Tag as string) == ActionId.ExportPdf);

            object? primary = window.TryFindResource(Tokens.PrimaryButtonTheme, out object? theme)
                ? theme
                : null;
            Assert.NotNull(primary);
            Assert.Same(primary, export.Theme);

            // The catalog is the one place a command is named (M27). A toolbar that says something
            // else is a second name for the same thing, and this audience learns names once.
            Assert.True(ActionCatalog.TryGet(ActionId.ExportPdf, out EditorAction? action));
            string label = ActionPanel.LabelOf(export);
            Assert.Contains(
                action!.Title.Replace("…", string.Empty, StringComparison.Ordinal).Trim(),
                label,
                StringComparison.Ordinal);

            // The only primary in the region. Two would be two answers to "what did you come here to
            // do?", which is the same as no answer at all (docs/M76-spec.md §3).
            List<string> primaries =
            [
                .. window.ToolbarButtons
                    .Concat(window.CanvasFooterButtons)
                    .Where(b => ReferenceEquals(b.Theme, primary))
                    .Select(b => b.Name ?? "(unnamed)"),
            ];
            Assert.Equal(["ExportPdfButton"], primaries);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A window showing the five-page issue, measured and arranged at exactly the size
    /// <c>MainWindow.axaml</c> opens at — which is the whole point: the defect this class exists for
    /// only appears at the size the application chooses for itself.
    /// </summary>
    private static MainWindow OpenAtTheDefaultSize()
    {
        var window = new MainWindow();
        window.Show();
        window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
        window.OpenIssueSample();
        window.Measure(new Size(DefaultWidth, DefaultHeight));
        window.Arrange(new Rect(0, 0, DefaultWidth, DefaultHeight));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }
}
