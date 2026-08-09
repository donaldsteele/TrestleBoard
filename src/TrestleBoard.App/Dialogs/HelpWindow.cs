using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Help;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "How do I…?" (PLAN.md §11 M63): a search box over everything the app can do.
///
/// <para><b>Not modal</b>, for the reason <see cref="ReviewWindow"/> gives: "Take me there" would be
/// a lie if the user were not allowed to touch the thing it took them to. Help that locks the app
/// while you read it is help you have to memorise first.</para>
///
/// <para>The window holds no help text of its own. <see cref="HelpIndex"/> generated every answer
/// from the action catalog before this window existed, and the three things handed in — the menu
/// paths, a way to ask whether a command can run, and a way to run it — are the only ways it can
/// learn anything or change anything. That is what makes the help unable to drift from the app:
/// there is nowhere for a stale sentence to live.</para>
/// </summary>
public sealed class HelpWindow : Window
{
    private readonly IReadOnlyDictionary<string, string> _menuPaths;
    private readonly Func<string, ActionAvailability> _ask;
    private readonly Func<string, Task> _run;

    private readonly TextBox _search;
    private readonly TextBlock _count;
    private readonly ListBox _list;
    private readonly TextBlock _answerTitle;
    private readonly TextBlock _answer;
    private readonly TextBlock _whereItIs;
    private readonly Button _takeMeThere;

    private IReadOnlyList<HelpTopic> _showing = [];

    public HelpWindow(
        IReadOnlyDictionary<string, string> menuPaths,
        Func<string, ActionAvailability> ask,
        Func<string, Task> run)
    {
        _menuPaths = menuPaths ?? throw new ArgumentNullException(nameof(menuPaths));
        _ask = ask ?? throw new ArgumentNullException(nameof(ask));
        _run = run ?? throw new ArgumentNullException(nameof(run));

        Title = "How do I…?";
        Width = 720;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = "How do I…?",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        };

