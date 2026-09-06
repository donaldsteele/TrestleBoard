using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using PageLooks = TrestleBoard.Core.Model.PageLooks;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Change what colour the emblem is…" (PLAN.md §11 M104).
///
/// <para><b>The same seven colours as the writing.</b> Which colours print clearly on white paper
/// does not depend on whether the ink is making a letter or a square and compasses, and a second
/// palette would be a second thing to learn for no gain. It is <see cref="TextColourDialog"/>'s
/// list, asked about a drawing.</para>
///
/// <para><b>Black is how you put it back</b> — the colour every emblem arrives in, so choosing it
/// is the undo of a colour without there being a separate command to hunt for.</para>
/// </summary>
public sealed class EmblemColourDialog : Window
{
    private readonly ListBox _list;

    public EmblemColourDialog(uint current)
    {
        Title = "What colour the emblem is";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _list = new ListBox { MinWidth = 420, MaxHeight = 400 };

        foreach ((string name, uint argb) in PageLooks.TextColours)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                MinHeight = 44,
                Children =
                {
                    new Border
                    {
                        Width = 24,
                        Height = 24,
                        CornerRadius = new Avalonia.CornerRadius(3),
                        BorderThickness = new Avalonia.Thickness(1),
                        VerticalAlignment = VerticalAlignment.Center,

                        // document colour, not chrome: the swatch IS the answer being chosen, so a
                        // palette token here would show the wrong thing.
                        Background = new SolidColorBrush(Color.FromUInt32(argb)),
                    }.Token(Border.BorderBrushProperty, Tokens.ChromeBorder),
                    new TextBlock
                    {
                        Text = name,
                        FontSize = 17,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            };

            Avalonia.Automation.AutomationProperties.SetName(row, name);
            _list.Items.Add(row);
        }

        _list.SelectedIndex = 0;
        for (int i = 0; i < PageLooks.TextColours.Count; i++)
        {
            if (current == PageLooks.TextColours[i].Argb)
            {
                _list.SelectedIndex = i;
                break;
            }
        }

        _list.DoubleTapped += (_, _) => Accept();

        var apply = new Button
        {
            Content = "Use this colour",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Use this colour");
        apply.Click += (_, _) => Accept();

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsCancel = true,
        };
        cancel.Action();
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel, change nothing");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "What colour should the emblem be drawn in?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _list,
                new TextBlock
                {
                    Text = "The whole emblem is drawn in one colour. Choose Black to put it back "
                        + "the way it came, which is what prints best on a black-and-white copier.",
                    FontSize = 15,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, apply },
                },
            },
        };
    }

    /// <summary>False when the window was cancelled — nothing is changed then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The colour the user chose.</summary>
    public uint Chosen { get; private set; }

    private void Accept()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= PageLooks.TextColours.Count)
        {
            return;
        }

        Chosen = PageLooks.TextColours[index].Argb;
        Confirmed = true;
        Close();
    }
}
