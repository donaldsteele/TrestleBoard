using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The keyboard audit (PLAN.md §6, docs/M9-spec.md §6, §11 M11). Every gesture the app advertises
/// must actually reach its own action — a menu that promises Ctrl+Shift+Y and silently redoes
/// instead is worse than one that promises nothing, because the user has no way to tell.
///
/// Until M11 the sharp end of this was a test that read <c>MainWindow.axaml.cs</c> looking for
/// <c>case Key.X when …</c> in the wrong order. That caught exactly one shadowing pattern and only
/// while the dispatcher stayed a switch. Pressing every registered key through the real window and
/// checking where it lands catches every mis-dispatch, including ones no regex would describe.
/// </summary>
public sealed class KeyboardAuditTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    [Fact]
    public async Task EveryAdvertisedGestureIsUniqueAmongTheMenus()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenSample();

            var byGesture = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (MenuItem item in window.GetLogicalDescendants().OfType<MenuItem>())
            {
                if (item is not { InputGesture: { } gesture, Header: string header })
                {
                    continue;
                }

                string key = gesture.ToString();
                if (!byGesture.TryGetValue(key, out List<string>? headers))
                {
                    headers = [];
                    byGesture[key] = headers;
                }

                headers.Add(header);
            }

            Assert.True(byGesture.Count > 10, $"only {byGesture.Count} gestures found — the walk found nothing");

            List<string> duplicated = byGesture
                .Where(kv => kv.Value.Count > 1)
                .Select(kv => $"{kv.Key} → {string.Join(" / ", kv.Value)}")
                .ToList();

            Assert.True(duplicated.Count == 0, "two commands claim the same gesture: " + string.Join("; ", duplicated));

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The replacement for the old source-scraping test, and strictly stronger than it: press every
    /// registered gesture through the real window and assert which action it reached. A shifted
    /// gesture swallowed by its unshifted twin fails here as a wrong action id, whatever the shape
    /// of the dispatcher happens to be.
    /// </summary>
    [Fact]
    public async Task EveryRegisteredKeyPressReachesItsOwnAction()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            // Record and stop short: pressing Ctrl+O for real would open a file dialog on the runner.
            var invoked = new List<string>();
            window.ActionsForTest.InterceptorForTest = id =>
            {
                invoked.Add(id);
                return true;
            };

            var wrong = new List<string>();
            foreach (KeyShortcut shortcut in KeyboardMap.All)
            {
                // Only the shortcuts that apply outside a text session can be exercised here; the
                // ones scoped to typing are covered by the editor's own tests.
                if (shortcut.Scope == KeyScope.WhileTyping)
                {
                    continue;
                }

                invoked.Clear();

                // The physical key is irrelevant here — the dispatcher matches on the logical Key,
                // which is what a menu shortcut means on any keyboard layout.
                window.KeyPress(
                    shortcut.Key, ToRawModifiers(shortcut.Modifiers), PhysicalKey.None, keySymbol: null);

                if (invoked.Count != 1 || invoked[0] != shortcut.ActionId)
                {
                    wrong.Add(
                        $"{KeyboardMap.Describe(shortcut)} should reach {shortcut.ActionId} "
                        + $"but reached [{string.Join(", ", invoked)}]");
                }
            }

            Assert.True(KeyboardMap.All.Count > 20, "the keyboard table is suspiciously small");
            Assert.True(wrong.Count == 0, string.Join("; ", wrong));

            window.ActionsForTest.InterceptorForTest = null;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The panel prints a shortcut beside each action. A promise the keyboard cannot keep is worse
    /// than no promise, so the two lists are compared rather than maintained in parallel by hand.
    /// </summary>
    [Fact]
    public void EveryShortcutTheCatalogAdvertisesIsRegistered()
    {
        var missing = new List<string>();
        foreach (EditorAction action in ActionCatalog.All)
        {
            if (action.DisplayGesture is not { } advertised)
            {
                continue;
            }

            bool found = KeyboardMap.All.Any(b =>
                b.ActionId == action.Id
                && string.Equals(KeyboardMap.Describe(b), advertised, StringComparison.Ordinal));
            if (!found)
            {
                missing.Add($"{action.Id} advertises {advertised}");
            }
        }

        Assert.True(missing.Count == 0, "advertised but not registered: " + string.Join("; ", missing));
    }

    /// <summary>Every key press the table knows about must land on an action that exists.</summary>
    [Fact]
    public void EveryRegisteredGestureNamesARealAction()
    {
        foreach (KeyShortcut shortcut in KeyboardMap.All)
        {
            Assert.True(
                ActionCatalog.TryGet(shortcut.ActionId, out _),
                $"{KeyboardMap.Describe(shortcut)} names an action that does not exist: {shortcut.ActionId}");
        }
    }

    /// <summary>The regression itself, driven through the real window.</summary>
    [Fact]
    public async Task ControlShiftYFitsToContentsRatherThanRedoing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            // Something to redo, so a mistaken Redo would be observable.
            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.SessionForTest!.Undo();
            Assert.True(window.SessionForTest.CanRedo);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control | RawInputModifiers.Shift);

            // Redo must NOT have fired: the frame is still gone and still redoable.
            Assert.True(window.SessionForTest.CanRedo);
            Assert.DoesNotContain(window.SessionForTest.Document.Pages[0].Blocks, b => b.Id == blockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ControlYStillRedoes()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenSample();

            string blockId = window.FramesForTest!.AddTextFrame(0);
            window.SessionForTest!.Undo();

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);

            Assert.False(window.SessionForTest.CanRedo);
            Assert.Contains(window.SessionForTest.Document.Pages[0].Blocks, b => b.Id == blockId);

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static RawInputModifiers ToRawModifiers(KeyModifiers modifiers)
    {
        RawInputModifiers raw = RawInputModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            raw |= RawInputModifiers.Control;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            raw |= RawInputModifiers.Shift;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            raw |= RawInputModifiers.Alt;
        }

        return raw;
    }
}
