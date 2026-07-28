using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The menu index (PLAN.md §11 M17). PLAN.md §6 has promised since M0 that every command has a
/// menu item; until M17 nothing checked it, and the app shipped M11 through M16 with
/// <c>view.previousRegion</c> reachable only by pressing Shift+F6 on a hunch.
///
/// These two tests are the reason the M17 restructure could move twelve items between five menus
/// without anything falling off the edge — and the reason a future milestone cannot quietly drop a
/// command's menu home while adding a panel button for it.
/// </summary>
public sealed class MenuIndexTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// Actions that are deliberately absent from the menu bar.
    ///
    /// <para><b>This list must stay empty.</b> It exists so that a future exception is a visible,
    /// argued-for line in a test rather than a silent hole in the keyboard story — not so that
    /// anything can be put in it. If you are adding a name here, PLAN.md §6 says you are wrong.</para>
    /// </summary>
    private static readonly string[] MayLiveOutsideTheMenus = [];

    [Fact]
    public async Task EveryActionInTheCatalogHasAMenuItem()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            HashSet<string> tagged = window.GetLogicalDescendants().OfType<MenuItem>()
                .Select(item => item.Tag as string)
                .Where(tag => tag is not null)
                .Select(tag => tag!)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(tagged.Count > 30, $"only {tagged.Count} tagged menu items — the walk found nothing");

            List<string> homeless = ActionCatalog.All
                .Select(a => a.Id)
                .Where(id => !tagged.Contains(id))
                .Where(id => !MayLiveOutsideTheMenus.Contains(id, StringComparer.Ordinal))
                .ToList();

            Assert.True(
                homeless.Count == 0,
                "commands with no menu item: " + string.Join(", ", homeless));

            Assert.True(
                MayLiveOutsideTheMenus.Length == 0,
                "the exception list is meant to be empty: " + string.Join(", ", MayLiveOutsideTheMenus));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The gesture a menu item prints must be the gesture the keyboard table actually dispatches.
    /// A menu that advertises Ctrl+Shift+P for a command the table binds to something else is a
    /// promise the app cannot keep, and the user has no way to tell which of the two is lying.
    ///
    /// <para>The comparison is on the parsed <see cref="KeyGesture"/>, not on the string. XAML
    /// spells the bracket keys <c>OemCloseBrackets</c> and <c>KeyboardMap.Describe</c> spells the
    /// same key <c>]</c>, because one is written for a parser and the other for a person; comparing
    /// the two texts would only prove that they are written for different readers.</para>
    /// </summary>
    [Fact]
    public async Task EveryMenuGestureIsTheOneTheKeyboardTableDispatches()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            var wrong = new List<string>();
            int checkedItems = 0;

            foreach (MenuItem item in window.GetLogicalDescendants().OfType<MenuItem>())
            {
                if (item is not { Tag: string actionId, InputGesture: { } gesture })
                {
                    continue;
                }

                checkedItems++;
                bool matches = KeyboardMap.All.Any(b =>
                    string.Equals(b.ActionId, actionId, StringComparison.Ordinal)
                    && b.Key == gesture.Key
                    && b.Modifiers == gesture.KeyModifiers);

                if (!matches)
                {
                    string registered = string.Join(
                        " / ",
                        KeyboardMap.All.Where(b => b.ActionId == actionId).Select(KeyboardMap.Describe));
                    wrong.Add(
                        $"{item.Header} advertises {gesture} for {actionId}, "
                        + $"but the table binds [{(registered.Length == 0 ? "nothing" : registered)}]");
                }
            }

            Assert.True(checkedItems > 20, $"only {checkedItems} menu gestures examined");
            Assert.True(wrong.Count == 0, string.Join("; ", wrong));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Nine top-level menus, before M17's restructure and after it. The chrome budget is the
    /// reason: at 200% UI scale a tenth menu wraps the bar onto a second row, which costs the page
    /// a strip of height on every window (PLAN.md §11 M11).
    /// </summary>
    [Fact]
    public async Task TheMenuBarStaysNineWide()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();

            List<string> top = window.GetLogicalDescendants().OfType<Menu>()
                .SelectMany(m => m.Items.OfType<MenuItem>())
                .Select(m => m.Header as string ?? "?")
                .ToList();

            Assert.Equal(9, top.Count);
            Assert.Equal(
                ["_File", "_Edit", "F_ormat", "_Insert", "A_rrange", "_View", "_Page", "Peop_le", "_Help"],
                top);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
