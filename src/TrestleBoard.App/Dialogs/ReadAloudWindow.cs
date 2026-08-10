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
    /// <summary>
    /// Why the window is drawing itself again — and therefore how much it is allowed to do. M74 (e):
    /// this used to be guesswork, because there was nothing to guess from. Every keystroke on the
    /// page behind raises <c>Changed</c>, and the redraw that followed turned the page, took the
    /// focus and repeated itself, all of which belong to somebody pressing a button in this window.
    /// </summary>
    private enum Because
    {
        /// <summary>Next, Say-that-again, or the window opening. The user asked; act like it.</summary>
        TheUserAsked,

        /// <summary>Somebody typed on the page behind. Redraw, and do nothing they did not ask for.</summary>
        TheNewsletterChanged,
    }

    private readonly ReadAloudSession _session;
    private readonly ISpeaker _speaker;
    private readonly Action<Sentence?, bool> _show;
    private readonly Action<string> _say;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _sentence;
    private readonly TextBlock _status;
    private readonly Button _next;

    /// <summary>
    /// False once a sentence has been handed to the voice and the voice did not take it. M70: this
    /// answer used to be thrown away, so the window went on saying "Reading" while the room stayed
    /// silent — the app reporting something it had not done.
    /// </summary>
    private bool _voiceAnswered = true;

    /// <summary>
    /// The sentence the "this computer could not say that out loud" message was last said about, or
    /// null while no such message is standing. M74 (e): the message was re-said on every keystroke,
    /// to the window and to the status bar, about a failure that had not happened again.
    /// </summary>
    private string? _toldTheVoiceFailedFor;

    internal ReadAloudWindow(
        ReadAloudSession session,
        ISpeaker speaker,
        Action<Sentence?, bool> show,
        Action<string> say)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _say = say ?? throw new ArgumentNullException(nameof(say));

        Title = speaker.Available ? "Read it back to me" : "Walk me through it";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _heading = Text(24, bold: true);
        _progress = Text(16);
        _sentence = Text(22);

        _status = Text(17);
        _status.Text = "";
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

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
                _status,
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

        // M73(g): the walk follows the newsletter now instead of a copy of it taken when the window
        // opened. Editing while proofreading is what this window is FOR, so the answer could not be
        // "throw the read-through away on the first keystroke" the way the find window throws its
        // hit away — it had to be to re-read and to say what moved.
        _session.Changed += OnTheNewsletterChanged;

        Closed += (_, _) =>
        {
            _session.Changed -= OnTheNewsletterChanged;
            _session.Dispose();
            _speaker.Hush();
            _show(null, false);
        };

        Render(Because.TheUserAsked);
    }

    internal string HeadingForTest => _heading.Text ?? "";

    internal string SentenceForTest => _sentence.Text ?? "";

    internal string ProgressForTest => _progress.Text ?? "";

    internal string StatusForTest => _status.Text ?? "";

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

    /// <summary>
    /// Somebody typed on the page behind this window. The walk has already caught up; what is left
    /// is to draw the sentence as it now reads, move the highlight to where those characters
    /// actually are, and say what happened to them — but NOT to speak. A voice starting up
    /// unasked in the middle of a correction would be alarming, and the user is looking at the
    /// words they just typed rather than waiting to be read to. "Say that again" speaks the new
    /// wording the moment they ask for it.
    ///
    /// <para><b>M74 (e): and not to move anything either.</b> Editing on a different page from the
    /// one being read is the stated workflow, and the redraw turned the canvas back to the sentence
    /// on every keystroke, leaving the caret off-screen; it also took the focus and repeated the
    /// voice-failure message each time. A redraw caused by typing draws, and that is all: turning
    /// the page and moving the focus belong to Next, Back and Say-that-again.</para>
    /// </summary>
    private void OnTheNewsletterChanged(object? sender, EventArgs e)
    {
        string? notice = _session.TakeChangeNotice();

        // Render redraws the highlight from the sentence's new offsets and rewrites the status line,
        // so the notice goes on afterwards — beside, not instead of, anything Render put there
        // (the "this computer has no voice for that sentence" message is still true).
        Render(Because.TheNewsletterChanged);
        if (notice is null)
        {
            return;
        }

        string standing = _status.Text ?? "";
        Tell(standing.Length > 0 ? standing + " " + notice : notice);
    }

    private void Step(Sentence? sentence)
    {
        if (sentence is not null)
        {
            // M70: what the voice answers is the whole point. A machine that reports it can speak
            // and then cannot — no voice installed on Windows, speech-dispatcher not running — was
            // the one case where this window said "Reading" to somebody hearing nothing.
            _voiceAnswered = _speaker.Say(sentence.Text);
        }

        Render(Because.TheUserAsked);
    }

    private void Render(Because because)
    {
        bool theUserAsked = because == Because.TheUserAsked;
        Sentence? current = _session.Current;

        // Turning the page is a navigation, and navigations belong to the user. On a redraw caused
        // by typing the highlight is still drawn — on the page they are looking at, where the
        // sentence may well not be, in which case there is nothing to light up and nothing moves.
        _show(current, theUserAsked);

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
            // Only claim to be reading if the voice took the sentence. Otherwise this is the same
            // walk-through the machines with no voice get, and it is named honestly.
            _heading.Text = _speaker.Available && _voiceAnswered ? "Reading" : "This one";
            _sentence.Text = current.Text;
        }

        if (_speaker.Available && !_voiceAnswered)
        {
            // Said once per sentence it is about. The failure did not happen again because somebody
            // typed a letter, and saying so again — in the window and in the status bar, on every
            // keystroke — is the app reporting an event that did not occur.
            string about = current?.Text ?? "";
            if (theUserAsked || !string.Equals(about, _toldTheVoiceFailedFor, StringComparison.Ordinal))
            {
                Tell("This computer could not say that sentence out loud, so it is here to read "
                    + "instead. Nothing is wrong with your newsletter. Press “Read the next one” to "
                    + "carry on, or close this and ask somebody to check the computer's voice.");
                _toldTheVoiceFailedFor = about;
            }
        }
        else
        {
            _status.Text = "";
            _toldTheVoiceFailedFor = null;
        }

        _progress.Text = _session.ProgressText;
        _next.IsEnabled = !_session.Finished && !_session.IsEmpty;

        AutomationProperties.SetName(this, $"{Title} — {_progress.Text}");

        if (theUserAsked)
        {
            // Avalonia has no live region for a whole panel, so the heading is focused to make a
            // screen reader read the new sentence (the WizardWindow technique, docs/M7-spec.md
            // §6.6). Only where the user asked for a new sentence: doing it per keystroke moves the
            // focus off whatever they were on and makes a screen reader start again, mid-word.
            _heading.Focusable = true;
            _heading.Focus();
            _heading.Focusable = false;
        }
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
