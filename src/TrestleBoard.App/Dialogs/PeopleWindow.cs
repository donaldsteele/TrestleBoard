using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Roster;
using TrestleBoard.Roster.Import;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The lodge address book (PLAN.md §11 M12).
///
/// The shape of this window follows from what the user is actually doing when they open it, which is
/// almost never "browse the membership": it is <em>"look somebody up and fix their phone number"</em>.
/// So the search box is the first thing focus lands in, the rows show
/// <c>Name — office — birthday</c> so the useful facts are readable without clicking, and the editor
/// on the right is a <b>single page</b> rather than a wizard. The wizard shape is right for entering
/// data you do not have yet; it is wrong for correcting one field of data you do.
///
/// The same form is reused for "Add a person", so it is learned once.
/// </summary>
public sealed class PeopleWindow : Window
{
    private readonly RosterService _roster;
    private readonly TextBox _search;
    private readonly TextBlock _count;
    private readonly ListBox _list;
    private readonly TextBox _name;
    private readonly TextBox _birthday;
    private readonly TextBox _phone;
    private readonly TextBox _email;
    private readonly TextBox _office;
    private readonly ComboBox _degreeKind;
    private readonly TextBox _degreeDate;
    private readonly CheckBox _active;
    private readonly TextBlock _status;
    private readonly Button _delete;

    private IReadOnlyList<Member> _shown = [];
    private string? _selectedId;
    private bool _adding;
    private bool _updating;

    private static readonly (string? Kind, string Label)[] DegreeKinds =
    [
        (null, "Not said"),
        (DegreeKind.Raised, "Raised"),
        (DegreeKind.Initiated, "Initiated"),
    ];

