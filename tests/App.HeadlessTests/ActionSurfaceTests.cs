using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M11's acceptance criteria and PLAN.md §12 gate 8, as assertions: nothing in the panel is ever
/// greyed, no unavailable action is silent about why, and every menu item that greys itself carries
/// the same sentence where a screen reader will find it.
/// </summary>
public sealed class ActionSurfaceTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// The machine-checked version of the owner's complaint. Every block kind on the page gets
    /// selected in turn, and after each one the panel is inspected: nothing greyed, nothing silent.
    /// </summary>
    [Fact]
    public async Task NoPanelControlIsEverGreyedForAnyKindOfSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            var greyed = new List<string>();
            int selectionsChecked = 0;

            foreach (string? blockId in EverySelection(window))
            {
                window.FramesForTest!.Select(blockId);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                selectionsChecked++;

                foreach (Button button in window.PanelForTest.ButtonsForTest)
                {
                    if (!button.IsEnabled)
                    {
                        greyed.Add($"{window.PanelForTest.HeadingForTest}: {ActionPanel.LabelOf(button)}");
                    }
                }
            }

            Assert.True(selectionsChecked > 5, $"only {selectionsChecked} selections were tried");
            Assert.True(greyed.Count == 0, "these panel controls were greyed: " + string.Join(", ", greyed));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Everything the panel offers must be performable — or, if blocked, must say why. "Absent"
    /// is the third answer, and the panel uses it rather than greying.
    /// </summary>
    [Fact]
    public async Task EveryActionThePanelOffersIsEitherPerformableOrExplained()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            var silent = new List<string>();
            foreach (string? blockId in EverySelection(window))
            {
                window.FramesForTest!.Select(blockId);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                foreach (ActionOffer offer in ActionCatalog.ForSelection(window.CurrentActionContext))
                {
                    Assert.NotEqual(ActionAvailabilityKind.NotApplicable, offer.Availability.Kind);
                    if (!offer.IsAvailable && string.IsNullOrWhiteSpace(offer.Availability.Reason))
                    {
                        silent.Add(offer.Action.Id);
                    }

                    // An offered action with no handler would be a button that does nothing at all.
                    Assert.True(
                        window.ActionsForTest.Handles(offer.Action.Id),
                        $"the panel offers {offer.Action.Id} but nothing performs it");
                }
            }

            Assert.True(silent.Count == 0, "offered with no reason: " + string.Join(", ", silent));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The panel hides what does not apply; the menu bar keeps conventional greying, because
    /// "dimmed" is a convention screen readers announce. The trade is that every greyed item must
    /// carry the reason where the screen reader will read it (PLAN.md §6).
    /// </summary>
    [Fact]
    public async Task EveryGreyedMenuItemCarriesItsReasonInHelpText()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            var silent = new List<string>();
            int greyedSeen = 0;

            foreach (MenuItem item in window.GetLogicalDescendants().OfType<MenuItem>())
            {
                if (item is not { Tag: string actionId } || !ActionCatalog.TryGet(actionId, out _))
                {
                    continue;
                }

                if (item.IsEnabled)
                {
                    continue;
                }

                greyedSeen++;
                if (string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetHelpText(item)))
                {
                    silent.Add(actionId);
                }
            }

            Assert.True(greyedSeen > 3, $"only {greyedSeen} greyed items found — nothing was checked");
            Assert.True(silent.Count == 0, "greyed with no explanation: " + string.Join(", ", silent));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Pressing an action that cannot run must say why out loud rather than doing nothing — the
    /// exact failure M11 exists to remove.
    /// </summary>
    [Fact]
    public async Task AnActionThatCannotRunSaysWhyInTheStatusBar()
    {
        await HeadlessSession.DispatchAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            // Nothing is selected, so there is no picture to fix.
            window.FramesForTest!.ClearSelection();
            await window.ActionsForTest.RunAsync(ActionId.FixPhoto);

            Assert.Equal(
                ActionCatalog.Evaluate(ActionId.FixPhoto, window.CurrentActionContext).Reason,
                window.StatusLabelTextForTest);
            Assert.False(string.IsNullOrWhiteSpace(window.StatusLabelTextForTest));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>The panel heading is a polite live region: it has to say what became possible.</summary>
    [Fact]
    public async Task ThePanelHeadingFollowsTheSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();

            Assert.Equal("No newsletter is open", window.PanelForTest.HeadingForTest);

            window.OpenIssueSample();
            Assert.Equal("Nothing is selected", window.PanelForTest.HeadingForTest);

            string photo = window.SessionForTest!.Document.Pages
                .SelectMany(p => p.Blocks)
                .OfType<Core.Model.ImageFrame>()
                .First().Id;
            window.FramesForTest!.Select(photo);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal("A photo is selected", window.PanelForTest.HeadingForTest);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// With nothing selected the panel is the checklist. Right after a carry-forward the articles
    /// still hold their prompts and no PDF has been made, so it has something to say.
    /// </summary>
    [Fact]
    public async Task WithNothingSelectedThePanelSaysWhatIsLeftToDo()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Assert.True(window.StartFromLastMonth());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IReadOnlyList<NextStep> steps = WhatsNext.Suggestions(window.CurrentActionContext);

            Assert.Contains(steps, s => s.ActionId == ActionId.ExportPdf);
            Assert.Contains(steps, s => s.Why.Contains("remind you", StringComparison.Ordinal));
            Assert.Contains(window.PanelForTest.ButtonsForTest, b => ActionPanel.LabelOf(b) == "Make the PDF");

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every action offered anywhere in the panel can be reached with the keyboard alone — the
    /// panel is a focus region of its own, reached with F6 (PLAN.md §12 gate 8).
    /// </summary>
    [Fact]
    public async Task EveryPanelActionIsReachableFromTheKeyboard()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            IReadOnlyList<Button> buttons = window.PanelForTest.ButtonsForTest;
            Assert.NotEmpty(buttons);
            Assert.All(buttons, b =>
            {
                Assert.True(b.Focusable, $"{ActionPanel.LabelOf(b)} cannot take focus");
                Assert.True(b.IsTabStop, $"{ActionPanel.LabelOf(b)} is not in the tab order");
            });

            window.CanvasForTest.Focus();
            window.CycleRegion(forward: true);
            Assert.Contains("Moved to", window.StatusLabelTextForTest ?? "", StringComparison.Ordinal);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Nothing selected, a photo, a text frame, and one of each widget kind.</summary>
    private static IEnumerable<string?> EverySelection(MainWindow window)
    {
        yield return null;

        foreach (Core.Model.Block block in window.SessionForTest!.Document.Pages[0].Blocks.ToList())
        {
            yield return block.Id;
        }

        foreach (string typeId in new[]
                 {
                     "officersTable", "birthdayList", "committeeList",
                     "districtCalendar", "eventCard", "coverBanner",
                 })
        {
            yield return window.WidgetsForTest!.InsertWidget(0, typeId);
        }
    }
    /// <summary>
    /// M37, review §14.4: every button the APP made says whether it can be pressed.
    ///
    /// <para>Measured from the shipped screenshots, an enabled toolbar button and a disabled one
    /// had the identical fill — #BEBFC2 for both — and differed only in label contrast, 11.42:1
    /// against 2.61:1. Meanwhile the action panel's items, which by M11 are NEVER disabled, looked
    /// exactly like the disabled ones. "Grey slab" meant "press me" and "you cannot" at once.</para>
    ///
    /// <para>The affordance is opted into with <c>Tokens.Action()</c> rather than applied by a bare
    /// Button selector, because a ScrollBar's repeat buttons and a ComboBox's toggle are Buttons too
    /// and would take it with them. This test is what makes opting in un-forgettable: a new button
    /// that carries neither treatment fails here rather than quietly shipping looking dead.</para>
    ///
    /// <para><c>TemplatedParent is null</c> is the discriminator. A button the app constructed has
    /// none; one that Fluent built inside a ScrollBar or ComboBox template has its host.</para>
    /// </summary>
    [Fact]
    public async Task EveryButtonTheAppMadeSaysWhetherItCanBePressed()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var bare = new List<string>();
            int checkedCount = 0;

            foreach (Button button in window.GetLogicalDescendants().OfType<Button>())
            {
                // Framework internals are not ours to restyle.
                if (button.TemplatedParent is not null)
                {
                    continue;
                }

                checkedCount++;
                bool marked = button.Classes.Contains("action")
                    || button.IsDefault
                    || ReferenceEquals(button.Theme, window.TryFindResource(Tokens.PrimaryButtonTheme, out object? t) ? t : null);

                if (!marked)
                {
                    bare.Add(ActionPanel.LabelOf(button) is { Length: > 0 } label
                        ? label
                        : button.Name ?? "(unnamed)");
                }
            }

            Assert.True(checkedCount >= 8, $"only {checkedCount} app-made buttons were found");
            Assert.True(
                bare.Count == 0,
                "these buttons carry neither the action nor the primary treatment, so they look "
                    + "the same whether or not they can be pressed: " + string.Join(", ", bare));

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

}
