using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Roster;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The three-way diff shown before the birthday list is filled in from the address book
/// (PLAN.md §11 M13): what will be added, what will be taken away, and what will be left exactly as
/// the user typed it.
///
/// It borrows M12's import-wizard voice deliberately — 20pt, plain sentences, and the promise
/// <em>nothing changes until you press the button</em> written where the user is looking. This
/// window decides nothing: <see cref="BirthdayRosterProjection.Plan"/> has already worked out the
/// answer, and the shell commits it in one undo step.
/// </summary>
internal sealed class BirthdaySyncDialog : Window
{
    private BirthdaySyncDialog(BirthdayProjection plan, int month, bool inserting)
    {
        Title = inserting ? "Add the birthdays?" : "Update the birthday list?";
        MinWidth = 720;
        Width = 760;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, Title);

        Button confirm = MakeButton(inserting ? "Yes, add them" : "Update the list");
        confirm.IsDefault = true;
        confirm.Click += (_, _) =>
        {
            Confirmed = true;
            Close();
        };

        Button leave = MakeButton(inserting ? "No, I'll type them myself" : "Leave it as it is");
        leave.IsCancel = true;
        leave.Click += (_, _) => Close();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = Headline(plan, month, inserting),
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });

        AddSection(body, "These will be added", plan.Additions);
        AddSection(body, "These will be taken away", plan.Removals);
        AddSection(body, "These will be brought up to date", plan.Updates);
        AddSection(body, "These are yours, and will be left alone", plan.KeptManual);

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Spacing = 12,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Nothing changes until you press “"
                                + (inserting ? "Yes, add them" : "Update the list") + "”.",
                            FontSize = 18,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 12,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { leave, confirm },
                        },
                    },
                },
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = body,
                },
            },
        };
    }

    /// <summary>True when the user asked for the change. Nothing has been applied either way.</summary>
    internal bool Confirmed { get; private set; }

    /// <summary>
    /// Shows the diff and answers whether to go ahead. <paramref name="inserting"/> only changes the
    /// words: at insert time the list is empty, so there is nothing to take away and the question is
    /// simply whether to start from the address book.
    /// </summary>
    internal static async Task<bool> AskAsync(
        Window owner, BirthdayProjection plan, int month, bool inserting)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(plan);

        var dialog = new BirthdaySyncDialog(plan, month, inserting);
        await dialog.ShowDialog(owner);
        return dialog.Confirmed;
    }

    /// <summary>"March", from a month number the document already holds. Invariant, so it is the
    /// same word on every machine that opens this newsletter.</summary>
    internal static string MonthName(int month) => month is >= 1 and <= 12
        ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)
        : "this month";

    private static string Headline(BirthdayProjection plan, int month, bool inserting)
    {
        string monthName = MonthName(month);
        if (inserting)
        {
            int found = plan.Additions.Count;
            return found == 1
                ? $"We found one birthday in {monthName} in your address book. Add it?"
                : $"We found {found} birthdays in {monthName} in your address book. Add them?";
        }

        return $"Here is what would change in the {monthName} birthday list.";
    }

    /// <summary>
    /// One heading and its names, or nothing at all. An empty "These will be taken away (0)" is
    /// noise for someone reading every word on the screen.
    /// </summary>
    private static void AddSection(Panel body, string heading, IReadOnlyList<BirthdayEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        body.Children.Add(new TextBlock
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"{heading} ({entries.Count})"),
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (BirthdayEntry entry in entries)
        {
            body.Children.Add(new TextBlock
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{entry.Name} — {entry.Month}/{entry.Day}"),
                FontSize = 18,
                Margin = new Avalonia.Thickness(16, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static Button MakeButton(string text)
    {
        var button = new Button { Content = text, FontSize = 20, MinHeight = 44, MinWidth = 200 };
        AutomationProperties.SetName(button, text);
        return (button).Action();
    }
}
