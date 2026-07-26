using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Widgets.Wizards;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// THE generic wizard host (PLAN.md §5/§6, docs/M7-spec.md §6.6). One window renders every widget's
/// wizard: one question per screen, 20pt+ text, 44pt targets, big Back/Next, a review screen, and no
/// gesture that lacks a keyboard path. It holds NO wizard state — <see cref="WizardSession"/> owns
/// all of it, which is why the whole flow is testable without a window.
/// </summary>
public sealed class WizardWindow : Window
{
    private readonly WizardSession _session;
    private readonly TextBlock _header = new()
    {
        FontSize = 24,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 780,
    };

    private readonly TextBlock _progress = new() { FontSize = 16, Opacity = 0.8 };
    private readonly TextBlock _help = new()
    {
        FontSize = 18,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 760,
    };

    private readonly StackPanel _errorPanel = new() { Spacing = 4, IsVisible = false };
    private readonly StackPanel _body = new() { Spacing = 16 };
    private readonly Button _back;
    private readonly Button _next;
    private readonly Button _showAll;
    private readonly ScrollViewer _scroller;

    public WizardWindow(WizardSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Title = session.Title;
        MinWidth = 900;
        MinHeight = 700;
        Width = 900;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _back = MakeButton("◀ Back", OnBack);
        _next = MakeButton("Next ▶", OnNext);
        _next.IsDefault = true;
        _showAll = MakeButton("Show all at once", OnShowAll);
        Button cancel = MakeButton("Cancel", (_, _) => CancelWithConfirmation());
        cancel.IsCancel = true;

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = _body,
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Avalonia.Thickness(24, 12, 24, 24),
            Children = { _back, _showAll, new Panel { Width = 40 }, cancel, _next },
        };

        Content = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Margin = new Avalonia.Thickness(24, 24, 24, 8),
                    Spacing = 6,
                    Children = { _header, _progress, _help, _errorPanel },
                },
                new Border
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Child = footer,
                },
                new Border { Margin = new Avalonia.Thickness(24, 0, 24, 0), Child = _scroller },
            },
        };

        KeyDown += OnKeyDown;
        Opened += (_, _) => RenderScreen();
    }

    /// <summary>True when the user pressed "Save it" and the data validated.</summary>
    public bool Confirmed { get; private set; }

    public System.Text.Json.JsonElement Data { get; private set; }

    public int DataVersion { get; private set; }

    public string UndoLabel => _session.UndoLabel;

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
        _next.Content = _session.IsReviewScreen ? "Save it" : "Next ▶";
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
                Foreground = Brushes.Firebrick,
            });
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
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            BorderBrush = Brushes.LightGray,
            Padding = new Avalonia.Thickness(0, 0, 0, 12),
            Child = panel,
        };
    }

    private StackPanel BuildField(WizardField field, int rowIndex, double? labelWidth)
    {
        var panel = new StackPanel { Spacing = 4 };
        var label = new TextBlock
        {
            Text = field.IsOptional ? $"{field.Label} (optional)" : field.Label,
            FontSize = 18,
            Width = labelWidth ?? double.NaN,
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(label);

        Control input;
        if (field.Kind == WizardFieldKind.Choice && field.Choices is { Count: > 0 } choices)
        {
            var combo = new ComboBox
            {
                FontSize = 20,
                MinHeight = 44,
                Width = 560,
                ItemsSource = choices.Select(c => c.Label).ToList(),
                SelectedIndex = Math.Max(0, IndexOfChoice(choices, _session.GetValue(field.Key, rowIndex))),
            };
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedIndex >= 0)
                {
                    _session.SetValue(field.Key, choices[combo.SelectedIndex].Value, rowIndex);
                    RenderScreen();
                }
            };
            input = combo;
        }
        else
        {
            bool multiLine = field.Kind == WizardFieldKind.MultiLineText;
            var box = new TextBox
            {
                FontSize = 20,
                MinHeight = multiLine ? 260 : 44,
                Width = 560,
                Text = _session.GetValue(field.Key, rowIndex),
                AcceptsReturn = multiLine,
                TextWrapping = multiLine ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Watermark = field.ExampleText,
                MaxLength = field.MaxLength,
            };
            box.LostFocus += (_, _) => _session.SetValue(field.Key, box.Text ?? "", rowIndex);
            box.TextChanged += (_, _) => _session.SetValue(field.Key, box.Text ?? "", rowIndex);
            input = box;
        }

        AutomationProperties.SetName(input, field.Label);
        if (field.HelpText is { } help)
        {
            AutomationProperties.SetHelpText(input, help);
        }

        AutomationProperties.SetLabeledBy(input, label);
        panel.Children.Add(input);

        if (field.HelpText is { Length: > 0 } helpText)
        {
            panel.Children.Add(new TextBlock
            {
                Text = helpText,
                FontSize = 16,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
            });
        }

        return panel;
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
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    change,
                },
            });
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
            if (control is TextBox or ComboBox)
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

    private static Button MakeButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 160,
        };
        button.Click += click;
        AutomationProperties.SetName(button, text);
        return button;
    }
}
