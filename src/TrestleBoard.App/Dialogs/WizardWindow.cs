using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Widgets.Wizards;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// THE generic wizard host (PLAN.md §5/§6, docs/M7-spec.md §6.6). One window renders every widget's
/// wizard: one question per screen, 20pt+ text, 44pt targets, big Back/Next, a review screen, and no
/// gesture that lacks a keyboard path. It holds NO wizard state — <see cref="WizardSession"/> owns
/// all of it, which is why the whole flow is testable without a window.
/// </summary>
public sealed class WizardWindow : Window
{
    /// <summary>One brother picked from the address book, and where he was picked.</summary>
    private sealed record PersonPick(IWizardStep Step, int RowIndex, string? PhoneFieldKey, PersonSuggestion Person);

    private readonly WizardSession _session;
    private readonly IReadOnlyList<PersonSuggestion> _people;
    private readonly List<PersonPick> _picks = [];
    private readonly HashSet<string> _writeBackIds = new(StringComparer.Ordinal);
    // Every capped control on this window carries HorizontalAlignment.Left, and the reason is one
    // bug repeated: a Width or MaxWidth on a child of a VERTICAL StackPanel, under the default
    // HorizontalAlignment.Stretch, makes Avalonia centre that child. wizard-officers-step.png shows
    // five different left edges on one screen — x = 24, 62, 68, 24, 170 — and the arithmetic
    // accounts for every one of them: 900px of window less the 24px gutters is 852px of content, so
    // the 780-wide header landed at 24 + (852-780)/2 = 60, the 760-wide help text at 70 and the
    // 560-wide input at 170, while the two uncapped children stayed honestly at 24.
    private readonly TextBlock _header = new()
    {
        FontSize = 24,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 780,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly TextBlock _progress =
        new TextBlock { FontSize = 16 }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);
    private readonly TextBlock _help = new()
    {
        FontSize = 18,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 760,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly StackPanel _errorPanel = new() { Spacing = 4, IsVisible = false };
    private readonly StackPanel _body = new() { Spacing = 16 };
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _showAll;
    private readonly ScrollViewer _scroller;

    /// <param name="session">The wizard's whole state. This window holds none of it.</param>
    /// <param name="people">
    /// Names the address book can offer (M13), handed in by the shell rather than fetched — PLAN.md
    /// §5's projection rule. Null or empty simply means the wizard behaves exactly as it did before
    /// there was an address book.
    /// </param>
    /// <param name="rosterBanner">
    /// M19: "Filled in from your address book on 14 July 2026." when this list came out of the
    /// address book, and null when somebody typed it. The panel prints the same sentence, so wherever
    /// the user is standing, a generated list says it is one.
    /// </param>
    public WizardWindow(
        WizardSession session,
        IReadOnlyList<PersonSuggestion>? people = null,
        string? rosterBanner = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _people = people ?? [];

        Title = session.Title;
        MinWidth = 900;
        MinHeight = 700;
        Width = 900;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // The chevrons are geometry now, not ◀ and ▶ rendered in whatever fallback font happens to
        // carry them — which is why these two buttons never shared a baseline with Cancel.
        _back = MakeButton("Back", OnBack, "TrestleBoard.Icon.chevron-left");
        _next = MakeButton("Next", OnNext, "TrestleBoard.Icon.chevron-right", Dock.Right);
        // IsDefault carries the emphasis now: Theme/Controls.axaml gives every default button in
        // the application the primary treatment, so no dialog can forget to ask for it.
        _next.IsDefault = true;
        _showAll = MakeButton("Show all at once", OnShowAll);
        Button cancel = MakeButton("Cancel", (_, _) => CancelWithConfirmation());
        cancel.IsCancel = true;

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _body,
        };

        // Back and "Show all at once" belong to the wizard; Cancel and Next belong to the decision
        // the user is making, so they sit at the other end. A DockPanel says that; the 40px spacer
        // Panel this replaces only said it at one window width.
        var footer = new DockPanel { Margin = new Avalonia.Thickness(24, 12, 24, 16) };

        var leftButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { _back, _showAll },
        };
        DockPanel.SetDock(leftButtons, Dock.Left);
        footer.Children.Add(leftButtons);

