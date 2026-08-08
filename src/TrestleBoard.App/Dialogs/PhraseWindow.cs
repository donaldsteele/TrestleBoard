using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Core.Phrases;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Words for hard news" (PLAN.md §11 M54): choose a paragraph, answer its blanks one at a time,
/// read it back, put it in.
///
/// <para>Blank-page paralysis is worst under grief. The committee re-drafts a memorial every time
/// one is needed, or digs through old issues to copy one; a dignified starting text with two blanks
/// to fill is a different task altogether, and it is the one this window offers.</para>
///
/// <para><b>Modal, unlike M51's and M52's windows.</b> Those point at the page behind them, so they
/// had to leave it reachable. This one asks a short series of questions and then does a single
/// thing — the wizard shape, and the wizard's manners: one question per screen, 20pt, a big Back
/// and a big Next, and a look at the whole thing before anything is inserted.</para>
///
/// <para>What it inserts is <b>ordinary editable text</b>. It is a starting point, not a form
/// letter: the paragraph lands in the newsletter as words the committee can change, and most of
/// them will.</para>
/// </summary>
public sealed class PhraseWindow : Window
{
    private readonly IReadOnlyList<Phrase> _shelf;
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _body;
    private readonly StackPanel _content;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _cancel;

    private Phrase? _chosen;

    /// <summary>0 = choose a paragraph; 1..n = its blanks; n+1 = read it back.</summary>
    private int _screen;

    public PhraseWindow(IReadOnlyList<Phrase> shelf)
    {
        _shelf = shelf ?? throw new ArgumentNullException(nameof(shelf));

        Title = "Words for hard news";
        Width = 660;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _heading = Text(24, bold: true);
        _progress = Text(16);
        _body = Text(20);
        _content = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _back = Wide("Back");
        _back.Click += (_, _) => GoTo(_screen - 1);
        _next = Wide("Next");
        _next.Click += (_, _) => Advance();
        _cancel = Wide("Cancel");
        _cancel.IsCancel = true;
        _cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                _heading,
                _progress,
                _body,
                _content,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { _back, _next, _cancel },
                },
            },
        };

        KeyDown += OnKeyDown;
        Render();
    }

    /// <summary>True when the user reached the end and asked for the words to go in.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The paragraph, with the blanks filled in. Only meaningful once confirmed.</summary>
    public string? Words { get; private set; }

    /// <summary>Which shelf entry it came from, for the announcement.</summary>
    public Phrase? Chosen => _chosen;

    internal int ScreenForTest => _screen;

    internal string HeadingForTest => _heading.Text ?? "";

    internal string BodyForTest => _body.Text ?? "";

    internal IReadOnlyList<Button> ButtonsForTest
    {
        get
        {
            var all = new List<Button>();
            foreach (Control child in _content.Children)
            {
                if (child is Button b)
                {
                    all.Add(b);
                }
            }

            all.Add(_back);
            all.Add(_next);
            all.Add(_cancel);
            return all;
        }
    }

    internal void ChooseForTest(Phrase phrase)
    {
        _chosen = phrase;
        GoTo(1);
    }

    internal void AnswerForTest(string token, string value) => _answers[token] = value;

    internal void AdvanceForTest() => Advance();

    private int LastScreen => 1 + (_chosen?.Blanks.Count ?? 0);

    private void Advance()
    {
        if (_screen == 0)
        {
            // Nothing chosen yet: the buttons in the list are how you choose, and Next has nothing
            // to do until one is pressed.
            return;
        }

        if (_screen >= LastScreen)
        {
            Confirmed = true;
            Words = _chosen?.Fill(_answers);
            Close();
            return;
        }

        GoTo(_screen + 1);
    }

    private void GoTo(int screen)
    {
        if (screen < 0 || screen > LastScreen)
        {
            return;
        }

        if (screen == 0)
        {
            _chosen = null;
            _answers.Clear();
        }

        _screen = screen;
        Render();
    }

    private void Render()
    {
        _content.Children.Clear();

        if (_screen == 0)
        {
            RenderShelf();
        }
        else if (_chosen is { } phrase && _screen <= phrase.Blanks.Count)
        {
            RenderBlank(phrase, phrase.Blanks[_screen - 1]);
        }
        else if (_chosen is { } ready)
        {
            RenderReadBack(ready);
        }

        _back.IsVisible = _screen > 0;
        _next.IsVisible = _screen > 0;
        _next.Content = _screen >= LastScreen && _screen > 0 ? "Put these words in" : "Next";
        AutomationProperties.SetName(_next, (string)_next.Content!);
        AutomationProperties.SetName(this, $"Words for hard news — {_progress.Text}");

        // The heading is focused so a screen reader reads each new screen; Avalonia has no live
        // region for a whole panel (the WizardWindow technique, docs/M7-spec.md §6.6).
        _heading.Focusable = true;
        _heading.Focus();
        _heading.Focusable = false;
    }

    private void RenderShelf()
    {
        _heading.Text = "Which words would you like?";
        _progress.Text = "Step 1";
        _body.Text = "Each of these is a starting point. It goes into your newsletter as ordinary "
            + "writing, and you can change every word of it afterwards.";

        foreach (Phrase phrase in _shelf)
        {
            Phrase chosen = phrase;
            Button choose = Wide(phrase.Title);
            AutomationProperties.SetHelpText(choose, phrase.WhenToUse);
            choose.Click += (_, _) =>
            {
                _chosen = chosen;
                _answers.Clear();
                GoTo(1);
            };
            _content.Children.Add(choose);

            _content.Children.Add(new TextBlock
            {
                Text = phrase.WhenToUse,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                Margin = new Avalonia.Thickness(8, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }
    }

    private void RenderBlank(Phrase phrase, PhraseBlank blank)
    {
        _heading.Text = blank.Question;
        _progress.Text = $"Step {_screen + 1} of {LastScreen + 1} — {phrase.Title}";
        _body.Text = blank.Hint;

        var box = new TextBox
        {
            Text = _answers.TryGetValue(blank.Token, out string? already) ? already : "",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 460,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(box, blank.Question);
        box.TextChanged += (_, _) => _answers[blank.Token] = box.Text ?? "";
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Advance();
                e.Handled = true;
            }
        };
        _content.Children.Add(box);

        // Left blank on purpose is allowed: a memorial may be written before the date is known,
        // and a line of underscores in the newsletter is a better prompt than a wizard that will
        // not let you past it.
        _content.Children.Add(new TextBlock
        {
            Text = "You can leave this blank and fill it in later.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        box.Focus();
    }

    private void RenderReadBack(Phrase phrase)
    {
        _heading.Text = "Here is how it reads";
        _progress.Text = $"Last step — {phrase.Title}";
        _body.Text = phrase.Fill(_answers);

        _content.Children.Add(new TextBlock
        {
            Text = "Nothing is in your newsletter yet. When you put these words in, they become "
                + "ordinary writing you can change.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.B when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                GoTo(_screen - 1);
                e.Handled = true;
                break;
            case Key.N when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                Advance();
                e.Handled = true;
                break;
        }
    }

    private static TextBlock Text(double size, bool bold = false) => new()
    {
        FontSize = size,
        FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 580,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Button Wide(string content)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(button, content);
        return button.Action();
    }
}
