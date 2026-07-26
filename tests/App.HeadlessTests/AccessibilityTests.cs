using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The automated half of the accessibility work (PLAN.md §6, docs/M9-spec.md §5/§6). The other half
/// is `docs/accessibility-test-script.md`, which a person runs with a real screen reader — no
/// automated check substitutes for hearing what NVDA actually says.
/// </summary>
public sealed class AccessibilityTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// A control with no accessible name is a control a screen reader announces as "button" and
    /// nothing else. This is the check that stops one being added.
    /// </summary>
    [Fact]
    public async Task EveryControlTheUserCanReachHasSomethingToSay()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            var unnamed = new List<string>();
            int checked_ = 0;
            foreach (Control control in Descendants(window))
            {
                if (control is not (Button or MenuItem or ComboBox or TextBox))
                {
                    continue;
                }

                checked_++;

                if (control is MenuItem { Header: null })
                {
                    continue;
                }

                string? name = Avalonia.Automation.AutomationProperties.GetName(control);
                if (string.IsNullOrWhiteSpace(name) && control is not MenuItem)
                {
                    unnamed.Add($"{control.GetType().Name} '{control.Name ?? "(unnamed)"}'");
                }
            }

            // Guard against the walk finding nothing and the assertion passing vacuously.
            Assert.True(checked_ > 20, $"only {checked_} controls were examined — the tree walk found nothing");
            Assert.True(unnamed.Count == 0, "controls with no accessible name: " + string.Join(", ", unnamed));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The canvas is one control with a whole document inside it. Without a peer tree a screen
    /// reader user cannot find out what is on the page at all.
    /// </summary>
    [Fact]
    public async Task TheCanvasAnnouncesEveryBlockOnThePage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();

            AutomationPeer peer = window.CanvasForTest.CreateAutomationPeerForTest();
            Assert.Contains("Newsletter page 1", peer.GetName(), StringComparison.Ordinal);

            IReadOnlyList<AutomationPeer> blocks = peer.GetChildren();
            List<string> names = blocks.Select(b => b.GetName()).ToList();

            // Page 1 of the issue: the cover heading widget, the essay frame, the photo.
            Assert.Equal(3, names.Count);
            Assert.Contains(names, n => n.StartsWith("Text frame:", StringComparison.Ordinal));
            Assert.Contains(names, n => n.StartsWith("Photo:", StringComparison.Ordinal));
            Assert.Contains("Cover heading", names);

            // Each name comes from what the page actually prints, so it cannot drift.
            Assert.Contains(names, n => n.Contains("Brethren", StringComparison.Ordinal));
            Assert.Contains(names, n => n.Contains("Brothers gathered outside", StringComparison.Ordinal));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A screen reader must see the page as it is now, not as it was when it first looked.
    ///
    /// Honest note on what this proves: it passes with the explicit InvalidateBlocks call removed,
    /// because Avalonia re-queries the children on this path anyway. It pins the OUTCOME the user
    /// depends on, not the mechanism. The invalidation call is kept because the caching behaviour is
    /// Avalonia's to change and the call costs nothing — but this test would not catch its loss.
    /// </summary>
    [Fact]
    public async Task TheBlockListKeepsUpWithEditsToThePage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();

            AutomationPeer peer = window.CanvasForTest.CreateAutomationPeerForTest();
            int before = peer.GetChildren().Count;

            window.WidgetsForTest!.InsertWidget(0, "birthdayList");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Equal(before + 1, peer.GetChildren().Count);
            Assert.Contains("Birthdays", peer.GetChildren().Select(c => c.GetName()));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SelectingABlockThroughTheScreenReaderMovesTheRealSelection()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();

            AutomationPeer canvas = window.CanvasForTest.CreateAutomationPeerForTest();
            AutomationPeer block = canvas.GetChildren()[0];

            block.SetFocus();

            // The same editor everyone else drives, not a parallel one.
            Assert.NotNull(window.FramesForTest!.SelectedBlockId);
            Assert.True(block.HasKeyboardFocus());

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md §6: every command has a menu item AND a shortcut, and drag-and-drop is never the only
    /// path. The menu is the thing that makes the app operable without a mouse at all.
    /// </summary>
    [Fact]
    public async Task EveryMenuItemThatDoesSomethingSaysHowToReachItFromTheKeyboard()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            var withoutAccessKey = new List<string>();
            int seen = 0;
            foreach (Control control in Descendants(window))
            {
                if (control is not MenuItem { Header: string header })
                {
                    continue;
                }

                seen++;

                // An underscore in the header is the Alt-key path; a top-level menu or a leaf both
                // need one, or the menu cannot be walked without a mouse.
                if (!header.Contains('_', StringComparison.Ordinal))
                {
                    withoutAccessKey.Add(header);
                }
            }

            Assert.True(seen > 20, $"only {seen} menu items were examined — the tree walk found nothing");
            Assert.True(
                withoutAccessKey.Count == 0,
                "menu items with no keyboard path: " + string.Join(", ", withoutAccessKey));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.GetVisualDescendants().OfType<Control>())
        {
            yield return child;
        }

        // Menus are built lazily, so their items are not in the visual tree until opened.
        foreach (Control child in root.GetLogicalDescendants().OfType<Control>())
        {
            yield return child;
        }
    }
}
