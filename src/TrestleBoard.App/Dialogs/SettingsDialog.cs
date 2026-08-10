using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Settings;
using TrestleBoard.App.Theme;
using TrestleBoard.Core.Phrases;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Look and size (PLAN.md §6), and from M54 the one word of the app's writing that is the lodge's
/// rather than ours. All of it plainly worded, previewed live, and reachable from the keyboard like
/// everything else — the first two settings are ones some people genuinely need to use the app at
/// all, and the third is one no lodge but Indian Land 414 would get right by default.
/// </summary>
public sealed class SettingsDialog : Window
{
    private readonly AppSettings _current;
    private readonly ComboBox _theme;
    private readonly Slider _scale;
    private readonly TextBlock _scaleLabel;
    private readonly TextBox _sicknessOffice;
    private readonly TextBlock _preview;

    private static readonly (ThemeChoice Choice, string Label)[] Themes =
    [
        (ThemeChoice.System, "Follow my computer"),
        (ThemeChoice.Light, "Light"),
        (ThemeChoice.Dark, "Dark"),
        (ThemeChoice.HighContrast, "High contrast"),
    ];

    public SettingsDialog(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _current = current;

        Title = "How things look";
        SizeToContent = SizeToContent.Height;
        Width = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        AutomationProperties.SetName(this, "How things look");

        _theme = new ComboBox
        {
            FontSize = 20,
            MinHeight = 44,
            Width = 380,
            ItemsSource = Themes.Select(t => t.Label).ToList(),
            SelectedIndex = Array.FindIndex(Themes, t => t.Choice == current.Theme),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(_theme, "Colours");

        _scale = new Slider
        {
            Minimum = AppSettings.MinScalePercent,
            Maximum = AppSettings.MaxScalePercent,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            Value = current.UiScalePercent,
            Width = 380,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Left,

            // M42, review §14.4: the slider showed only where it currently was, so neither end
            // meant anything. Ticks give the travel a shape, and the two labels below say what the
            // ends ARE - in words about the buttons and menus, not about percentages.
            TickPlacement = TickPlacement.Outside,
        };
        AutomationProperties.SetName(_scale, "How big the buttons and menus are");

        _scaleLabel = new TextBlock { FontSize = 20, MinWidth = 80 };

        // M54, the owner's ruling of 2026-08-09. Free text rather than a list of offices: a list
        // would be safer to render and wrong for the first lodge whose answer nobody here thought
        // of. Left empty it goes back to Secretary — the sentence has to name somebody.
        _sicknessOffice = new TextBox
        {
            Text = current.SicknessContactOffice,
            FontSize = 20,
            MinHeight = 44,
            Width = 380,
            HorizontalAlignment = HorizontalAlignment.Left,
            Watermark = PhraseLibrary.DefaultOffice,
        };
        AutomationProperties.SetName(
            _sicknessOffice, "Who should members speak to about sickness and distress?");

        _preview = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 540,
        };

        // M70(f): this sentence is the only place the app says in words what the two choices above
        // will actually do, and it is the one thing a screen-reader user cannot check by looking
        // before pressing Save. A polite live region, so it is re-read every time either choice
        // changes it. Its name is the sentence itself, kept in step in UpdatePreview — a fixed name
        // would REPLACE the text a screen reader reads rather than introduce it, which is the trap
        // in naming a TextBlock whose whole value is its content.
        AutomationProperties.SetLiveSetting(_preview, AutomationLiveSetting.Polite);

        _scale.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                UpdatePreview();
            }
        };
        _theme.SelectionChanged += (_, _) => UpdatePreview();
        _sicknessOffice.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        var save = new Button
        {
            Content = "Use these settings",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 220,
            IsDefault = true,
        };
        save.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };
        AutomationProperties.SetName(save, "Use these settings");

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 140,
            IsCancel = true,
        };
        cancel.Action();
        cancel.Click += (_, _) => Close();
        AutomationProperties.SetName(cancel, "Cancel");

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Colours", FontSize = 20, FontWeight = FontWeight.Bold },
                _theme,
                new TextBlock { Text = "How big the buttons and menus are", FontSize = 20, FontWeight = FontWeight.Bold },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children = { _scale, _scaleLabel },
                },
                new Grid
                {
                    Width = 380,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    Children =
                    {
                        EndLabel($"Normal ({AppSettings.MinScalePercent}%)", 0, HorizontalAlignment.Left),
                        EndLabel($"Twice as big ({AppSettings.MaxScalePercent}%)", 1, HorizontalAlignment.Right),
                    },
                },
                new TextBlock
                {
                    Text = "Who should members speak to about sickness and distress?",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                },
                _sicknessOffice,
                new TextBlock
                {
                    Text = "The office, not a person's name — it goes into the sickness and "
                        + "distress words under Insert, Words for hard news. If you leave it "
                        + $"empty, it goes back to {PhraseLibrary.DefaultOffice}.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                },
                _preview,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, save },
                },
            },
        };
    }

    public bool Confirmed { get; private set; }

    /// <summary>
    /// The settings that were brought in, with the three this window can change put back — rather
    /// than a fresh <see cref="AppSettings"/>, which would take everything the window does not show
    /// back to its default.
    /// </summary>
    public AppSettings Result => (_current with
    {
        Theme = Themes[Math.Max(0, _theme.SelectedIndex)].Choice,
        UiScalePercent = (int)_scale.Value,
        SicknessContactOffice = OfficeAsTyped,
    }).Normalised();

    /// <summary>Empty means "the default", which <see cref="AppSettings.Normalised"/> puts back.</summary>
    private string OfficeAsTyped => _sicknessOffice.Text ?? "";

    /// <summary>
    /// One end of the size slider - what "all the way left" and "all the way right" actually mean,
    /// which the slider itself never said.
    /// </summary>
    private static TextBlock EndLabel(string text, int column, HorizontalAlignment side)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 16,
            HorizontalAlignment = side,
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private void UpdatePreview()
    {
        int percent = (int)_scale.Value;
        _scaleLabel.Text = $"{percent}%";

        // Says what the setting does to the thing the user cares about, not what it does internally.
        string themeNote = Themes[Math.Max(0, _theme.SelectedIndex)].Choice switch
        {
            ThemeChoice.HighContrast => "Black background, white text, thick outlines. ",
            ThemeChoice.Dark => "Dark background, light text. ",
            ThemeChoice.Light => "Light background, dark text. ",
            _ => "Matches whatever your computer is set to. ",
        };

        string office = string.IsNullOrWhiteSpace(OfficeAsTyped)
            ? PhraseLibrary.DefaultOffice
            : OfficeAsTyped.Trim();

        _preview.Text = themeNote
            + $"Menus and buttons will be {percent}% of their normal size. "
            + "The newsletter page itself always stays white, because that is how it will print. "
            + $"The sickness and distress words will say to speak to the {office}.";
        AutomationProperties.SetName(_preview, _preview.Text);
    }
}
