using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using PageLooks = TrestleBoard.Core.Model.PageLooks;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "What colour the writing is…" (PLAN.md §11 M99 — M86's third deliverable).
///
/// <para><b>Seven colours, every one of them legible.</b> M86 asked for a palette plus a full
/// picker with a contrast warning for anything under 4.5:1. This ships the palette and NOT the
/// picker, deliberately: every colour offered here already clears the floor against white, so there
/// is no warning to give and no argument to have with somebody about their own choice. A picker
/// would add a package reference, a dialog whose meaning is not in words (M86 notes it would be the
/// first such control in the app), and the need to tell a user their colour is a bad one.</para>
///
/// <para><b>Black is how you put it back.</b> It is the colour every style already is, so choosing
/// it names the role again rather than minting an override — the rule the alignment verbs follow
/// for left. That is why there is no separate "put the colour back" command to find.</para>
///
/// <para>Each colour is named as well as shown, because colour is never the only signal (§6).</para>
/// </summary>
public sealed class TextColourDialog : Window
{
    private readonly ListBox _list;

    public TextColourDialog(uint? current, string sample)
    {
        Title = "What colour the writing is";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        string shown = sample.Length > 40 ? sample[..40] + "…" : sample;
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

                        // The swatch and the sample show the document colour, not chrome: they are
                        // the answer being chosen, and a palette token would show the wrong one.
                        Background = new SolidColorBrush(Color.FromUInt32(argb)),
                    }.Token(Border.BorderBrushProperty, Tokens.ChromeBorder),
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = name, FontSize = 17 },
                            new TextBlock
                            {
                                // document colour, not chrome
                                Text = shown,
                                FontSize = 15,
                                Foreground = new SolidColorBrush(Color.FromUInt32(argb)),
                                TextWrapping = TextWrapping.NoWrap,
                            },
                        },
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
                    Text = "What colour should the highlighted words be?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _list,
                new TextBlock
                {
                    Text = "Every colour here prints clearly on white paper. Choose Black to put "
                        + "the writing back the way it was.",
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
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        Chosen = PageLooks.TextColours[_list.SelectedIndex].Argb;
        Confirmed = true;
        Close();
    }
}
