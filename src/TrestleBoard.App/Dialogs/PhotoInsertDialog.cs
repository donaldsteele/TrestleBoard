using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Asks for the picture's description before it goes on the page (PLAN.md §6, docs/M6-spec.md §6):
/// a screen-reader user must never meet an unlabelled photo. Plain language, 18pt text, big
/// targets, and the description box already focused so a keyboard user can just type.
/// </summary>
public sealed class PhotoInsertDialog : Window
{
    private readonly TextBox _altText;
    private readonly TextBox _caption;
    private readonly TextBlock _warning;

    public PhotoInsertDialog(string fileName)
    {
        Title = "Describe this picture";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _altText = new TextBox
        {
            FontSize = 18,
            MinHeight = 44,
            Width = 460,
            AcceptsReturn = false,
            Watermark = "For example: Brothers at the summer picnic",
        };
        Avalonia.Automation.AutomationProperties.SetName(_altText, "Description of the picture");

        _caption = new TextBox
        {
            FontSize = 18,
            MinHeight = 44,
            Width = 460,
            AcceptsReturn = false,
            Watermark = "Optional — printed under the picture",
        };
        Avalonia.Automation.AutomationProperties.SetName(_caption, "Caption printed under the picture");

        _warning = new TextBlock
        {
            Text = "⚠ Please type a short description first. People who cannot see the picture "
                + "have nothing else to go on.",
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460,
            IsVisible = false,
        }.Token(TextBlock.ForegroundProperty, Tokens.Warning);

        var insert = new Button
        {
            Content = "Put it on the page",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        insert.Click += (_, _) =>
        {
            // A description is what a screen-reader user gets instead of the picture (PLAN.md §6),
            // so the button that puts a photo on the page refuses until there is one. The dialog
            // asks for it first for the same reason.
            if (string.IsNullOrWhiteSpace(_altText.Text))
            {
                _warning.IsVisible = true;
                _altText.Focus();
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
                    Text = $"Adding “{fileName}”.",
                    FontSize = 18,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                new TextBlock
                {
                    Text = "Type a short description. People who cannot see the picture will have "
                        + "this read to them.",
                    FontSize = 16,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _altText,
                _warning,
                new TextBlock { Text = "Caption (optional)", FontSize = 16 },
                _caption,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, insert },
                },
            },
        };

        Opened += (_, _) => _altText.Focus();
    }

    public bool Confirmed { get; private set; }

    public string AltText => _altText.Text ?? "";

    public string? Caption => string.IsNullOrWhiteSpace(_caption.Text) ? null : _caption.Text;
}
