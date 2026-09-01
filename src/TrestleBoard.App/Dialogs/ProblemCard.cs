using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user said to the card.</summary>
internal enum ProblemCardChoice
{
    /// <summary>They closed it without saving a report. The default, and never a failure.</summary>
    NoThankYou,

    /// <summary>They want the report. The shell then asks where to put it.</summary>
    SaveTheReport,
}

/// <summary>
/// The card shown after something went wrong (PLAN.md §11 M77).
///
/// <para><b>Two answers, and the first line is not the question.</b> The first line is the promise:
/// the newsletter has been kept. Somebody whose screen has just misbehaved is not, at that moment,
/// a person who wants to be asked for a favour, and every second they spend not knowing whether
/// their evening's work survived is a second the app owes them. The offer to send a report comes
/// second, in smaller words, and "No thank you" is a complete answer.</para>
///
/// <para><b>It never shows the exception.</b> A stack trace on screen teaches this audience that
/// the app is broken in a way they cannot understand; the same text goes into the report, where
/// the person who can read it will read it. What the card says instead is what the app did and
/// what they can do — which is the same rule every refusal in the catalog follows.</para>
/// </summary>
internal sealed class ProblemCard : Window
{
    /// <param name="workWasKept">
    /// Whether the recovery snapshot was actually written. False changes the promise into an
    /// honest warning rather than being hidden — M73's standard: never claim what did not happen.
    /// </param>
    /// <param name="appWillClose">
    /// True when the runtime is going down regardless, so the card says so instead of implying the
    /// user can carry on where they were.
    /// </param>
    internal ProblemCard(bool workWasKept, bool appWillClose)
    {
        Title = "Something went wrong";
        SizeToContent = SizeToContent.Height;
        Width = 620;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        AutomationProperties.SetName(this, "Something went wrong");

        var save = new Button
        {
            Content = "Save a report",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        save.Primary();
        save.Click += (_, _) =>
        {
            Choice = ProblemCardChoice.SaveTheReport;
            Close();
        };
        AutomationProperties.SetName(save, "Save a report to send to the person who looks after TrestleBoard");

        var no = new Button
        {
            Content = "No thank you",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 160,
            IsCancel = true,
        };
        no.Action();
        no.Click += (_, _) =>
        {
            Choice = ProblemCardChoice.NoThankYou;
            Close();
        };
        AutomationProperties.SetName(no, "No thank you, carry on without saving a report");

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Something went wrong.",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = Promise(workWasKept),
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                },
                new TextBlock
                {
                    Text = appWillClose
                        ? "TrestleBoard has to close. Start it again and it will offer your work back."
                        : "You can carry on. If it happens again, it is worth sending a report.",
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                },
                new TextBlock
                {
                    Text = "A report says which version of TrestleBoard this is, what this computer "
                        + "is, and what went wrong. It holds no names, no telephone numbers and "
                        + "nothing from the newsletter.",
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, save },
                },
            },
        };
    }

    /// <summary>What they chose. Closing the window with the X means <see cref="ProblemCardChoice.NoThankYou"/>,
    /// which is the harmless answer — M73(b3)'s rule that a window close must never be read as a
    /// decision with consequences.</summary>
    internal ProblemCardChoice Choice { get; private set; } = ProblemCardChoice.NoThankYou;

    /// <summary>For the tests, which cannot click.</summary>
    internal void ChooseForTest(ProblemCardChoice choice) => Choice = choice;

    private static string Promise(bool workWasKept) => workWasKept
        ? "Your newsletter has been kept. Nothing you have written is lost."
        : "TrestleBoard tried to keep your newsletter and could not be sure it succeeded. "
            + "If you can still see your work, save it now with Ctrl+S.";
}
