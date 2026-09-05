using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "A character not on the keyboard…" (PLAN.md §11 M98).
///
/// <para><b>A short named list, not a Unicode table.</b> Every other program offers a grid of
/// several thousand glyphs organised by a scheme only typographers know. What a trestle board needs
/// is about a dozen marks, and what §6 asks is that each one be named in words rather than left to
/// be recognised at 11 point — "a long dash" tells somebody what they are choosing in a way that
/// seeing "—" does not.</para>
///
/// <para><b>Every character here is in the bundled fonts.</b> The font rule (M14) is bundled-only,
/// so a symbol the app offers has to be one the newsletter can actually print — a picker that could
/// insert a missing glyph would produce a box in the PDF and no warning anywhere.</para>
/// </summary>
public sealed class SymbolPickerDialog : Window
{
    /// <summary>
    /// What a lodge newsletter actually needs, in the order it needs it.
    ///
    /// <para>The dashes lead because they are the commonest and the least reachable: a committee
    /// types two hyphens and the app has had no way to offer the real mark.</para>
    /// </summary>
    public static readonly IReadOnlyList<(string Character, string Name, string Where)> Symbols =
    [
        ("—", "A long dash", "between parts of a sentence — like this"),
        ("–", "A short dash", "between numbers, as in 7–9pm"),
        ("’", "A curly apostrophe", "in the Master’s message"),
        ("“", "An opening quote", "“the brethren"),
        ("”", "A closing quote", "the brethren”"),
        ("•", "A round bullet", "• for a list inside a sentence"),
        ("½", "A half", "½ past seven"),
        ("¼", "A quarter", "¼ of an hour"),
        ("¾", "Three quarters", "¾ full"),
        ("°", "A degree sign", "72° in the hall"),
        ("©", "Copyright", "© Indian Land Lodge"),
        ("®", "Registered", "for a registered name"),
        ("…", "Three dots", "and so on…"),
    ];

    private readonly ListBox _list;

    public SymbolPickerDialog()
    {
        Title = "A character you cannot type";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _list = new ListBox { MinWidth = 420, MaxHeight = 420 };
        foreach ((string character, string name, string where) in Symbols)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                MinHeight = 44,
                Children =
                {
                    new TextBlock
                    {
                        Text = character,
                        FontSize = 24,
                        Width = 40,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = name, FontSize = 17 },
                            new TextBlock
                            {
                                Text = where,
                                FontSize = 15,
                            }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                        },
                    },
                },
            };

            // Named for a screen reader, which cannot say what the glyph looks like.
            Avalonia.Automation.AutomationProperties.SetName(row, $"{name}, {where}");
            _list.Items.Add(row);
        }

        _list.SelectedIndex = 0;

        // Double-click is the gesture this window is most likely to be used with, and M17 taught it
        // everywhere else in the app; without it the user picks, then hunts for the button.
        _list.DoubleTapped += (_, _) => Accept();

        var insert = new Button
        {
            Content = "Put it in",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
        };
        insert.Primary();
        Avalonia.Automation.AutomationProperties.SetName(insert, "Put it in");
        insert.Click += (_, _) => Accept();

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 120,
            IsCancel = true,
        };
        cancel.Action();
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel, put nothing in");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Which character do you want?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                },
                _list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, insert },
                },
            },
        };
    }

    /// <summary>False when the window was cancelled — nothing is put in then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>The character the user chose.</summary>
    public string Chosen { get; private set; } = string.Empty;

    private void Accept()
    {
        if (_list.SelectedIndex < 0)
        {
            return;
        }

        Chosen = Symbols[_list.SelectedIndex].Character;
        Confirmed = true;
        Close();
    }
}
