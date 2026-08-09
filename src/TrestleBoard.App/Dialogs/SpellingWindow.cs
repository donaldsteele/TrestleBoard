using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Spelling;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Check my spelling" (PLAN.md §11 M52): one word per screen, with the sentence around it.
///
/// <para>This, not the dotted underline, is the primary path for this audience. An underline says
/// "something is wrong somewhere near here" to somebody who then has to find it, right-click it,
/// and read a small menu — three fine-motor operations §6 exists to avoid. A screen that says
/// <i>"chruch" — did you mean church?</i> with three big buttons is a question anyone can answer.</para>
///
/// <para>Not modal, and the shape and the manners come from <see cref="ReviewWindow"/> (M51): the
/// same screen-index model, the same 20pt/44-high buttons, the same trick of focusing the heading
/// so a screen reader reads each new screen. It is deliberately a sibling rather than a subclass —
/// the two windows share a look and share nothing else, and the day one of them grows a second
/// column the inheritance would be in the way.</para>
/// </summary>
public sealed class SpellingWindow : Window
{
    /// <summary>
    /// Not readonly, and that is the fix for M52's staleness bug: the list is re-read from the
    /// document after every change. Correcting one word moves every later word in the same
    /// paragraph, so a list captured once goes wrong the moment a replacement is a different length
    /// from what it replaced.
    /// </summary>
    private IReadOnlyList<Misspelling> _words;
    private readonly SpellChecker _checker;
    private readonly Action<Misspelling> _takeMeThere;
    private readonly Func<Misspelling, string, bool> _changeItTo;
    private readonly Func<IReadOnlyList<Misspelling>> _lookAgain;
    private readonly Action<string> _say;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _sentence;
    private readonly TextBlock _status;
    private readonly StackPanel _buttons;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _close;

    /// <summary>Screen 0 is the summary; screen n is <c>_words[n - 1]</c>.</summary>
    private int _screen;

