using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Find, and find-and-replace, in one small window (PLAN.md §11 M21).
///
/// <para><b>It is deliberately not modal.</b> Every other dialog in this app is, because every other
/// one asks a question and goes away; this one exists to point at something on the page behind it,
/// and a modal window pointing at a page the user is not allowed to touch would be useless. That is
/// also why M21 had to make the text session survive losing focus first — before that, opening this
/// window threw away the caret it was about to move.</para>
///
/// <para>One class, two modes, the way M20's font window is one class in two modes: the replace half
/// is shown or hidden. Two windows would have kept two copies of the search box, the match-case tick
/// and the messages, and those are exactly the parts that must not drift apart.</para>
/// </summary>
public sealed class FindWindow : Window
{
    private readonly FindController _find;
    private readonly TextBox _search;
    private readonly TextBox _replacement;
    private readonly CheckBox _matchCase;
    private readonly TextBlock _message;
    private readonly StackPanel _replaceRow;
    private readonly Button _replace;
    private readonly Button _replaceAll;

    public FindWindow(FindController find)
    {
        _find = find ?? throw new ArgumentNullException(nameof(find));

        Title = "Find";
        SizeToContent = SizeToContent.Height;
        Width = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _search = MakeBox("What are you looking for?", find.SearchText);
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                FindNext();
                e.Handled = true;
            }
        };

        _replacement = MakeBox("What should go there instead?", find.ReplacementText);

        _matchCase = new CheckBox
        {
            Content = "Capital letters have to match",
            FontSize = 16,
            MinHeight = 44,
            IsChecked = find.MatchCase,
        };
        Avalonia.Automation.AutomationProperties.SetName(_matchCase, "Capital letters have to match");
        _matchCase.IsCheckedChanged += (_, _) =>
        {
            _find.MatchCase = _matchCase.IsChecked == true;

            // A different question deserves a fresh answer: carrying the old hit forward would
            // make the next "Find next" skip a match the new setting has just brought into scope.
            _find.Restart();
        };

        _message = new TextBlock
        {
            Text = "",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
        }.Token(TextBlock.ForegroundProperty, Tokens.ChromeForeground);
        Avalonia.Automation.AutomationProperties.SetName(_message, "What the search found");
        Avalonia.Automation.AutomationProperties.SetLiveSetting(
            _message, Avalonia.Automation.AutomationLiveSetting.Polite);

        var findNext = new Button
        {
            Content = "Find the next one",
            FontSize = 18,
            MinHeight = 48,
            MinWidth = 200,
            IsDefault = true,
        }.Primary();
        Avalonia.Automation.AutomationProperties.SetName(findNext, "Find the next one");
        findNext.Click += (_, _) => FindNext();

        _replace = MakeButton("Replace this one", () =>
        {
            PushBoxes();
            _find.ReplaceCurrent();
            ShowMessage();
        });

        _replaceAll = MakeButton("Replace every one", () =>
        {
            PushBoxes();
            _find.ReplaceAll();
            ShowMessage();
        });

        var close = new Button
        {
            Content = "Close",
            FontSize = 18,
            MinHeight = 48,
            MinWidth = 120,
            IsCancel = true,
        };
        Avalonia.Automation.AutomationProperties.SetName(close, "Close the find window");
        close.Click += (_, _) => Close();

        _replaceRow = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Put this instead", FontSize = 16 },
                _replacement,
            },
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Look for", FontSize = 16 },
                _search,
                _replaceRow,
                _matchCase,
                _message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close, _replaceAll, _replace, findNext },
                },
            },
        };

        Opened += (_, _) =>
        {
            _search.Focus();
            _search.SelectAll();
        };
    }

    /// <summary>Whether the replace half is showing.</summary>
    public bool IsReplacing { get; private set; }

    /// <summary>The message the window is currently showing, for the tests and the status bar.</summary>
    internal string MessageForTest => _message.Text ?? string.Empty;

    /// <summary>Shows or hides the replace half, and re-titles the window to match.</summary>
    public void SetMode(bool replacing)
    {
        IsReplacing = replacing;
        Title = replacing ? "Find and replace" : "Find";
        _replaceRow.IsVisible = replacing;
        _replace.IsVisible = replacing;
        _replaceAll.IsVisible = replacing;
        Avalonia.Automation.AutomationProperties.SetName(
            this, replacing ? "Find and replace window" : "Find window");
    }

    /// <summary>The Find-next button, reachable by the tests without a pointer.</summary>
    internal void FindNextForTest() => FindNext();

    internal void ReplaceForTest()
    {
        PushBoxes();
        _find.ReplaceCurrent();
        ShowMessage();
    }

    internal void ReplaceAllForTest()
    {
        PushBoxes();
        _find.ReplaceAll();
        ShowMessage();
    }

    internal void TypeForTest(string search, string? replacement = null)
    {
        _search.Text = search;
        if (replacement is not null)
        {
            _replacement.Text = replacement;
        }
    }

    private void FindNext()
    {
        PushBoxes();
        _find.FindNext();
        ShowMessage();
    }

    /// <summary>
    /// The boxes are read at the moment a button is pressed rather than bound continuously: the
    /// controller's state is what the buttons act on, and one place to read it is one place to be
    /// wrong.
    /// </summary>
    private void PushBoxes()
    {
        _find.SearchText = _search.Text ?? string.Empty;
        _find.ReplacementText = _replacement.Text ?? string.Empty;
        _find.MatchCase = _matchCase.IsChecked == true;
    }

    private void ShowMessage() => _message.Text = _find.StatusMessage ?? string.Empty;

    private static TextBox MakeBox(string automationName, string text)
    {
        var box = new TextBox
        {
            FontSize = 18,
            MinHeight = 44,
            AcceptsReturn = false,
            Text = text,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }

    private static Button MakeButton(string label, Action onClick)
    {
        var button = new Button
        {
            Content = label,
            FontSize = 18,
            MinHeight = 48,
            MinWidth = 160,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }
}