        footer.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, _next },
        });

        Content = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Margin = new Avalonia.Thickness(24, 24, 24, 8),
                    Spacing = 6,
                    Children = { _header, _progress, RosterBanner(rosterBanner), _help, _errorPanel },
                },
                // Shaded, like the toolbar, the status bar and WidgetGridWindow's own bar: the
                // scroller's clip then reads as a footer rather than as content cut off.
                new Border
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Child = footer,
                }
                    .Token(Border.BackgroundProperty, Tokens.ChromeBackground)
                    .Token(Border.BorderBrushProperty, Tokens.ChromeBorder)
                    .Token(Border.BorderThicknessProperty, Tokens.RuleTop),
                new Border { Margin = new Avalonia.Thickness(24, 0, 24, 0), Child = _scroller },
            },
        };

        KeyDown += OnKeyDown;
        Opened += (_, _) => RenderScreen();
    }

    /// <summary>
    /// The one line that says a list came out of the address book (M19). Collapsed rather than
    /// absent, so the header's spacing is the same whichever kind of list this is.
    /// </summary>
    internal static TextBlock RosterBanner(string? text) => new()
    {
        Text = text ?? string.Empty,
        IsVisible = !string.IsNullOrWhiteSpace(text),
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 760,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>True when the user pressed "Save it" and the data validated.</summary>
    public bool Confirmed { get; private set; }

    public System.Text.Json.JsonElement Data { get; private set; }

    public int DataVersion { get; private set; }

    public string UndoLabel => _session.UndoLabel;

    /// <summary>
    /// Builds the current screen's controls without showing the window. Tests use it because a shown
    /// headless window runs a real layout pass, and this suite's headless platform has no font
    /// manager behind it — the same reason <c>WidgetShellTests</c> deliberately never shows one.
    /// </summary>
    internal void RenderForTest() => RenderScreen();

    /// <summary>The controls the current screen built, for the tests that check which kind got which.</summary>
    internal IReadOnlyList<Control> ScreenControlsForTest => [.. Descendants(_body)];

    // ---- navigation ------------------------------------------------------------------------

    private void OnBack(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _session.GoBack();
        RenderScreen();
    }

    private void OnNext(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session.IsReviewScreen)
        {
            Commit();
            return;
        }

        _session.TryGoNext();
        RenderScreen();
    }

    private void OnShowAll(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _session.ShowAllRows();
        RenderScreen();
    }

    /// <summary>
    /// Throwing away typed answers is not something to do on a stray Esc (docs/M7-spec.md §6.6).
    /// Nothing has reached the newsletter yet, so the confirmation says so — the fear being
    /// answered is "have I just lost it?", not "what is a dialog?".
    /// </summary>
    private async void CancelWithConfirmation()
    {
        if (!_session.IsDirty)
        {
            Close();
            return;
        }

        var keepGoing = new Button
        {
            Content = "Keep going",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
        };
        var discard = new Button
        {
            Content = "Throw it away",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
        };

        var confirm = new Window
        {
            Title = "Throw away what you typed?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Throw away what you typed? Nothing has been added to the newsletter yet.",
                        FontSize = 20,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { discard, keepGoing },
                    },
                },
            },
        };

        bool discarded = false;
        keepGoing.Click += (_, _) => confirm.Close();
        discard.Click += (_, _) =>
        {
            discarded = true;
            confirm.Close();
        };

        AutomationProperties.SetName(confirm, "Throw away what you typed?");
        await confirm.ShowDialog(this);

        if (discarded)
        {
            Close();
        }
    }

    private void Commit()
    {
        if (!_session.TryCommit(out System.Text.Json.JsonElement data, out int version,
            out IReadOnlyList<WizardFieldError> errors))
        {
            _session.TryGoTo(_session.FindScreen(errors[0]));
            RenderScreen(errors);
            return;
        }

        Data = data;
        DataVersion = version;
        Confirmed = true;
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None })
        {
            CancelWithConfirmation();
            e.Handled = true;
            return;
        }

        // Every gesture has a menu/button twin; these are accelerators, never the only path.
        if (e.KeyModifiers == KeyModifiers.Alt)
        {
            switch (e.Key)
            {
                case Key.B when _back.IsEnabled:
                    OnBack(this, new Avalonia.Interactivity.RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.N or Key.S:
                    OnNext(this, new Avalonia.Interactivity.RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.A when _session.CurrentStep is IWizardListStep { FixedRows: null }:
                    AddRow();
                    e.Handled = true;
                    return;
            }
        }
        else if (e is { Key: Key.Enter, KeyModifiers: KeyModifiers.Control })
        {
            // Only when everything already validates: a review screen listing half-answered
            // questions as "—" would be a worse place to land than where the user is standing.
            if (_session.TryCommit(out _, out _, out IReadOnlyList<WizardFieldError> pending))
            {
                _session.TryGoTo(_session.ScreenCount - 1);
                RenderScreen();
            }
            else
            {
                _session.TryGoTo(_session.FindScreen(pending[0]));
                RenderScreen(pending);
            }

            e.Handled = true;
        }
    }

    // ---- rendering -------------------------------------------------------------------------

    private void RenderScreen(IReadOnlyList<WizardFieldError>? errors = null)
    {
        IWizardStep step = _session.CurrentStep;
        _header.Text = _session.ScreenTitle;
        _progress.Text = _session.ProgressText;
        _help.Text = _session.IsFirstScreen && _session.IntroText.Length > 0
            ? _session.IntroText
            : step.HelpText ?? "";
        _help.IsVisible = _help.Text.Length > 0;

        RenderErrors(errors ?? _session.Errors);

        _back.IsEnabled = !_session.IsFirstScreen;
        // Rebuilt rather than assigned a string, because the content is an IconText and "Save it"
        // is the one screen where the forward chevron would be a lie: it does not go forward, it
        // finishes. AutomationProperties.Name follows, so the screen reader agrees with the button.
        string nextLabel = _session.IsReviewScreen ? "Save it" : "Next";
        _next.Content = _session.IsReviewScreen
            ? new IconText(null, nextLabel, labelSize: 20)
            : new IconText("TrestleBoard.Icon.chevron-right", nextLabel, labelSize: 20, glyphSide: Dock.Right);
        AutomationProperties.SetName(_next, nextLabel);
        _showAll.IsVisible = _session.CurrentRowIndex >= 0;

        _body.Children.Clear();
        switch (step.Kind)
        {
            case WizardStepKind.Review:
                RenderReview();
                break;
            case WizardStepKind.RecordList when _session.CurrentRowIndex < 0:
                RenderList((IWizardListStep)step);
                break;
            default:
                RenderFields(_session.CurrentRowIndex);
                break;
        }

        AutomationProperties.SetName(this, $"{_session.Title} — {_session.ProgressText}");

        // Avalonia has no live region, so the header is focused to make Narrator/VoiceOver announce
        // the new question (docs/M7-spec.md §6.6). Focus then moves to the first input.
        _header.Focusable = true;
        _header.Focus();
        _header.Focusable = false;
        FocusFirstInput();
    }

    private void RenderErrors(IReadOnlyList<WizardFieldError> errors)
    {
        _errorPanel.Children.Clear();
        _errorPanel.IsVisible = errors.Count > 0;
        foreach (WizardFieldError error in errors)
        {
            // Colour is never the only signal: the warning glyph and the words carry it too.
            _errorPanel.Children.Add(new TextBlock
            {
                Text = $"⚠ Check this — {error.Message}",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
            }.Token(TextBlock.ForegroundProperty, Tokens.Warning));
        }
    }

    private void RenderFields(int rowIndex)
    {
        foreach (WizardField field in _session.CurrentStep.Fields)
        {
            _body.Children.Add(BuildField(field, rowIndex, labelWidth: null));
        }
    }

    private void RenderList(IWizardListStep list)
    {
        int rows = _session.RowCount;
        if (rows == 0)
        {
            _body.Children.Add(new TextBlock
            {
                Text = list.EmptyText,
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
        }

        for (int row = 0; row < rows; row++)
        {
            _body.Children.Add(BuildRow(list, row));
        }

        if (list.FixedRows is null)
        {
            Button add = MakeButton(list.AddButtonText, (_, _) => AddRow());
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body.Children.Add(add);
        }
    }

    private Border BuildRow(IWizardListStep list, int row)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 0, 0, 8) };
        string label = _session.GetRowLabel(row);
        if (label.Length > 0)
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 18, FontWeight = FontWeight.Bold });
        }

        foreach (WizardField field in _session.CurrentStep.Fields)
        {
            panel.Children.Add(BuildField(field, row, labelWidth: 180));
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (list.FixedRows is null)
        {
            int captured = row;
            buttons.Children.Add(MakeButton("Remove", (_, _) =>
            {
                _session.RemoveRow(captured);
                RenderScreen();
            }));
        }

        if (list.AllowReorder)
        {
            int captured = row;
            buttons.Children.Add(MakeButton("▲ Move up", (_, _) =>
            {
                _session.MoveRow(captured, captured - 1);
                RenderScreen();
            }));
            buttons.Children.Add(MakeButton("▼ Move down", (_, _) =>
            {
                _session.MoveRow(captured, captured + 1);
                RenderScreen();
            }));
        }

        if (buttons.Children.Count > 0)
        {
            panel.Children.Add(buttons);
        }

        return new Border
        {
            Padding = new Avalonia.Thickness(0, 0, 0, 12),
            Child = panel,
        }
            .Token(Border.BorderBrushProperty, Tokens.ChromeDivider)
            .Token(Border.BorderThicknessProperty, Tokens.RuleBottom);
    }

    private StackPanel BuildField(WizardField field, int rowIndex, double? labelWidth)
    {
        // Bound to the step this control was built for, not to "whatever step the wizard is on when
        // the event arrives". Avalonia raises TextChanged through the dispatcher, so a box from the
        // screen the user has just left can still fire — and a write aimed at a step that no longer
        // has that field in it is a KeyNotFoundException out of the middle of a wizard.
        IWizardStep owner = _session.CurrentStep;

        var panel = new StackPanel { Spacing = 4 };
        var label = new TextBlock
        {
            Text = field.IsOptional ? $"{field.Label} (optional)" : field.Label,
            FontSize = 18,
            Width = labelWidth ?? double.NaN,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(label);

        Control input;
        if (field.Kind == WizardFieldKind.Choice && field.Choices is { Count: > 0 } choices)
        {
            var combo = new ComboBox
            {
                FontSize = 20,
                MinHeight = 44,
                MinWidth = 560,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                ItemsSource = choices.Select(c => c.Label).ToList(),
                SelectedIndex = Math.Max(0, IndexOfChoice(choices, _session.GetValue(owner, field.Key, rowIndex))),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    _session.SetValue(owner, field.Key, rowIndex, choices[combo.SelectedIndex].Value);
                    RenderScreen();
                }
            };
            input = combo;
        }
        else if (field.Kind == WizardFieldKind.Person)
        {
            // Editable, always: a brother who is not in the address book must still be typeable, or
            // the wizard has a dead end in it (PLAN.md §11 M13).
            var picker = new AutoCompleteBox
            {
                FontSize = 20,
                MinHeight = 44,
                MinWidth = 560,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                ItemsSource = _people.Select(p => p.Name).ToList(),
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MinimumPrefixLength = 1,
                Text = _session.GetValue(owner, field.Key, rowIndex),
                Watermark = field.ExampleText,
            };
            // The property, not the TextChanged event: AutoCompleteBox raises that one from its
            // templated inner text box, so it says nothing until the control has been shown.
            picker.PropertyChanged += (_, e) =>
            {
                if (e.Property != AutoCompleteBox.TextProperty)
                {
                    return;
                }

                _session.SetValue(owner, field.Key, rowIndex, picker.Text ?? "");
                NotePersonPicked(owner, rowIndex, picker.Text);
            };
            input = picker;
        }
        else
        {
            bool multiLine = field.Kind == WizardFieldKind.MultiLineText;
            var box = new TextBox
            {
                FontSize = 20,
                MinHeight = multiLine ? 260 : 44,
                MinWidth = 560,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                Text = _session.GetValue(owner, field.Key, rowIndex),
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Watermark = field.ExampleText,
                MaxLength = field.MaxLength,
            };
            box.LostFocus += (_, _) => _session.SetValue(owner, field.Key, rowIndex, box.Text ?? "");
            box.TextChanged += (_, _) => _session.SetValue(owner, field.Key, rowIndex, box.Text ?? "");
            input = box;
        }

        AutomationProperties.SetName(input, field.Label);
        if (field.HelpText is { } help)
        {
            AutomationProperties.SetHelpText(input, help);
        }

        AutomationProperties.SetLabeledBy(input, label);
        panel.Children.Add(input);

        // Committees keep a plain list of names, so the picker only ever appends a line (M13).
        if (field is { Kind: WizardFieldKind.MultiLineText, AllowsPeoplePicker: true } && _people.Count > 0)
        {
            panel.Children.Add(MakeButton("Add someone from the address book…", async (_, _) =>
            {
                if (await PersonPickerDialog.PickAsync(this, _people) is { } person)
                {
                    string existing = _session.GetValue(owner, field.Key, rowIndex);
                    _session.SetValue(
                        owner,
                        field.Key,
                        rowIndex,
                        existing.Length == 0 ? person.Name : existing.TrimEnd('\n') + "\n" + person.Name);
                    RenderScreen();
                }
            }));
        }

        if (field.HelpText is { Length: > 0 } helpText)
        {
            panel.Children.Add(new TextBlock
            {
                Text = helpText,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                HorizontalAlignment = HorizontalAlignment.Left,
            }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted));
        }

        return panel;
    }

    /// <summary>
    /// Two cheap wins that hit the "I type the same name three times" complaint directly (M13).
    /// Picking a brother fills in a <em>blank</em> phone box from the address book — never one the
    /// user has already typed in — and remembers the pick, so the review screen can offer to send a
    /// corrected number back the other way.
    /// </summary>
    private void NotePersonPicked(IWizardStep step, int rowIndex, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        PersonSuggestion? person = _people.FirstOrDefault(
            p => string.Equals(p.Name, text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (person is null)
        {
            return;
        }

        string? phoneKey = step.Fields.FirstOrDefault(f => f.Kind == WizardFieldKind.Phone)?.Key;

        _picks.RemoveAll(p => ReferenceEquals(p.Step, step) && p.RowIndex == rowIndex);
        _picks.Add(new PersonPick(step, rowIndex, phoneKey, person));

        if (phoneKey is null || string.IsNullOrWhiteSpace(person.Phone))
        {
            return;
        }

        if (_session.GetValue(step, phoneKey, rowIndex).Length == 0)
        {
            _session.SetValue(step, phoneKey, rowIndex, person.Phone!);
            RenderScreen();
        }
    }

    /// <summary>
    /// Phone numbers the user agreed to send back to the address book. Empty unless they ticked a
    /// box on the review screen — keeping the book fresh must never be something that happens to
    /// somebody (PLAN.md §11 M13).
    /// </summary>
    internal IReadOnlyList<(string MemberId, string Phone)> PhoneWriteBacks
    {
        get
        {
            var writes = new List<(string, string)>();
            foreach (PersonPick pick in _picks)
            {
                if (pick.PhoneFieldKey is not { } key || !_writeBackIds.Contains(pick.Person.Id))
                {
                    continue;
                }

                string typed = _session.GetValue(pick.Step, key, pick.RowIndex).Trim();
                if (typed.Length > 0 && !string.Equals(typed, pick.Person.Phone, StringComparison.Ordinal))
                {
                    writes.Add((pick.Person.Id, typed));
                }
            }

            return writes;
        }
    }

    /// <summary>Picks whose typed phone number disagrees with the one in the address book.</summary>
    private IEnumerable<PersonPick> PhoneDisagreements()
    {
        foreach (PersonPick pick in _picks)
        {
            if (pick.PhoneFieldKey is not { } key)
            {
                continue;
            }

            string typed = _session.GetValue(pick.Step, key, pick.RowIndex).Trim();
            if (typed.Length > 0 && !string.Equals(typed, pick.Person.Phone ?? "", StringComparison.Ordinal))
            {
                yield return pick;
            }
        }
    }

    private void RenderReview()
    {
        _body.Children.Add(new TextBlock
        {
            Text = "Here is what will go on the page.",
            FontSize = 18,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
        });

        // Every line carries its OWN "Change this": someone who has just answered fourteen questions
        // and spots a typo in the seventh must not be sent back to the first (docs/M7-spec.md §6.6).
        foreach (WizardReviewLine line in _session.ReviewLines)
        {
            int target = line.ScreenIndex;
            Button change = MakeButton("Change this", (_, _) =>
            {
                _session.TryGoTo(target);
                RenderScreen();
            });
            change.MinWidth = 140;

            _body.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = line.Text,
                        FontSize = 18,
                        TextWrapping = TextWrapping.Wrap,
                        Width = 560,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    change,
                },
            });
        }

        RenderPhoneWriteBacks();
    }

    /// <summary>
    /// One checkbox per brother whose number the user has just typed differently from the address
    /// book (M13). Off by default and phrased as a question, because keeping the book fresh is a
    /// favour the user does, not something the app does behind their back.
    /// </summary>
    private void RenderPhoneWriteBacks()
    {
        List<PersonPick> disagreements = [.. PhoneDisagreements()];
        if (disagreements.Count == 0)
        {
            return;
        }

        _body.Children.Add(new TextBlock
        {
            Text = "While we are here",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 16, 0, 4),
        });

        foreach (PersonPick pick in disagreements)
        {
            string id = pick.Person.Id;
            string text = string.IsNullOrWhiteSpace(pick.Person.Phone)
                ? $"Also add this phone number to {pick.Person.Name} in your address book?"
                : $"Also update {pick.Person.Name}'s phone in your address book?";
            var box = new CheckBox
            {
                Content = text,
                FontSize = 18,
                MinHeight = 44,
                IsChecked = _writeBackIds.Contains(id),
            };
            AutomationProperties.SetName(box, text);
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _writeBackIds.Add(id);
                }
                else
                {
                    _writeBackIds.Remove(id);
                }
            };
            _body.Children.Add(box);
        }
    }

    private void AddRow()
    {
        if (_session.AddRow() >= 0)
        {
            RenderScreen();
        }
    }

    private void FocusFirstInput()
    {
        foreach (Control control in Descendants(_body))
        {
            if (control is TextBox or ComboBox or AutoCompleteBox)
            {
                control.Focus();
                return;
            }
        }
    }

    private static IEnumerable<Control> Descendants(Panel root)
    {
        foreach (Control child in root.Children)
        {
            yield return child;
            switch (child)
            {
                case Panel panel:
                    foreach (Control nested in Descendants(panel))
                    {
                        yield return nested;
                    }

                    break;
                case Border { Child: Panel inner }:
                    foreach (Control nested in Descendants(inner))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static int IndexOfChoice(IReadOnlyList<WizardChoice> choices, string value)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            if (string.Equals(choices[i].Value, value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static Button MakeButton(
        string text,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> click,
        string? glyphKey = null,
        Dock glyphSide = Dock.Left)
    {
        var button = new Button
        {
            Content = glyphKey is null
                ? text
                : new IconText(glyphKey, text, labelSize: 20, glyphSide: glyphSide),
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 160,
        };
        button.Click += click;
        AutomationProperties.SetName(button, text);
        return button;
    }
}
