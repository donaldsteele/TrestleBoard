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

    /// <summary>M57: one of the committee's own saved layouts.</summary>
    MyTemplate,

    /// <summary>
    /// M76 (h): one of the newsletters offered back by name on the start screen. Which one is in
    /// <see cref="StartDialog.SelectedRecentPath"/>, and the shell opens it by the ordinary open
    /// path — this is a shortcut past the file dialog, not a second way of loading a newsletter.
    /// </summary>
    RecentFile,
}

/// <summary>
/// The start screen (PLAN.md §7): three large tiles, "Start from last month" first because it is
/// what the committee does eleven months out of twelve. Big targets, 20pt+ text, every tile a real
/// button so the whole screen is one Tab cycle.
/// </summary>
public sealed class StartDialog : Window
{
    public StartDialog(bool canStartFromLastMonth)
        : this(canStartFromLastMonth, [])
    {
    }

    internal StartDialog(bool canStartFromLastMonth, IReadOnlyList<Integration.UserTemplate> mine)
        : this(
            canStartFromLastMonth,
            mine,
            Integration.PastIssues.Recent(Settings.AppSettings.Load().OldIssuesFolder))
    {
    }

    /// <param name="recent">The newsletters to offer back by name (M76 (h)). Passed in rather than
    /// looked up, so a test — and the screenshot harness — can say exactly what the screen shows;
    /// the constructor above is the live one, and it asks <c>PastIssues</c> itself.</param>
    internal StartDialog(
        bool canStartFromLastMonth,
        IReadOnlyList<Integration.UserTemplate> mine,
        IReadOnlyList<Integration.RecentIssue> recent)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(recent);

        Title = "TrestleBoard";
        MinWidth = 760;
        MinHeight = 520;
        Width = 800;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetName(this, "Start a newsletter");

        var tiles = new StackPanel { Spacing = 16 };

        // M57. The committee's own layouts go ABOVE the three built-ins, because a lodge that has
        // saved one is a lodge that means to use it — and below the three big verbs, because
        // "start from last month" is still what eleven months out of twelve look like.
        var mineTiles = new StackPanel { Spacing = 12, IsVisible = mine.Count > 0 };
        var mineHeading = new TextBlock
        {
            Text = "My templates",
            FontSize = 18,
            IsVisible = mine.Count > 0,
        };

        foreach (Integration.UserTemplate template in mine)
        {
            Integration.UserTemplate chosen = template;
            Button tile = Tile(
                template.Name,
                "One of your own saved layouts.",
                StartChoice.MyTemplate,
                primary: false,
                enabled: true);
            tile.Click += (_, _) => SelectedUserTemplateId = chosen.Id;
            mineTiles.Children.Add(tile);
        }

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

