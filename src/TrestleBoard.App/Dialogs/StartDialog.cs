using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Core.Templates;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user picked on the start screen.</summary>
public enum StartChoice
{
    Nothing,
    LastMonth,
    OpenFile,
    Template,
}

/// <summary>
/// The start screen (PLAN.md §7): three large tiles, "Start from last month" first because it is
/// what the committee does eleven months out of twelve. Big targets, 20pt+ text, every tile a real
/// button so the whole screen is one Tab cycle.
/// </summary>
public sealed class StartDialog : Window
{
    public StartDialog(bool canStartFromLastMonth)
    {
        Title = "TrestleBoard";
        MinWidth = 760;
        MinHeight = 520;
        Width = 800;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetName(this, "Start a newsletter");

        var tiles = new StackPanel { Spacing = 16 };

        tiles.Children.Add(Tile(
            "Start from last month",
            canStartFromLastMonth
                ? "Carries the officers, committees and district table across, bumps the date, and "
                  + "clears last month's articles so you can write this month's."
                : "Open last month's newsletter first, then this will carry it forward.",
            StartChoice.LastMonth,
            primary: true,
            enabled: canStartFromLastMonth));

        tiles.Children.Add(Tile(
            "Open a newsletter",
            "Open a newsletter you already have.",
            StartChoice.OpenFile,
            primary: false,
            enabled: true));

        tiles.Children.Add(Tile(
            "Start from a template",
            "Begin with a ready-made layout you can fill in.",
            StartChoice.Template,
            primary: false,
            enabled: true));

        var templates = new ComboBox
        {
            FontSize = 18,
            MinHeight = 44,
            Width = 420,
            ItemsSource = TemplateLibrary.All.Select(t => t.DisplayName).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(templates, "Which template");
        _templates = templates;

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(32),
                Spacing = 20,
                Children =
                {
                    new TextBlock
                    {
                        Text = "TrestleBoard",
                        FontSize = 30,
                        FontWeight = FontWeight.Bold,
                    },
                    new TextBlock
                    {
                        Text = "What would you like to do?",
                        FontSize = 20,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    tiles,
                    new TextBlock { Text = "Template", FontSize = 18 },
                    templates,
                },
            },
        };
    }

    private readonly ComboBox _templates;

    public StartChoice Choice { get; private set; } = StartChoice.Nothing;

    /// <summary>The template the user had selected, whether or not they chose that tile.</summary>
    public string SelectedTemplateId =>
        TemplateLibrary.All[Math.Max(0, _templates.SelectedIndex)].Id;

    private Button Tile(string heading, string detail, StartChoice choice, bool primary, bool enabled)
    {
        var detailLine = new TextBlock
        {
            Text = detail,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Muted only on a tile that is NOT the primary one. On the accent fill the muted token is
        // 1.66:1 in Light and 1.00:1 in Dark — the live-tree contrast walk caught exactly that the
        // day default buttons started taking the accent. There the detail inherits the button's own
        // foreground and the size difference alone carries the hierarchy.
        if (!primary)
        {
            detailLine.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);
        }

        var button = new Button
        {
            FontSize = 20,
            MinHeight = 96,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsDefault = primary,
            IsEnabled = enabled,
            Padding = new Avalonia.Thickness(20, 14),
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = heading,
                        FontSize = 22,
                        FontWeight = primary ? FontWeight.Bold : FontWeight.Normal,
                    },
                    detailLine,
                },
            },
        };

        // The heading alone is the accessible name; the detail is help text, so a screen reader
        // announces "Start from last month, button" rather than a paragraph.
        AutomationProperties.SetName(button, heading);
        AutomationProperties.SetHelpText(button, detail);

        button.Click += (_, _) =>
        {
            Choice = choice;
            Close();
        };

        return button;
    }
}
