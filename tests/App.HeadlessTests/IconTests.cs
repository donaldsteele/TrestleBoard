using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Widgets;
using Xunit;
using GlyphPath = Avalonia.Controls.Shapes.Path;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The icon gate (PLAN.md §12 item 13, §11 M16). Four things, in the order they can break:
/// every mapped key resolves to real geometry, every geometry is actually used, every action has an
/// icon decision recorded about it, and no button anywhere is a picture with no words.
/// </summary>
public sealed class IconTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    private static string IconsPath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null
                && !File.Exists(System.IO.Path.Combine(directory.FullName, "TrestleBoard.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return System.IO.Path.Combine(
                directory!.FullName, "src", "TrestleBoard.App", "Theme", "Icons.axaml");
        }
    }

    /// <summary>
    /// Both directions, in the house style set by <c>DocsTests.EveryCommittedImageIsReferenced</c>:
    /// a mapped key that resolves to nothing is a blank button, and a geometry nothing references is
    /// a drawing somebody spent time on that no user will ever see.
    ///
    /// <para>The 0–24 grid is checked here too, because it is what makes every glyph the same
    /// apparent weight inside the same <see cref="Viewbox"/>. A geometry that overflows it comes out
    /// larger than its neighbours for no reason a reader can see.</para>
    /// </summary>
    [Fact]
    public async Task EveryIconKeyResolvesToGeometryAndEveryGeometryIsUsed()
    {
        HashSet<string> declared = DeclaredGeometryKeys();
        IReadOnlySet<string> referenced = ActionIcons.EveryGlyphKey;

        Assert.Equal(24, declared.Count);
        Assert.True(
            declared.SetEquals(referenced),
            $"declared but never referenced: [{string.Join(", ", declared.Except(referenced).Order())}]; "
                + $"referenced but not declared: [{string.Join(", ", referenced.Except(declared).Order())}]");

        await Session.Dispatch(() =>
        {
            Application application = Application.Current!;
            var wrong = new List<string>();

            foreach (string key in declared.Order())
            {
                if (!application.TryGetResource(key, null, out object? value) || value is not Geometry geometry)
                {
                    wrong.Add($"{key}: does not resolve to a Geometry");
                    continue;
                }

                Rect bounds = geometry.Bounds;
                if (bounds.X < -0.01 || bounds.Y < -0.01 || bounds.Right > 24.01 || bounds.Bottom > 24.01)
                {
                    wrong.Add($"{key}: {bounds} leaves the 0–24 grid");
                }

                if (bounds.Width < 4 || bounds.Height < 4)
                {
                    wrong.Add($"{key}: {bounds} is too small to be a glyph — is the path data empty?");
                }
            }

            Assert.True(wrong.Count == 0, string.Join("; ", wrong));
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <c>docs/M7-spec.md</c> deferred the icon set and said the App layer would map
    /// <see cref="IWidgetDefinition.IconKey"/> to a glyph. This is that mapping being complete, both
    /// ways round: a widget whose key has no glyph shows nothing, and a glyph mapped from a key no
    /// widget returns is dead weight.
    /// </summary>
    [Fact]
    public void EveryWidgetIconKeyHasAGlyphAndEveryMappedKeyBelongsToAWidget()
    {
        HashSet<string> fromWidgets = WidgetRegistry.CreateDefault().All
            .Select(d => d.IconKey)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(6, fromWidgets.Count);
        Assert.True(
            fromWidgets.SetEquals(ActionIcons.WidgetKeysWithAnIcon),
            "widget IconKeys and the glyph map disagree: "
                + $"widgets only: [{string.Join(", ", fromWidgets.Except(ActionIcons.WidgetKeysWithAnIcon).Order())}]; "
                + $"map only: [{string.Join(", ", ActionIcons.WidgetKeysWithAnIcon.Except(fromWidgets).Order())}]");

        foreach (string key in fromWidgets)
        {
            Assert.NotNull(ActionIcons.ForWidget(key));
        }
    }

    /// <summary>
    /// The two sets partition the catalog exactly, so <b>a new action cannot arrive without somebody
    /// deciding, on the record, whether it gets a picture.</b> The list of the ones that do not is a
    /// design record and each entry states its reason — that is why this test also insists the
    /// reasons are non-empty.
    /// </summary>
    [Fact]
    public void EveryActionEitherHasAnIconOrIsListedAsIconLess()
    {
        HashSet<string> all = ActionCatalog.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> withIcon = ActionIcons.ActionsWithAnIcon.ToHashSet(StringComparer.Ordinal);
        HashSet<string> without = ActionIcons.ActionsWithoutAnIcon.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.True(
            !withIcon.Overlaps(without),
            "an action is both mapped and listed as icon-less: "
                + string.Join(", ", withIcon.Intersect(without).Order()));

        HashSet<string> covered = withIcon.Union(without).ToHashSet(StringComparer.Ordinal);
        Assert.True(
            all.SetEquals(covered),
            $"no icon decision recorded for: [{string.Join(", ", all.Except(covered).Order())}]; "
                + $"decided but not in the catalog: [{string.Join(", ", covered.Except(all).Order())}]");

        foreach ((string id, string reason) in ActionIcons.ActionsWithoutAnIcon)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(reason),
                $"{id} is listed as icon-less with no reason given");
        }
    }

    /// <summary>
    /// <b>An icon never appears without its label</b> (PLAN.md §6). Unconditional: §6 grants no size
    /// exemption, because the audience is elderly and several of them use this application a few
    /// times a month.
    ///
    /// <para>This is the strongest evidence that M16 is a net accessibility gain rather than a
    /// cosmetic risk — the milestone that introduces pictures is the one that makes a picture-only
    /// button impossible to ship.</para>
    /// </summary>
    [Fact]
    public async Task AnIconIsNeverTheOnlyContent()
    {
        await Session.Dispatch(() =>
        {
            var wordless = new List<string>();
            int withGlyphs = 0;

            foreach ((string windowName, Window window) in AccessibilityTests.EveryWindow())
            {
                window.Measure(new Size(1280, 900));
                window.Arrange(new Rect(0, 0, 1280, 900));

                foreach (Button button in window.GetLogicalDescendants().OfType<Button>())
                {
                    List<GlyphPath> glyphs = button.GetLogicalDescendants().OfType<GlyphPath>().ToList();
                    if (glyphs.Count == 0)
                    {
                        continue;
                    }

                    withGlyphs++;

                    bool hasWords = button.GetLogicalDescendants()
                        .OfType<TextBlock>()
                        .Any(t => !string.IsNullOrWhiteSpace(t.Text));

                    if (!hasWords)
                    {
                        wordless.Add($"{windowName}: {Avalonia.Automation.AutomationProperties.GetName(button)}");
                    }
                }

                window.Close();
            }

            Assert.True(withGlyphs > 10, $"only {withGlyphs} buttons with a glyph were found — the walk found nothing");
            Assert.True(wordless.Count == 0, "buttons showing a picture and no words: " + string.Join(", ", wordless));
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// <b>Non-optional, and the reason is worth stating.</b> <c>ActionPanel.LabelOf</c> would have
    /// started returning the empty string the moment M16 changed panel-button content from a bare
    /// TextBlock to an <see cref="IconText"/> — and that would have failed <em>nothing</em>: of the
    /// four places the audit suite calls it, three use the result only to build a failure message
    /// and the fourth is one <c>Assert.Contains</c>. The suite would have stayed green while quietly
    /// auditing buttons it could no longer name.
    /// </summary>
    [Fact]
    public async Task EveryPanelButtonStillReportsItsLabel()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow();
            window.OpenIssueSample();
            window.Measure(new Size(1280, 900));
            window.Arrange(new Rect(0, 0, 1280, 900));

            IReadOnlyList<Button> buttons = window.PanelForTest!.ButtonsForTest;
            Assert.True(buttons.Count > 5, $"only {buttons.Count} panel buttons were offered");

            foreach (Button button in buttons)
            {
                string label = TrestleBoard.App.Actions.ActionPanel.LabelOf(button);
                Assert.False(
                    string.IsNullOrWhiteSpace(label),
                    "a panel button reports no label at all — LabelOf has stopped understanding "
                        + $"the button content ({button.Content?.GetType().Name ?? "null"})");

                // And it is the TITLE, not the title plus its shortcut: the gesture moved to a
                // second line in M16 and a screen reader has always heard the title alone.
                Assert.DoesNotContain("Ctrl+", label, StringComparison.Ordinal);
            }

            window.Close();
        }, TestContext.Current.CancellationToken);
    }

    private static HashSet<string> DeclaredGeometryKeys()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        XDocument document = XDocument.Load(IconsPath);

        return document.Root!
            .Elements()
            .Where(e => e.Name.LocalName == "StreamGeometry")
            .Select(e => (string)e.Attribute(xaml + "Key")!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
