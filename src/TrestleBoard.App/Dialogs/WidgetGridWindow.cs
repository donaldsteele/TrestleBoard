using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The big-row grid re-editor (PLAN.md §5, docs/M7-spec.md §7.2): every list on one scrolling page
/// with Add, Remove and Move, for the user who already knows the answers and does not want fifteen
/// screens. It is a second VIEW over the SAME <see cref="WizardSession"/>, so both editors commit
/// the identical POCO through the identical command.
/// </summary>
public sealed class WidgetGridWindow : Window
{
    private readonly WizardSession _session;
    private readonly IReadOnlyList<PersonSuggestion> _people;
    private readonly StackPanel _body = new() { Spacing = 16, Margin = new Avalonia.Thickness(24) };
    private readonly StackPanel _errorPanel = new() { Spacing = 4, IsVisible = false };

    /// <param name="session">The same session the step wizard drives. One POCO, one command.</param>
    /// <param name="people">
    /// Names the address book can offer (M13). The grid is a second view of the same wizard, not a
    /// laxer one, so a Person field is a picker here too — otherwise the fast path would quietly be
    /// the one without the help in it.
    /// </param>
    public WidgetGridWindow(WizardSession session, IReadOnlyList<PersonSuggestion>? people = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _people = people ?? [];

        Title = $"{session.Title} — edit list";
        MinWidth = 900;
        MinHeight = 700;
        Width = 960;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, Title);

        Button save = MakeButton("Save it", (_, _) => Commit());
        save.IsDefault = true;
        Button cancel = MakeButton("Cancel", (_, _) => Close());
        cancel.IsCancel = true;

