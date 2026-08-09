using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Bring in a predecessor's pack" (PLAN.md §11 M64): what is in the file, item by item, and a tick
/// against each thing the user wants to take.
///
/// <para><b>Nothing that would replace something is ticked when the window opens.</b> A successor
/// is meeting this window on their first day, holding a file they cannot see inside, and the
/// difference between "add" and "replace" is the difference between a good afternoon and losing the
/// lodge's address book. Taking is one click; replacing is one click and a decision.</para>
///
/// <para>Each row says three things in order: what it is, what the pack has of it, and — only when
/// there is something to lose — what is here now and what would happen to it. The third line is
/// what makes this window honest, and it is written as a consequence ("your 12 saved wordings would
/// be replaced") rather than a warning symbol, because a symbol is a thing to click past.</para>
/// </summary>
internal sealed class BringInPackWindow : Window
{
    private readonly List<(PackPartChoice Choice, CheckBox Box)> _rows = [];
    private readonly TextBlock _summary;

    internal BringInPackWindow(IReadOnlyList<PackPartChoice> choices, DateTimeOffset packedOn, string fileName)
    {
        ArgumentNullException.ThrowIfNull(choices);

        Title = "Bring in a predecessor's pack";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = false;

        var heading = new TextBlock
        {
            Text = "What would you like to take?",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        };

        var whence = new TextBlock
        {
            Text = $"From {fileName}, packed up on {packedOn.ToLocalTime():d MMMM yyyy}.",
            FontSize = 18,
            TextWrapping = TextWrapping.Wrap,
        };

        var rows = new StackPanel { Spacing = 20 };
        foreach (PackPartChoice choice in choices)
        {
            var box = new CheckBox
            {
                Content = choice.Title,
                FontSize = 20,
                MinHeight = 44,
                IsChecked = !choice.WouldReplace,
            };
            AutomationProperties.SetName(box, AnnouncementFor(choice));
            box.IsCheckedChanged += (_, _) => Recount();
            _rows.Add((choice, box));

            var lines = new StackPanel
            {
                Margin = new Avalonia.Thickness(34, 0, 0, 0),
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = choice.Description, FontSize = 17, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = $"The pack has {choice.Incoming}.",
                        FontSize = 17,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            };

            if (choice.WouldReplace)
            {
                lines.Children.Add(new TextBlock
                {
                    Text = ReplacementLine(choice),
                    FontSize = 17,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            rows.Children.Add(new StackPanel { Spacing = 4, Children = { box, lines } });
        }

        _summary = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_summary, "What will be brought in");
        AutomationProperties.SetLiveSetting(_summary, AutomationLiveSetting.Polite);

        Button take = Wide("Bring these in", "Bring these in");
        take.IsDefault = true;
        take.Click += (_, _) =>
        {
            Chosen = [.. _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Choice.Id)];
            Close();
        };

        Button cancel = Wide("Take nothing", "Take nothing");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                heading,
                whence,
                rows,
                _summary,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { take, cancel },
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Recount();
    }

    /// <summary>
    /// What the user chose, or empty if they closed the window. Empty is a real answer here and not
    /// a missing one — "take nothing" is a thing somebody may well decide.
    /// </summary>
    internal IReadOnlyList<string> Chosen { get; private set; } = [];

    internal IReadOnlyList<(PackPartChoice Choice, CheckBox Box)> RowsForTest => _rows;

    internal string SummaryForTest => _summary.Text ?? "";

    internal void TickForTest(string partId, bool on)
    {
        foreach ((PackPartChoice choice, CheckBox box) in _rows.Where(r => r.Choice.Id == partId))
        {
            _ = choice;
            box.IsChecked = on;
        }
    }

    internal void TakeForTest() =>
        Chosen = [.. _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Choice.Id)];

    /// <summary>
    /// The consequence, spelled out. Named separately from the row because a screen reader reads the
    /// tick box's own name and would otherwise never reach the line that says what is at stake.
    /// </summary>
    private static string ReplacementLine(PackPartChoice choice) =>
        choice.Id == Core.Container.SuccessorPackParts.Roster
            ? $"You already have {choice.AlreadyHere} here. Taking this replaces them — a copy of what "
              + "you have now is kept, so it can be brought back."
            : $"You already have {choice.AlreadyHere} here. Taking this replaces them.";

    private static string AnnouncementFor(PackPartChoice choice) =>
        choice.WouldReplace
            ? $"{choice.Title}. The pack has {choice.Incoming}. {ReplacementLine(choice)}"
            : $"{choice.Title}. The pack has {choice.Incoming}. Nothing here would be replaced.";

    private void Recount()
    {
        int taking = _rows.Count(r => r.Box.IsChecked == true);
        int replacing = _rows.Count(r => r.Box.IsChecked == true && r.Choice.WouldReplace);

        _summary.Text = taking switch
        {
            0 => "Nothing is ticked, so nothing on this computer will change.",
            _ when replacing == 0 => $"{Things(taking)} will be brought in. Nothing here will be replaced.",
            _ => $"{Things(taking)} will be brought in, and {replacing} of them "
                 + $"{(replacing == 1 ? "replaces something" : "replace something")} you already have.",
        };
    }

    private static string Things(int n) => n == 1 ? "1 thing" : $"{n} things";

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
