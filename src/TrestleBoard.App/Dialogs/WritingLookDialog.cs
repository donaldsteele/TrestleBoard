using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "How spaced out the writing is…" (PLAN.md §11 M93).
///
/// <para><b>Three named choices and one tick box, not four numbers.</b> The four fields behind this
/// — line spacing, the gap above and below a paragraph, and the first-line indent — are a
/// multiplier and three point values, and §6 is explicit that this audience should not be asked to
/// operate spinners over those to say a thing they can say in a word. The word they say when a page
/// looks cramped is "spread out"; the app already prefers naming outcomes over numbers.</para>
///
/// <para><b>Each choice shows what it does.</b> Three lines of real text at each setting, drawn at
/// the size they will print, because "1.15" and "1.5" mean nothing to somebody who has never had to
/// know what they mean and everything is guessable from looking.</para>
///
/// <para><b>It says out loud that it changes the whole newsletter.</b> The locked constraint (§1,
/// M14) is that nothing carries direct formatting, so this edits the body style and every article
/// wearing it. That is also what is meant — nobody asks for one paragraph to be roomier — but a
/// dialog that quietly reached further than the user expected would be the M55 defect.</para>
/// </summary>
public sealed class WritingLookDialog : Window
{
    private const string Sample =
        "The stated communication will be held on the first Tuesday. Supper is at half past six "
        + "and the brethren are asked to arrive early.";

    private readonly List<RadioButton> _choices = [];
    private readonly CheckBox _indent;

    public WritingLookDialog(WritingLookController.Spacing? current, bool indented)
    {
        Title = "How spaced out the writing is";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var choices = new StackPanel { Spacing = 12 };
        foreach (WritingLookController.Spacing spacing in Enum.GetValues<WritingLookController.Spacing>())
        {
            RadioButton row = BuildChoice(spacing, current == spacing);
            _choices.Add(row);
            choices.Children.Add(row);
        }

        // Nothing is chosen when the newsletter matches none of the three — which one written by a
        // later version, or edited by hand, legitimately can be. Showing no choice is the honest
        // answer; picking the nearest would be the app telling the user something untrue about
        // their own file.
        _indent = new CheckBox
        {
            Content = "Start each paragraph pushed in a little",
            FontSize = 17,
            MinHeight = 44,
            IsChecked = indented,
        };
        Avalonia.Automation.AutomationProperties.SetName(
            _indent, "Start each paragraph pushed in a little");

        var apply = new Button
        {
            Content = "Use this",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        apply.Primary();
        Avalonia.Automation.AutomationProperties.SetName(apply, "Use this");
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
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "How spaced out should the writing be?",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                },
                new TextBlock
                {
                    Text = "This changes the articles all through the newsletter, not just the "
                        + "page you are looking at. Press Ctrl+Z afterwards if you want it back.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520,
                },
                choices,
                _indent,
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

    /// <summary>False when the window was cancelled or closed — nothing is applied then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Which of the three the user left chosen, or null if they chose none.</summary>
    public WritingLookController.Spacing? ChosenSpacing
    {
        get
        {
            for (int i = 0; i < _choices.Count; i++)
            {
                if (_choices[i].IsChecked == true)
                {
                    return (WritingLookController.Spacing)i;
                }
            }

            return null;
        }
    }

    /// <summary>Whether the user left the first-line indent ticked.</summary>
    public bool IndentFirstLine => _indent.IsChecked == true;

    private static RadioButton BuildChoice(WritingLookController.Spacing spacing, bool chosen)
    {
        (string label, string explanation) = spacing switch
        {
            WritingLookController.Spacing.Tight =>
                ("Closer together", "Fits more on a page, for a month with a lot of news."),
            WritingLookController.Spacing.Normal =>
                ("Normal", "What the templates come with."),
            _ =>
                ("More spread out", "Easier to read, and takes more room."),
        };

        (float lineSpacing, float spaceAfter) = WritingLookController.NumbersFor(spacing);

        var preview = new TextBlock
        {
            Text = Sample,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,

            // The point of the preview is the spacing, so it is the one thing drawn to scale: the
            // multiplier is the style's own, and the gap under it is the paragraph gap in points.
            LineHeight = 13 * lineSpacing,
            Margin = new Avalonia.Thickness(0, 4, 0, spaceAfter),
        }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);

        var row = new RadioButton
        {
            GroupName = "writing-look",
            IsChecked = chosen,
            MinHeight = 44,
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 17, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = explanation,
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 460,
                    }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                    preview,
                },
            },
        };

        // The whole row is one thing to a screen reader, which is what it is to everybody else.
        Avalonia.Automation.AutomationProperties.SetName(row, $"{label}. {explanation}");
        return row;
    }
}
