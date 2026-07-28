using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
/// The two jobs this window used to do at once (PLAN.md M20 (d)).
/// </summary>
public enum TextStylesMode
{
    /// <summary>The whole-newsletter assignment sheet: every kind of writing, one at a time.</summary>
    Newsletter,

    /// <summary>"Use a different font just here": the highlighted words only, no roles.</summary>
    JustHere,
}

/// <summary>
/// Font and size, per style role (PLAN.md M14, taken apart and put back in M20).
/// <para>
/// Deliberately NOT a general style editor — that is a scope trap. It is an assignment sheet:
/// left, the roles this newsletter has, one big row each; right, two controls only. The user never
/// sees <c>body-bold-italic</c>: only BASE styles are listed, labelled through
/// <see cref="StyleLabels"/>, because bold and italic follow their base atomically.
/// </para>
/// <para>
/// <b>M20 changed the chrome and nothing else.</b> M14's engine semantics
/// (<c>SetCharacterStyleFontCommand</c>, the <c>~</c> override, the sibling sweep) are untouched;
/// M14's font gate re-runs byte-for-byte as the proof. What changed is that the window stopped
/// throwing choices away. The rules it now keeps, each closing a numbered defect:
/// </para>
/// <list type="bullet">
/// <item><b>Two jobs, two modes.</b> <see cref="TextStylesMode.JustHere"/> shows no role list, the
/// selection's own words, and the truthful selection-scoped warning — never the whole-newsletter
/// one.</item>
/// <item><b>Category headings are not choices.</b> They are unselectable, unfocusable and skipped
/// by the arrow keys; the category name is carried on each family row's automation name instead,
/// so a screen reader still hears the grouping.</item>
/// <item><b>Nothing is discarded without a word.</b> Apply applies and stays open; switching roles
/// with an unapplied edit applies it first and says so; closing the window applies it; only Cancel
/// throws a choice away, and Cancel says that on the button. Apply with nothing pending explains
/// instead of doing nothing.</item>
/// <item><b>The lists own their scrolling.</b> No fixed pixel heights, no ListBox inside a
/// ScrollViewer, so virtualization and bring-into-view work and large UI fonts do not clip.</item>
/// </list>
/// </summary>
public sealed class TextStylesWindow : Window
{
    private const float PreviewSizePt = 17f;
    private const int RowHeight = 56;

    /// <summary>
    /// Long enough that a slow typist's word costs one rebuild, short enough that the count under
    /// the box still feels like it is answering the keystroke.
    /// </summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

    private readonly TextStylesMode _mode;
    private readonly FontPreviewCache _previews;
    private readonly List<RoleEntry> _roles;
    private readonly ListBox? _roleList;
    private readonly ListBox _fontList;
    private readonly TextBox _search;
    private readonly TextBlock _searchCount;
    private readonly TextBlock _sizeLabel;
    private readonly Image _howItWillLookImage;
    private readonly TextBlock _reflowWarning;
    private readonly TextBlock _status;
    private readonly TextBlock _overrideFooter;
    private readonly Button _showOverrides;
    private readonly Button _clearOverrides;
    private readonly List<FontFamilyInfo> _allFamilies;
    private readonly DispatcherTimer _searchTimer;
    private readonly string _sampleText;
    private readonly uint _foreground;
    private readonly uint _background;

    private List<FontRow> _fontRows = [];
    private int _roleIndex;
    private int _lastFamilyIndex = -1;
    private bool _suppressSelection;
    private bool _cancelled;
    private float _sizePt = 11f;

    /// <summary>
    /// Builds the window.
    /// </summary>
    /// <param name="fonts">The bundled store — previews are drawn with the real faces.</param>
    /// <param name="styles">The document's style sheet.</param>
    /// <param name="initialStyleName">The role to start on, usually the one at the caret.</param>
    /// <param name="sampleText">
    /// The user's own words for "How it will look" — in <see cref="TextStylesMode.JustHere"/> that
    /// is the highlighted words themselves, because those are the only words about to change.
    /// </param>
    /// <param name="overrideCount">How many pieces of text carry a "just here" font.</param>
    /// <param name="darkTheme">Preview colours are parameters, never read from a static.</param>
    /// <param name="mode">Which of the two jobs this window is doing.</param>
    public TextStylesWindow(
        FontStore fonts,
        StyleSheet styles,
        string? initialStyleName,
        string sampleText,
        int overrideCount,
        bool darkTheme,
        TextStylesMode mode = TextStylesMode.Newsletter)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(styles);
        _mode = mode;
        _previews = new FontPreviewCache(fonts);
        _sampleText = string.IsNullOrWhiteSpace(sampleText) ? "The Trestle Board" : sampleText;
        _foreground = darkTheme ? 0xFFF0F0F0u : 0xFF101010u;

