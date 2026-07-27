using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TrestleBoard.Roster;
using TrestleBoard.Roster.Import;
using TrestleBoard.Roster.Tables;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The import flow's six screens (PLAN.md §11 M12).
///
/// This window is <b>only a renderer</b>: every decision, every count and every sentence lives in
/// <see cref="RosterImportSession"/>, which has no Avalonia in it and is tested without one. That is
/// the trick borrowed from the widget wizard, and the reason it is worth borrowing is that the
/// awkward parts of an import — what matched, what did not, what will change — are exactly the
/// parts that are miserable to assert through a window.
///
/// It borrows <c>WizardWindow</c>'s visual language too: 20pt, one question per screen, big Back and
/// Next, a review before anything happens. It does not borrow its machinery, because the mapping
/// step's questions are discovered at runtime.
/// </summary>
public sealed class RosterImportWindow : Window
{
    private readonly RosterImportSession _session;
    private readonly TextBlock _title;
    private readonly TextBlock _explanation;
    private readonly ContentControl _body;
    private readonly Button _back;
    private readonly Button _next;
    private readonly TextBlock _status;

    public RosterImportWindow(RosterBook current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _session = new RosterImportSession(current);

        Title = "Import people from a file";
        Width = 900;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Import people from a file");

        _title = new TextBlock { FontSize = 26, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(_title, AutomationLiveSetting.Polite);

        _explanation = new TextBlock { FontSize = 20, TextWrapping = TextWrapping.Wrap };
        _status = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);

        _body = new ContentControl();

        _back = Big("Back", "Go back a step");
        _back.Click += (_, _) =>
        {
            _session.Back();
            Render();
        };

        _next = Big("Next", "Go on to the next step");
        _next.Click += async (_, _) => await NextAsync();

        var cancel = Big("Stop", "Stop importing and change nothing");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(28),
            Children =
            {
                Dock(new StackPanel
                {
                    Spacing = 8,
                    Children = { _title, _explanation },
                }, Avalonia.Controls.Dock.Top),
                Dock(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children = { cancel, _back, _next },
                }, Avalonia.Controls.Dock.Bottom),
                Dock(_status, Avalonia.Controls.Dock.Bottom),
                new ScrollViewer { Content = _body, Margin = new Avalonia.Thickness(0, 16, 0, 0) },
            },
        };

        Render();
    }

    /// <summary>The address book as the import left it, or null if the user stopped.</summary>
    public RosterBook? Result { get; private set; }

    internal RosterImportSession SessionForTest => _session;

    internal string StatusTextForTest => _status.Text ?? string.Empty;

    /// <summary>
    /// Drives the flow with a path rather than a file picker — the headless tests' entry point, and
    /// the reason the picker is the only part of this window that is not exercised by them.
    /// </summary>
    internal void ChooseFileForTest(string path)
    {
        try
        {
            _session.ChooseFile(path);
            _status.Text = string.Empty;
        }
        catch (TableReadException e)
        {
            _status.Text = e.Message;
        }

        Render();
    }

    internal async Task NextForTest() => await NextAsync();

    private static Button Big(string content, string automationName)
    {
        var button = new Button { Content = content, FontSize = 20, MinHeight = 44, MinWidth = 140 };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static Control Dock(Control control, Dock side)
    {
        DockPanel.SetDock(control, side);
        return control;
    }

    private async Task NextAsync()
    {
        switch (_session.Step)
        {
            case ImportStep.ChooseFile:
                await BrowseAsync();
                break;

            case ImportStep.ChooseSheet:
                _session.ChooseSheet(Math.Max(0, SelectedIndex()));
                break;

            case ImportStep.ChooseHeaderRow:
                _session.ChooseHeaderRow(SelectedIndex() - 1);
                break;

            case ImportStep.MapColumns:
                if (!_session.CanReview)
                {
                    // Never a dead Next button: the reason is said out loud instead (PLAN.md §6).
                    _status.Text = _session.WhyNotReady;
                    return;
                }

                _session.GoToReview();
                break;

            case ImportStep.Review:
                Result = _session.Commit();
                break;

            default:
                Close();
                return;
        }

        _status.Text = string.Empty;
        Render();

        if (_session.Step == ImportStep.Done)
        {
            _status.Text = RosterImportSession.DoneMessage(Result?.Count ?? 0);
        }
    }

    private async Task BrowseAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Find your list of members",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Spreadsheets and lists")
                {
                    Patterns = ["*.xlsx", "*.xlsm", "*.csv", "*.txt", "*.tsv"],
                },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            _session.ChooseFile(path);
        }
        catch (TableReadException e)
        {
            _status.Text = e.Message;
        }
    }

    private int SelectedIndex() =>
        _body.Content is ListBox { SelectedIndex: var index } ? index : 0;

    private void Render()
    {
        _title.Text = _session.Title;
        _explanation.Text = _session.Explanation;
        _back.IsEnabled = _session.Step is not (ImportStep.ChooseFile or ImportStep.Done);

        _next.Content = _session.Step switch
        {
            ImportStep.ChooseFile => "Find the file…",
            ImportStep.Review => "Add these people",
            ImportStep.Done => "Close",
            _ => "Next",
        };
        AutomationProperties.SetName(_next, (string)_next.Content);

        _body.Content = _session.Step switch
        {
            ImportStep.ChooseSheet => SheetChooser(),
            ImportStep.ChooseHeaderRow => HeaderChooser(),
            ImportStep.MapColumns => ColumnMapper(),
            ImportStep.Review => ReviewPanel(),
            ImportStep.Done => DonePanel(),
            _ => FileChooser(),
        };
    }

    private TextBlock FileChooser() => new TextBlock
    {
        FontSize = 20,
        TextWrapping = TextWrapping.Wrap,
        Text = _session.SourcePath is { } path
            ? $"Chosen: {Path.GetFileName(path)}"
            : "TrestleBoard can read a spreadsheet saved as .xlsx, or a list saved as .csv.",
    };

    private ListBox SheetChooser()
    {
        var list = new ListBox
        {
            FontSize = 20,
            ItemsSource = _session.SheetNames.ToList(),
            SelectedIndex = 0,
        };
        AutomationProperties.SetName(list, "Which sheet has the members in it");
        return list;
    }

    /// <summary>
    /// The first rows as they are, one big row each, with "There are no column titles" at the top.
    /// Almost no import interface offers this question, and it is where confusion starts.
    /// </summary>
    private ListBox HeaderChooser()
    {
        var rows = new List<string> { "There are no column titles" };
        rows.AddRange(_session.HeaderCandidates.Select(r => string.Join("  |  ", r)));

        var list = new ListBox
        {
            FontSize = 18,
            ItemsSource = rows,
            SelectedIndex = _session.HeaderRow + 1,
        };
        AutomationProperties.SetName(list, "Which row has the column titles");
        return list;
    }

    /// <summary>
    /// One question per lodge field, each dropdown item showing the column letter, its title and its
    /// first values — so the user recognises their own data instead of decoding a header.
    /// </summary>
    private StackPanel ColumnMapper()
    {
        var panel = new StackPanel { Spacing = 12 };
        IReadOnlyList<TableColumn> columns = _session.Columns;

        foreach (RosterFieldInfo field in RosterFieldInfo.All)
        {
            var items = new List<string> { "Not in this file" };
            items.AddRange(columns.Select(c => c.Describe()));

            int selected = _session.Mapping.TryGetValue(field.Field, out int column) ? column + 1 : 0;
            var combo = new ComboBox
            {
                FontSize = 18,
                MinHeight = 44,
                Width = 640,
                ItemsSource = items,
                SelectedIndex = selected,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(combo, field.Question);

            RosterField which = field.Field;
            combo.SelectionChanged += (_, _) =>
                _session.MapColumn(which, combo.SelectedIndex <= 0 ? null : combo.SelectedIndex - 1);

            panel.Children.Add(new TextBlock
            {
                Text = field.Required ? field.Question + " (needed)" : field.Question,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(combo);
        }

        return panel;
    }

    private StackPanel ReviewPanel()
    {
        MergePlan plan = _session.Plan();
        var panel = new StackPanel { Spacing = 12 };

        foreach (string line in plan.Summary())
        {
            panel.Children.Add(new TextBlock { Text = line, FontSize = 20, TextWrapping = TextWrapping.Wrap });
        }

        var leaveAlone = new CheckBox
        {
            Content = "Leave the people I already have alone",
            FontSize = 20,
            MinHeight = 44,
            IsChecked = !_session.UpdateExisting,
        };
        AutomationProperties.SetName(leaveAlone, "Leave the people I already have alone");
        leaveAlone.IsCheckedChanged += (_, _) =>
        {
            _session.UpdateExisting = leaveAlone.IsChecked != true;
            Render();
        };
        panel.Children.Add(leaveAlone);

        foreach (DuplicateQuestion question in plan.Questions)
        {
            panel.Children.Add(QuestionRow(question));
        }

        if (plan.Unusable.Count > 0)
        {
            panel.Children.Add(UnusableRows(plan));
        }

        panel.Children.Add(new TextBlock
        {
            Text = "The first few, as they will be stored:",
            FontSize = 20,
            FontWeight = FontWeight.Bold,
        });

        var preview = new ListBox
        {
            FontSize = 18,
            Height = 240,
            ItemsSource = plan.Rows
                .Where(r => r.Outcome is RowOutcome.New or RowOutcome.Updated)
                .Take(20)
                .Select(r => $"{r.Result.DisplayName} — {r.Result.BirthdayText} — {r.Result.Phone}")
                .ToList(),
        };
        AutomationProperties.SetName(preview, "The first few people, as they will be stored");
        panel.Children.Add(preview);

        return panel;
    }

    private StackPanel QuestionRow(DuplicateQuestion question)
    {
        var same = Big("Same person", "These are the same person");
        same.Click += (_, _) =>
        {
            _session.Answer(question.RowNumber, DuplicateAnswer.SamePerson);
            Render();
        };

        var different = Big("Different person", "These are different people");
        different.Click += (_, _) =>
        {
            _session.Answer(question.RowNumber, DuplicateAnswer.DifferentPerson);
            Render();
        };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = question.Question, FontSize = 20, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { same, different },
                },
            },
        };
    }

    /// <summary>
    /// Rows that could not be used, one line each, with a way to keep them. Nothing is silently
    /// lost — a row the app quietly dropped is a brother nobody notices is missing.
    /// </summary>
    private Expander UnusableRows(MergePlan plan)
    {
        var save = Big("Save these to a file…", "Save the rows we could not use to a file");
        save.Click += async (_, _) => await SaveUnusableAsync();

        var expander = new Expander
        {
            Header = plan.Unusable.Count == 1
                ? "1 row we couldn't use"
                : $"{plan.Unusable.Count} rows we couldn't use",
            FontSize = 20,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new ListBox
                    {
                        FontSize = 18,
                        MaxHeight = 160,
                        ItemsSource = plan.Unusable.Select(r => $"Row {r.RowNumber}: {r.Note}").ToList(),
                    },
                    save,
                },
            },
        };
        AutomationProperties.SetName(expander, "Rows we could not use");
        return expander;
    }

    private async Task SaveUnusableAsync()
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the rows we could not use",
            DefaultExtension = "txt",
            SuggestedFileName = "Rows-we-could-not-use.txt",
            FileTypeChoices = [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_session.UnusableRowsText());
            _status.Text = "Those rows were saved.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Those rows could not be saved there. " + e.Message;
        }
    }

    private TextBlock DonePanel() => new TextBlock
    {
        FontSize = 22,
        TextWrapping = TextWrapping.Wrap,
        Text = RosterImportSession.DoneMessage(Result?.Count ?? 0),
    };
}
