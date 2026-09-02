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

    /// <summary>M85: the results from the archive, hidden until there are some.</summary>
    private readonly ListBox _archiveResults;

    private readonly Button _searchArchive;

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
        close.Action();
        Avalonia.Automation.AutomationProperties.SetName(close, "Close the find window");
        close.Click += (_, _) => Close();

        // M85. "When did we last mention the fish fry?" is a real question and a quarterly one,
        // and until now the only way to answer it was to open eleven files one at a time.
        _searchArchive = MakeButton("Look in earlier newsletters too", () => SearchTheArchive?.Invoke());
        Avalonia.Automation.AutomationProperties.SetName(
            _searchArchive, "Look for these words in earlier newsletters too");

        _archiveResults = new ListBox
        {
            FontSize = 16,
            MaxHeight = 220,
            IsVisible = false,
        };
        Avalonia.Automation.AutomationProperties.SetName(
            _archiveResults, "What was found in earlier newsletters");

        // Double-click opens the issue the words were found in, which is the one thing anybody
        // wants to do with a result.
        _archiveResults.DoubleTapped += (_, _) =>
        {
            if (_archiveResults.SelectedItem is ListBoxItem { Tag: Integration.ArchiveHit hit })
            {
                OpenTheIssue?.Invoke(hit);
            }
        };

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

                // M85's button on its OWN row, left-aligned, and not in the row below. Putting it
                // with the other four overflowed this window at its own 520pt width and pushed
                // "Replace every one" off the right-hand edge — the M76 toolbar finding, repeating
                // one milestone later in a different window. It also belongs apart: the other four
                // act on the newsletter on screen, and this one goes looking somewhere else.
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { _searchArchive },
                },
                _message,
                _archiveResults,
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

    // ---- Looking in earlier newsletters (PLAN.md §11 M85) ---------------------------------------

    /// <summary>Raised when the user asks to look in earlier newsletters. The shell does the work:
    /// this window knows nothing about folders, and nothing about files.</summary>
    internal event Action? SearchTheArchive;

    /// <summary>Raised when the user picks a result. The shell opens it, read-only, on M59's pattern.</summary>
    internal event Action<Integration.ArchiveHit>? OpenTheIssue;

    /// <summary>What the user typed, for the shell to search with.</summary>
    internal string WordsToLookFor => _search.Text ?? string.Empty;

    /// <summary>Whether the user asked for exact capitals.</summary>
    internal bool MatchCase => _matchCase.IsChecked == true;

    /// <summary>
    /// Shows what came back. An empty result hides the list rather than showing an empty box: a box
    /// with nothing in it is a question the user then has to answer for themselves.
    /// </summary>
    internal void ShowArchiveResults(Integration.ArchiveSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Each row is built rather than bound to ToString, so the sentence WRAPS. A row clipped at
        // the window's edge loses the words the reader is looking at — and the sentence the hit is
        // in is the whole reason the row is worth showing.
        _archiveResults.ItemsSource = result.Hits.Select(hit => new ListBoxItem
        {
            Tag = hit,
            Content = new TextBlock
            {
                Text = hit.Describe(),
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
            },
        }).ToList();
        _archiveResults.IsVisible = result.Hits.Count > 0;
        _message.Text = DescribeArchive(result);
    }

    /// <summary>Said before the search starts, because reading a folder of newsletters is slow.</summary>
    internal void SayTheArchiveIsBeingRead() =>
        _message.Text = "Looking through your earlier newsletters. This can take a moment.";

    /// <summary>The sentence after a search. Public so a test can assert it without a window.</summary>
    internal static string DescribeArchive(Integration.ArchiveSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Problem is { Length: > 0 } problem)
        {
            return problem;
        }

        if (result.Hits.Count == 0)
        {
            return result.IssuesRead == 0
                ? "There were no earlier newsletters in that folder to look through."
                : $"Those words are not in any of the {result.IssuesRead} earlier newsletters.";
        }

        string found = result.Hits.Count == 1
            ? "Found it once"
            : $"Found it {result.Hits.Count} times";

        return $"{found}, newest first. Double-click one to open that newsletter.";
    }

    /// <summary>For the tests, which cannot double-click.</summary>
    internal Integration.ArchiveHit? FirstArchiveHitForTest =>
        _archiveResults.ItemsSource?.Cast<ListBoxItem>().FirstOrDefault()?.Tag as Integration.ArchiveHit;

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
        // M37: the affordance that says "you can press this". IsDefault buttons take the
        // primary treatment instead, via the property-qualified selector in Controls.axaml, and
        // the Style there wins over this class.
        return button.Action();
    }
}
