using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Roster;
using TrestleBoard.Roster.Import;
using TrestleBoard.App.Theme;

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

    // ---- M88 ------------------------------------------------------------------------------------
    private readonly TextBox _birthYear;
    private readonly TextBox _memberNumber;
    private readonly TextBox _masonicTitle;
    private readonly TextBox _addressLine1;
    private readonly TextBox _addressLine2;
    private readonly TextBox _city;
    private readonly TextBox _state;
    private readonly TextBox _zip;
    private readonly TextBox _homePhone;
    private readonly TextBox _mobilePhone;
    private readonly TextBox _workPhone;
    private readonly TextBox _spouseName;
    private readonly TextBox _spouseEmail;
    private readonly TextBox _spousePhone;
    private readonly TextBox _notes;
    private readonly CheckBox _undeliverable;
    private readonly TabControl _tabs;

    /// <summary>
    /// Every plain text box on this form, once (M88).
    ///
    /// <para><b>This table exists because the alternative had seven copies of the same list.</b> A
    /// field used to be declared, laid out, searched, signed for dirty-tracking, loaded, cleared and
    /// saved in seven separate places, none of them checked by anything. That was survivable at
    /// seven fields; at twenty-two it is a promise nobody can keep, and the failure is silent — a
    /// box the user types in that quietly is not saved. Everything below reads this list, and
    /// <c>PeopleFormTests</c> fails if a stored text property on <see cref="Member"/> is not in it.
    /// </para>
    /// </summary>
    private readonly List<FormField> _fields = [];

    private readonly CheckBox _active;
    private readonly CheckBox _passed;
    private readonly TextBox _passedOn;
    private readonly Control _passedOnLabel;
    private readonly StackPanel _groupsPanel;
    private readonly List<CheckBox> _groupBoxes = [];
    private readonly TextBlock _status;
    private readonly Button _delete;

    /// <summary>
    /// Whether the shell has a newsletter a memorial could be written in. M73(a), gate 26: the
    /// predicate that decides whether the card <i>offers</i> to write one is the shell's own
    /// precondition for writing one, handed in — not a second opinion restated here.
    /// </summary>
    private readonly Func<bool> _canWriteAMemorial;

    /// <summary>
    /// Brothers saved as passed during this visit whose card has not been put on screen yet.
    ///
    /// <para>M73(a): <see cref="Save"/> is synchronous and is called from close handlers, so it
    /// cannot put a modal card up itself — it used to try, with <c>_ = OfferAMemorialAsync(name)</c>,
    /// and on the close path the window went away while its own child was being raised and the
    /// request was lost without a word. It queues here instead, and every path out of the window
    /// drains the queue before it goes.</para>
    /// </summary>
    private readonly List<string> _memorialsToOffer = [];

    /// <summary>Brothers the user has said yes to, in the order they said yes.</summary>
    private readonly List<string> _memorialsRequested = [];

    private IReadOnlyList<Member> _shown = [];
    private string? _selectedId;
    private bool _adding;
    private bool _updating;

    /// <summary>
    /// M40: what the form held when it was last filled in from the address book, or last saved. The
    /// form is dirty when it no longer matches (review §14.4).
    /// </summary>
    private string _formAsLoaded = string.Empty;

    /// <summary>M40: the close has already asked about a pending edit and been answered.</summary>
    private bool _closeAgreed;

    /// <summary>
    /// The degrees the combo offers (M88).
    ///
    /// <para>M12 offered "Raised" and "Initiated" — which ceremony the date beside it records. The
    /// lodge's own list answers a different question, and the one it actually keeps: how far a man
    /// has come. A Fellowcraft had nowhere to sit in the old pair, and this lodge has one.</para>
    ///
    /// <para>The older field is not shown anywhere. It is still stored and still exported, so a book
    /// written before M88 loses nothing, and a man recorded as "raised" reads as a Master Mason the
    /// first time his card is normalised.</para>
    /// </summary>
    private static readonly (string? Kind, string Label)[] DegreeKinds =
    [
        (null, "Not said"),
        (Degree.EnteredApprentice, "Entered Apprentice"),
        (Degree.Fellowcraft, "Fellowcraft"),
        (Degree.MasterMason, "Master Mason"),
    ];

    public PeopleWindow(RosterService roster, Func<bool>? canWriteAMemorial = null)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;
        _canWriteAMemorial = canWriteAMemorial ?? (static () => true);

        Title = "People";

        // Grown for M88's second tab, with floors under it. The form column used to hold seven
        // fields; it now holds a tab strip, up to fifteen rows and the button row below them, and at
        // the 200% scale the settings offer, a window that can be dragged smaller than its own
        // buttons is a window somebody can lose the Save button in.
        Width = 1160;
        Height = 820;
        MinWidth = 900;
        MinHeight = 620;
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

        // M88's boxes. The year sits under the birthday and says out loud what it is for, because
        // "why does it want my age?" is the question M12 refused the field over.
        _birthYear = Field("Year he was born, like 1957 — never printed in the newsletter");
        _memberNumber = Field("Lodge member number");
        _masonicTitle = Field("Letters after his name, like PM");
        _addressLine1 = Field("Street address");
        _addressLine2 = Field("Flat, unit or second line");
        _city = Field("Town or city");
        _state = Field("State");
        _zip = Field("ZIP code");
        _homePhone = Field("Home telephone");
        _mobilePhone = Field("Mobile telephone");
        _workPhone = Field("Work telephone");
        _spouseName = Field("His wife's name");
        _spouseEmail = Field("Her email address");
        _spousePhone = Field("Her telephone number");
        _notes = Field("Notes");

        _undeliverable = new CheckBox
        {
            Content = "Post to this address comes back undelivered",
            FontSize = 20,
            MinHeight = 44,
        };
        AutomationProperties.SetName(_undeliverable, "Post to this address comes back undelivered");
        // M40: the birthday field has carried an example since M12 and this one never did, though
        // it is the harder of the two to guess - a date with a year in it, in a window where the
        // other date deliberately has none.
        _degreeDate = Field("The date he was raised or initiated, like 3/14/1998");

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

        // M55. A tick box and a date, not a status list: the owner chose two states, and the date
        // is the thing the committee actually knows. Un-ticking "Still a member" and recording a
        // brother as passed are different acts and read as different acts.
        _passed = new CheckBox
        {
            Content = "Passed to the Celestial Lodge",
            FontSize = 20,
            MinHeight = 44,
        };
        AutomationProperties.SetName(_passed, "Passed to the Celestial Lodge");
        _passed.IsCheckedChanged += (_, _) => UpdatePassedRow();

        _passedOn = new TextBox
        {
            FontSize = 20,
            MinHeight = 44,
            Width = 260,
            Watermark = "2026-09-14",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(_passedOn, "The date he passed");

        _passedOnLabel = Label("The date, if you know it");
        _groupsPanel = new StackPanel { Spacing = 6 };
        AutomationProperties.SetName(_groupsPanel, "Groups");

        _status = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        // The one list of fields (M88). Searchable is what somebody types into the box above the
        // list expecting to find a person by: a name, a number they can read off a card, a place.
        _fields =
        [
            Text(_name, nameof(Member.DisplayName), Basics, searchable: true,
                m => m.DisplayName, (m, v) => m with { DisplayName = v ?? string.Empty }),
            Text(_phone, nameof(Member.Phone), Basics, searchable: true,
                m => m.Phone, (m, v) => m with { Phone = v }),
            Text(_email, nameof(Member.Email), Basics, searchable: true,
                m => m.Email, (m, v) => m with { Email = v }),
            Text(_office, nameof(Member.Office), Basics, searchable: true,
                m => m.Office, (m, v) => m with { Office = v }),

            Text(_memberNumber, nameof(Member.MemberNumber), More, searchable: true,
                m => m.MemberNumber, (m, v) => m with { MemberNumber = v }),
            Text(_masonicTitle, nameof(Member.MasonicTitle), More, searchable: false,
                m => m.MasonicTitle, (m, v) => m with { MasonicTitle = v }),
            Text(_addressLine1, nameof(Member.AddressLine1), More, searchable: true,
                m => m.AddressLine1, (m, v) => m with { AddressLine1 = v }),
            Text(_addressLine2, nameof(Member.AddressLine2), More, searchable: false,
                m => m.AddressLine2, (m, v) => m with { AddressLine2 = v }),
            Text(_city, nameof(Member.City), More, searchable: true,
                m => m.City, (m, v) => m with { City = v }),
            Text(_state, nameof(Member.State), More, searchable: false,
                m => m.State, (m, v) => m with { State = v }),
            Text(_zip, nameof(Member.Zip), More, searchable: false,
                m => m.Zip, (m, v) => m with { Zip = v }),
            Text(_homePhone, nameof(Member.HomePhone), More, searchable: true,
                m => m.HomePhone, (m, v) => m with { HomePhone = v }),
            Text(_mobilePhone, nameof(Member.MobilePhone), More, searchable: true,
                m => m.MobilePhone, (m, v) => m with { MobilePhone = v }),
            Text(_workPhone, nameof(Member.WorkPhone), More, searchable: true,
                m => m.WorkPhone, (m, v) => m with { WorkPhone = v }),
            Text(_spouseName, nameof(Member.SpouseName), More, searchable: true,
                m => m.SpouseName, (m, v) => m with { SpouseName = v }),
            Text(_spouseEmail, nameof(Member.SpouseEmail), More, searchable: false,
                m => m.SpouseEmail, (m, v) => m with { SpouseEmail = v }),
            Text(_spousePhone, nameof(Member.SpousePhone), More, searchable: false,
                m => m.SpousePhone, (m, v) => m with { SpousePhone = v }),
            Text(_notes, nameof(Member.Notes), More, searchable: false,
                m => m.Notes, (m, v) => m with { Notes = v }),
        ];

        _tabs = new TabControl { FontSize = 20 };
        AutomationProperties.SetName(_tabs, "Which part of this person's details");

        var add = Action("Add a person", "Add a person to your address book");
        add.Click += async (_, _) => await BeginAddAsync();

        var save = Action("Save this person", "Save the details on this form");
        save.Click += async (_, _) =>
        {
            Save();
            await OfferAnyMemorialsAsync();
        };

        // M40: the one button in this window that takes something away, and it used to look
        // exactly like "Save this person" beside it (review §14.4).
        _delete = Action("Remove this person…", "Remove this person from your address book");
        _delete.Destructive();
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
        //
        // Unsubscribed on close, because RosterService lives as long as the app does: a handler
        // left attached kept every People window that had ever been opened alive for the rest of
        // the session, and ran RefreshList on all of the dead ones at every roster change (review
        // §14.2). Named rather than a lambda so there is something to detach.
        _roster.Changed += OnRosterChanged;
        Closed += (_, _) => _roster.Changed -= OnRosterChanged;

        // M40: closing the window is the third way to walk away from an unsaved edit, and it was
        // the quietest of them.
        Closing += OnWindowClosing;
        Opened += (_, _) => _search.Focus();

        RefreshList();
        Clear();
    }

    private void OnRosterChanged(object? sender, EventArgs e) => RefreshList();

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeAgreed)
        {
            return;
        }

        if (!FormHasUnsavedEdits() && _memorialsToOffer.Count == 0)
        {
            return;
        }

        if (FormHasUnsavedEdits() && AnswerWithoutAsking is { } answer)
        {
            if (answer == PendingEdit.Stay || (answer == PendingEdit.Save && !Save()))
            {
                e.Cancel = true;
                return;
            }

            // The save may have queued a memorial card. M73(a): it is asked BEFORE this window
            // goes, never as a child of a window that is already closing.
            if (_memorialsToOffer.Count == 0)
            {
                return;
            }
        }

        e.Cancel = true;
        _ = FinishClosingAsync();
    }

    private async Task FinishClosingAsync()
    {
        try
        {
            if (FormHasUnsavedEdits())
            {
                PendingEdit answer = AnswerWithoutAsking ?? await AskAboutPendingEditAsync();
                if (answer == PendingEdit.Stay || (answer == PendingEdit.Save && !Save()))
                {
                    return;
                }
            }

            await OfferAnyMemorialsAsync();

            _closeAgreed = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = $"That could not be finished, so nothing has changed. ({ex.Message})";
        }
    }

    /// <summary>What to do about an edit that has been typed but not saved (M40).</summary>
    public enum PendingEdit
    {
        /// <summary>Write it, then go where the user was going.</summary>
        Save,

        /// <summary>Throw it away and go anyway.</summary>
        Discard,

        /// <summary>Stay on this person with the edit still on the form.</summary>
        Stay,
    }

    /// <summary>
    /// Set by tests in place of the three-button dialog, which cannot be answered headlessly. Null
    /// means "ask the user"; a value means "this is what they would have said".
    /// </summary>
    internal PendingEdit? PendingEditAnswerForTest { get; set; }

    /// <summary>
    /// The answer to use without asking, or null to put the question on screen. A headless run
    /// cannot answer a modal dialog, so under MainWindow's test flag an unanswered question means
    /// "carry on" - the same flag that keeps the start screen and the update check out of a test.
    /// </summary>
    private PendingEdit? AnswerWithoutAsking =>
        PendingEditAnswerForTest ?? (MainWindow.SuppressStartupForTest ? PendingEdit.Discard : null);

    /// <summary>The rows the list is showing, for the headless tests.</summary>
    internal IReadOnlyList<Member> ShownForTest => _shown;

    internal bool FormHasUnsavedEditsForTest => FormHasUnsavedEdits();

    internal string? SelectedIdForTest => _selectedId;

    internal string PhoneTextForTest => _phone.Text ?? string.Empty;

    /// <summary>Types a telephone number into the form without saving it, as a person would.</summary>
    internal void TypePhoneForTest(string phone)
    {
        _phone.Text = phone;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    internal string CountTextForTest => _count.Text ?? string.Empty;

    internal string StatusTextForTest => _status.Text ?? string.Empty;

    internal TextBox SearchBoxForTest => _search;

    // ---- M88's two tabs, as the tests reach them -------------------------------------------------

    /// <summary>Which tab is showing: 0 "The basics", 1 "Address and more".</summary>
    internal int SelectedTabForTest
    {
        get => _tabs.SelectedIndex;
        set => _tabs.SelectedIndex = value;
    }

    /// <summary>Types into one of the boxes on the second tab, by the member property it edits.</summary>
    internal void TypeForTest(string property, string text)
    {
        FormField field = _fields.First(f => f.Property == property);
        field.Box.Text = text;
    }

    internal string TextForTest(string property) =>
        _fields.First(f => f.Property == property).Box.Text ?? string.Empty;

    internal TextBox BoxForTest(string property) => _fields.First(f => f.Property == property).Box;

    internal TextBox BirthYearBoxForTest => _birthYear;

    internal bool SaveForTest() => Save();

    /// <summary>Types into the form and saves, as a person would. Used by the headless tests.</summary>
    internal void AddForTest(string name, string birthday, string phone)
    {
        BeginAdd();
        _name.Text = name;
        _birthday.Text = birthday;
        _phone.Text = phone;
        Save();
    }

    /// <summary>
    /// Clicks a name in the list, through the SAME path a person's click takes — M40 put a question
    /// on that path, and a test hook that stepped around it would be testing the wrong door.
    /// </summary>
    internal void SelectForTest(string memberId) =>
        _list.SelectedIndex = IndexOf(memberId);

    internal void DeleteSelectedForTest() => DeleteSelected();

    /// <summary>Ticks "he has passed" on the form, leaving the edit unsaved as a person's would.</summary>
    internal void TickPassedForTest()
    {
        _passed.IsChecked = true;
        UpdatePassedRow();
    }

    /// <summary>
    /// Presses "Add a person" — the button's own path, drain and all, which is what M74 (f) is
    /// about. <see cref="AddForTest"/> below goes straight to <c>BeginAdd</c> and does not.
    /// </summary>
    internal Task PressAddForTest() => BeginAddAsync();

    /// <summary>Presses "Save this person", then answers any memorial card the save raises.</summary>
    internal async Task SaveAndOfferForTest()
    {
        Save();
        await OfferAnyMemorialsAsync();
    }

    private static TextBox Field(string label) => new()
    {
        FontSize = 20,
        MinHeight = 44,
        Tag = label,
    };

    private static Button Action(string content, string automationName)
    {
        var button = new Button { Content = content, FontSize = 20, MinHeight = 44, MinWidth = 180 };
        button.Action();
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

    /// <summary>
    /// The form, in two tabs (M88).
    ///
    /// <para><b>The first tab is M12's form, unmoved.</b> Somebody who opened this window to correct
    /// a telephone number sees exactly what they saw before, in the same order — that was the whole
    /// argument for a single screen, and it is kept. What the second tab holds is everything the
    /// lodge's own member system knows, which used to mean keeping a second list in another program.
    /// </para>
    ///
    /// <para>The status line and the buttons sit <em>outside</em> the tabs, so a refusal is readable
    /// whichever tab is showing — and a refusal about a field on the other tab switches to it before
    /// it takes the focus, because focusing a control the reader cannot see is worse than silence.
    /// </para>
    /// </summary>
    private Grid FormPanel()
    {
        _tabs.ItemsSource = new[]
        {
            Tab("The basics", BasicsPanel()),
            Tab("Address and more", MorePanel()),
        };
        _tabs.SelectedIndex = 0;

        // A grid rather than a stack, so the tabs take the height that is left over and the two
        // things below them keep theirs. Stacked, the tab body took only its natural height and the
        // fields past it were cut off with empty window underneath them.
        var footnote = new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,

            // A lodge on two laptops has two address books and no way to merge them; saying so here
            // is the only place the user will ever read it (PLAN.md flagged uncertainties, M12).
            Text = "This address book is kept on this computer only. To share it with somebody else "
                + "on the committee, use People, then Save as a spreadsheet, and send them the file.",
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        };

        var grid = new Grid
        {
            Margin = new Avalonia.Thickness(16, 0, 0, 12),
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
        };
        grid.Children.Add(Place(_tabs, 0, 0));
        grid.Children.Add(Place(_status, 0, 1));
        grid.Children.Add(Place(footnote, 0, 2));
        return grid;
    }

    private static TabItem Tab(string header, Control body)
    {
        var item = new TabItem
        {
            Header = header,
            FontSize = 20,
            MinHeight = 44,
            Content = new ScrollViewer { Content = body },
        };
        AutomationProperties.SetName(item, header);
        return item;
    }

    private StackPanel BasicsPanel()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(8) };
        AddField(panel, _name);
        AddField(panel, _birthday);
        AddField(panel, _birthYear);
        AddField(panel, _phone);

        // M88: three numbers are stored now, and exactly one of them is printed. Saying which, here,
        // is cheaper than a committee wondering why the officers table shows the wrong one.
        panel.Children.Add(new TextBlock
        {
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            Text = "This is the number printed in the newsletter. His home, mobile and work numbers "
                + "are on the next tab.",
        });

        AddField(panel, _email);
        AddField(panel, _office);

        panel.Children.Add(Label("Highest degree"));
        panel.Children.Add(_degreeKind);
        AddField(panel, _degreeDate);
        panel.Children.Add(_active);
        panel.Children.Add(_passed);
        panel.Children.Add(_passedOnLabel);
        panel.Children.Add(_passedOn);
        panel.Children.Add(Label("Groups"));
        panel.Children.Add(_groupsPanel);
        return panel;
    }

    private StackPanel MorePanel()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(8) };
        foreach (FormField field in _fields.Where(f => f.Section == More))
        {
            AddField(panel, field.Box);
            if (field.Box == _zip)
            {
                panel.Children.Add(_undeliverable);
            }
        }

        return panel;
    }

    /// <summary>One labelled box, with the label's words repeated onto the box for a screen reader.</summary>
    private static void AddField(StackPanel panel, TextBox box)
    {
        panel.Children.Add(Label((string)box.Tag!));
        AutomationProperties.SetName(box, (string)box.Tag!);
        panel.Children.Add(box);
    }

    private const RosterFieldSection Basics = RosterFieldSection.TheBasics;

    private const RosterFieldSection More = RosterFieldSection.AddressAndMore;

    /// <summary>
    /// One plain text box and everything the window needs to know about it (M88): which member
    /// property it is (by name, so a test can hold the form to the model), which tab it lives on,
    /// whether the search box looks at it, and how to read and write it.
    /// </summary>
    private sealed record FormField(
        string Property,
        RosterFieldSection Section,
        bool Searchable,
        Func<Member, string?> Read,
        Func<Member, string?, Member> Write,
        TextBox Box);

    private static FormField Text(
        TextBox box,
        string property,
        RosterFieldSection section,
        bool searchable,
        Func<Member, string?> read,
        Func<Member, string?, Member> write) =>
        new(property, section, searchable, read, write, box);

    /// <summary>Which member properties this form can edit — the completeness test reads it.</summary>
    internal IReadOnlyList<string> FormFieldsForTest => [.. _fields.Select(f => f.Property)];

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
    /// <summary>
    /// What the search box looks at: the name, however it is written, and every field marked
    /// searchable in <see cref="_fields"/> — which from M88 includes his member number, his other
    /// telephone numbers, his street and his town, because those are things a committee member
    /// genuinely searches by ("who lives on Example Street?").
    /// </summary>
    private bool Matches(Member member, string search) =>
        member.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
        || NameMatching.Normalise(member.DisplayName)
            .Contains(NameMatching.Normalise(search), StringComparison.Ordinal)
        || _fields.Any(f => f.Searchable
            && (f.Read(member)?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));

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

    /// <summary>
    /// M40, review section 14.4. Clicking a different name used to overwrite the form with no
    /// question asked, so a corrected telephone number that had been typed but not saved simply
    /// vanished - and the person who lost it had no way to know, because the list had done exactly
    /// what they clicked on. This is the address book, so what was being lost was a real member's
    /// real details (PLAN.md section 0 rule 5).
    ///
    /// <para>Per-person saving stays. It is the right shape for "look somebody up and fix their
    /// phone number", and the fix for a silent loss is a question, not a different model.</para>
    /// </summary>
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

        Member target = _shown[index];
        if (target.Id == _selectedId)
        {
            return;
        }

        if (!FormHasUnsavedEdits())
        {
            _adding = false;
            Show(target);
            return;
        }

        // The list is put back FIRST, so the window never sits showing one person's name over
        // another person's details while the question is on screen.
        RestoreSelection();

        if (AnswerWithoutAsking is { } answer)
        {
            Resolve(answer, target.Id);
            return;
        }

        _ = ResolveAfterAskingAsync(target.Id);
    }

    /// <summary>Puts the list back on whoever the form is showing, without re-entering the handler.</summary>
    private void RestoreSelection()
    {
        _updating = true;
        _list.SelectedIndex = _selectedId is { } id ? IndexOf(id) : -1;
        _updating = false;
    }

    private int IndexOf(string memberId)
    {
        for (int i = 0; i < _shown.Count; i++)
        {
            if (_shown[i].Id == memberId)
            {
                return i;
            }
        }

        return -1;
    }

    private async Task ResolveAfterAskingAsync(string? goingTo)
    {
        try
        {
            Resolve(await AskAboutPendingEditAsync(), goingTo);
            await OfferAnyMemorialsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Staying put is the outcome that keeps the edit, so a failure here says so and does
            // nothing else. An exception escaping would take the window down with the very details
            // this question exists to protect.
            _status.Text = $"That could not be finished, so nothing has changed. ({ex.Message})";
        }
    }

    /// <summary>
    /// Carries out the answer. <paramref name="goingTo"/> is a member id when the user was switching
    /// people, and null when they were adding somebody or closing the window.
    /// </summary>
    private void Resolve(PendingEdit answer, string? goingTo)
    {
        switch (answer)
        {
            case PendingEdit.Stay:
                RestoreSelection();
                return;

            case PendingEdit.Save when !Save():
                // The form was refused - an unreadable birthday, an empty name. The reason is
                // already in the status line, and going anywhere now would throw away the edit the
                // user has just been asked to correct.
                RestoreSelection();
                return;

            default:
                break;
        }

        _adding = false;
        if (goingTo is not null && _roster.Book.Find(goingTo) is { } member)
        {
            Show(member);
        }

        RestoreSelection();
    }

    /// <summary>
    /// The same three-button question the newsletter asks when something is about to replace unsaved
    /// work (M24), in the same words, because it is the same question about a different thing.
    /// </summary>
    private async Task<PendingEdit> AskAboutPendingEditAsync()
    {
        PendingEdit answer = PendingEdit.Stay;
        string who = (_name.Text ?? string.Empty).Trim();
        if (who.Length == 0)
        {
            who = "this person";
        }

        var save = new Button
        {
            Content = "Save this person",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        AutomationProperties.SetName(save, "Save this person");

        var discard = new Button { Content = "Do not save", FontSize = 20, MinHeight = 44, MinWidth = 170 };
        discard.Action();
        AutomationProperties.SetName(discard, "Do not save these changes");

        var stay = new Button
        {
            Content = "Go back",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 150,
            IsCancel = true,
        };
        stay.Action();
        AutomationProperties.SetName(stay, "Go back to the form");

        var dialog = new Window
        {
            Title = "You have changes you have not saved",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(28),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"You have changed {who} but not saved it yet.",
                        FontSize = 20,
                        FontWeight = FontWeight.Bold,
                        MaxWidth = 480,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "If you carry on without saving, those changes will be gone.",
                        FontSize = 18,
                        MaxWidth = 480,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { stay, discard, save },
                    },
                },
            },
        };
        AutomationProperties.SetName(dialog, "You have changes you have not saved");

        save.Click += (_, _) => { answer = PendingEdit.Save; dialog.Close(); };
        discard.Click += (_, _) => { answer = PendingEdit.Discard; dialog.Close(); };
        stay.Click += (_, _) => { answer = PendingEdit.Stay; dialog.Close(); };

        await dialog.ShowDialog(this);
        return answer;
    }

    /// <summary>
    /// The brothers the user asked to write a memorial for, in the order they were asked about
    /// (M55, made a list by M73(a)). The People window cannot write one — it has no newsletter —
    /// so it records the requests and the shell picks them up.
    ///
    /// <para>M73(a): this was a single slot, so two brothers recorded as passed in one visit meant
    /// "Not now" on the second wrote <c>null</c> over the first accepted yes. A list cannot lose an
    /// answer that way.</para>
    /// </summary>
    internal IReadOnlyList<string> MemorialsRequestedFor => _memorialsRequested;

    /// <summary>
    /// Answers the card in place of a person, which a headless run cannot do.
    ///
    /// <para>It is a function of the name rather than one bool, because one visit can raise the
    /// card more than once and each brother gets his own answer.</para>
    /// </summary>
    internal Func<string, bool>? MemorialAnswerForTest { get; set; }

    /// <summary>
    /// Puts every card this visit has queued on screen, one at a time, and waits for each answer.
    ///
    /// <para>M73(a): every way out of this window goes through here first, so no request can be
    /// left behind in a task nobody is waiting on.</para>
    /// </summary>
    private async Task OfferAnyMemorialsAsync()
    {
        while (_memorialsToOffer.Count > 0)
        {
            string name = _memorialsToOffer[0];
            _memorialsToOffer.RemoveAt(0);
            await OfferAMemorialAsync(name);
        }
    }

    /// <summary>
    /// Offered once, on the transition, and never inserted (PLAN.md §11 M55).
    ///
    /// <para>The card says what has already happened before it asks anything, because the thing
    /// the user most needs to know at that moment is that the brother's record has been kept. An
    /// app that responded to "he has died" by silently deleting him, or by writing something into
    /// the newsletter uninvited, would be unforgivable in a way no other bug here could be.</para>
    /// </summary>
    private async Task OfferAMemorialAsync(string name)
    {
        // M73(a), gate 26: the offer is made only where the shell could actually keep it. With no
        // newsletter open the card still says what has happened to his record — that is the half
        // that matters — and says plainly why it is not offering to write anything.
        bool canWrite = _canWriteAMemorial();

        if (MemorialAnswerForTest is { } answered)
        {
            // The yield is not decoration: the real card is a modal window and the answer NEVER
            // comes back in the same turn of the dispatcher. A seam that answered synchronously
            // would have hidden the close-path defect M73(a) names, which is a lost continuation.
            await Task.Yield();
            if (canWrite && answered(name))
            {
                _memorialsRequested.Add(name);
            }

            return;
        }

        bool write = false;
        var dialog = new Window
        {
            Title = "His record has been kept",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var yes = new Button
        {
            Content = "Write a memorial",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        yes.Action();
        var no = new Button
        {
            Content = "Not now",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsCancel = true,
        };
        no.Action();
        AutomationProperties.SetName(yes, "Write a memorial");
        AutomationProperties.SetName(no, "Not now");
        yes.Click += (_, _) => { write = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        var close = new Button
        {
            Content = "Close",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
            IsCancel = true,
        };
        close.Action();
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => dialog.Close();

        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        if (canWrite)
        {
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            buttons.Children.Add(close);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"{name} is recorded as passed to the Celestial Lodge.",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.Bold,
                    MaxWidth = 460,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "His record is kept in your address book. He has been taken out of the "
                        + "birthday list, and TrestleBoard will ask before it changes anything you "
                        + "have already put on a page.",
                    FontSize = 18,
                    MaxWidth = 460,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = canWrite
                        ? "Would you like to write a memorial notice for him?"
                        : "No newsletter is open, so TrestleBoard cannot write a memorial notice "
                            + "just now. Open this month's newsletter, then look under Insert for "
                            + "\"Words for hard news\".",
                    FontSize = 18,
                    MaxWidth = 460,
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        AutomationProperties.SetName(dialog, "His record has been kept");
        await dialog.ShowDialog(this);
        if (write)
        {
            _memorialsRequested.Add(name);
        }
    }

    /// <summary>
    /// The date typed, or today's if the box was left empty. A brother recorded as passed with no
    /// date at all would be indistinguishable from one who is not, because the date IS the status
    /// (see <c>Member.PassedOn</c>) — so the app supplies the one fact it can be sure of rather
    /// than refusing the tick box.
    /// </summary>
    private string PassedOnOrToday()
    {
        string typed = (_passedOn.Text ?? string.Empty).Trim();
        return typed.Length > 0
            ? typed
            : DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The date box only matters once the tick box is ticked, and says so by going away.</summary>
    private void UpdatePassedRow()
    {
        bool passed = _passed.IsChecked ?? false;
        _passedOnLabel.IsVisible = passed;
        _passedOn.IsVisible = passed;
    }

    private IReadOnlyList<string> CheckedGroups() =>
        [.. _groupBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!)];

    /// <summary>
    /// A tick box per group: the ones the app knows about, plus every one this lodge has invented,
    /// so a name is never re-typed. A group that is nearly spelled right is a brother who quietly
    /// stops getting his newsletter.
    /// </summary>
    private void ShowGroups(IReadOnlyList<string> mine)
    {
        _groupsPanel.Children.Clear();
        _groupBoxes.Clear();
        foreach (string group in MemberGroups.InUse(_roster.Book.Members))
        {
            var box = new CheckBox
            {
                Content = group,
                FontSize = 18,
                MinHeight = 44,
                IsChecked = mine.Any(g => string.Equals(g, group, StringComparison.OrdinalIgnoreCase)),
            };
            AutomationProperties.SetName(box, group);
            _groupBoxes.Add(box);
            _groupsPanel.Children.Add(box);
        }
    }

    /// <summary>
    /// Everything on the form, as one string. Comparing a signature rather than field by field means
    /// that a field added to this window in future is covered by whoever adds it to <see cref="Show"/>
    /// - there is no second list to keep in step.
    /// </summary>
    private string FormSignature() => string.Join(
        "\u001f",
        string.Join("\u001f", _fields.Select(f => (f.Box.Text ?? string.Empty).Trim())),
        (_birthday.Text ?? string.Empty).Trim(),
        (_birthYear.Text ?? string.Empty).Trim(),
        (_degreeDate.Text ?? string.Empty).Trim(),
        DegreeKinds[Math.Max(0, _degreeKind.SelectedIndex)].Kind ?? string.Empty,
        (_active.IsChecked ?? true) ? "1" : "0",
        (_passed.IsChecked ?? false) ? "1" : "0",
        (_undeliverable.IsChecked ?? false) ? "1" : "0",
        (_passedOn.Text ?? string.Empty).Trim(),
        string.Join(",", CheckedGroups()));

    private bool FormHasUnsavedEdits() =>
        !string.Equals(FormSignature(), _formAsLoaded, StringComparison.Ordinal);

    private void Show(Member member)
    {
        _selectedId = member.Id;
        foreach (FormField field in _fields)
        {
            field.Box.Text = field.Read(member) ?? string.Empty;
        }

        _birthday.Text = member.BirthdayText;
        _birthYear.Text = member.BirthYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _degreeDate.Text = member.DegreeDate ?? string.Empty;
        _degreeKind.SelectedIndex = Math.Max(0, Array.FindIndex(DegreeKinds, k => k.Kind == member.Degree));
        _undeliverable.IsChecked = member.AddressUndeliverable;
        _active.IsChecked = member.IsActive;
        _passed.IsChecked = member.HasPassed;
        _passedOn.Text = member.PassedOn ?? string.Empty;
        ShowGroups(member.Groups);
        UpdatePassedRow();
        _delete.IsEnabled = true;
        _status.Text = string.Empty;
        _formAsLoaded = FormSignature();
    }

    private void Clear()
    {
        _selectedId = null;
        foreach (FormField field in _fields)
        {
            field.Box.Text = string.Empty;
        }

        foreach (TextBox box in new[] { _birthday, _birthYear, _degreeDate })
        {
            box.Text = string.Empty;
        }

        _degreeKind.SelectedIndex = 0;
        _undeliverable.IsChecked = false;
        _active.IsChecked = true;
        _passed.IsChecked = false;
        _passedOn.Text = string.Empty;
        ShowGroups([]);
        UpdatePassedRow();
        _delete.IsEnabled = false;
        _formAsLoaded = FormSignature();
    }

    /// <summary>
    /// "Add a person", and then any memorial card the save underneath it queued.
    ///
    /// <para>M74 (f): the add path was the one way out of a filled-in form that did not drain the
    /// queue. Tick "he has passed on", then press "Add a person" without saving: the save that
    /// happens on the way out queues his card, the form is cleared for somebody new, and the card
    /// is not shown. It then appears at the next save or at the close — by which time the window is
    /// about a different brother entirely, and the card asking whether to write a memorial for the
    /// first one reads as being about the second. Deferred, not lost, is not good enough for this
    /// card.</para>
    /// </summary>
    private async Task BeginAddAsync()
    {
        BeginAdd();
        await OfferAnyMemorialsAsync();
    }

    private void BeginAdd()
    {
        // M40: leaving the form for a blank one loses an edit exactly as switching people does.
        if (FormHasUnsavedEdits())
        {
            if (AnswerWithoutAsking is { } answer)
            {
                if (answer == PendingEdit.Stay || (answer == PendingEdit.Save && !Save()))
                {
                    return;
                }
            }
            else
            {
                _ = BeginAddAfterAskingAsync();
                return;
            }
        }

        _adding = true;
        Clear();
        _updating = true;
        _list.SelectedIndex = -1;
        _updating = false;
        _status.Text = "Type the new person's details, then press Save this person.";
        _name.Focus();
    }

    /// <summary>
    /// Writes the person on the form. Returns false when the form was refused - M40 needs that
    /// answer, because "Save this person" as the reply to the unsaved-changes question must not
    /// carry the user onwards when the save did not happen.
    /// </summary>
    private bool Save()
    {
        string name = (_name.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            _status.Text = "Type a name first. Everything else is optional.";
            FocusOn(_name);
            return false;
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
                FocusOn(_birthday);
                return false;
            }

            month = m;
            day = d;
        }

        // M88. Four digits, and refused in words like the birthday above it rather than silently
        // dropped — a year the app quietly ignored would be retyped by somebody who watched it go.
        string yearText = (_birthYear.Text ?? string.Empty).Trim();
        int? birthYear = null;
        if (yearText.Length > 0)
        {
            if (!int.TryParse(yearText, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out int y)
                || y is < 1850 or > 2100)
            {
                _status.Text = "That year could not be read. Write it as four digits, like 1957.";
                FocusOn(_birthYear);
                return false;
            }

            birthYear = y;
        }

        Member existing = (_selectedId is not null ? _roster.Book.Find(_selectedId) : null) ?? new Member();
        var member = existing with
        {
            Id = _adding || _selectedId is null ? _roster.NextMemberId() : _selectedId,
            BirthMonth = month,
            BirthDay = day,
            BirthYear = birthYear,
            DegreeDate = Empty(_degreeDate),
            Degree = DegreeKinds[Math.Max(0, _degreeKind.SelectedIndex)].Kind,
            AddressUndeliverable = _undeliverable.IsChecked ?? false,
            IsActive = _active.IsChecked ?? true,
            PassedOn = (_passed.IsChecked ?? false) ? PassedOnOrToday() : null,
            Groups = CheckedGroups(),
        };

        // Every plain box, from the one table — including the ones on the second tab, whichever tab
        // is showing. A form that saved only what was visible would be the M88 defect.
        member = _fields.Aggregate(member, (m, f) => f.Write(m, Empty(f.Box)));

        bool adding = _adding || _selectedId is null;
        bool newlyPassed = member.HasPassed && existing is { HasPassed: false };
        _roster.Save(member, adding ? $"Add {name}" : $"Change {name}");
        if (newlyPassed)
        {
            // M73(a): queued, not fired. This method is synchronous and is called from the close
            // handlers, and `_ = OfferAMemorialAsync(name)` there raised a modal card as the child
            // of a window that was already going — the answer arrived after the shell had stopped
            // listening and the request was lost in silence.
            _memorialsToOffer.Add(name);
        }
        _adding = false;
        _selectedId = member.Id;
        _formAsLoaded = FormSignature();
        RefreshList();
        _status.Text = adding ? $"{name} was added." : $"{name} was saved.";
        return true;
    }

    private async Task BeginAddAfterAskingAsync()
    {
        try
        {
            PendingEdit answer = await AskAboutPendingEditAsync();
            if (answer == PendingEdit.Stay || (answer == PendingEdit.Save && !Save()))
            {
                RestoreSelection();
                return;
            }

            BeginAdd();

            // M74 (f): and the card the save above may have queued, before the form is somebody
            // else's. The other route in — BeginAddAsync, when the answer needed no dialog —
            // drains it the same way.
            await OfferAnyMemorialsAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _status.Text = $"That could not be finished, so nothing has changed. ({ex.Message})";
        }
    }

    private static string? Empty(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    /// <summary>
    /// Puts the focus on a box, showing the tab it lives on first (M88).
    ///
    /// <para>Every refusal in this window names the field it is about and then focuses it. With two
    /// tabs that is not enough on its own: focusing a control on the tab that is not showing moves
    /// the caret somewhere the reader cannot see, and a screen reader then reads out a field its
    /// user has no way to find. So the tab comes first, always.</para>
    /// </summary>
    private void FocusOn(TextBox box)
    {
        FormField? field = _fields.FirstOrDefault(f => f.Box == box);
        _tabs.SelectedIndex = field?.Section == RosterFieldSection.AddressAndMore ? 1 : 0;
        box.Focus();
    }

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
