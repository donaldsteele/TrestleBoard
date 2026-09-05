using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using PageLooks = TrestleBoard.Core.Model.PageLooks;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// What colour a box on the page is (PLAN.md §11 M95) — used both when one is put there and when
/// one already on the page is recoloured.
///
/// <para><b>A short fixed list, not a colour wheel.</b> Every colour offered prints legibly on
/// white and sits with the M16 palette; a freely chosen one cannot be trusted to do either, and §6
/// would rather this audience picked "cream" from seven than mixed one from a picker. The full
/// picker arrives with M86, for text, where the case for it is stronger and the contrast warning is
/// part of the deliverable.</para>
///
/// <para><b>Two questions, because a box has two colours.</b> What it is filled with, and what its
/// outline is — and either may be nothing at all, which is what "See-through" and "No outline"
/// mean. A see-through box with no outline would be invisible, so that pair is refused with a
/// sentence rather than quietly produced.</para>
/// </summary>
public sealed class BoxColoursDialog : Window
{
    private readonly ComboBox _fill;
    private readonly ComboBox _outline;
    private readonly TextBlock _warning;

    public BoxColoursDialog(string title, uint? fill, uint? outline)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _fill = Chooser("See-through", fill, "What the box is filled with");
        _outline = Chooser("No outline", outline, "What the outline round the box is");

        _warning = new TextBlock
        {
            Text = "⚠ A see-through box with no outline cannot be seen at all. "
                + "Please choose a colour for one of them.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            IsVisible = false,
        }.Token(TextBlock.ForegroundProperty, Tokens.Warning);

        var apply = new Button
        {
            Content = "Use these colours",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Use these colours");
        apply.Click += (_, _) =>
        {
            if (Fill is null && Outline is null)
            {
                _warning.IsVisible = true;
                return;
            }

            Confirmed = true;
            Close();
        };

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
                Row("Filled with", _fill),
                Row("Outline", _outline),
                _warning,
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

    /// <summary>False when the window was cancelled — nothing is applied then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The chosen fill, or null for see-through.</summary>
    public uint? Fill => Chosen(_fill);

    /// <summary>The chosen outline, or null for none.</summary>
    public uint? Outline => Chosen(_outline);

    private static uint? Chosen(ComboBox box) =>
        box.SelectedIndex <= 0 ? null : PageLooks.BoxColours[box.SelectedIndex - 1].Argb;

    private static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock
            {
                Text = label,
                FontSize = 17,
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
            },
            control,
        },
    };

    private static ComboBox Chooser(string noneLabel, uint? current, string automationName)
    {
        var box = new ComboBox { FontSize = 17, MinHeight = 44, Width = 260 };

        // The "nothing" choice is first, so it is what an unset box lands on and what the arrow
        // keys reach first.
        box.Items.Add(Swatch(noneLabel, null));
        foreach ((string name, uint argb) in PageLooks.BoxColours)
        {
            box.Items.Add(Swatch(name, argb));
        }

        box.SelectedIndex = 0;
        for (int i = 0; i < PageLooks.BoxColours.Count; i++)
        {
            if (current == PageLooks.BoxColours[i].Argb)
            {
                box.SelectedIndex = i + 1;
                break;
            }
        }

        Avalonia.Automation.AutomationProperties.SetName(box, automationName);
        return box;
    }

    /// <summary>
    /// A colour is named as well as shown. Colour is never the only signal (§6): somebody who
    /// cannot tell two of these apart still has the words, and so does a screen reader.
    /// </summary>
    private static StackPanel Swatch(string name, uint? argb)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new Border
                {
                    Width = 24,
                    Height = 24,
                    CornerRadius = new Avalonia.CornerRadius(3),
                    BorderThickness = new Avalonia.Thickness(1),

                    // The swatch shows the document colour, not chrome: it is the answer the user
                    // is choosing, so a palette token would make it show the wrong one. Its
                    // outline IS chrome and comes from the palette below.
                    Background = argb is { } value
                        ? new SolidColorBrush(Color.FromUInt32(value))
                        : Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center,
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
        return row;
    }
}
