using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Headless;
using TrestleBoard.App.Dialogs;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Widgets;
using TrestleBoard.Roster;
using TrestleBoard.Widgets;
using TrestleBoard.Widgets.Wizards;
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
    ///
    /// Until M11 it walked <c>MainWindow</c> only, so the wizard, the grid editor, the photo
    /// sliders, the settings dialog and the start screen had never been checked at all — which is
    /// the fair trade for amending the keyboard audit in the same milestone.
    /// </summary>
    [Fact]
    public async Task EveryControlTheUserCanReachHasSomethingToSay()
    {
        await Session.Dispatch(() =>
        {
            var unnamed = new List<string>();
            int checked_ = 0;

            foreach ((string windowName, Window window) in EveryWindow())
            {
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
                        unnamed.Add($"{windowName}: {control.GetType().Name} '{control.Name ?? "(unnamed)"}'");
                    }
                }

                window.Close();
            }

            // Guard against the walk finding nothing and the assertion passing vacuously.
            Assert.True(checked_ > 40, $"only {checked_} controls were examined — the tree walk found nothing");
            Assert.True(unnamed.Count == 0, "controls with no accessible name: " + string.Join(", ", unnamed));
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Two items in the same menu sharing an access key means Alt-F-A picks whichever comes first
    /// and the other is unreachable that way. Untested before M11, and likelier now that the Object
    /// menu has become three submenus.
    /// </summary>
    [Fact]
    public async Task NoTwoItemsInOneMenuShareAnAccessKey()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            var clashes = new List<string>();
            int menusChecked = 0;

            foreach (MenuItem parent in window.GetLogicalDescendants().OfType<MenuItem>())
            {
                List<MenuItem> children = parent.Items.OfType<MenuItem>().ToList();
                if (children.Count == 0)
                {
                    continue;
                }

                menusChecked++;
                var byKey = new Dictionary<char, List<string>>();
                foreach (MenuItem child in children)
                {
                    if (child.Header is not string header || AccessKeyOf(header) is not { } key)
                    {
                        continue;
                    }

                    if (!byKey.TryGetValue(key, out List<string>? headers))
                    {
                        headers = [];
                        byKey[key] = headers;
                    }

                    headers.Add(header);
                }

                clashes.AddRange(byKey
                    .Where(kv => kv.Value.Count > 1)
                    .Select(kv => $"{parent.Header}: '{kv.Key}' → {string.Join(" / ", kv.Value)}"));
            }

            // The top-level menu bar itself, which is not a MenuItem's child collection.
            var topLevel = new Dictionary<char, List<string>>();
            foreach (MenuItem item in window.GetLogicalDescendants().OfType<Menu>()
                         .SelectMany(m => m.Items.OfType<MenuItem>()))
            {
                if (item.Header is not string header || AccessKeyOf(header) is not { } key)
                {
                    continue;
                }

                if (!topLevel.TryGetValue(key, out List<string>? headers))
                {
                    headers = [];
                    topLevel[key] = headers;
                }

                headers.Add(header);
            }

            clashes.AddRange(topLevel
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => $"menu bar: '{kv.Key}' → {string.Join(" / ", kv.Value)}"));

            Assert.True(menusChecked >= 6, $"only {menusChecked} menus were examined");
            Assert.True(clashes.Count == 0, "two items share an access key: " + string.Join("; ", clashes));

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
    /// Asking for the context menu over a block used to return false outright, so a screen-reader
    /// user pressing the Applications key got nothing at all (PLAN.md §11 M11).
    /// </summary>
    [Fact]
    public async Task TheApplicationsKeyOverABlockAsksForItsActions()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            AutomationPeer block = window.CanvasForTest.CreateAutomationPeerForTest().GetChildren()[0];

            Assert.True(block.ShowContextMenu());
            Assert.NotNull(window.FramesForTest!.SelectedBlockId);

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

    /// <summary>The character after the first underscore, which is what Alt activates.</summary>
    private static char? AccessKeyOf(string header)
    {
        int index = header.IndexOf('_', StringComparison.Ordinal);
        return index >= 0 && index + 1 < header.Length
            ? char.ToUpperInvariant(header[index + 1])
            : null;
    }

    /// <summary>
    /// Every window the app can put in front of the user. Built with fictional placeholder data
    /// only (PLAN.md §0 rule 2).
    /// </summary>
    private static IEnumerable<(string Name, Window Window)> EveryWindow()
    {
        var main = new MainWindow();
        main.OpenSample();
        yield return (nameof(MainWindow), main);

        WizardSession wizard = NewOfficersWizard();
        yield return (nameof(WizardWindow), new WizardWindow(wizard));
        yield return (nameof(WidgetGridWindow), new WidgetGridWindow(NewOfficersWizard()));
        yield return (nameof(SettingsDialog), new SettingsDialog(new AppSettings()));
        yield return (nameof(StartDialog), new StartDialog(canStartFromLastMonth: true));

        var photoHost = new MainWindow();
        photoHost.OpenIssueSample();
        string? photoBlock = photoHost.SessionForTest!.Document.Pages
            .SelectMany(p => p.Blocks)
            .OfType<ImageFrame>()
            .Select(b => b.Id)
            .FirstOrDefault();
        Assert.NotNull(photoBlock);
        yield return (nameof(PhotoAdjustWindow), new PhotoAdjustWindow(photoHost.PhotosForTest!, photoBlock));
        photoHost.Close();

        // The address book's three windows (M12). The roster they are built over is fictional and
        // in-memory; the suite's app-state root is a temporary folder, so none of this can reach a
        // real address book (PLAN.md §0 rule 5).
        yield return (nameof(PeopleWindow), new PeopleWindow(FictionalRoster()));
        yield return (nameof(RosterImportWindow), new RosterImportWindow(RosterBook.Empty));
        yield return (nameof(RosterRestoreDialog), new RosterRestoreDialog(
        [
            new RosterBackup("roster-20260727-090000-000.roster.bak.json", DateTimeOffset.UnixEpoch, 3),
        ]));
    }

    private static RosterService FictionalRoster()
    {
        string path = Path.Combine(AppPaths.Root, "accessibility", "roster.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var service = new RosterService(new RosterStore(path));
        service.Save(
            new Member { Id = "person-1", DisplayName = "A. Placeholder", Office = "Worshipful Master" },
            "Add A. Placeholder");
        return service;
    }

    private static WizardSession NewOfficersWizard()
    {
        var provider = WidgetLayoutProvider.CreateDefault();
        Assert.True(provider.Registry.TryGet("officersTable", out IWidgetDefinition? definition));
        return WizardSession.Create(
            definition!,
            existingData: null,
            dataVersion: 1,
            new WidgetSeed("Placeholder Lodge", 3, 2026, "1st Tuesday"));
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
