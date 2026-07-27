using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Rendering;
using TextBlock = Avalonia.Controls.TextBlock;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user pressed Apply with.</summary>
/// <param name="StyleName">The base style the change is for.</param>
/// <param name="FontFamily">The chosen family, or null when only the size changed.</param>
/// <param name="SizePt">The chosen size, or null when only the font changed.</param>
public sealed record TextStyleChoice(string StyleName, string? FontFamily, float? SizePt);

/// <summary>
/// Font and size, per style role (PLAN.md M14).
/// <para>
/// Deliberately NOT a general style editor — that is a scope trap. It is an assignment sheet:
/// left, the roles this newsletter has, one big row each; right, two controls only. Nothing
/// changes until Apply, which is said out loud at the bottom in M12's import-wizard voice.
/// </para>
/// <para>
/// The user never sees <c>body-bold-italic</c>. Only BASE styles are listed, labelled through
/// <see cref="StyleLabels"/>, because bold and italic follow their base atomically.
/// </para>
/// </summary>
public sealed class TextStylesWindow : Window
{
    private const float PreviewSizePt = 17f;
    private const int RowHeight = 56;

    private readonly FontStore _fonts;
    private readonly Dictionary<string, CharacterStyleDef> _roles;
    private readonly ListBox _roleList;
    private readonly ListBox _fontList;
    private readonly TextBox _search;
    private readonly TextBlock _searchCount;
    private readonly TextBlock _sizeLabel;
    private readonly TextBlock _howItWillLook;
    private readonly Image _howItWillLookImage;
    private readonly TextBlock _reflowWarning;
    private readonly TextBlock _overrideFooter;
    private readonly Button _showOverrides;
    private readonly Button _clearOverrides;
    private readonly List<FontFamilyInfo> _allFamilies;
    private List<object> _fontRows = [];
    private readonly string _sampleText;
    private readonly uint _foreground;
    private readonly uint _background;

    private float _sizePt = 11f;

    /// <summary>
    /// Builds the sheet.
    /// </summary>
    /// <param name="fonts">The bundled store — previews are drawn with the real faces.</param>
    /// <param name="styles">The document's style sheet.</param>
    /// <param name="initialStyleName">The role to start on, usually the one at the caret.</param>
    /// <param name="sampleText">The user's own words, for "How it will look".</param>
    /// <param name="overrideCount">How many pieces of text carry a "just here" font.</param>
    /// <param name="darkTheme">Preview colours are parameters, never read from a static.</param>
    public TextStylesWindow(
        FontStore fonts,
        StyleSheet styles,
        string? initialStyleName,
        string sampleText,
        int overrideCount,
        bool darkTheme)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(styles);
        _fonts = fonts;
        _sampleText = string.IsNullOrWhiteSpace(sampleText) ? "The Trestle Board" : sampleText;
        _foreground = darkTheme ? 0xFFF0F0F0u : 0xFF101010u;
        _background = darkTheme ? 0xFF202020u : 0xFFFFFFFFu;

        // Base styles only: "body", never "body-bold-italic". Siblings follow atomically.
        _roles = styles.CharacterStyles
            .Where(s => CharacterStyleResolver.BaseName(s.Name) == s.Name
                        && !StyleOverrides.IsOverride(s.Name))
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Title = "Fonts and text styles";
        Width = 980;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Fonts and text styles");

        _roleList = new ListBox { FontSize = 18, MinWidth = 330 };
        AutomationProperties.SetName(_roleList, "The kinds of writing in this newsletter");

        _search = new TextBox
        {
            FontSize = 24,
            MinHeight = 48,
            Watermark = "Search for a font",
        };
        AutomationProperties.SetName(_search, "Search for a font");

