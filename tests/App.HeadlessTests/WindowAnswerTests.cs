using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// Verification gate 23, part two: a refusal reaches the window the user is looking at
/// (PLAN.md §11 M70 (b)).
///
/// <para><b>Why.</b> <c>MainWindow.Announce</c> sets the main window's status-bar text and nothing
/// else. Five windows are non-modal and sit over that status bar, so every answer given while one
/// of them has focus was invisible by construction — the shape the owner reported as "no error,
/// just nothing happens".</para>
///
/// <para>These tests are deliberately STRUCTURAL rather than scripted. Driving five different
/// windows through five different refusals would test five code paths; asserting that every
/// non-modal window carries a live-region answer line tests the <i>rule</i>, and it keeps holding
/// when somebody adds a sixth window.</para>
/// </summary>
public sealed class WindowAnswerTests
{
    /// <summary>
    /// Every non-modal window has somewhere of its own to answer, and it is a polite live region so
    /// a screen reader hears it.
    /// </summary>
    [Theory]
    [InlineData("SpellingWindow")]
    [InlineData("FindWindow")]
    [InlineData("HelpWindow")]
    [InlineData("ReviewWindow")]
    [InlineData("LastYearWindow")]
    [InlineData("ReadAloudWindow")]
    public void EveryNonModalWindowHasItsOwnAnswerLine(string windowName)
    {
        Type type = WindowType(windowName);

        FieldInfo[] textBlocks =
        [
            .. type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(TextBlock)),
        ];

        Assert.True(textBlocks.Length > 0, $"{windowName} has no TextBlock fields at all");

        // The answer line is the one wired as a live region. Its name varies by window, so the
        // property is what is asserted, not the field name.
        Assert.True(
            HasLiveRegionSource(type),
            $"{windowName} never marks anything as a polite live region, so nothing it says while "
            + "it has focus can reach a screen reader — and the main window's status bar is behind "
            + "it. See PLAN.md M70 (b).");
    }

    /// <summary>
    /// A window that answers must be able to reach the status bar as well: both places, always,
    /// because a screen-reader user may be following the one the sighted user is not.
    /// </summary>
    [Theory]
    [InlineData("HelpWindow")]
    [InlineData("ReviewWindow")]
    [InlineData("LastYearWindow")]
    [InlineData("ReadAloudWindow")]
    [InlineData("SpellingWindow")]
    public void EveryAnsweringWindowCanAlsoReachTheStatusBar(string windowName)
    {
        Type type = WindowType(windowName);

        bool relays =
            type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(f => f.FieldType == typeof(Action<string>))
            || type.GetConstructors()
                .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(Action<string>)));

        Assert.True(
            relays,
            $"{windowName} cannot reach MainWindow.Announce, so whatever it says exists only inside "
            + "itself. M70 (b) requires both places.");
    }

    /// <summary>
    /// The one that started it: <c>ISpeaker.Say</c> reports failure and the caller must not ignore
    /// it. The heading used to read "Reading" while nothing was heard.
    /// </summary>
    [Fact]
    public void ReadAloudDoesNotDiscardASpeakerFailure()
    {
        string source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "src", "TrestleBoard.App", "Dialogs", "ReadAloudWindow.cs"));

        int call = source.IndexOf("_speaker.Say(", StringComparison.Ordinal);
        Assert.True(call > 0, "ReadAloudWindow no longer calls ISpeaker.Say — update this test");

        // The call must be consumed: assigned, tested, or negated. A bare statement discards it.
        int lineStart = source.LastIndexOf('\n', call) + 1;
        string line = source[lineStart..source.IndexOf('\n', call)].Trim();

        Assert.False(
            line.StartsWith("_speaker.Say(", StringComparison.Ordinal),
            $"the return value is discarded: {line}  — Say returns false when this computer could "
            + "not speak, and the window then claims to be reading while nothing is heard.");
    }

    // ---- plumbing ----------------------------------------------------------------------------

    private static Type WindowType(string name)
    {
        Type? type = typeof(MainWindow).Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == name && typeof(Window).IsAssignableFrom(t));

        Assert.True(type is not null, $"no window type named {name}");
        return type!;
    }

    /// <summary>
    /// Reads the window's source for the live-region call. Reflection cannot see it: the setting is
    /// applied to a control instance at construction, and constructing these windows needs a live
    /// document and callbacks. The source is the honest place to ask.
    /// </summary>
    private static bool HasLiveRegionSource(Type type)
    {
        string path = System.IO.Path.Combine(
            RepoRoot(), "src", "TrestleBoard.App", "Dialogs", type.Name + ".cs");

        return System.IO.File.Exists(path)
            && System.IO.File.ReadAllText(path).Contains("AutomationLiveSetting.Polite", StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var at = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !System.IO.File.Exists(System.IO.Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
