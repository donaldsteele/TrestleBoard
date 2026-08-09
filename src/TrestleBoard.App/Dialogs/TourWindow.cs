using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The first-run tour (PLAN.md §11 M63): five screens on the shape of a month, then out of the way.
///
/// <para>It teaches the <b>cycle</b>, not the buttons. Somebody meeting this app for the first time
/// does not need to be told what Bold does — they need to know that a newsletter starts from last
/// month's, that the tables fill themselves in, that the app will read it back before it goes, and
/// that the PDF is the thing they send. The "How do I…?" window answers everything else, and screen
/// five says so, because a tour whose last word is "good luck" has taught nobody where to ask next.
/// </para>
///
/// <para><b>Shown once.</b> A tour that reappears is a tour people learn to dismiss without reading,
/// and this audience will assume they broke something. <c>AppSettings.HasSeenTheTour</c> is written
/// the moment it opens rather than when it finishes, because somebody who closed it on screen two
/// has decided, and asking again next Tuesday would be overriding that decision.</para>
///
/// <para>A plain <c>int</c> screen counter and not <c>WizardSession</c>, for the reason
/// <see cref="ReviewWindow"/>, <c>RosterImportWindow</c> and <c>PhraseWindow</c> all gave: that type
/// is bound to <c>IWidgetDefinition</c> and exists to collect and commit data. A tour collects
/// nothing. Borrowing the visual language and none of the machinery is the established call here.
/// </para>
/// </summary>
public sealed class TourWindow : Window
{
    /// <summary>
    /// The month, in the order it happens. Written in the second person and in the committee's own
    /// words — "the brethren", "the trestle board" — because the tour is the first thing the app
    /// ever says and it should sound like somebody from the lodge wrote it.
    /// </summary>
    private static readonly (string Heading, string Body)[] TheMonth =
    [
        ("Welcome to TrestleBoard",
            "This app makes your lodge's monthly trestle board and turns it into a PDF you can email "
            + "or take to the printer.\n\nThis is a quick look at how a month goes — five screens, "
            + "about a minute. You can stop at any point, and nothing here changes anything."),

        ("Start from last month",
            "You almost never start from nothing. Choose \"Start from last month\" and TrestleBoard "
            + "opens a copy of your last newsletter with the layout, the headings and the lodge's "
            + "details already in place — and last month's news cleared out, so you cannot send "
            + "September's notices in October by accident."),

        ("Let the tables fill themselves in",
            "The officers table, the birthday list, the committees and the district calendar are not "
            + "typed. Each one asks you a few questions and draws itself, and the birthdays come "
            + "from your address book.\n\nIf a table already knows the answer, it keeps it — you are "
            + "only ever asked about what changed."),

        ("Look it over before it goes",
            "When the writing is done, \"Look it over with me\" walks the newsletter with you one "
            + "question at a time, and \"Read it back to me\" says it out loud, which catches the "
            + "things eyes slide past.\n\nNothing you do here changes the newsletter. It only points."),

        ("Make the PDF, then send it",
            "\"Make the PDF\" produces the file you send. \"Now send it\" opens your email with the "
            + "brethren who get it by email already filled in — as BCC, so nobody's address is shown "
            + "to anybody else.\n\nThat is the whole month. Anything else you need, the Help menu has "
            + "\"How do I…?\" — a search box over everything this app can do. It is always there."),
    ];

    private readonly TextBlock _heading;
    private readonly TextBlock _progress;
    private readonly TextBlock _body;
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _skip;

    private int _screen;

    public TourWindow()
    {
        Title = "A quick look round";
        Width = 660;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        _heading = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 580,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _progress = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        _body = new TextBlock
        {
            FontSize = 20,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 580,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _back = Wide("Back", "Back");
        _back.Click += (_, _) => GoTo(_screen - 1);

        _next = Wide("Next", "Next");
        _next.Click += (_, _) => GoTo(_screen + 1);

        // "Skip" and not "Cancel": there is nothing to cancel, and a tour that calls leaving it
        // "cancelling" makes leaving sound like a mistake.
        _skip = Wide("Skip this", "Skip this");
        _skip.IsCancel = true;
        _skip.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                _heading,
                _progress,
                _body,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { _back, _next, _skip },
                },
            },
        };

        KeyDown += OnKeyDown;
        Render();
    }

    /// <summary>Five, and the acceptance says five.</summary>
    public static int ScreenCount => TheMonth.Length;

    internal int ScreenForTest => _screen;

    internal string HeadingForTest => _heading.Text ?? "";

    internal string BodyForTest => _body.Text ?? "";

    internal string ProgressForTest => _progress.Text ?? "";

    internal Button NextForTest => _next;

    internal Button BackForTest => _back;

    internal Button SkipForTest => _skip;

    internal static IReadOnlyList<(string Heading, string Body)> ScreensForTest => TheMonth;

    internal void GoToForTest(int screen) => GoTo(screen);

    private void GoTo(int screen)
    {
        if (screen < 0)
        {
            return;
        }

        if (screen >= TheMonth.Length)
        {
            Close();
            return;
        }

        _screen = screen;
        Render();
    }

    private void Render()
    {
        (string heading, string body) = TheMonth[_screen];
        _heading.Text = heading;
        _body.Text = body;
        _progress.Text = $"Screen {_screen + 1} of {TheMonth.Length}";

        _back.IsVisible = _screen > 0;
        bool last = _screen == TheMonth.Length - 1;
        _next.Content = last ? "Done" : "Next";
        AutomationProperties.SetName(_next, last ? "Done" : "Next");
        _skip.Content = last ? "Close" : "Skip this";
        AutomationProperties.SetName(_skip, last ? "Close" : "Skip this");
        AutomationProperties.SetName(this, $"A quick look round — {_progress.Text}");

        // Focus the heading so a screen reader reads each new screen; Avalonia has no live region
        // for a whole panel (the WizardWindow technique, docs/M7-spec.md §6.6).
        _heading.Focusable = true;
        _heading.Focus();
        _heading.Focusable = false;
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

    private static Button Wide(string content, string automationName)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(button, automationName);
        return button.Action();
    }
}