    public SpellingWindow(
        IReadOnlyList<Misspelling> words,
        SpellChecker checker,
        Action<Misspelling> takeMeThere,
        Func<Misspelling, string, bool> changeItTo,
        Action<string> say,
        Func<IReadOnlyList<Misspelling>>? lookAgain = null)
    {
        _words = words ?? throw new ArgumentNullException(nameof(words));
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _takeMeThere = takeMeThere ?? throw new ArgumentNullException(nameof(takeMeThere));
        _changeItTo = changeItTo ?? throw new ArgumentNullException(nameof(changeItTo));
        _say = say ?? throw new ArgumentNullException(nameof(say));

        // Defaulted rather than required so a test can build the window with a fixed list; the app
        // always passes the real re-scan.
        _lookAgain = lookAgain ?? (() => _words);

        Title = "Check my spelling";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _heading = Text(24, bold: true);
        _progress = Text(16);
        _sentence = Text(20);
        _sentence.FontStyle = FontStyle.Italic;

        // M52's answers used to go only to the main window's status bar — behind this window, at the
        // far bottom of the screen, while the user is looking here. "Nothing happened" was the
        // report, and the app had in fact said what happened, somewhere they were never going to
        // look. It is said in both places now, and a polite live region so a screen reader hears it.
        _status = Text(17);
        _status.Text = "";
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _buttons = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _back = Wide("Back");
        _back.Click += (_, _) => GoTo(_screen - 1);
        _next = Wide("Skip this one");
        _next.Click += (_, _) => GoTo(_screen + 1);
        _close = Wide("Close");
        _close.IsCancel = true;
        _close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                _heading,
                _progress,
                _sentence,
                _status,
                _buttons,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { _back, _next, _close },
                },
            },
        };

        KeyDown += OnKeyDown;
        Render();
    }

    internal int ScreenForTest => _screen;

    internal string HeadingForTest => _heading.Text ?? "";

    internal string SentenceForTest => _sentence.Text ?? "";

    internal string ProgressForTest => _progress.Text ?? "";

    internal IReadOnlyList<Button> ButtonsForTest
    {
        get
        {
            var all = new List<Button>();
            foreach (Control child in _buttons.Children)
            {
                if (child is Button b)
                {
                    all.Add(b);
                }
            }

            all.Add(_back);
            all.Add(_next);
            all.Add(_close);
            return all;
        }
    }

    internal void GoToForTest(int screen) => GoTo(screen);

    private void GoTo(int screen)
    {
        if (screen < 0)
        {
            return;
        }

        if (screen > _words.Count)
        {
            _say("That is every word I did not know.");
            Close();
            return;
        }

        _screen = screen;
        Render();
    }

    private void Render()
    {
        _buttons.Children.Clear();

        if (_screen == 0)
        {
            RenderSummary();
        }
        else
        {
            RenderWord(_words[_screen - 1]);
        }

        _back.IsVisible = _screen > 0;
        _next.IsVisible = _screen > 0;
        AutomationProperties.SetName(this, $"Check my spelling — {_progress.Text}");

        _heading.Focusable = true;
        _heading.Focus();
        _heading.Focusable = false;
    }

    private void RenderSummary()
    {
        _heading.Text = "Let's check the spelling";
        _progress.Text = _words.Count == 0 ? "Nothing to ask about" : $"{_words.Count} to look at";
        _sentence.Text = _words.Count == 0
            ? "I went through every word in the newsletter and knew all of them. That does not mean "
              + "every word is the right word — \"form\" and \"from\" are both spelled correctly — "
              + "but nothing is misspelled."
            : "I will show you one word at a time, with the sentence it is in. Nothing changes "
              + "unless you say so, and you can stop at any point.";

        if (_words.Count > 0)
        {
            Button start = Wide("Show me the first one");
            start.Click += (_, _) => GoTo(1);
            _buttons.Children.Add(start);
        }
    }

    private void RenderWord(Misspelling word)
    {
        _heading.Text = $"“{word.Word}” — I do not know this word";
        _progress.Text = $"Word {_screen} of {_words.Count}";
        _sentence.Text = word.Sentence;

        Button there = Wide("Show me where it is");
        there.Click += (_, _) => _takeMeThere(word);
        _buttons.Children.Add(there);

        foreach (string suggestion in _checker.Suggest(word.Word))
        {
            string replacement = suggestion;
            Button change = Wide($"Change it to “{replacement}”");
            change.Click += (_, _) =>
            {
                if (_changeItTo(word, replacement))
                {
                    Tell($"Changed “{word.Word}” to “{replacement}”.");

                    // Re-read the document rather than stepping an index down a list that is now
                    // one word out of date. The corrected word drops out of the new list, so the
                    // word that was next has taken this screen's number — going to _screen, not
                    // _screen + 1, is what keeps the walk on the next unfixed word.
                    _words = _lookAgain();
                    GoTo(_screen);
                }
                else
                {
                    // Kept as a guard even though the list is now refreshed after every change: the
                    // page is editable behind this window, so the user can still move the word
                    // themselves between the scan and the click. Changing the wrong six characters
                    // would be far worse than saying nothing happened.
                    Tell($"“{word.Word}” is not where it was any more, so nothing was changed. "
                        + "Press Next and come back to it, or close this and check again.");
                }
            };
            _buttons.Children.Add(change);
        }

        Button fine = Wide("It's fine, leave it");
        fine.Click += (_, _) => GoTo(_screen + 1);
        _buttons.Children.Add(fine);

        Button name = Wide("It's a name — never ask again");
        name.Click += (_, _) =>
        {
            _checker.NeverAskAgain(word.Word);
            _say($"“{word.Word}” has been added to your own list of words.");
            GoTo(_screen + 1);
        };
        _buttons.Children.Add(name);
    }

    /// <summary>
    /// Says it here AND in the main window's status bar. Here because this is the window the user is
    /// looking at; there because the status bar is where every other answer in the app appears and a
    /// screen-reader user may be following that one.
    /// </summary>
    private void Tell(string message)
    {
        _status.Text = message;
        _say(message);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.N when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                GoTo(_screen + 1);
                e.Handled = true;
                break;
            case Key.B when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                GoTo(_screen - 1);
                e.Handled = true;
                break;
        }
    }

    private static TextBlock Text(double size, bool bold = false) => new()
    {
        FontSize = size,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 560,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Button Wide(string content)
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
        AutomationProperties.SetName(button, content);
        return button.Action();
    }
}