        Content = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Margin = new Avalonia.Thickness(24, 24, 24, 0),
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = session.Title,
                            FontSize = 24,
                            FontWeight = FontWeight.Bold,
                        },
                        new TextBlock
                        {
                            Text = "Everything at once. Add or remove rows, then choose “Save it”.",
                            FontSize = 18,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 780,
                        },
                        _errorPanel,
                    },
                },
                ButtonBar(cancel, save),
                new ScrollViewer
                {
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    Content = _body,
                },
            },
        };

        Opened += (_, _) => Render();
    }

    /// <summary>
    /// Save and Cancel, on a shaded band of their own. The band is not decoration: the scrolling
    /// list is clipped wherever the bar begins, and without a background behind the buttons the
    /// half-cut row underneath read as though the bar were sitting on top of it.
    /// </summary>
    private static Border ButtonBar(Button cancel, Button save)
    {
        var bar = new Border
        {
            [DockPanel.DockProperty] = Dock.Bottom,
            Padding = new Avalonia.Thickness(24, 12, 24, 24),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, save },
            },
        };

        // By reference, like the toolbar and status bar: High Contrast swaps the brush out and a
        // hard-coded colour would survive the swap and stop meeting the contrast rule.
        bar[!BackgroundProperty] =
            new DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush");
        return bar;
    }

    public bool Confirmed { get; private set; }

    public System.Text.Json.JsonElement Data { get; private set; }

    public int DataVersion { get; private set; }

    public string UndoLabel => _session.UndoLabel;

    /// <summary>Builds the grid's controls without showing the window — see WizardWindow's note.</summary>
    internal void RenderForTest() => Render();

    /// <summary>The controls the grid built, for the field-kind tests.</summary>
    internal IReadOnlyList<Control> ScreenControlsForTest => [.. Descendants(_body)];

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

    private void Commit()
    {
        if (!_session.TryCommit(out System.Text.Json.JsonElement data, out int version,
            out IReadOnlyList<WizardFieldError> errors))
        {
            RenderErrors(errors);
            return;
        }

        Data = data;
        DataVersion = version;
        Confirmed = true;
        Close();
    }

    private void Render()
    {
        _body.Children.Clear();
        foreach (IWizardStep step in _session.ActiveSteps)
        {
            _body.Children.Add(new TextBlock
            {
                Text = step.Title,
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Margin = new Avalonia.Thickness(0, 12, 0, 0),
            });

            if (step is IWizardListStep list)
            {
                RenderList(step, list);
            }
            else
            {
                foreach (WizardField field in step.Fields)
                {
                    _body.Children.Add(BuildField(step, field, -1));
                }
            }
        }
    }

    private void RenderList(IWizardStep step, IWizardListStep list)
    {
        int rows = _session.GetRowCount(step);
        if (rows == 0)
        {
            _body.Children.Add(new TextBlock { Text = list.EmptyText, FontSize = 18 });
        }

        for (int row = 0; row < rows; row++)
        {
            var panel = new StackPanel { Spacing = 6 };
            string label = _session.GetRowLabel(step, row);
            if (label.Length > 0)
            {
                panel.Children.Add(new TextBlock { Text = label, FontSize = 18, FontWeight = FontWeight.Bold });
            }

            foreach (WizardField field in step.Fields)
            {
                panel.Children.Add(BuildField(step, field, row));
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            int captured = row;
            if (list.FixedRows is null)
            {
                buttons.Children.Add(MakeButton("Remove", (_, _) =>
                {
                    _session.RemoveRow(step, captured);
                    Render();
                }));
            }

            if (list.AllowReorder)
            {
                buttons.Children.Add(MakeButton("▲ Move up", (_, _) =>
                {
                    _session.MoveRow(step, captured, captured - 1);
                    Render();
                }));
                buttons.Children.Add(MakeButton("▼ Move down", (_, _) =>
                {
                    _session.MoveRow(step, captured, captured + 1);
                    Render();
                }));
            }

            if (buttons.Children.Count > 0)
            {
                panel.Children.Add(buttons);
            }

            _body.Children.Add(new Border
            {
                BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                BorderBrush = Brushes.LightGray,
                Padding = new Avalonia.Thickness(0, 0, 0, 10),
                Child = panel,
            });
        }

        if (list.FixedRows is null)
        {
            _body.Children.Add(MakeButton(list.AddButtonText, (_, _) =>
            {
                _session.AddRow(step);
                Render();
            }));
        }
    }

    private StackPanel BuildField(IWizardStep step, WizardField field, int rowIndex)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var label = new TextBlock
        {
            Text = field.IsOptional ? $"{field.Label} (optional)" : field.Label,
            FontSize = 18,
            Width = 200,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(label);

        // A Choice field MUST be a picker here too. Rendering it as a text box would let someone
        // type free text into a field the widget will silently reinterpret — the grid is a second
        // view of the same wizard, not a laxer one.
        Control input;
        if (field.Kind == WizardFieldKind.Choice && field.Choices is { Count: > 0 } choices)
        {
            var combo = new ComboBox
            {
                FontSize = 20,
                MinHeight = 44,
                Width = 520,
                ItemsSource = choices.Select(c => c.Label).ToList(),
                SelectedIndex = Math.Max(0, IndexOfChoice(choices, _session.GetValue(step, field.Key, rowIndex))),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    _session.SetValue(step, field.Key, rowIndex, choices[combo.SelectedIndex].Value);
                    Render();
                }
            };
            input = combo;
        }
        else if (field.Kind == WizardFieldKind.Person)
        {
            var picker = new AutoCompleteBox
            {
                FontSize = 20,
                MinHeight = 44,
                Width = 520,
                ItemsSource = _people.Select(p => p.Name).ToList(),
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MinimumPrefixLength = 1,
                Text = _session.GetValue(step, field.Key, rowIndex),
                Watermark = field.ExampleText,
            };
            // See WizardWindow: the event only fires once the control has a template.
            picker.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutoCompleteBox.TextProperty)
                {
                    _session.SetValue(step, field.Key, rowIndex, picker.Text ?? "");
                }
            };
            input = picker;
        }
        else
        {
            bool multiLine = field.Kind == WizardFieldKind.MultiLineText;
            var box = new TextBox
            {
                FontSize = 20,
                MinHeight = multiLine ? 120 : 44,
                Width = 520,
                Text = _session.GetValue(step, field.Key, rowIndex),
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Watermark = field.ExampleText,
                MaxLength = field.MaxLength,
            };
            box.TextChanged += (_, _) => _session.SetValue(step, field.Key, rowIndex, box.Text ?? "");
            input = box;
        }

        AutomationProperties.SetName(input, label.Text);
        AutomationProperties.SetLabeledBy(input, label);
        panel.Children.Add(input);

        if (field is { Kind: WizardFieldKind.MultiLineText, AllowsPeoplePicker: true } && _people.Count > 0)
        {
            panel.Children.Add(MakeButton("Add someone from the address book…", async (_, _) =>
            {
                if (await PersonPickerDialog.PickAsync(this, _people) is { } person)
                {
                    string existing = _session.GetValue(step, field.Key, rowIndex);
                    _session.SetValue(
                        step,
                        field.Key,
                        rowIndex,
                        existing.Length == 0 ? person.Name : existing.TrimEnd('\n') + "\n" + person.Name);
                    Render();
                }
            }));
        }

        return panel;
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

    private void RenderErrors(IReadOnlyList<WizardFieldError> errors)
    {
        _errorPanel.Children.Clear();
        _errorPanel.IsVisible = errors.Count > 0;
        foreach (WizardFieldError error in errors)
        {
            _errorPanel.Children.Add(new TextBlock
            {
                Text = $"⚠ Check this — {error.Message}",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 780,
                Foreground = Brushes.Firebrick,
            });
        }
    }

    private static Button MakeButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button { Content = text, FontSize = 20, MinHeight = 44, MinWidth = 160 };
        button.Click += click;
        AutomationProperties.SetName(button, text);
        return button;
    }
}