    public PeopleWindow(RosterService roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;

        Title = "People";
        Width = 1080;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Your lodge address book");

        _search = new TextBox
        {
            FontSize = 24,
            MinHeight = 52,
            Watermark = "Type a few letters of a name",
        };
        AutomationProperties.SetName(_search, "Search for a person by name");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                RefreshList();
            }
        };

        // A polite live region: a search that silently finds nothing is indistinguishable from a
        // search that is still thinking (PLAN.md §6).
        _count = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_count, "How many people were found");
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);

        _list = new ListBox { FontSize = 18 };
        AutomationProperties.SetName(_list, "The people in your address book");
        _list.SelectionChanged += (_, _) => OnListSelectionChanged();

        _name = Field("Name");
        _birthday = Field("Birthday, as a month and a day, like 7/4");
        _phone = Field("Telephone number");
        _email = Field("Email address");
        _office = Field("Lodge office");
        _degreeDate = Field("The date he was raised or initiated");

        _degreeKind = new ComboBox
        {
            FontSize = 20,
            MinHeight = 44,
            Width = 260,
            ItemsSource = DegreeKinds.Select(k => k.Label).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(_degreeKind, "Raised or initiated");

        _active = new CheckBox { Content = "Still a member", FontSize = 20, MinHeight = 44, IsChecked = true };
        AutomationProperties.SetName(_active, "Still a member");

        _status = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        var add = Action("Add a person", "Add a person to your address book");
        add.Click += (_, _) => BeginAdd();

        var save = Action("Save this person", "Save the details on this form");
        save.Click += (_, _) => Save();

        _delete = Action("Remove this person…", "Remove this person from your address book");
        _delete.Click += async (_, _) => await ConfirmDeleteAsync();

        var close = Action("Close", "Close the address book");
        close.IsCancel = true;
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new Avalonia.Thickness(24),
            ColumnDefinitions = new ColumnDefinitions("2*,3*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                Place(SearchPanel(), 0, 0),
                Place(_list, 0, 1),
                Place(FormPanel(), 1, 0, rowSpan: 2),
                Place(ButtonRow(add, save, _delete, close), 0, 2, columnSpan: 2),
            },
        };

        // The book can be changed from under this window by an import; the list has to follow.
        _roster.Changed += (_, _) => RefreshList();
        Opened += (_, _) => _search.Focus();

        RefreshList();
        Clear();
    }

    /// <summary>The rows the list is showing, for the headless tests.</summary>
    internal IReadOnlyList<Member> ShownForTest => _shown;

    internal string CountTextForTest => _count.Text ?? string.Empty;

    internal string StatusTextForTest => _status.Text ?? string.Empty;

    internal TextBox SearchBoxForTest => _search;

    /// <summary>Types into the form and saves, as a person would. Used by the headless tests.</summary>
    internal void AddForTest(string name, string birthday, string phone)
    {
        BeginAdd();
        _name.Text = name;
        _birthday.Text = birthday;
        _phone.Text = phone;
        Save();
    }

    internal void SelectForTest(string memberId)
    {
        _selectedId = memberId;
        _list.SelectedIndex = _shown.ToList().FindIndex(m => m.Id == memberId);
    }

    internal void DeleteSelectedForTest() => DeleteSelected();

    private static TextBox Field(string label) => new()
    {
        FontSize = 20,
        MinHeight = 44,
        Tag = label,
    };

    private static Button Action(string content, string automationName)
    {
        var button = new Button { Content = content, FontSize = 20, MinHeight = 44, MinWidth = 180 };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static Control Place(Control control, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        Grid.SetColumn(control, column);
        Grid.SetRow(control, row);
        Grid.SetColumnSpan(control, columnSpan);
        Grid.SetRowSpan(control, rowSpan);
        return control;
    }

    private StackPanel SearchPanel() => new StackPanel
    {
        Spacing = 8,
        Margin = new Avalonia.Thickness(0, 0, 16, 12),
        Children = { _search, _count },
    };

    private ScrollViewer FormPanel()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(16, 0, 0, 12) };
        foreach (TextBox box in new[] { _name, _birthday, _phone, _email, _office })
        {
            panel.Children.Add(Label((string)box.Tag!));
            AutomationProperties.SetName(box, (string)box.Tag!);
            panel.Children.Add(box);
        }

        panel.Children.Add(Label("Raised or initiated"));
        panel.Children.Add(_degreeKind);
        panel.Children.Add(Label((string)_degreeDate.Tag!));
        AutomationProperties.SetName(_degreeDate, (string)_degreeDate.Tag!);
        panel.Children.Add(_degreeDate);
        panel.Children.Add(_active);
        panel.Children.Add(_status);

        // A lodge on two laptops has two address books and no way to merge them; saying so here is
        // the only place the user will ever read it (PLAN.md flagged uncertainties, M12).
        panel.Children.Add(new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Text = "This address book is kept on this computer only. To share it with somebody else "
                + "on the committee, use People, then Save as a spreadsheet, and send them the file.",
        });

        return new ScrollViewer { Content = panel };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeight.Bold,
    };

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        foreach (Button button in buttons)
        {
            panel.Children.Add(button);
        }

        return panel;
    }

    private void RefreshList()
    {
        string search = (_search.Text ?? string.Empty).Trim();
        IReadOnlyList<Member> all = _roster.Book.InListOrder();
        _shown = search.Length == 0
            ? all
            : all.Where(m => Matches(m, search)).ToList();

        _updating = true;
        _list.ItemsSource = _shown.Select(Describe).ToList();
        _list.SelectedIndex = _shown.ToList().FindIndex(m => m.Id == _selectedId);
        _updating = false;

        _count.Text = (search.Length, _shown.Count) switch
        {
            (0, 0) => "Your address book is empty. Press Add a person, or import a list you already have.",
            (0, 1) => "1 person.",
            (0, int n) => $"{n} people.",
            (_, 0) => $"Nobody matches \"{search}\".",
            (_, 1) => $"1 person matches \"{search}\".",
            (_, int n) => $"{n} people match \"{search}\".",
        };

        _delete.IsEnabled = _selectedId is not null;
    }

    /// <summary>
    /// Matched on the normalised name as well as the written one, so typing "placeholder a" finds
    /// "A. Placeholder" — the same comparison the importer uses, for the same reason.
    /// </summary>
    private static bool Matches(Member member, string search) =>
        member.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || NameMatching.Normalise(member.DisplayName)
            .Contains(NameMatching.Normalise(search), StringComparison.Ordinal)
        || (member.Office?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || (member.Phone?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || (member.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>"A. Placeholder — Worshipful Master — 7/4". The facts worth seeing without clicking.</summary>
    private static string Describe(Member member)
    {
        var parts = new List<string> { member.DisplayName };
        if (!string.IsNullOrWhiteSpace(member.Office))
        {
            parts.Add(member.Office);
        }

        if (member.HasBirthday)
        {
            parts.Add(member.BirthdayText);
        }

        return string.Join(" — ", parts);
    }

    private void OnListSelectionChanged()
    {
        if (_updating)
        {
            return;
        }

        int index = _list.SelectedIndex;
        if (index < 0 || index >= _shown.Count)
        {
            return;
        }

        _adding = false;
        Show(_shown[index]);
    }

    private void Show(Member member)
    {
        _selectedId = member.Id;
        _name.Text = member.DisplayName;
        _birthday.Text = member.BirthdayText;
        _phone.Text = member.Phone ?? string.Empty;
        _email.Text = member.Email ?? string.Empty;
        _office.Text = member.Office ?? string.Empty;
        _degreeDate.Text = member.DegreeDate ?? string.Empty;
        _degreeKind.SelectedIndex = Math.Max(0, Array.FindIndex(DegreeKinds, k => k.Kind == member.DegreeKind));
        _active.IsChecked = member.IsActive;
        _delete.IsEnabled = true;
        _status.Text = string.Empty;
    }

    private void Clear()
    {
        _selectedId = null;
        foreach (TextBox box in new[] { _name, _birthday, _phone, _email, _office, _degreeDate })
        {
            box.Text = string.Empty;
        }

        _degreeKind.SelectedIndex = 0;
        _active.IsChecked = true;
        _delete.IsEnabled = false;
    }

    private void BeginAdd()
    {
        _adding = true;
        Clear();
        _updating = true;
        _list.SelectedIndex = -1;
        _updating = false;
        _status.Text = "Type the new person's details, then press Save this person.";
        _name.Focus();
    }

    private void Save()
    {
        string name = (_name.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            _status.Text = "Type a name first. Everything else is optional.";
            _name.Focus();
            return;
        }

        string birthdayText = (_birthday.Text ?? string.Empty).Trim();
        int? month = null;
        int? day = null;
        if (birthdayText.Length > 0)
        {
            if (!FieldValues.TryReadBirthday(birthdayText, out int m, out int d))
            {
                // Refused in words, with an example — never a red box and nothing else (PLAN.md §6).
                _status.Text = "That birthday could not be read. Write it as a month and a day, like 7/4.";
                _birthday.Focus();
                return;
            }

            month = m;
            day = d;
        }

        Member existing = (_selectedId is not null ? _roster.Book.Find(_selectedId) : null) ?? new Member();
        var member = existing with
        {
            Id = _adding || _selectedId is null ? _roster.NextMemberId() : _selectedId,
            DisplayName = name,
            BirthMonth = month,
            BirthDay = day,
            Phone = Empty(_phone),
            Email = Empty(_email),
            Office = Empty(_office),
            DegreeDate = Empty(_degreeDate),
            DegreeKind = DegreeKinds[Math.Max(0, _degreeKind.SelectedIndex)].Kind,
            IsActive = _active.IsChecked ?? true,
        };

        bool adding = _adding || _selectedId is null;
        _roster.Save(member, adding ? $"Add {name}" : $"Change {name}");
        _adding = false;
        _selectedId = member.Id;
        RefreshList();
        _status.Text = adding ? $"{name} was added." : $"{name} was saved.";
    }

    private static string? Empty(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    /// <summary>
    /// Deletion is the one destructive act in this window, so it asks — and the confirm answers the
    /// question the user is actually worried about, which is whether the newsletters they have
    /// already made are about to change.
    /// </summary>
    private async Task ConfirmDeleteAsync()
    {
        if (_selectedId is null || _roster.Book.Find(_selectedId) is not { } member)
        {
            return;
        }

        var yes = new Button { Content = "Yes, remove them", FontSize = 20, MinHeight = 44, MinWidth = 200 };
        AutomationProperties.SetName(yes, "Yes, remove them");
        var no = new Button
        {
            Content = "No, keep them",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
            IsCancel = true,
        };
        AutomationProperties.SetName(no, "No, keep them");

        var dialog = new Window
        {
            Title = "Remove this person?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(28),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        FontSize = 20,
                        MaxWidth = 520,
                        TextWrapping = TextWrapping.Wrap,
                        Text = $"Remove {member.DisplayName} from your address book? "
                            + "Newsletters you already made will not change. "
                            + "You can put this back with People, then Undo the last change.",
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { no, yes },
                    },
                },
            },
        };
        AutomationProperties.SetName(dialog, "Remove this person?");

        bool confirmed = false;
        yes.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        no.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
        if (confirmed)
        {
            DeleteSelected();
        }
    }

    private void DeleteSelected()
    {
        if (_selectedId is null || _roster.Book.Find(_selectedId) is not { } member)
        {
            return;
        }

        _roster.Delete(member.Id, $"Remove {member.DisplayName}");
        Clear();
        RefreshList();
        _status.Text = $"{member.DisplayName} was removed. Undo the last change puts them back.";
    }
}