        // Transparent, not white (M20 (g)). An opaque white plate behind every preview was
        // illegible the moment the row was selected and wrong in both dark themes; the row's own
        // background — whatever the theme and the selection say it is — now shows through.
        _background = 0x00000000u;

        _roles = BuildRoles(styles, initialStyleName, mode);
        _roleIndex = 0;
        _sizePt = _roles[0].SizePt;

        Title = mode == TextStylesMode.JustHere
            ? "Use a different font just here"
            : "Fonts and text styles";
        Width = mode == TextStylesMode.JustHere ? 660 : 980;
        Height = 720;
        MinWidth = mode == TextStylesMode.JustHere ? 520 : 760;
        MinHeight = 520;
        MaxWidth = 1400;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, Title);

        _search = new TextBox { FontSize = 24, MinHeight = 48, Watermark = "Search for a font" };
        AutomationProperties.SetName(_search, "Search for a font");

        _searchCount = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_searchCount, AutomationLiveSetting.Polite);

        _fontList = new ListBox { FontSize = 16, ItemTemplate = BuildFontRowTemplate() };
        AutomationProperties.SetName(_fontList, "Fonts");
        _fontList.ContainerPrepared += OnFontContainerPrepared;

        _sizeLabel = new TextBlock
        {
            FontSize = 22,
            MinWidth = 90,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        AutomationProperties.SetLiveSetting(_sizeLabel, AutomationLiveSetting.Polite);

        _howItWillLookImage = new Image
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _reflowWarning = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        AutomationProperties.SetLiveSetting(_reflowWarning, AutomationLiveSetting.Polite);

        _status = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        };
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _overrideFooter = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap };
        _showOverrides = SmallButton("Show me");
        _clearOverrides = SmallButton("Put them all back");
        _showOverrides.Click += (_, _) => { ShowOverridesRequested = true; _cancelled = true; Close(); };
        _clearOverrides.Click += (_, _) => { ClearOverridesRequested = true; _cancelled = true; Close(); };
        SetOverrideFooter(mode == TextStylesMode.JustHere ? 0 : overrideCount);

        _allFamilies = BundledFontCatalog.Families.ToList();

        if (mode == TextStylesMode.Newsletter)
        {
            _roleList = new ListBox
            {
                FontSize = 18,
                MinWidth = 330,
                ItemTemplate = BuildRoleRowTemplate(),
                ItemsSource = _roles.ToList(),
            };
            AutomationProperties.SetName(_roleList, "The kinds of writing in this newsletter");
            _roleIndex = Math.Max(0, _roles.FindIndex(r => r.IsStartingRole));
            _roleList.SelectedIndex = _roleIndex;
            _roleList.SelectionChanged += (_, _) => OnRoleSelectionChanged();
        }

        BuildFontRows(string.Empty);
        SelectFontForTest(_roles[_roleIndex].Family);
        _fontList.SelectionChanged += (_, _) => OnFontSelectionChanged();

        _searchTimer = new DispatcherTimer { Interval = SearchDelay };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            BuildFontRows(_search.Text ?? string.Empty);
        };
        _search.TextChanged += (_, _) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        Content = BuildLayout();
        UpdatePreview();
    }

    /// <summary>
    /// Raised every time Apply applies something. The window stays open in
    /// <see cref="TextStylesMode.Newsletter"/>, so one visit can carry more than one change and
    /// the caller has to hear about each of them as it happens.
    /// </summary>
    public event EventHandler<TextStyleChoice>? Applied;

    /// <summary>The last change Apply made, or null when the window changed nothing.</summary>
    public TextStyleChoice? Result { get; private set; }

    /// <summary>The user asked to be shown where the overridden text is.</summary>
    public bool ShowOverridesRequested { get; private set; }

    /// <summary>The user asked to put every overridden piece of text back.</summary>
    public bool ClearOverridesRequested { get; private set; }

    /// <summary>The role rows, in the order the window lists them, for the headless tests.</summary>
    internal IReadOnlyList<string> RoleNamesForTest => _roles.Select(r => r.StyleName).ToList();

    /// <summary>The families currently offered, for the headless tests.</summary>
    internal IReadOnlyList<string> VisibleFontsForTest =>
        _fontRows.Where(r => r.Kind == FontRowKind.Family).Select(r => r.Family!).ToList();

    /// <summary>Every row, headings included, for the "headings are not choices" tests.</summary>
    internal IReadOnlyList<string> FontRowLabelsForTest => _fontRows.Select(r => r.Label).ToList();

    internal IReadOnlyList<int> HeaderIndexesForTest =>
        _fontRows.Select((row, i) => (row, i)).Where(p => p.row.Kind != FontRowKind.Family)
            .Select(p => p.i).ToList();

    internal string? AutomationNameForRowForTest(int index) => _fontRows[index].AutomationName;

    internal string? SelectedFamilyForTest => SelectedFamily;

    internal string? SelectedRoleForTest => _roles[_roleIndex].StyleName;

    internal string StatusForTest => _status.Text ?? string.Empty;

    internal string ReflowWarningForTest => _reflowWarning.Text ?? string.Empty;

    internal string OverrideFooterForTest => _overrideFooter.Text ?? string.Empty;

    internal bool HasPendingChangeForTest => PendingChoice() is not null;

    internal int PreviewCacheHitsForTest => _previews.Hits;

    internal int PreviewCacheMissesForTest => _previews.Misses;

    internal float SizeForTest => _sizePt;

    internal bool ShowsRoleListForTest => _roleList is not null;

    internal void SearchForTest(string text)
    {
        _searchTimer.Stop();
        BuildFontRows(text);
    }

    internal void SelectFontForTest(string family)
    {
        int index = _fontRows.FindIndex(row => row.Kind == FontRowKind.Family && row.Family == family);
        if (index >= 0)
        {
            _fontList.SelectedIndex = index;
        }
    }

    /// <summary>Tries to select a row by index — the way a mouse click or an arrow key would.</summary>
    internal void SelectRowForTest(int index) => _fontList.SelectedIndex = index;

    internal int SelectedRowIndexForTest => _fontList.SelectedIndex;

    internal void SelectRoleForTest(string styleName)
    {
        int index = _roles.FindIndex(r => r.StyleName == styleName);
        if (index >= 0 && _roleList is not null)
        {
            _roleList.SelectedIndex = index;
        }
    }

    internal void StepSizeForTest(int direction) => StepSize(direction);

    internal void ApplyForTest() => Apply();

    internal void CancelForTest()
    {
        _cancelled = true;
        Close();
    }

    /// <summary>The title-bar X: the path that used to lose a pending choice in silence.</summary>
    internal void CloseWithoutAnsweringForTest() => Close();

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing is not an answer, so it must not be read as "throw it away". Cancel, "Show me"
        // and "Put them all back" all say what they do; the X does not, so it applies (M20 (c)).
        if (!_cancelled)
        {
            ApplyPending(announceNothingPending: false);
        }

        base.OnClosing(e);
        if (!e.Cancel)
        {
            // The timer stops; the bitmaps do NOT get disposed here. A render pass can still be
            // queued against the rows this window drew, and a disposed Bitmap under it is a crash
            // in the compositor — the cache is small, bounded by the bundled catalogue, and the
            // collector is the right owner once the window is gone.
            _searchTimer.Stop();
        }
    }

    private static List<RoleEntry> BuildRoles(
        StyleSheet styles, string? initialStyleName, TextStylesMode mode)
    {
        string? role = initialStyleName is null
            ? null
            : StyleOverrides.RoleOf(CharacterStyleResolver.BaseName(initialStyleName));

        if (mode == TextStylesMode.JustHere)
        {
            // "Just here" edits the words, not a kind of writing. The one entry exists so the rest
            // of the window has a baseline to measure a pending change against; it is never listed.
            CharacterStyleDef? at = initialStyleName is null
                ? null
                : styles.CharacterStyles.Find(s => s.Name == initialStyleName);
            CharacterStyleDef? roleDef = role is null
                ? null
                : styles.CharacterStyles.Find(s => s.Name == role);
            CharacterStyleDef def = at ?? roleDef ?? styles.CharacterStyles[0];
            return
            [
                new RoleEntry
                {
                    StyleName = StyleOverrides.RoleOf(CharacterStyleResolver.BaseName(def.Name)),
                    Label = StyleLabels.Describe(def.Name),
                    Family = def.FontFamily,
                    SizePt = def.SizePt,
                    IsStartingRole = true,
                },
            ];
        }

        // Base styles only: "body", never "body-bold-italic". Siblings follow atomically.
        List<CharacterStyleDef> bases = styles.CharacterStyles
            .Where(s => CharacterStyleResolver.BaseName(s.Name) == s.Name
                        && !StyleOverrides.IsOverride(s.Name))
            .GroupBy(s => s.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        // Declared semantic order, not the raw style name's alphabet (M20 (h)). A role this build
        // has never heard of sorts last rather than being dropped, and ties break ordinally so the
        // list is stable whatever a template threw at it.
        return
        [
            .. bases
                .OrderBy(s => StyleLabels.OrderOf(s.Name))
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .Select(s => new RoleEntry
                {
                    StyleName = s.Name,
                    Label = StyleLabels.Describe(s.Name),
                    Family = s.FontFamily,
                    SizePt = s.SizePt,
                    IsStartingRole = s.Name == role,
                }),
        ];
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

    private static void PlaceRow(Control control, int row)
    {
        Grid.SetRow(control, row);
    }

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
        apply.Click += (_, _) => Apply();
        AutomationProperties.SetName(apply, "Apply");

        var cancel = new Button
        {
            Content = "Cancel — change nothing",
            FontSize = 20,
            MinHeight = 48,
            MinWidth = 140,
            IsCancel = true,
        };
        cancel.Click += (_, _) => { _cancelled = true; Close(); };
        AutomationProperties.SetName(cancel, "Cancel — change nothing");

        var smaller = SmallButton("− Smaller");
        var bigger = SmallButton("+ Bigger");
        smaller.Click += (_, _) => StepSize(-1);
        bigger.Click += (_, _) => StepSize(+1);
        string sizeWhat = _mode == TextStylesMode.JustHere
            ? "the writing you have selected"
            : "this kind of writing";
        AutomationProperties.SetName(smaller, $"Make {sizeWhat} smaller");
        AutomationProperties.SetName(bigger, $"Make {sizeWhat} bigger");

        // Rows, not a StackPanel with a fixed-height ScrollViewer in it (M20 (e), (f)): the list
        // gets the star row, owns its own scrolling, and keeps its virtualization.
        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"),
            Margin = new Avalonia.Thickness(_mode == TextStylesMode.JustHere ? 0 : 24, 0, 0, 0),
        };
        AddRow(right, SectionHeading("Font"), 0);
        AddRow(right, _search, 1);
        AddRow(right, _searchCount, 2);
        AddRow(right, _fontList, 3);
        AddRow(right, SectionHeading("Size"), 4);
        AddRow(
            right,
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
                Children = { smaller, _sizeLabel, bigger },
            },
            5);
        AddRow(
            right,
            new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text = "How it will look",
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    },
                    _howItWillLookImage,
                },
            },
            6);
        AddRow(right, _reflowWarning, 7);

        Control body;
        if (_roleList is null)
        {
            body = right;
        }
        else
        {
            var left = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                MinWidth = 330,
            };
            AddRow(
                left,
                new TextBlock
                {
                    Text = "The kinds of writing in this newsletter",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 330,
                    Margin = new Avalonia.Thickness(0, 0, 0, 10),
                },
                0);
            AddRow(left, _roleList, 1);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(left, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            body = grid;
        }

        var footer = new StackPanel { Spacing = 10 };
        if (_mode == TextStylesMode.Newsletter)
        {
            footer.Children.Add(_overrideFooter);
            footer.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { _showOverrides, _clearOverrides },
            });
        }

        footer.Children.Add(new TextBlock
        {
            Text = _mode == TextStylesMode.JustHere
                ? "Nothing changes until you press Apply. Apply then closes this window."
                : "Nothing changes until you press Apply. Apply leaves this window open, so you "
                  + "can change another kind of writing before you close it.",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        });
        footer.Children.Add(_status);
        footer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, apply },
        });

        return new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children = { Bottom(footer), body },
        };
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        PlaceRow(control, row);
        grid.Children.Add(control);
    }

    private static TextBlock SectionHeading(string text) => new()
    {
        Text = text,
        FontSize = 20,
        FontWeight = Avalonia.Media.FontWeight.Bold,
        Margin = new Avalonia.Thickness(0, 8, 0, 4),
    };

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

    // ---- The lists --------------------------------------------------------------------------

    private FuncDataTemplate<RoleEntry> BuildRoleRowTemplate() => new(
        (entry, _) => entry is null ? null : BuildRoleRow(entry),
        supportsRecycling: false);

    private StackPanel BuildRoleRow(RoleEntry entry)
    {
        var stack = new StackPanel
        {
            Spacing = 2,
            MinHeight = RowHeight,
            Margin = new Avalonia.Thickness(4),
        };

        // One label per row (M20 (i)). The rendered-PNG copy of the same words went: it said
        // nothing the text label above it did not, and a screen reader read the row twice.
        stack.Children.Add(new TextBlock
        {
            Text = entry.Label,
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{entry.Family}, {StyleOverrides.Size(entry.SizePt)}",
            FontSize = 14,
        });
        stack.Children.Add(PreviewImage(entry.Family, PreviewSizePt, _sampleText));
        AutomationProperties.SetName(
            stack, $"{entry.Label}, {entry.Family}, {StyleOverrides.Size(entry.SizePt)}");
        return stack;
    }

    private FuncDataTemplate<FontRow> BuildFontRowTemplate() => new(
        (row, _) => row is null ? null : BuildFontRow(row),
        supportsRecycling: false);

    private Control BuildFontRow(FontRow row)
    {
        if (row.Kind == FontRowKind.Header)
        {
            var heading = new TextBlock
            {
                Text = row.Label,
                FontSize = 16,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Margin = new Avalonia.Thickness(0, 10, 0, 2),
                IsHitTestVisible = false,
            };
            AutomationProperties.SetName(heading, row.AutomationName ?? row.Label);
            return heading;
        }

        if (row.Kind == FontRowKind.Unavailable)
        {
            var missing = new TextBlock
            {
                Text = row.Label,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520,
                Margin = new Avalonia.Thickness(0, 10, 0, 6),
                IsHitTestVisible = false,
            };
            AutomationProperties.SetName(missing, row.AutomationName ?? row.Label);
            return missing;
        }

        FontFamilyInfo family = row.Info!;
        var stack = new StackPanel
        {
            Spacing = 2,
            MinHeight = RowHeight,
            Margin = new Avalonia.Thickness(4),
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
        AutomationProperties.SetName(stack, row.AutomationName!);
        return stack;
    }

    /// <summary>
    /// Headings are not choices (M20 (a)). The container is switched off as well as guarded in
    /// <see cref="OnFontSelectionChanged"/>, because a mouse, the arrow keys and a screen reader
    /// each reach a row by a different route and only one of them goes through selection.
    /// </summary>
    private void OnFontContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not ListBoxItem item)
        {
            return;
        }

        bool selectable = e.Index >= 0 && e.Index < _fontRows.Count
            && _fontRows[e.Index].Kind == FontRowKind.Family;
        item.IsEnabled = selectable;
        item.Focusable = selectable;
        item.IsHitTestVisible = selectable;
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

        var rows = new List<FontRow>();
        if (UnavailableFamilyRow(search) is { } missing)
        {
            rows.Add(missing);
        }

        FontCategory? group = null;
        foreach (FontFamilyInfo family in matches)
        {
            string categoryLabel = BundledFontCatalog.CategoryLabel(family.Category);
            if (group != family.Category)
            {
                group = family.Category;
                rows.Add(new FontRow(FontRowKind.Header, categoryLabel, null, categoryLabel));
            }

            rows.Add(new FontRow(
                FontRowKind.Family,
                family.Family,
                family,
                $"{family.Family}. {categoryLabel}. {family.Description}"));
        }

        string? previous = SelectedFamily;
        _fontRows = rows;
        _suppressSelection = true;
        _fontList.ItemsSource = rows;
        _suppressSelection = false;
        _lastFamilyIndex = -1;

        int count = rows.Count(r => r.Kind == FontRowKind.Family);
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
    /// A family the document names and this build does not bundle shows itself, in M14's words,
    /// instead of being silently absent from the list it is supposed to be selected in.
    /// </summary>
    private FontRow? UnavailableFamilyRow(string search)
    {
        string family = _roles[_roleIndex].Family;
        if (BundledFontCatalog.FamilyNames.Contains(family))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(search)
            && !family.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string text = $"{family} — this newsletter asks for this font and this copy of TrestleBoard "
            + $"does not have it. That text is shown in {BundledFonts.BodyFamily} instead. The "
            + "newsletter itself is not changed, and it will save back exactly as it was. Choosing "
            + "a font below replaces it.";
        return new FontRow(FontRowKind.Unavailable, text, null, text);
    }

    // ---- Selection --------------------------------------------------------------------------

    private string? SelectedFamily =>
        _fontList.SelectedItem is FontRow { Kind: FontRowKind.Family } row ? row.Family : null;

    private void OnFontSelectionChanged()
    {
        if (_suppressSelection)
        {
            return;
        }

        int index = _fontList.SelectedIndex;
        if (index >= 0 && index < _fontRows.Count && _fontRows[index].Kind != FontRowKind.Family)
        {
            // The arrow keys land here when a heading is between two families: step past it in the
            // direction of travel and the heading is unreachable without being invisible.
            int direction = _lastFamilyIndex < 0 || index > _lastFamilyIndex ? 1 : -1;
            int? next = NextSelectable(index, direction) ?? NextSelectable(index, -direction);
            _fontList.SelectedIndex = next ?? _lastFamilyIndex;
            return;
        }

        _lastFamilyIndex = index;
        UpdatePreview();
    }

    private int? NextSelectable(int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < _fontRows.Count; i += direction)
        {
            if (_fontRows[i].Kind == FontRowKind.Family)
            {
                return i;
            }
        }

        return null;
    }

    private void OnRoleSelectionChanged()
    {
        if (_roleList is null || _suppressSelection)
        {
            return;
        }

        int index = _roleList.SelectedIndex;
        if (index < 0 || index == _roleIndex)
        {
            return;
        }

        // Switching roles used to be the quietest way to lose work in this window (M20 (c)).
        // An unapplied edit is applied on the way out, and the footer says whose it was.
        string previousLabel = _roles[_roleIndex].Label;
        bool applied = ApplyPending(announceNothingPending: false);

        _roleIndex = index;
        RoleEntry entry = _roles[index];
        _sizePt = entry.SizePt;
        BuildFontRows(_search.Text ?? string.Empty);
        SelectFontForTest(entry.Family);
        UpdatePreview();

        if (applied)
        {
            SetStatus($"Your change to {previousLabel} was applied before moving on — nothing was "
                      + $"lost. You are now changing {entry.Label}.");
        }
    }

    // ---- Size, preview, Apply -----------------------------------------------------------------

    private void StepSize(int direction)
    {
        float? next = FontSizeLadder.Step(_sizePt, direction);
        if (next is null)
        {
            SetStatus(direction > 0
                ? "This writing is already as large as TrestleBoard goes."
                : "This writing is already as small as TrestleBoard goes.");
            return;
        }

        _sizePt = next.Value;
        UpdatePreview();
    }

    /// <summary>
    /// A rasterised line, shrunk to fit rather than cropped. An <c>Image</c> draws an unstretched
    /// bitmap CENTRED in its slot whatever its alignment says, so before M20 a preview wider than
    /// the column lost a letter off each end — "Brethren" read as "rethren". DownOnly keeps the
    /// 1.5x rasterisation crisp at every size that fits, and only scales the ones that do not.
    /// </summary>
    private Image PreviewImage(string family, float sizePt, string text) => new()
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.DownOnly,
        HorizontalAlignment = HorizontalAlignment.Left,
        Source = _previews.Get(family, sizePt, text, _foreground, _background),
        [AutomationProperties.NameProperty] = text,
    };

    private void UpdatePreview()
    {
        _sizeLabel.Text = StyleOverrides.Size(_sizePt);
        AutomationProperties.SetName(_sizeLabel, $"Size {StyleOverrides.Size(_sizePt)}");

        string family = SelectedFamily ?? _roles[_roleIndex].Family;
        _howItWillLookImage.Source = _previews.Get(
            family, _sizePt, _sampleText, _foreground, _background);
        AutomationProperties.SetName(_howItWillLookImage, _sampleText);

        // The warning is not optional: line height comes from font metrics and the minimum wrap
        // segment is 4 x the average character width, so a font or size change can repaginate.
        // What IS optional is which warning — and "just here" was showing the wrong one (M20 (d)).
        _reflowWarning.Text = _mode == TextStylesMode.JustHere
            ? "This changes only the writing you have selected. Those words may move to different "
              + "lines. Ctrl+Z puts it back in one step."
            : $"This changes every piece of writing that uses {_roles[_roleIndex].Label}. The words "
              + "may move to different lines, and the newsletter may end up with a different number "
              + "of pages. Ctrl+Z puts it back in one step.";
    }

    private TextStyleChoice? PendingChoice()
    {
        RoleEntry entry = _roles[_roleIndex];
        string? family = SelectedFamily is { } chosen
            && !string.Equals(chosen, entry.Family, StringComparison.Ordinal)
                ? chosen
                : null;
        float? size = Math.Abs(_sizePt - entry.SizePt) >= 0.01f ? _sizePt : null;
        return family is null && size is null ? null : new TextStyleChoice(entry.StyleName, family, size);
    }

    private void Apply()
    {
        if (!ApplyPending(announceNothingPending: true))
        {
            return;
        }

        if (_mode == TextStylesMode.JustHere)
        {
            // One selection, one answer: there is no second thing to do in this mode.
            _cancelled = true;
            Close();
        }
    }

    /// <summary>Applies whatever is pending. Returns false when there was nothing to apply.</summary>
    private bool ApplyPending(bool announceNothingPending)
    {
        if (PendingChoice() is not { } choice)
        {
            if (announceNothingPending)
            {
                // Apply used to be a silent no-op here (M20 (b)) — indistinguishable, to an
                // elderly user, from an app that had stopped responding.
                SetStatus("Nothing to change yet — pick a different font or size first.");
            }

            return false;
        }

        RoleEntry entry = _roles[_roleIndex];
        entry.Family = choice.FontFamily ?? entry.Family;
        entry.SizePt = choice.SizePt ?? entry.SizePt;
        Result = choice;
        Applied?.Invoke(this, choice);

        if (_mode == TextStylesMode.Newsletter)
        {
            RefreshRoleRows();
            SetStatus($"{entry.Label} now uses {entry.Family} at {StyleOverrides.Size(entry.SizePt)}.");
        }

        return true;
    }

    private void RefreshRoleRows()
    {
        if (_roleList is null)
        {
            return;
        }

        _suppressSelection = true;
        _roleList.ItemsSource = _roles.ToList();
        _roleList.SelectedIndex = _roleIndex;
        _suppressSelection = false;
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        AutomationProperties.SetName(_status, text);
    }

    private enum FontRowKind
    {
        Header,
        Family,
        Unavailable,
    }

    private sealed record FontRow(
        FontRowKind Kind, string Label, FontFamilyInfo? Info, string? AutomationName)
    {
        public string? Family => Info?.Family;
    }

    private sealed class RoleEntry
    {
        public required string StyleName { get; init; }

        public required string Label { get; init; }

        public required string Family { get; set; }

        public required float SizePt { get; set; }

        public bool IsStartingRole { get; init; }
    }
}
