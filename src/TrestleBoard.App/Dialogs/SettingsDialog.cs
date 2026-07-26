using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Settings;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Look and size (PLAN.md §6). Two settings only, both of which some people genuinely need to use
/// the app at all — so they are plainly worded, previewed live, and reachable from the keyboard
/// like everything else.
/// </summary>
public sealed class SettingsDialog : Window
{
    private readonly ComboBox _theme;
    private readonly Slider _scale;
    private readonly TextBlock _scaleLabel;
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
        };
        AutomationProperties.SetName(_scale, "How big the buttons and menus are");

        _scaleLabel = new TextBlock { FontSize = 20, MinWidth = 80 };
        _preview = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 540,
        };

        _scale.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
            {
                UpdatePreview();
            }
        };
        _theme.SelectionChanged += (_, _) => UpdatePreview();
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

    public AppSettings Result => new AppSettings
    {
        Theme = Themes[Math.Max(0, _theme.SelectedIndex)].Choice,
        UiScalePercent = (int)_scale.Value,
    }.Normalised();

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

        _preview.Text = themeNote
            + $"Menus and buttons will be {percent}% of their normal size. "
            + "The newsletter page itself always stays white, because that is how it will print.";
    }
}