        // M76 (h), spec §7. The picker used to sit below all three tiles, so the one control on
        // this screen that modifies an offer stood nowhere near the offer it modifies: somebody who
        // pressed "Start from a template" had no reason to connect it with a combo further down the
        // window. It is now grouped WITH that tile, inside one bordered card, so the offer and the
        // choice that shapes it are one thing to look at and one thing to Tab through.
        //
        // The combo sits beside the tile rather than inside the button's own content, on purpose: a
        // ComboBox nested in a Button is a keyboard trap waiting to happen — Space and Enter belong
        // to the dropdown while it is open and to the button the rest of the time. Grouped like
        // this, Tab reaches the tile and then the picker, in the order they are read.
        tiles.Children.Add(new Border
        {
            Padding = new Avalonia.Thickness(12),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Tile(
                        "Start from a template",
                        "Begin with a ready-made layout you can fill in.",
                        StartChoice.Template,
                        primary: false,
                        enabled: true),
                    new TextBlock { Text = "Template", FontSize = 18 },
                    templates,
                },
            },
        }
            .Token(Border.BorderBrushProperty, Tokens.ChromeBorder)

            // The THICKNESS is a token as well as the colour. It steps from 1 to 2 in High
            // Contrast, which is the non-colour half of "colour is never the only signal" — and
            // this border is the only thing bounding the group, because the card has no fill of its
            // own to fall back on. A literal 1 here would have left it the one hairline boundary on
            // the start screen while every rule and panel edge around it doubled.
            .Token(Border.BorderThicknessProperty, Tokens.BorderThickness));

        var page = new StackPanel
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
                    // M76 (c). Cinzel is engraved Roman lapidary lettering, which is what the title
                    // of a printed trestle board has always been set in. This is its ONLY use in the
                    // application — the wordmark and nothing else; everything around it is the
                    // Source Sans 3 the TopLevel rule in Theme/Controls.axaml sets.
                    // $Default second for the reason written against the TopLevel rule in
                    // Theme/Controls.axaml: a family Avalonia cannot produce throws out of the
                    // render pass rather than degrading, and a wordmark is never worth a window.
                    FontFamily = FontFamily.Parse(
                        "avares://TrestleBoard.App/Theme/Fonts#Cinzel, $Default"),
                },
                new TextBlock
                {
                    Text = "What would you like to do?",
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap,
                },
                tiles,
                mineHeading,
                mineTiles,
            },
        };

        // M76 (h). This committee reopens the same newsletter every evening for a week and, until
        // now, walked a file dialog to do it each time — the kind of cost §6 exists to remove. The
        // list is short, every entry is a full-width labelled button carrying the file's own plain
        // name and the date it was last saved, and there is no icon anywhere near it (§6: an icon
        // never appears without its text label, and a bare file path is not a label a person reads).
        //
        // WHEN THERE IS NOTHING TO OFFER, NOTHING IS BUILT — not an empty box, and certainly not a
        // greyed one. A first run must not open on a shape that says something is missing.
        if (recent.Count > 0)
        {
            page.Children.Add(new TextBlock
            {
                Text = "Newsletters you worked on recently",
                FontSize = 18,
            });

            var recentTiles = new StackPanel { Spacing = 8 };
            foreach (Integration.RecentIssue issue in recent)
            {
                recentTiles.Children.Add(RecentTile(issue));
            }

            page.Children.Add(recentTiles);
            _recentList = recentTiles;
        }

        Content = new ScrollViewer { Content = page };

        // M48, review §14.2: this window had no Escape path at all. Every other dialog in the app
        // closes on Esc, and the one a user meets FIRST did not — so somebody who opened it by
        // accident, or wanted to look at an empty window, had no way out but the mouse.
        //
        // StartChoice.Nothing is already what the shell treats as "they did not choose", so leaving
        // by Esc lands in a state the caller has always handled.
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                Choice = StartChoice.Nothing;
                e.Handled = true;
                Close();
            }
        };
    }

    private readonly ComboBox _templates;

    private readonly StackPanel? _recentList;

    public StartChoice Choice { get; private set; } = StartChoice.Nothing;

    /// <summary>Which of the user's own templates was pressed, when <see cref="Choice"/> says so.</summary>
    internal string? SelectedUserTemplateId { get; private set; }

    /// <summary>
    /// Which recently-saved newsletter was pressed, when <see cref="Choice"/> is
    /// <see cref="StartChoice.RecentFile"/> (M76 (h)); null otherwise.
    /// </summary>
    internal string? SelectedRecentPath { get; private set; }

    /// <summary>The recent-newsletter buttons, or null when the screen offered none (M76 (h)).</summary>
    internal StackPanel? RecentListForTest => _recentList;

    /// <summary>The template the user had selected, whether or not they chose that tile.</summary>
    public string SelectedTemplateId =>
        TemplateLibrary.All[Math.Max(0, _templates.SelectedIndex)].Id;

    /// <summary>
    /// One recently-saved newsletter, offered back by name (M76 (h)).
    ///
    /// <para>Full width and 44px at least, per §6; the action treatment so it reads as a button the
    /// app made; and two lines, because one is not enough to tell two newsletters apart. The name
    /// is the file's own — for anything this application saved that is "Trestle Board 2026-07", so
    /// it says which issue it is — and under it the date it was last saved.</para>
    /// </summary>
    private Button RecentTile(Integration.RecentIssue issue)
    {
        var detailLine = new TextBlock
        {
            Text = issue.Detail,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        };
        detailLine.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);

        var button = new Button
        {
            FontSize = 18,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(16, 10),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = issue.Name, FontSize = 18, TextWrapping = TextWrapping.Wrap },
                    detailLine,
                },
            },
        };

        // M37: an app-made button carries the action treatment or it reads as disabled — and
        // ActionSurfaceTests walks every window to make sure of it. Nothing here is ever greyed: a
        // newsletter that has been moved since it was listed is still offered, and the ordinary
        // open path says so in the M25 sentence if it will not open.
        button.Action();

        AutomationProperties.SetName(button, issue.Name);
        AutomationProperties.SetHelpText(button, issue.Detail);

        button.Click += (_, _) =>
        {
            SelectedRecentPath = issue.Path;
            Choice = StartChoice.RecentFile;
            Close();
        };

        return button;
    }

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

        // M37. These three tiles are what the review pointed at first: on the start screen, "Open a
        // newsletter" and "Start from a template" were flat grey slabs beside the navy primary one,
        // and read as disabled — the very first thing a new user sees, offering them two choices
        // that look like they are not available. The primary tile has IsDefault and takes its own
        // treatment; the other two take the border.
        if (!primary)
        {
            button.Action();
        }

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
