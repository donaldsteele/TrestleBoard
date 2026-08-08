using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using TrestleBoard.Core.Text;
using TrestleBoard.App.Actions;
using TrestleBoard.Editing.Actions;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M27, review §14.3–§14.4: the app says what it means, in one voice.
///
/// Three separate complaints from the review are one property underneath: the words on a control
/// have to be the words for that command, everywhere, and never the words the code happens to use
/// internally. A user who is shown <c>lodge-table</c>, or who finds "Smaller" meaning two different
/// things in two places, learns that the labels cannot be trusted — and after that no amount of
/// careful wording elsewhere helps.
/// </summary>
public sealed class PlainLanguageTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// The paragraph-style menu showed raw style ids — `body`, `heading`, `caption`. StyleLabels
    /// was written for exactly this and simply was not being called, so this was the one place in
    /// the app that still leaked the document model's vocabulary at the user (review §14.3).
    /// </summary>
    [Fact]
    public async Task TheParagraphStyleMenuNamesRolesInPlainLanguage()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();

            // Click into some writing, which is what makes the menu live.
            Assert.True(window.EditorForTest!.TryBeginAt(0, 100f, 200f));
            window.RefreshActions();

            MenuItem menu = window.GetLogicalDescendants()
                .OfType<MenuItem>()
                .Single(m => m.Name == "ParagraphStyleMenu");

            List<string> headers = [.. menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString() ?? "")];
            Assert.NotEmpty(headers);

            foreach (string header in headers)
            {
                // No raw id ever reaches the screen: they are lower-case, hyphenated and wordless.
                Assert.False(
                    Regex.IsMatch(header, "^[a-z]+(-[a-z]+)*$"),
                    $"the paragraph style menu shows the raw style id \"{header}\"");
                Assert.DoesNotContain("Style: ", header, StringComparison.Ordinal);
            }

            // And the labels are the ones StyleLabels declares.
            Assert.Contains("Body text", headers);
            Assert.Contains("Headings", headers);

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A toolbar button says what the catalog calls that command.
    ///
    /// <para>The catalog is the one place a command is named (M11), and the menu bar already reads
    /// from it. The toolbar did not: it said "Smaller"/"Bigger" for zoom — the very same words the
    /// font window uses for POINT SIZE — and a bare "Back"/"Next" for pages, which is also what the
    /// officers wizard's own buttons say. The same word doing two jobs in one program is how a user
    /// learns not to trust any of them (review §14.4).</para>
    ///
    /// <para>A shorter label is allowed, because the toolbar has less room; a DIFFERENT one is not.
    /// Containment in either direction is the test for "the same words".</para>
    /// </summary>
    [Fact]
    public async Task EveryToolbarButtonSaysWhatTheCatalogCallsThatCommand()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var mismatched = new List<string>();
            int compared = 0;

            foreach (Button button in window.ToolbarButtons)
            {
                // LabelOf, because DressToolbar replaces a button's plain string Content with an
                // icon-plus-label composite; reading Content directly finds nothing.
                string label = ActionPanel.LabelOf(button);
                if (button.Tag is not string actionId
                    || label.Length == 0
                    || !ActionCatalog.TryGet(actionId, out EditorAction? action))
                {
                    continue;
                }

                compared++;
                string title = Strip(action.Title);
                if (!title.Contains(label, StringComparison.OrdinalIgnoreCase)
                    && !label.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    mismatched.Add($"{actionId}: toolbar says \"{label}\", the catalog says \"{title}\"");
                }
            }

            Assert.True(compared >= 5, $"only {compared} toolbar buttons were compared");
            Assert.True(
                mismatched.Count == 0,
                "a toolbar button uses different words from its command: " + string.Join("; ", mismatched));

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// PLAN.md §6's 16pt floor, as a standing assertion rather than a promise. The review found it
    /// broken in five places (review §14.3).
    ///
    /// <para>Scope, stated honestly: this walks the TextBlocks and Buttons in the main window's
    /// visual tree. It does NOT see text the canvas paints through Skia — the empty-frame hint was
    /// one of the five and was the smallest text in the app — nor text inside dialogs that are not
    /// open. Those were fixed by hand and are guarded only by the comments at their call sites.</para>
    /// </summary>
    [Fact]
    public async Task NoChromeTextIsSmallerThanSixteenPoints()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var tooSmall = new List<string>();
            int seen = 0;

            foreach (TextBlock text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                seen++;
                if (text.FontSize < 16)
                {
                    tooSmall.Add($"\"{text.Text}\" at {text.FontSize}");
                }
            }

            foreach (Button button in window.GetLogicalDescendants().OfType<Button>())
            {
                if (button.FontSize < 16)
                {
                    tooSmall.Add($"button \"{button.Content}\" at {button.FontSize}");
                }
            }

            Assert.True(seen > 5, $"only {seen} text blocks found — the walk found nothing");
            Assert.True(tooSmall.Count == 0, "below the 16pt floor: " + string.Join("; ", tooSmall));

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Header text without its access-key underscore or trailing submenu arrow.</summary>
    private static string Strip(string header) =>
        header.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("▸", string.Empty, StringComparison.Ordinal)
            .Replace("…", string.Empty, StringComparison.Ordinal)
            .Trim();

    /// <summary>
    /// M45, review §14.3: there was not one <c>ToolTip.Tip</c> anywhere in the App project.
    ///
    /// <para>The toolbar is where a tooltip earns its keep — short words beside a glyph, and a
    /// catalog that already holds a sentence saying what the command does, written for this
    /// audience. Nothing new was authored: the tooltip is the description a screen reader has been
    /// getting since M11, finally shown to people who use a mouse.</para>
    /// </summary>
    [Fact]
    public async Task EveryToolbarButtonExplainsItselfOnHover()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.OpenIssueSample();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var missing = new List<string>();
            foreach (Button button in window.ToolbarButtons)
            {
                if (button.Tag is not string actionId
                    || !ActionCatalog.TryGet(actionId, out EditorAction? action))
                {
                    continue;
                }

                if (ToolTip.GetTip(button) as string is not { Length: > 0 } tip)
                {
                    missing.Add(actionId);
                    continue;
                }

                // The catalog's own sentence, not a second one written beside it that could drift.
                Assert.Contains(action.ShortDescription, tip, StringComparison.Ordinal);

                // And the keys, because a tooltip is read by somebody already using the mouse —
                // the one moment they are looking at a place that can teach them the keyboard.
                if (action.DisplayGesture is { Length: > 0 } keys)
                {
                    Assert.Contains(keys, tip, StringComparison.Ordinal);
                }
            }

            Assert.True(
                missing.Count == 0,
                "these toolbar buttons say nothing on hover: " + string.Join(", ", missing));

            window.SaveFirstAnswerForTest = MainWindow.SaveFirst.Discard;
            window.Close();
        }, TestContext.Current.CancellationToken);
    }
}