        _search = new TextBox
        {
            FontSize = 24,
            MinHeight = 52,
            Watermark = "Type what you are trying to do",
        };
        AutomationProperties.SetName(_search, "Type what you are trying to do");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Look();
            }
        };

        // The hint is real text rather than placeholder-only, because a watermark disappears the
        // moment somebody types and a screen reader may never have announced it at all.
        var hint = new TextBlock
        {
            Text = "Use your own words — \"picture\", \"email it\", \"the writing is too small\". "
                + "Leave the box empty to see everything TrestleBoard can do.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        };

        _count = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_count, "How many answers were found");
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);

        _list = new ListBox { FontSize = 20, MinHeight = 200 };
        AutomationProperties.SetName(_list, "The things TrestleBoard can do");
        _list.SelectionChanged += (_, _) => ShowAnswer();

        _answerTitle = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        };
        _answer = new TextBlock { FontSize = 20, TextWrapping = TextWrapping.Wrap };
        _whereItIs = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };

        _takeMeThere = Wide("Take me there", "Take me there");
        _takeMeThere.Click += async (_, _) => await TakeThemThere();

        Button close = Wide("Close", "Close");
        close.IsCancel = true;
        close.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Spacing = 12,
                    Children = { heading, _search, hint, _count },
                },
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Spacing = 12,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children =
                    {
                        _answerTitle,
                        _answer,
                        _whereItIs,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Children = { _takeMeThere, close },
                        },
                    },
                },
                new ScrollViewer { Content = _list, Margin = new Avalonia.Thickness(0, 16, 0, 0) },
            },
        };

        KeyDown += OnKeyDown;
        Opened += (_, _) => _search.Focus();
        Look();
    }

    internal TextBox SearchBoxForTest => _search;

    internal ListBox ListForTest => _list;

    internal string CountTextForTest => _count.Text ?? "";

    internal string AnswerTextForTest => _answer.Text ?? "";

    internal string WhereItIsTextForTest => _whereItIs.Text ?? "";

    internal Button TakeMeThereForTest => _takeMeThere;

    internal IReadOnlyList<HelpTopic> ShowingForTest => _showing;

    internal void TypeForTest(string what) => _search.Text = what;

    internal void ChooseForTest(int index) => _list.SelectedIndex = index;

    /// <summary>Runs the search and rebuilds the list. Called on every keystroke.</summary>
    private void Look()
    {
        _showing = HelpIndex.Search(_search.Text);

        _list.ItemsSource = _showing.Select(Describe).ToList();
        _count.Text = _showing.Count switch
        {
            0 => "Nothing matched those words. Try a different one — or empty the box to see everything.",
            1 => "One thing matched.",
            _ when string.IsNullOrWhiteSpace(_search.Text) =>
                $"Everything TrestleBoard can do — {_showing.Count} things.",
            _ => $"{_showing.Count} things matched.",
        };

        // Choosing the best answer for them is the whole value of ranking it: this audience reads
        // the first answer as the answer, so it may as well already be open.
        _list.SelectedIndex = _showing.Count > 0 ? 0 : -1;
        ShowAnswer();
    }

    /// <summary>
    /// One line per answer: what the command is called, and which part of the app it belongs to.
    /// The group is there because "Bold" alone does not tell somebody they are in the right place.
    /// </summary>
    private static string Describe(HelpTopic topic) =>
        $"{topic.Title.TrimEnd('…', ' ')}  —  {topic.GroupLabel}";

    private void ShowAnswer()
    {
        HelpTopic? topic = Chosen();
        if (topic is null)
        {
            _answerTitle.Text = "";
            _answer.Text = "";
            _whereItIs.Text = "";
            _takeMeThere.IsVisible = false;
            return;
        }

        _answerTitle.Text = topic.Title.TrimEnd('…', ' ');
        _answer.Text = topic.Answer;
        _whereItIs.Text = WhereToFindIt(topic);

        // The window renames itself so a screen reader announces which answer is open; there is no
        // live region for a whole panel (the WizardWindow technique, docs/M7-spec.md §6.6).
        AutomationProperties.SetName(this, $"How do I…? — {_answerTitle.Text}");

        ActionAvailability can = _ask(topic.ActionId);
        _takeMeThere.IsVisible = can.IsAvailable;
        if (!can.IsAvailable)
        {
            // M11's rule, kept here too: a thing that cannot be done says why, in the same words the
            // menu bar and the action panel would use. Silence would read as "the help is broken".
            _whereItIs.Text += Environment.NewLine + Environment.NewLine + can.Reason;
        }
    }

    /// <summary>
    /// Where the command lives and how to reach it without the mouse. Both come from the app
    /// itself — the path is read off the menu bar, the shortcut off the catalog — so neither can
    /// describe a version of TrestleBoard that no longer exists.
    /// </summary>
    private string WhereToFindIt(HelpTopic topic)
    {
        var parts = new List<string>();
        if (_menuPaths.TryGetValue(topic.ActionId, out string? path))
        {
            parts.Add("You will find it at " + path + ".");
        }

        if (topic.Shortcut is { Length: > 0 } gesture)
        {
            parts.Add($"The keyboard way is {gesture}.");
        }

        return parts.Count == 0
            ? "This one is not in the menus."
            : string.Join(" ", parts);
    }

    private HelpTopic? Chosen() =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _showing.Count
            ? _showing[_list.SelectedIndex]
            : null;

    private async Task TakeThemThere()
    {
        if (Chosen() is not { } topic || !_ask(topic.ActionId).IsAvailable)
        {
            return;
        }

        // The help window stays open behind it. Somebody who asked how to do a thing is very often
        // about to ask how to do the next thing, and making them find this window again each time
        // is the small cruelty that stops people using help at all.
        await _run(topic.ActionId);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static Button Wide(string content, string automationName)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(button, automationName);
        return button.Action();
    }
}
