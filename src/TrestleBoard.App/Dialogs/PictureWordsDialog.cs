using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The two small windows behind "Describe this picture…" and "Write a caption…" (PLAN.md §11 M18).
///
/// <para>One class, two callers, because the two are the same window with different words: a
/// heading, a sentence of explanation, one big text box and two buttons. The difference that
/// matters is not the layout but the rule — a description may not be left blank, because a
/// screen-reader user has nothing else to go on (PLAN.md §6), while a caption may, because a
/// picture that needs no words under it is an ordinary thing to want.</para>
///
/// <para>Both were collected at insert time by <see cref="PhotoInsertDialog"/> and could never be
/// changed afterwards; that is the hole this closes.</para>
/// </summary>
public sealed class PictureWordsDialog : Window
{
    private readonly TextBox _box;
    private readonly TextBlock _warning;
    private readonly bool _required;

    private PictureWordsDialog(
        string title,
        string heading,
        string explanation,
        string watermark,
        string automationName,
        string confirmLabel,
        string? initialText,
        bool required)
    {
        _required = required;

        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _box = new TextBox
        {
            FontSize = 18,
            MinHeight = 44,
            Width = 460,
            AcceptsReturn = false,
            Text = initialText ?? "",
            Watermark = watermark,
        };
        Avalonia.Automation.AutomationProperties.SetName(_box, automationName);

        _warning = new TextBlock
        {
            Text = "⚠ Please type a short description first. People who cannot see the picture "
                + "have nothing else to go on.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            IsVisible = false,
        }.Token(TextBlock.ForegroundProperty, Tokens.Warning);

        var confirm = new Button
        {
            Content = confirmLabel,
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        confirm.Click += (_, _) =>
        {
            if (_required && string.IsNullOrWhiteSpace(_box.Text))
            {
                _warning.IsVisible = true;
                _box.Focus();
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
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = heading,
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                new TextBlock
                {
                    Text = explanation,
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _box,
                _warning,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, confirm },
                },
            },
        };

        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public bool Confirmed { get; private set; }

    /// <summary>What the user typed; null when the box was left empty.</summary>
    public string? Text => string.IsNullOrWhiteSpace(_box.Text) ? null : _box.Text.Trim();

    public static PictureWordsDialog ForAltText(string? current) => new(
        "Describe this picture",
        "Describe this picture for people who can't see it.",
        "A screen reader says these words instead of showing the picture. One short sentence is "
            + "plenty — for example: Brothers at the summer picnic.",
        "For example: Brothers at the summer picnic",
        "Description of the picture",
        "Save the description",
        current,
        required: true);

    public static PictureWordsDialog ForCaption(string? current) => new(
        "Caption for this picture",
        "Write a caption.",
        "The caption is printed under the picture in the newsletter. Leave it empty if the picture "
            + "does not need one.",
        "Optional — printed under the picture",
        "Caption printed under the picture",
        "Save the caption",
        current,
        required: false);
}
