using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing.Actions;
using TrestleBoard.Editing.Review;

namespace TrestleBoard.App.Dialogs;

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
    private readonly Action<ReviewFinding> _takeMeThere;
    private readonly Func<string, Task> _run;
    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _body;
    private readonly StackPanel _buttons;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _close;

    /// <summary>Screen 0 is the summary; screen n is <c>_findings[n - 1]</c>.</summary>
    private int _screen;

    public ReviewWindow(
        IReadOnlyList<ReviewFinding> findings,
        Action<ReviewFinding> takeMeThere,
        Func<string, Task> run)
    {
        _findings = findings ?? throw new ArgumentNullException(nameof(findings));
        _takeMeThere = takeMeThere ?? throw new ArgumentNullException(nameof(takeMeThere));
        _run = run ?? throw new ArgumentNullException(nameof(run));

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
            there.Click += (_, _) => _takeMeThere(finding);
            _buttons.Children.Add(there);
        }
        else if (finding.Kind == ReviewFindingKind.LookAtThePage)
        {
            Button there = Wide($"Show me page {finding.PageNumber}", $"Show me page {finding.PageNumber}");
            there.Click += (_, _) => _takeMeThere(finding);
            _buttons.Children.Add(there);
        }

        if (finding.RemedyActionId is { } remedy && ActionCatalog.TryGet(remedy, out EditorAction? action))
        {
            Button fix = Wide(action.Title, action.Title);
            fix.Click += async (_, _) =>
            {
                _takeMeThere(finding);
                await _run(remedy);
            };
            _buttons.Children.Add(fix);
        }
    }

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
