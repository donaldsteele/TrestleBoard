using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using TrestleBoard.App.Settings;
using TrestleBoard.App.Theme;
using TrestleBoard.Rendering;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// The palette gate (PLAN.md §12 item 13, §11 M16).
///
/// <para>Two different things are proved here. The first is that the theming <b>composition</b>
/// works at all: High Contrast is a custom <see cref="ThemeVariant"/> inheriting Dark, and the whole
/// milestone rests on FluentTheme's several hundred control resources resolving through that
/// inheritance chain. Nothing in the tree exercised that before M16, and an Avalonia bump could take
/// it away silently — the app would not crash, it would just start rendering unstyled controls.</para>
///
/// <para>The second is that every declared colour pair still meets its floor. That check parses
/// <c>Theme/Palette.axaml</c> <b>as a file</b> and recomputes the ratios from the hex values in it,
/// so the numbers written beside each pair cannot drift from the colours they describe: the header
/// comment is the test's expected output, not documentation about it.</para>
/// </summary>
public sealed class ThemeCompositionTests
{
    private static HeadlessUnitTestSession Session => HeadlessSession.Instance;

    /// <summary>
    /// Where the palette lives on disk. The test reads the source file, not the compiled resource,
    /// because the point is to check what a reviewer will read.
    /// </summary>
    private static string PalettePath
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrestleBoard.slnx")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory!.FullName, "src", "TrestleBoard.App", "Theme", "Palette.axaml");
        }
    }

    /// <summary>
    /// <b>The assumption the whole milestone rests on.</b> High Contrast is
    /// <c>new ThemeVariant("HighContrast", inheritVariant: Dark)</c>, and if Avalonia did not follow
    /// that inheritance for resources declared by FluentTheme itself, collapsing the old
    /// <c>StyleInclude</c> overlay into theme dictionaries would leave the variant missing every
    /// control brush in the framework.
    ///
    /// <para>What is asserted: a spread of FluentTheme keys — a background, a foreground, a border
    /// and an accent — resolve in the custom variant to <b>exactly</b> the values they have in Dark,
    /// and to something different from Light, so the test cannot pass by resolving nothing or by
    /// falling through to the default variant.</para>
    /// </summary>
    [Fact]
    public async Task HighContrastInheritsEveryFluentResourceThePaletteDoesNotOverride()
    {
        // A spread of Fluent's own control resources, including the one whose absence from the old
        // overlay left the toolbar and the status bar mid-grey in High Contrast.
        string[] fluentKeys =
        [
            "SystemControlBackgroundChromeMediumLowBrush",
            "SystemControlForegroundBaseHighBrush",
            "SystemControlBackgroundAltHighBrush",
            "ButtonBackground",
            "ButtonForeground",
            "ButtonBorderBrush",
            "TextControlBackground",
            "TextControlForeground",
        ];

        await Session.Dispatch(() =>
        {
            Application application = Application.Current!;

            int differedFromLight = 0;
            foreach (string key in fluentKeys)
            {
                Assert.True(
                    application.TryGetResource(key, ThemeVariant.Dark, out object? dark),
                    $"FluentTheme no longer declares '{key}' in Dark — the spread needs rewriting");
                Assert.True(
                    application.TryGetResource(key, AppTheme.HighContrast, out object? high),
                    $"'{key}' does not resolve in the custom HighContrast variant: inheritVariant is "
                        + "not being followed, and Theme/AppTheme.cs names the fallback");

                Assert.Equal(Describe(dark), Describe(high));

                application.TryGetResource(key, ThemeVariant.Light, out object? light);
                if (Describe(light) != Describe(dark))
                {
                    differedFromLight++;
                }
            }

            // Without this the assertions above would pass just as happily if every variant returned
            // the same thing — which is what a broken inheritance chain falling through to Default
            // would look like.
            Assert.True(
                differedFromLight >= 4,
                $"only {differedFromLight} of {fluentKeys.Length} Fluent keys differ between Light and "
                    + "Dark, so matching Dark proves nothing");
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Every token answers in every variant. A key present in Light and Dark but missing from High
    /// Contrast does not fail loudly — it inherits the Dark value and quietly breaks 7:1, which is
    /// the exact failure M16 exists to fix.
    /// </summary>
    [Fact]
    public async Task EveryTokenResolvesInAllThreeVariants()
    {
        List<string> keys = DeclaredKeys();

        await Session.Dispatch(() =>
        {
            Application application = Application.Current!;
            var missing = new List<string>();

            foreach ((string name, ThemeVariant variant) in Variants())
            {
                foreach (string key in keys)
                {
                    if (!application.TryGetResource(key, variant, out object? value) || value is null)
                    {
                        missing.Add($"{name}: {key}");
                    }
                }
            }

            Assert.True(keys.Count >= 14, $"only {keys.Count} tokens were found in the palette");
            Assert.True(missing.Count == 0, "tokens that do not resolve: " + string.Join(", ", missing));
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A token declared in one variant's dictionary and forgotten in another. Checked against the
    /// file rather than the resolved tree, because the resolved tree hides the omission behind
    /// inheritance — which is the whole point.
    /// </summary>
    [Fact]
    public void TheThreeVariantsDeclareExactlyTheSameKeys()
    {
        Dictionary<string, HashSet<string>> byVariant = KeysByVariant();

        Assert.Equal(3, byVariant.Count);
        HashSet<string> light = byVariant["Light"];

        foreach ((string variant, HashSet<string> keys) in byVariant)
        {
            Assert.True(
                light.SetEquals(keys),
                $"{variant} declares a different set of tokens from Light: "
                    + $"only in {variant}: [{string.Join(", ", keys.Except(light).Order())}]; "
                    + $"only in Light: [{string.Join(", ", light.Except(keys).Order())}]");
        }
    }

    /// <summary>
    /// <b>The gate.</b> Recomputes every ratio in the palette's CONTRAST block from the hex values
    /// declared above it, and fails on either a missed floor or a stale number. In High Contrast
    /// every floor is raised to 7:1 (PLAN.md §6); pairs declared <c>decorative</c> carry no floor
    /// anywhere and exist so that a hairline nobody has to perceive is not quietly promoted into a
    /// boundary somebody does.
    /// </summary>
    [Fact]
    public void EveryDeclaredPairMeetsItsFloorAndTheWrittenRatioIsRight()
    {
        Dictionary<string, Dictionary<string, string>> colours = ColoursByVariant();
        List<Pair> pairs = DeclaredPairs();

        Assert.True(pairs.Count >= 10, $"only {pairs.Count} pairs are declared in the palette");

        var failures = new List<string>();
        foreach (Pair pair in pairs)
        {
            foreach ((string variant, double written) in pair.Written)
            {
                string foreground = Colour(colours, variant, pair.Foreground);
                string background = Colour(colours, variant, pair.Background);
                double actual = ContrastRatio(foreground, background);

                if (Math.Abs(actual - written) > 0.005)
                {
                    failures.Add(
                        $"{variant} {pair.Foreground} on {pair.Background}: written {written:0.00}, "
                            + $"actually {actual:0.00}");
                }

                if (pair.Floor is not { } floor)
                {
                    continue; // decorative
                }

                double required = variant == "HighContrast" ? Math.Max(floor, 7d) : floor;
                if (actual + 0.005 < required)
                {
                    failures.Add(
                        $"{variant} {pair.Foreground} on {pair.Background}: {actual:0.00} is below "
                            + $"the {required:0.0} floor");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// <b>The live-tree contrast walk</b>, over the same nine windows the accessibility suite builds
    /// (PLAN.md §12 item 13), in all three variants.
    ///
    /// <para><b>What this test can and cannot see, stated plainly.</b> It reads what the styling
    /// system RESOLVED, not what the compositor PAINTED. A <c>TextBlock</c> has no background of its
    /// own, so the walk climbs to the nearest ancestor that has one — and that answer is simply
    /// wrong wherever a control template paints a layer the logical tree does not expose, popup menu
    /// items and the <c>ComboBox</c> dropdown among them. Those are skipped rather than guessed at.
    /// It is worth having anyway, because the mid-grey toolbar behind a black button is exactly the
    /// shape of defect it catches, and nothing caught that one for five milestones.</para>
    ///
    /// <para>Disabled controls are skipped: Fluent dims them deliberately and WCAG 1.4.3 exempts
    /// them. Large text takes the 3:1 floor the specification allows it.</para>
    /// </summary>
    [Fact]
    public async Task EveryPieceOfTextTheWalkCanSeeMeetsItsFloorInAllThreeVariants()
    {
        await Session.Dispatch(() =>
        {
            Application application = Application.Current!;
            ThemeVariant original = application.RequestedThemeVariant ?? ThemeVariant.Default;
            var failures = new List<string>();
            int measured = 0;

            try
            {
                foreach ((string variantName, ThemeVariant variant) in Variants())
                {
                    application.RequestedThemeVariant = variant;
                    double floor = variantName == "HighContrast" ? 7d : 4.5d;

                    foreach ((string windowName, Window window) in AccessibilityTests.EveryWindow())
                    {
                        // Set on the window as well as the application: a headless window that is
                        // never shown does not necessarily pick the application's variant up, and a
                        // walk that silently measured Light three times would prove nothing. The
                        // assertion below is what stops that being a silent condition.
                        window.RequestedThemeVariant = variant;
                        Assert.Equal(variant, window.ActualThemeVariant);

                        window.Measure(new Size(1280, 900));
                        window.Arrange(new Rect(0, 0, 1280, 900));

                        foreach (TextBlock text in window.GetLogicalDescendants().OfType<TextBlock>())
                        {
                            if (!text.IsEffectivelyEnabled || string.IsNullOrWhiteSpace(text.Text))
                            {
                                continue;
                            }

                            if (text.Foreground is not ISolidColorBrush { Color.A: 255 } foreground)
                            {
                                continue;
                            }

                            if (NearestBackground(text) is not { } background)
                            {
                                continue; // A template layer the logical tree does not expose.
                            }

                            measured++;

                            // WCAG 1.4.3's large-text allowance, in the points this app uses.
                            double required = text.FontSize >= 24
                                || (text.FontSize >= 18.66 && text.FontWeight >= FontWeight.Bold)
                                ? Math.Min(floor, variantName == "HighContrast" ? 7d : 3d)
                                : floor;

                            double ratio = ContrastRatio(
                                foreground.Color.ToString(),
                                background.ToString());

                            if (ratio + 0.005 < required)
                            {
                                failures.Add(
                                    $"{variantName}/{windowName}: {ratio:0.00} for "
                                        + $"\"{Shorten(text.Text!)}\" ({foreground.Color} on {background})");
                            }
                        }

                        window.Close();
                    }
                }
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }

            Assert.True(measured > 150, $"only {measured} pieces of text were measured over three variants");
            Assert.True(failures.Count == 0, string.Join("; ", failures.Distinct().Take(20)));
        }, TestContext.Current.CancellationToken);
    }

    private static string Shorten(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>
    /// The nearest logical ancestor that actually paints something opaque, which is the best a tree
    /// walk can do — see the limits on the test above.
    /// </summary>
    private static Color? NearestBackground(StyledElement element)
    {
        for (StyledElement? node = element; node is not null; node = node.Parent)
        {
            IBrush? brush = node switch
            {
                Border border => border.Background,
                TemplatedControl control => control.Background,
                Panel panel => panel.Background,
                _ => null,
            };

            if (brush is ISolidColorBrush { Color.A: 255 } solid)
            {
                return solid.Color;
            }
        }

        return null;
    }

    /// <summary>
    /// <b>No chrome control carries a brush of its own</b> (PLAN.md §12 item 13). This is the check
    /// that would have stopped the defect M16 was written to fix: a hard-coded colour does not fail,
    /// it survives the theme swap, and the toolbar and status bar stayed mid-grey in High Contrast
    /// for five milestones because nothing looked.
    ///
    /// <para>A source scan rather than a tree walk, deliberately. The resolved tree cannot tell a
    /// brush that came from a token apart from an identical brush somebody typed, and the point is
    /// to catch the typing.</para>
    ///
    /// <para><c>Opacity</c> counts as a colour here, because that is what it is being used for: it
    /// multiplies against whatever happens to be behind it, so the result is unknown to the palette
    /// and invisible to the contrast gate. De-emphasis is <c>Chrome.Muted</c>, which has a measured
    /// ratio.</para>
    /// </summary>
    [Fact]
    public void NoChromeControlPaintsItselfWithALiteralColour()
    {
        string appRoot = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(PalettePath))!);

        var offences = new List<string>();
        int scanned = 0;

        foreach (string file in Directory
                     .EnumerateFiles(appRoot, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal)
                         || f.EndsWith(".axaml", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                             StringComparison.Ordinal)
                         && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                             StringComparison.Ordinal))
                     // The palette IS the literals. Tokens.cs names one in its own documentation.
                     .Where(f => Path.GetFileName(f) is not ("Palette.axaml" or "Tokens.cs")))
        {
            scanned++;
            string relative = Path.GetRelativePath(appRoot, file);
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (LiteralColour.Match(line) is not { Success: true } match)
                {
                    continue;
                }

                offences.Add($"{relative}:{i + 1}: {match.Value.Trim()}");
            }
        }

        Assert.True(scanned > 20, $"only {scanned} source files were scanned");
        Assert.True(
            offences.Count == 0,
            "chrome painted with a literal instead of a palette token (Theme/Tokens.cs): "
                + string.Join("; ", offences));
    }

    /// <summary>
    /// A literal colour, in either of the two forms this codebase writes chrome in. The one
    /// permitted spelling is <c>Transparent</c>, which is the absence of a colour rather than one.
    /// </summary>
    private static readonly Regex LiteralColour = new(
        @"Brushes\.\w+"
            + @"|Colors\.\w+"
            + @"|new SolidColorBrush"
            + @"|(?:Background|Foreground|BorderBrush)\s*=\s*""(?!Transparent""|\{)"
            + @"|\bOpacity\s*=",
        RegexOptions.ExplicitCapture);

    private static string Colour(
        Dictionary<string, Dictionary<string, string>> colours,
        string variant,
        string role)
    {
        Dictionary<string, string> variantColours = colours[variant];
        string key = "TrestleBoard." + role;
        Assert.True(variantColours.ContainsKey(key), $"{variant} declares no '{key}'");
        return variantColours[key];
    }

    private static IEnumerable<(string Name, ThemeVariant Variant)> Variants()
    {
        yield return ("Light", ThemeVariant.Light);
        yield return ("Dark", ThemeVariant.Dark);
        yield return ("HighContrast", AppTheme.HighContrast);
    }

    private static List<string> DeclaredKeys() =>
        KeysByVariant()["Light"].Order().ToList();

    private static Dictionary<string, HashSet<string>> KeysByVariant()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach ((string variant, XElement dictionary) in VariantDictionaries())
        {
            result[variant] = dictionary
                .Elements()
                .Select(e => (string?)e.Attribute(XamlNamespace + "Key"))
                .Where(k => k is not null)
                .Select(k => k!)
                .ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> ColoursByVariant()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach ((string variant, XElement dictionary) in VariantDictionaries())
        {
            result[variant] = dictionary
                .Elements()
                .Where(e => e.Name.LocalName == "SolidColorBrush")
                .ToDictionary(
                    e => (string)e.Attribute(XamlNamespace + "Key")!,
                    e => (string)e.Attribute("Color")!,
                    StringComparer.Ordinal);
        }

        return result;
    }

    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static IEnumerable<(string Variant, XElement Dictionary)> VariantDictionaries()
    {
        XDocument document = XDocument.Load(PalettePath);
        XElement themeDictionaries = document.Root!
            .Elements()
            .Single(e => e.Name.LocalName == "ResourceDictionary.ThemeDictionaries");

        foreach (XElement dictionary in themeDictionaries.Elements())
        {
            string key = (string)dictionary.Attribute(XamlNamespace + "Key")!;

            // The custom variant's key is an {x:Static} reference, not a bare name.
            yield return (key.Contains("HighContrast", StringComparison.Ordinal) ? "HighContrast" : key,
                dictionary);
        }
    }

    private sealed record Pair(
        string Foreground,
        string Background,
        double? Floor,
        IReadOnlyList<(string Variant, double Ratio)> Written);

    private static readonly Regex PairLine = new(
        @"^\s*PAIR\s+(?<fg>[\w.]+)\s+on\s+(?<bg>[\w.]+)\s+>=\s+(?<floor>[\d.]+|decorative)\s*:\s*"
            + @"Light\s+(?<light>[\d.]+)\s+Dark\s+(?<dark>[\d.]+)\s+HighContrast\s+(?<high>[\d.]+)\s*$",
        RegexOptions.ExplicitCapture);

    private static List<Pair> DeclaredPairs()
    {
        var pairs = new List<Pair>();
        foreach (string line in File.ReadAllLines(PalettePath))
        {
            if (PairLine.Match(line) is not { Success: true } match)
            {
                continue;
            }

            double? floor = match.Groups["floor"].Value == "decorative"
                ? null
                : double.Parse(match.Groups["floor"].Value, CultureInfo.InvariantCulture);

            pairs.Add(new Pair(
                match.Groups["fg"].Value,
                match.Groups["bg"].Value,
                floor,
                [
                    ("Light", Ratio(match, "light")),
                    ("Dark", Ratio(match, "dark")),
                    ("HighContrast", Ratio(match, "high")),
                ]));
        }

        return pairs;
    }

    private static double Ratio(Match match, string group) =>
        double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    /// <summary>WCAG 2.1 relative luminance and contrast ratio, straight from the specification.</summary>
    internal static double ContrastRatio(string first, string second)
    {
        double a = RelativeLuminance(first);
        double b = RelativeLuminance(second);
        (double high, double low) = a > b ? (a, b) : (b, a);
        return (high + 0.05) / (low + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        Color colour = Color.Parse(hex);
        return (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));
    }

    private static double Channel(byte value)
    {
        double c = value / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static string Describe(object? value) => value switch
    {
        null => "(null)",
        ISolidColorBrush brush => brush.Color.ToString(),
        _ => value.ToString() ?? "(null)",
    };
    /// <summary>
    /// M34, review §14.3: the selection chrome follows the theme like everything else.
    ///
    /// <para>The outline, the resize handles, the snap guides and the overset badge were five
    /// <c>const uint</c> literals — a mid blue, a mid pink and a mid red — and they stayed exactly
    /// that with High Contrast on. A user who turned High Contrast on because they could not see
    /// the interface still could not see the part of it that says WHAT IS SELECTED.</para>
    ///
    /// <para>The rule High Contrast is held to (PLAN.md §6) is a 7:1 floor. Black and white against
    /// each other is 21:1, and every one of these marks is a shape as well as a colour, so nothing
    /// is lost by dropping the hue.</para>
    /// </summary>
    [Fact]
    public void TheSelectionChromeIsBlackAndWhiteInHighContrast()
    {
        FrameOverlayColours hc = FrameOverlayColours.HighContrast;

        foreach ((string name, uint argb) in new[]
                 {
                     ("selection outline", hc.Selection),
                     ("handle fill", hc.HandleFill),
                     ("snap guide", hc.SnapGuide),
                     ("overset badge", hc.Overset),
                 })
        {
            byte r = (byte)(argb >> 16);
            byte g = (byte)(argb >> 8);
            byte b = (byte)argb;
            Assert.True(
                (r == 0 && g == 0 && b == 0) || (r == 255 && g == 255 && b == 255),
                $"the {name} is #{r:X2}{g:X2}{b:X2} in High Contrast, which is neither black nor white");
        }

        // The handles must invert against the outline, or eight solid squares sitting on a line of
        // the same colour read as one thick line rather than as grab points.
        Assert.NotEqual(hc.Selection, hc.HandleFill);

        // And High Contrast really is a different set from the ordinary one, which is the bug.
        Assert.NotEqual(FrameOverlayColours.Default, hc);
    }

}
