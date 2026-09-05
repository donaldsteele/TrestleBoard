using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Change capitals…" (PLAN.md §11 M98).
///
/// <para>Three choices, each shown done to the user's own highlighted words rather than to a sample
/// — the same rule M93's spacing window follows. "TITLE CASE" means nothing to this audience;
/// seeing their own heading in each of the three forms means everything.</para>
/// </summary>
public sealed class ChangeCaseDialog : Window
{
    private readonly List<RadioButton> _choices = [];

    public ChangeCaseDialog(string highlighted)
    {
        Title = "Change capitals";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        // Enough to recognise, short enough to fit on one line at 17pt.
        string sample = highlighted.Length > 48 ? highlighted[..48] + "…" : highlighted;

        var choices = new StackPanel { Spacing = 10 };
        foreach (TextEditorController.LetterCase option in Enum.GetValues<TextEditorController.LetterCase>())
        {
            RadioButton row = Build(option, sample);
            _choices.Add(row);
            choices.Children.Add(row);
        }

        _choices[0].IsChecked = true;

        var apply = new Button
        {
            Content = "Change them",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Change them");
        apply.Click += (_, _) =>
        {
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
                new TextBlock
                {
                    Text = "How should the highlighted words look?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480,
                },
                choices,
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

    /// <summary>Which of the three the user left chosen.</summary>
    public TextEditorController.LetterCase Chosen
    {
        get
        {
            for (int i = 0; i < _choices.Count; i++)
            {
                if (_choices[i].IsChecked == true)
                {
                    return (TextEditorController.LetterCase)i;
                }
            }

            return TextEditorController.LetterCase.Upper;
        }
    }

    private static RadioButton Build(TextEditorController.LetterCase option, string sample)
    {
        string label = option switch
        {
            TextEditorController.LetterCase.Upper => "ALL CAPITALS",
            TextEditorController.LetterCase.Lower => "all small letters",
            _ => "One Capital Per Word",
        };

        string shown = option switch
        {
            TextEditorController.LetterCase.Upper =>
                sample.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            TextEditorController.LetterCase.Lower =>
                sample.ToLower(System.Globalization.CultureInfo.CurrentCulture),
            _ => TitleCaseForPreview(sample),
        };

        var row = new RadioButton
        {
            GroupName = "letter-case",
            MinHeight = 44,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeight.SemiBold },

                    // The user's own words, done. Seeing it settles the question that naming it
                    // cannot.
                    new TextBlock
                    {
                        Text = shown,
                        FontSize = 16,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                    }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                },
            },
        };

        Avalonia.Automation.AutomationProperties.SetName(row, $"{label}. Like this: {shown}");
        return row;
    }

    /// <summary>
    /// The preview's own copy of the rule, matching <c>TextEditorController</c>'s: a word already in
    /// capitals must come DOWN, which <c>TextInfo.ToTitleCase</c> refuses to do, and that input is
    /// the one somebody reaches for this command to fix.
    /// </summary>
    private static string TitleCaseForPreview(string text)
    {
        var built = new System.Text.StringBuilder(text.Length);
        bool startOfWord = true;
        foreach (char c in text)
        {
            built.Append(startOfWord
                ? char.ToUpper(c, System.Globalization.CultureInfo.CurrentCulture)
                : char.ToLower(c, System.Globalization.CultureInfo.CurrentCulture));
            startOfWord = !char.IsLetterOrDigit(c) && c != '\'' && c != '’';
        }

        return built.ToString();
    }
}
