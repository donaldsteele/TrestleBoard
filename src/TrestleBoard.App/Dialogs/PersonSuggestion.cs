using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// One person the address book can offer to a wizard (PLAN.md §11 M13). Handed to the window by the
/// shell — the wizard never reaches for a roster, which is §5's projection rule applied to the one
/// other place people's names could have leaked in.
/// </summary>
/// <param name="Id">The address-book id, so a phone written back lands on the right person.</param>
/// <param name="Name">The name as the book spells it. This is what goes on the page.</param>
/// <param name="Phone">His phone number, or null. Used to fill a blank Phone box, never to overwrite one.</param>
public sealed record PersonSuggestion(string Id, string Name, string? Phone);

/// <summary>
/// "Add someone from the address book…" — a searchable list of big rows, for the committee members
/// field (M13). Deliberately a plain list rather than a combo: twenty-odd names at 20pt scroll
/// better than they drop down, and every row is a 44pt target.
/// </summary>
internal sealed class PersonPickerDialog : Window
{
    private readonly ListBox _list;
    private readonly TextBlock _count;
    private readonly IReadOnlyList<PersonSuggestion> _people;

    internal PersonPickerDialog(IReadOnlyList<PersonSuggestion> people)
    {
        _people = people ?? throw new ArgumentNullException(nameof(people));

        Title = "Add someone from the address book";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, Title);

        var search = new TextBox
        {
            FontSize = 24,
            MinHeight = 48,
            Watermark = "Type a few letters",
        };
        AutomationProperties.SetName(search, "Search for a person");

        _count = new TextBlock { FontSize = 16, Margin = new Avalonia.Thickness(0, 4, 0, 8) };
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);

        _list = new ListBox { FontSize = 20 };
        AutomationProperties.SetName(_list, "People in your address book");

        Button add = MakeButton("Add this person");
        add.IsDefault = true;
        add.Click += (_, _) =>
        {
            Picked = _list.SelectedItem as PersonSuggestion;
            Close();
        };

        Button cancel = MakeButton("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();

        search.TextChanged += (_, _) => Filter(search.Text);
        _list.DoubleTapped += (_, _) =>
        {
            Picked = _list.SelectedItem as PersonSuggestion;
            Close();
        };

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Children = { search, _count },
                },
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    Children = { cancel, add },
                },
                _list,
            },
        };

        Filter(null);
    }

    /// <summary>Whoever the user chose, or null if they changed their mind.</summary>
    internal PersonSuggestion? Picked { get; private set; }

    /// <summary>Shows the picker and answers with the chosen person, or null.</summary>
    internal static async Task<PersonSuggestion?> PickAsync(Window owner, IReadOnlyList<PersonSuggestion> people)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new PersonPickerDialog(people);
        await dialog.ShowDialog(owner);
        return dialog.Picked;
    }

    private void Filter(string? query)
    {
        List<PersonSuggestion> shown = string.IsNullOrWhiteSpace(query)
            ? [.. _people]
            : [.. _people.Where(p => p.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))];

        _list.ItemsSource = shown;
        _list.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(PersonSuggestion.Name));
        _count.Text = shown.Count == 1 ? "1 person." : $"{shown.Count} people.";
        if (shown.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private static Button MakeButton(string text)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 160,
            FontWeight = FontWeight.Normal,
        };
        AutomationProperties.SetName(button, text);
        return (button).Action();
    }
}
