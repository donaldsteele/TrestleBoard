using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The keyboard audit (PLAN.md §6, docs/M9-spec.md §6). Every gesture a menu advertises must
/// actually reach its own handler — a menu that promises Ctrl+Shift+Y and silently redoes instead
/// is worse than one that promises nothing, because the user has no way to tell.
/// </summary>
public sealed partial class KeyboardAuditTests
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
    /// A `case Key.X when ctrl:` matches Ctrl+SHIFT+X too, so it shadows any later case for the
    /// shifted gesture. This is how Ctrl+Shift+Y ("Fit to contents") silently redid instead — the
    /// menu advertised it, and nothing in the app could reach it. Read the handler and refuse the
    /// pattern outright, because it fails silently and only a user would notice.
    /// </summary>
    [Fact]
    public void NoUnshiftedShortcutSwallowsItsShiftedTwin()
    {
        string source = File.ReadAllText(FindHandlerSource());
        MatchCollection cases = KeyCaseRegex().Matches(source);
        Assert.True(cases.Count > 20, $"only {cases.Count} key cases found — the handler was not read");

        var bareByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var shifted = new List<(string Key, int Index)>();

        foreach (Match match in cases)
        {
            string key = match.Groups["key"].Value;
            string guard = match.Groups["guard"].Value;
            bool mentionsShift = guard.Contains("Shift", StringComparison.Ordinal);

            if (mentionsShift)
            {
                shifted.Add((key, match.Index));
            }
            else if (!bareByKey.ContainsKey(key))
            {
                bareByKey[key] = match.Index;
            }
        }

        var swallowed = new List<string>();
        foreach ((string key, int index) in shifted)
        {
            // A bare case EARLIER in the switch wins the match, so the shifted one is unreachable.
            if (bareByKey.TryGetValue(key, out int bareIndex) && bareIndex < index)
            {
                swallowed.Add(key);
            }
        }

        Assert.True(
            swallowed.Count == 0,
            "these shifted shortcuts are unreachable because an un-shifted case matches first: "
            + string.Join(", ", swallowed)
            + ". Add `&& !e.KeyModifiers.HasFlag(KeyModifiers.Shift)` to the un-shifted case.");
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

    private static string FindHandlerSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "TrestleBoard.App", "MainWindow.axaml.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("MainWindow.axaml.cs not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"case Key\.(?<key>\w+) when (?<guard>[^:]+):")]
    private static partial Regex KeyCaseRegex();
}