        _searchCount = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_searchCount, AutomationLiveSetting.Polite);

        _fontList = new ListBox { FontSize = 16 };
        AutomationProperties.SetName(_fontList, "Fonts");

        _sizeLabel = new TextBlock { FontSize = 22, MinWidth = 90, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(_sizeLabel, AutomationLiveSetting.Polite);

        _howItWillLook = new TextBlock { FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        _howItWillLookImage = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };

        _reflowWarning = new TextBlock
        {
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
        };
        AutomationProperties.SetLiveSetting(_reflowWarning, AutomationLiveSetting.Polite);

        _overrideFooter = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        _showOverrides = SmallButton("Show me");
        _clearOverrides = SmallButton("Put them all back");
        _showOverrides.Click += (_, _) => { ShowOverridesRequested = true; Close(); };
        _clearOverrides.Click += (_, _) => { ClearOverridesRequested = true; Close(); };
        SetOverrideFooter(overrideCount);

        _allFamilies = BundledFontCatalog.Families.ToList();
        BuildFontRows(string.Empty);

        foreach (string name in _roles.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            _roleList.Items.Add(BuildRoleRow(name));
        }

        string? start = initialStyleName is null
            ? null
            : StyleOverrides.RoleOf(CharacterStyleResolver.BaseName(initialStyleName));
        _roleList.SelectedIndex = Math.Max(0, _roles.Keys
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList()
            .FindIndex(n => n == start));

        _roleList.SelectionChanged += (_, _) => OnRoleChanged();
        _fontList.SelectionChanged += (_, _) => UpdatePreview();
        _search.TextChanged += (_, _) => BuildFontRows(_search.Text ?? string.Empty);

        Content = BuildLayout();
        OnRoleChanged();
    }

    /// <summary>Null unless Apply was pressed.</summary>
    public TextStyleChoice? Result { get; private set; }

    /// <summary>The user asked to be shown where the overridden text is.</summary>
    public bool ShowOverridesRequested { get; private set; }

    /// <summary>The user asked to put every overridden piece of text back.</summary>
    public bool ClearOverridesRequested { get; private set; }

    /// <summary>The role rows, for the headless tests.</summary>
    internal IReadOnlyList<string> RoleNamesForTest =>
        _roles.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The families currently offered, for the headless tests.</summary>
    internal IReadOnlyList<string> VisibleFontsForTest =>
        _fontRows.OfType<StackPanel>().Select(p => (string)p.Tag!).ToList();

    internal void SearchForTest(string text) => BuildFontRows(text);

    internal void SelectFontForTest(string family)
    {
        int index = _fontRows.FindIndex(row => row is StackPanel p && (string)p.Tag! == family);
        if (index >= 0)
        {
            _fontList.SelectedIndex = index;
        }
    }

    internal void SelectRoleForTest(string styleName)
    {
        int index = RoleNamesForTest.ToList().IndexOf(styleName);
        if (index >= 0)
        {
            _roleList.SelectedIndex = index;
        }
    }

    internal void ApplyForTest() => ApplyAndClose();

    internal string ReflowWarningForTest => _reflowWarning.Text ?? string.Empty;

    private DockPanel BuildLayout()
    {
        var apply = new Button
        {
            Content = "Apply",
            FontSize = 20,
            MinHeight = 48,
            MinWidth = 200,
            IsDefault = true,
        };
        apply.Click += (_, _) => ApplyAndClose();
        AutomationProperties.SetName(apply, "Apply");

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 20,
            MinHeight = 48,
            MinWidth = 140,
            IsCancel = true,
        };
        cancel.Click += (_, _) => Close();
        AutomationProperties.SetName(cancel, "Cancel");

        var smaller = SmallButton("− Smaller");
        var bigger = SmallButton("+ Bigger");
        smaller.Click += (_, _) => StepSize(-1);
        bigger.Click += (_, _) => StepSize(+1);
        AutomationProperties.SetName(smaller, "Make this kind of writing smaller");
        AutomationProperties.SetName(bigger, "Make this kind of writing bigger");

        var right = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Font", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold },
                _search,
                _searchCount,
                new ScrollViewer { Height = 300, Content = _fontList },
                new TextBlock { Text = "Size", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { smaller, _sizeLabel, bigger },
                },
                _howItWillLook,
                _howItWillLookImage,
                _reflowWarning,
            },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var left = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "The kinds of writing in this newsletter",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 330,
                },
                new ScrollViewer { Height = 520, Content = _roleList },
            },
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        right.Margin = new Avalonia.Thickness(24, 0, 0, 0);
        grid.Children.Add(left);
        grid.Children.Add(right);

        return new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                Bottom(new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        _overrideFooter,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 10,
                            Children = { _showOverrides, _clearOverrides },
                        },
                        new TextBlock
                        {
                            Text = "Nothing changes until you press Apply.",
                            FontSize = 18,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Margin = new Avalonia.Thickness(0, 8, 0, 0),
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { cancel, apply },
                        },
                    },
                }),
                grid,
            },
        };
    }

    private static Control Bottom(Control control)
    {
        DockPanel.SetDock(control, Dock.Bottom);
        return control;
    }

    private static Button SmallButton(string text)
    {
        var button = new Button { Content = text, FontSize = 18, MinHeight = 44, MinWidth = 140 };
        AutomationProperties.SetName(button, text);
        return button;
    }

    private void SetOverrideFooter(int count)
    {
        bool any = count > 0;
        _overrideFooter.Text = count switch
        {
            0 => "Every piece of writing in this newsletter uses the font its kind of writing says.",
            1 => "1 piece of text uses a different font.",
            _ => $"{count} pieces of text use a different font.",
        };
        _showOverrides.IsVisible = any;
        _clearOverrides.IsVisible = any;
    }

    /// <summary>One role row: its plain-language name, and a live sample in its actual font.</summary>
    private StackPanel BuildRoleRow(string styleName)
    {
        CharacterStyleDef def = _roles[styleName];
        var stack = new StackPanel
        {
            Spacing = 2,
            MinHeight = RowHeight,
            Margin = new Avalonia.Thickness(4),
            Tag = styleName,
        };
        stack.Children.Add(new TextBlock
        {
            Text = StyleLabels.Describe(styleName),
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{def.FontFamily}, {StyleOverrides.Size(def.SizePt)}",
            FontSize = 14,
        });
        stack.Children.Add(PreviewImage(def.FontFamily, PreviewSizePt, StyleLabels.Describe(styleName)));
        AutomationProperties.SetName(
            stack,
            $"{StyleLabels.Describe(styleName)}, {def.FontFamily}, {StyleOverrides.Size(def.SizePt)}");
        return stack;
    }

    private void BuildFontRows(string search)
    {
        IEnumerable<FontFamilyInfo> matches = _allFamilies;
        if (!string.IsNullOrWhiteSpace(search))
        {
            matches = matches.Where(f =>
                f.Family.Contains(search, StringComparison.OrdinalIgnoreCase)
                || f.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var rows = new List<object>();
        FontCategory? group = null;
        foreach (FontFamilyInfo family in matches)
        {
            if (group != family.Category)
            {
                group = family.Category;
                rows.Add(new TextBlock
                {
                    Text = BundledFontCatalog.CategoryLabel(family.Category),
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    Margin = new Avalonia.Thickness(0, 10, 0, 2),
                    IsHitTestVisible = false,
                });
            }

            rows.Add(BuildFontRow(family));
        }

        _fontRows = rows;
        string? previous = SelectedFamily;
        _fontList.ItemsSource = rows;
        int count = rows.OfType<StackPanel>().Count();
        _searchCount.Text = count switch
        {
            0 => "No fonts match what you typed. Clear the box to see them all again.",
            1 => "1 font matches.",
            _ => $"{count} fonts to choose from.",
        };

        if (previous is not null)
        {
            SelectFontForTest(previous);
        }
    }

    /// <summary>
    /// A row ≥56px showing the family's name in its own face, a sample line, and one sentence of
    /// plain language. A ListBox rather than a ComboBox: 20 families is right at the edge where a
    /// dropdown becomes tedious to scroll, and rows this size can carry a real preview.
    /// </summary>
    private StackPanel BuildFontRow(FontFamilyInfo family)
    {
        var stack = new StackPanel
        {
            Spacing = 2,
            MinHeight = RowHeight,
            Margin = new Avalonia.Thickness(4),
            Tag = family.Family,
        };
        stack.Children.Add(PreviewImage(family.Family, PreviewSizePt, family.Family));
        stack.Children.Add(PreviewImage(family.Family, 13f, family.SampleText));
        stack.Children.Add(new TextBlock
        {
            Text = family.Description,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
        });
        AutomationProperties.SetName(stack, $"{family.Family}. {family.Description}");
        return stack;
    }

    private Image PreviewImage(string family, float sizePt, string text)
    {
        var image = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
        SetPreview(image, family, sizePt, text);
        return image;
    }

    private void SetPreview(Image image, string family, float sizePt, string text)
    {
        FontPreview preview = FontPreviewRenderer.Render(
            _fonts,
            new FontKey(family, TrestleBoard.Layout.Fonts.FontWeight.Regular, FontStyleSlant.Normal),
            sizePt,
            text,
            _foreground,
            _background,
            scale: 1.5f);
        using var stream = new MemoryStream(preview.Png);
        image.Source = new Bitmap(stream);
        AutomationProperties.SetName(image, text);
    }

    private string? SelectedFamily =>
        _fontList.SelectedItem is StackPanel { Tag: string family } ? family : null;

    private string? SelectedRole =>
        _roleList.SelectedItem is StackPanel { Tag: string role } ? role : null;

    private void OnRoleChanged()
    {
        if (SelectedRole is not { } role || !_roles.TryGetValue(role, out CharacterStyleDef? def))
        {
            return;
        }

        _sizePt = def.SizePt;
        SelectFontForTest(def.FontFamily);
        UpdatePreview();
    }

    private void StepSize(int direction)
    {
        float? next = FontSizeLadder.Step(_sizePt, direction);
        if (next is not null)
        {
            _sizePt = next.Value;
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        _sizeLabel.Text = StyleOverrides.Size(_sizePt);
        AutomationProperties.SetName(_sizeLabel, $"Size {StyleOverrides.Size(_sizePt)}");

        string family = SelectedFamily ?? (SelectedRole is { } role && _roles.TryGetValue(role, out CharacterStyleDef? d)
            ? d.FontFamily
            : BundledFonts.BodyFamily);

        _howItWillLook.Text = "How it will look";
        SetPreview(_howItWillLookImage, family, _sizePt, _sampleText);

        // The warning is not optional: line height comes from font metrics and the minimum wrap
        // segment is 4 x the average character width, so a font or size change can repaginate.
        string label = SelectedRole is { } r ? StyleLabels.Describe(r) : "this kind of writing";
        _reflowWarning.Text =
            $"This changes every piece of writing that uses {label}. The words may move to different "
            + "lines, and the newsletter may end up with a different number of pages. Ctrl+Z puts it "
            + "back in one step.";
    }

    private void ApplyAndClose()
    {
        if (SelectedRole is not { } role || !_roles.TryGetValue(role, out CharacterStyleDef? def))
        {
            Close();
            return;
        }

        string? family = SelectedFamily is { } chosen
            && !string.Equals(chosen, def.FontFamily, StringComparison.Ordinal)
                ? chosen
                : null;
        float? size = Math.Abs(_sizePt - def.SizePt) >= 0.01f ? _sizePt : null;
        Result = family is null && size is null ? null : new TextStyleChoice(role, family, size);
        Close();
    }
}
