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
    private readonly IReadOnlyList<Misspelling> _words;
    private readonly SpellChecker _checker;
    private readonly Action<Misspelling> _takeMeThere;
    private readonly Func<Misspelling, string, bool> _changeItTo;
    private readonly Action<string> _say;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _sentence;
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
        Action<string> say)
    {
        _words = words ?? throw new ArgumentNullException(nameof(words));
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _takeMeThere = takeMeThere ?? throw new ArgumentNullException(nameof(takeMeThere));
        _changeItTo = changeItTo ?? throw new ArgumentNullException(nameof(changeItTo));
        _say = say ?? throw new ArgumentNullException(nameof(say));

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
                    _say($"Changed “{word.Word}” to “{replacement}”.");
                    GoTo(_screen + 1);
                }
                else
                {
                    // The word moved or was edited since the list was made. Saying so beats
                    // changing the wrong six characters.
                    _say($"“{word.Word}” is not where it was any more, so nothing was "
                        + "changed. Close this and check the spelling again.");
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
