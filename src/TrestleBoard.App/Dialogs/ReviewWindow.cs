using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Actions;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Review;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Where "Take me there" actually landed (PLAN.md §11 M73 (g)).
/// </summary>
/// <param name="FoundIt">
/// False when the thing the finding is about is not in the newsletter any more.
/// </param>
/// <param name="PageNumber">
/// The page the user is now looking at, counting from 1 — which is NOT always the page the finding
/// names. The review is a snapshot and the newsletter behind it stays editable, so a page can be
/// deleted between the scan and the button; the turn is clamped to the last page that exists, and
/// before this the window went on to narrate the page number it had asked for.
/// </param>
public readonly record struct ReviewLanding(bool FoundIt, int PageNumber);

/// <summary>
/// "Look it over with me" (PLAN.md §11 M51): the review checklist, one question per screen.
///
/// <para><b>Not modal</b>, for the same reason <see cref="FindWindow"/> is not: every screen here is
/// about something on the page behind it, and "Take me there" would be a lie if the user were not
/// allowed to touch the page it took them to. Everything else in this app that asks a question is
/// modal; this one does not ask, it points.</para>
///
/// <para>It borrows <c>WizardWindow</c>'s visual language — 20pt, one thing per screen, a big Back
/// and a big Next, a count so nobody is lost — and none of its machinery, because the questions are
/// discovered at runtime from the newsletter rather than declared in advance. That is the same call
/// <c>RosterImportWindow</c> made, and for the same reason.</para>
///
/// <para>The window holds no opinion about the newsletter. <see cref="ReviewChecklist"/> produced
/// the findings before this window existed, and the two callbacks it is handed are the only ways it
/// can affect anything.</para>
/// </summary>
public sealed class ReviewWindow : Window
{
    private readonly IReadOnlyList<ReviewFinding> _findings;

    /// <summary>
    /// Turns to the finding and answers with what actually happened: whether the thing it is about
    /// is still in the newsletter (M70), and which page the user is now on, which is the page asked
    /// for only if that page still exists (M73 (g)).
    /// </summary>
    private readonly Func<ReviewFinding, ReviewLanding> _takeMeThere;
    private readonly Func<string, Task<ActionOutcome>> _run;
    private readonly Action<string> _say;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _body;
    private readonly TextBlock _status;
    private readonly StackPanel _buttons;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _close;

    /// <summary>Screen 0 is the summary; screen n is <c>_findings[n - 1]</c>.</summary>
    private int _screen;

