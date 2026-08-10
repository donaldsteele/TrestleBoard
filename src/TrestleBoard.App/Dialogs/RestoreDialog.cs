using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrestleBoard.Editing;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Offered when a recovery file survived a previous run — which, because a clean close deletes it,
/// means the app did not close cleanly (PLAN.md §4, docs/M9-spec.md §1.5).
///
/// The thumbnail is the point: "is this the work I lost?" is a question answered by looking, not by
/// reading a filename.
/// </summary>
public sealed class RestoreDialog : Window
{
    public RestoreDialog(RecoverySnapshot snapshot, byte[]? thumbnailPng, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Title = "TrestleBoard closed unexpectedly";
        SizeToContent = SizeToContent.Height;
        Width = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        AutomationProperties.SetName(this, "Put your work back");

        string when = RecoveryService.DescribeAge(snapshot.SavedAt, now);
        string where = snapshot.OriginalPath is { Length: > 0 } path
            ? System.IO.Path.GetFileName(path)
            : "a newsletter you had not saved yet";

        var restore = new Button
        {
            Content = "Put my work back",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 220,
            IsDefault = true,
        };
        restore.Click += (_, _) =>
        {
            Choice = RestoreChoice.PutItBack;
            Close();
        };
        AutomationProperties.SetName(restore, "Put my work back");

        var discard = new Button
        {
            Content = "Start fresh",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 160,
        };
        discard.Action();

        // M73(b3): this is the ONLY thing that means "throw the recovered work away". Closing the
        // window with the title-bar X used to mean the same, because the caller read a plain false
        // — so a card that had just promised "Nothing has been lost" deleted the snapshot on a
        // window close, which is nobody's decision about anything.
        discard.Click += (_, _) =>
        {
            Choice = RestoreChoice.StartFresh;
            Close();
        };
        AutomationProperties.SetName(discard, "Start fresh without recovering");

        var panel = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "TrestleBoard closed unexpectedly.",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = $"Your work on {where} was saved {when}. Nothing has been lost.",
                    FontSize = 20,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 560,
                },
            },
        };

        if (thumbnailPng is { Length: > 0 })
        {
            using var stream = new MemoryStream(thumbnailPng);

            // Disposed with the window: this one is small, but it is the same rule as the position
            // window's preview, and a leak nobody bothered with is how the rule stops being one.
            var thumbnail = new Bitmap(stream);
            Closed += (_, _) => thumbnail.Dispose();

            var image = new Image
            {
                Source = thumbnail,
                Width = 220,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(image, "A picture of the first page of your recovered work");
            panel.Children.Add(new Border
            {
                Child = image,
                HorizontalAlignment = HorizontalAlignment.Left,
            }
                .Token(Border.BorderBrushProperty, Tokens.ChromeBorder)
                .Token(Border.BorderThicknessProperty, Tokens.BorderThickness));
        }

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { discard, restore },
        });

        Content = panel;
    }

    /// <summary>
    /// What the user decided, which is three answers and not two (M73(b3)). Closing the window is
    /// not one of the two decisions this card asks for, and it must not be read as either.
    /// </summary>
    public RestoreChoice Choice { get; private set; } = RestoreChoice.Closed;

    /// <summary>True when the user asked for the work back.</summary>
    public bool Restore => Choice == RestoreChoice.PutItBack;
}

/// <summary>The three ways out of <see cref="RestoreDialog"/> (M73(b3)).</summary>
public enum RestoreChoice
{
    /// <summary>The window was closed without answering. The recovered work stays where it is.</summary>
    Closed,

    /// <summary>"Put my work back."</summary>
    PutItBack,

    /// <summary>"Start fresh" — said deliberately, and the only answer that throws the work away.</summary>
    StartFresh,
}
