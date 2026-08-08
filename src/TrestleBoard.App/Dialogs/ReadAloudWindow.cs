using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Theme;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Review;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Read it back to me" (PLAN.md §11 M58): the newsletter one sentence at a time, spoken where a
/// voice is available and highlighted either way.
///
/// <para>Not modal, like M51's and M52's windows and for the same reason: every sentence it moves to
/// is on the page behind it, and a window that would not let the user touch what it is pointing at
/// would be useless.</para>
///
/// <para><b>The silent walk-through is the baseline, not the fallback.</b> Where no voice answers,
/// the spacebar advances the same walk and the same highlight — and a committee member who reads
/// one sentence at a time with everything else out of the way catches things they would not
/// otherwise. The window says which of the two it is doing rather than leaving somebody pressing
/// Play and hearing nothing.</para>
/// </summary>
internal sealed class ReadAloudWindow : Window
{
    private readonly ReadAloudSession _session;
    private readonly ISpeaker _speaker;
    private readonly Action<Sentence?> _show;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _sentence;
    private readonly Button _next;

    internal ReadAloudWindow(ReadAloudSession session, ISpeaker speaker, Action<Sentence?> show)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _show = show ?? throw new ArgumentNullException(nameof(show));

        Title = speaker.Available ? "Read it back to me" : "Walk me through it";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _heading = Text(24, bold: true);
        _progress = Text(16);
        _sentence = Text(22);

        _next = Wide(speaker.Available ? "Read the next one" : "Next sentence");
        _next.Click += (_, _) => Advance();
        Button back = Wide("Say that again");
        back.Click += (_, _) => Step(_session.Back());
        Button stop = Wide("Stop");
        stop.IsCancel = true;
        stop.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                _heading,
                _progress,
                _sentence,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { _next, back, stop },
                },
                new TextBlock
                {
                    Text = "The spacebar moves to the next sentence. Nothing here changes your "
                        + "newsletter.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 560,
                },
            },
        };

        KeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            _speaker.Hush();
            _show(null);
        };

        Render();
    }

    internal string HeadingForTest => _heading.Text ?? "";

    internal string SentenceForTest => _sentence.Text ?? "";

    internal string ProgressForTest => _progress.Text ?? "";

    internal IReadOnlyList<Button> ButtonsForTest
    {
        get
        {
            var all = new List<Button>();
            if (Content is StackPanel root)
            {
                foreach (Control child in root.Children)
                {
                    if (child is StackPanel row)
                    {
                        foreach (Control inner in row.Children)
                        {
                            if (inner is Button button)
                            {
                                all.Add(button);
                            }
                        }
                    }
                }
            }

            return all;
        }
    }

    internal void AdvanceForTest() => Advance();

    internal void BackForTest() => Step(_session.Back());

    private void Advance() => Step(_session.Next());

    private void Step(Sentence? sentence)
    {
        if (sentence is not null)
        {
            _speaker.Say(sentence.Text);
        }

        Render();
    }

    private void Render()
    {
        Sentence? current = _session.Current;
        _show(current);

        if (_session.IsEmpty)
        {
            _heading.Text = "There is nothing to read yet";
            _sentence.Text = "This newsletter has no writing in it, only pictures and lists. "
                + "Write something first and TrestleBoard will read it back to you.";
        }
        else if (_session.Finished)
        {
            _heading.Text = "That is the whole newsletter";
            _sentence.Text = "You have been through every sentence. Close this, or press “Say "
                + "that again” to go back over the last one.";
        }
        else if (current is null)
        {
            _heading.Text = _speaker.Available ? "Ready when you are" : "Ready to walk through it";
            _sentence.Text = _speaker.Available
                ? "TrestleBoard will read the newsletter to you one sentence at a time, and show "
                  + "you each one on the page as it goes."
                : "This computer has no voice TrestleBoard can use, so it will show you one "
                  + "sentence at a time instead, with everything else out of the way. Reading them "
                  + "one at a time catches things a whole page hides.";
        }
        else
        {
            _heading.Text = _speaker.Available ? "Reading" : "This one";
            _sentence.Text = current.Text;
        }

        _progress.Text = _session.ProgressText;
        _next.IsEnabled = !_session.Finished && !_session.IsEmpty;

        AutomationProperties.SetName(this, $"{Title} — {_progress.Text}");

        // Avalonia has no live region for a whole panel, so the heading is focused to make a screen
        // reader read the new sentence (the WizardWindow technique, docs/M7-spec.md §6.6).
        _heading.Focusable = true;
        _heading.Focus();
        _heading.Focusable = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                Advance();
                e.Handled = true;
                break;
            case Key.Back:
                Step(_session.Back());
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
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
            MinWidth = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(button, content);
        return button.Action();
    }
}