    public ReviewWindow(
        IReadOnlyList<ReviewFinding> findings,
        Func<ReviewFinding, ReviewLanding> takeMeThere,
        Func<string, Task<ActionOutcome>> run,
        Action<string> say)
    {
        _findings = findings ?? throw new ArgumentNullException(nameof(findings));
        _takeMeThere = takeMeThere ?? throw new ArgumentNullException(nameof(takeMeThere));
        _run = run ?? throw new ArgumentNullException(nameof(run));
        _say = say ?? throw new ArgumentNullException(nameof(say));

        Title = "Look it over";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _heading = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _progress = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _body = new TextBlock
        {
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // M70: the remedy buttons and "Take me there" answered into the main window's status bar,
        // which is behind this window — and a finding whose block had been deleted since the scan
        // turned the page, selected nothing and said nothing at all. Both are said here now, and in
        // the status bar as well.
        _status = new TextBlock
        {
            Text = "",
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _buttons = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _back = Wide("Back", "Back");
        _back.Click += (_, _) => GoTo(_screen - 1);

        _next = Wide("That's fine, next", "That's fine, next");
        _next.Click += (_, _) => GoTo(_screen + 1);

        _close = Wide("Close", "Close");
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
                _body,
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

    /// <summary>How many questions there are, not counting the summary.</summary>
    public int QuestionCount => _findings.Count;

    /// <summary>Which screen is showing. 0 is the summary.</summary>
    internal int ScreenForTest => _screen;

    internal string HeadingForTest => _heading.Text ?? "";

    internal string BodyForTest => _body.Text ?? "";

    internal string ProgressForTest => _progress.Text ?? "";

    internal string StatusForTest => _status.Text ?? "";

    /// <summary>Every button on the screen right now, so a test can press one by its words.</summary>
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

        // Past the last question is the end of the review. Closing is the honest answer: there is
        // no "finish" step, because the checklist never changed anything to finish.
        if (screen > _findings.Count)
        {
            Close();
            return;
        }

        _screen = screen;
        Render();
    }

    private void Render()
    {
        _buttons.Children.Clear();

        // A new screen is a new question; the last one's answer beside it would read as this one's.
        _status.Text = "";

        if (_screen == 0)
        {
            RenderSummary();
        }
        else
        {
            RenderFinding(_findings[_screen - 1]);
        }

        _back.IsVisible = _screen > 0;
        _next.Content = _screen == _findings.Count ? "Done" : "That's fine, next";
        AutomationProperties.SetName(_next, _screen == _findings.Count ? "Done" : "That's fine, next");
        AutomationProperties.SetName(this, $"Look it over — {_progress.Text}");

        // Avalonia has no live region for a whole panel, so the heading is focused to make the
        // screen reader read the new question out (the WizardWindow technique, docs/M7-spec.md §6.6).
        _heading.Focusable = true;
        _heading.Focus();
        _heading.Focusable = false;
    }

    private void RenderSummary()
    {
        int questions = CountThatAreNotJustLooking();
        _heading.Text = "Let's look it over together";
        _progress.Text = $"Start — {_findings.Count} screens";
        _body.Text = questions == 0
            ? "I went through the newsletter and nothing jumped out at me. That is not the same as "
              + "it being right, so the rest of this is a look at each page with your own eyes. "
              + "Nothing here changes your newsletter, and you can stop at any point."
            : $"I went through the newsletter and found {Count(questions)} worth asking you about, "
              + "and then we will look at each page together. Nothing here changes your newsletter, "
              + "and you can stop at any point.";
    }

    private void RenderFinding(ReviewFinding finding)
    {
        _heading.Text = finding.Heading;
        _progress.Text = $"Screen {_screen} of {_findings.Count}";
        _body.Text = finding.Question;

        if (finding.BlockId is not null)
        {
            Button there = Wide("Take me there", "Take me there");
            there.Click += (_, _) => GoAndSayWhereWeAre(finding);
            _buttons.Children.Add(there);
        }
        else if (finding.Kind == ReviewFindingKind.LookAtThePage)
        {
            Button there = Wide($"Show me page {finding.PageNumber}", $"Show me page {finding.PageNumber}");
            there.Click += (_, _) => GoAndSayWhereWeAre(finding);
            _buttons.Children.Add(there);
        }

        if (finding.RemedyActionId is { } remedy && ActionCatalog.TryGet(remedy, out EditorAction? action))
        {
            Button fix = Wide(action.Title, action.Title);
            fix.Click += async (_, _) =>
            {
                // The page is turned first because most of these remedies act on what is selected;
                // if the thing has been deleted since the scan there is nothing to act on, and
                // running the command anyway would be a command aimed at the wrong part of the
                // newsletter.
                if (!GoAndSayWhereWeAre(finding))
                {
                    return;
                }

                // M73(f). Every remedy button here said "Done: …" on anything that did not hand
                // back a sentence — including the picker or wizard the user had just cancelled.
                // The whole point of this window is that it is walking somebody through their
                // newsletter one worry at a time, so a false "Done" gets a real defect ticked off.
                ActionOutcome outcome = await _run(remedy);
                switch (outcome.Result)
                {
                    case ActionResult.Refused:
                        Echo(outcome.Message!);
                        break;

                    case ActionResult.NothingHappened:
                        Tell($"Nothing has changed — you stopped part way, or there was nothing to "
                            + $"do. “{action.Title}” is still here to try again, or press "
                            + "“That's fine, next” to carry on.");
                        break;

                    default:
                        Tell($"Done: {action.Title}.");
                        break;
                }
            };
            _buttons.Children.Add(fix);
        }
    }

    /// <summary>
    /// Turns to the finding and says where the user has ended up — including, and this is the M70
    /// finding, when the thing it was going to point at has been taken out of the newsletter since
    /// the review was run. Before this the page turned, nothing was selected and nothing was said.
    ///
    /// <para>M73 (g): every page number in these sentences is now the page the user is actually
    /// looking at, read back from the turn, rather than the one the finding asked for. Delete a page
    /// with the review open and the turn is clamped to the last page that exists — so the window
    /// said "You are on page 4" while page 3 was on screen, and the clamp is exactly what made that
    /// confident. A wrong page number here is worse than most: the user's next move is to look at
    /// the page and decide whether the thing being described is really there.</para>
    /// </summary>
    private bool GoAndSayWhereWeAre(ReviewFinding finding)
    {
        ReviewLanding landing = _takeMeThere(finding);
        bool pageMoved = landing.PageNumber != finding.PageNumber;

        if (!landing.FoundIt)
        {
            Tell("That part has been taken out of the newsletter since I looked it over, so there "
                + $"is nothing left for me to point at. You are on page {landing.PageNumber}. Press "
                + "“That's fine, next” to carry on.");
            return false;
        }

        if (pageMoved)
        {
            Tell($"Page {finding.PageNumber} is not in the newsletter any more — it is shorter than "
                + $"when I looked it over. You are on page {landing.PageNumber} instead, which is as "
                + "far as it goes. Press “That's fine, next” to carry on.");
            return false;
        }

        Tell(finding.BlockId is null
            ? $"You are on page {landing.PageNumber}."
            : $"That is it, picked out on page {landing.PageNumber}.");
        return true;
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

    /// <summary>
    /// For a sentence the action runner has already put in the status bar. Saying it again would
    /// make a screen reader read the same refusal twice; showing it here is the part that was
    /// missing.
    /// </summary>
    private void Echo(string message) => _status.Text = message;

    private int CountThatAreNotJustLooking()
    {
        int n = 0;
        foreach (ReviewFinding f in _findings)
        {
            if (f.Kind != ReviewFindingKind.LookAtThePage)
            {
                n++;
            }
        }

        return n;
    }

    private static string Count(int n) => n == 1 ? "one thing" : $"{n} things";

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

    /// <summary>
    /// One button shape for the whole window: 20pt, 44 high, and wide enough that the label is not
    /// the target's only clue (PLAN.md §6).
    /// </summary>
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
