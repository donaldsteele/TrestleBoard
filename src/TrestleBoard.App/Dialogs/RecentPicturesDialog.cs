using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user said to the strip of pictures they have used before.</summary>
internal enum RecentPictureChoice
{
    /// <summary>They closed it. Nothing goes on the page.</summary>
    Cancelled,

    /// <summary>They want the ordinary file picker.</summary>
    BrowseForOne,

    /// <summary>They chose one they have used before; <see cref="RecentPicturesDialog.ChosenPath"/> says which.</summary>
    UseThisOne,
}

/// <summary>
/// The pictures this committee has used before, offered before the file picker (PLAN.md §11 M80).
///
/// <para><b>Most photographs in a trestle board are re-used.</b> The lodge front, the Master's
/// portrait, the emblem the committee scanned in years ago — they go into most issues, and every
/// month somebody navigates the same four folders to find the same file. A strip of what was used
/// before turns that into one press.</para>
///
/// <para><b>Labels, never thumbnails alone.</b> A grid of small pictures is a memory test, and this
/// audience is the one least served by one; the file's own name is what somebody actually
/// remembers. The thumbnail is beside the name, not instead of it.</para>
///
/// <para><b>It never blocks the ordinary route.</b> "Choose a file…" is always there, is the
/// default button, and is what the window becomes when there is nothing to remember yet.</para>
/// </summary>
internal sealed class RecentPicturesDialog : Window
{
    /// <param name="recent">
    /// Paths, newest first. Ones that no longer exist have already been dropped by the caller —
    /// offering a picture that has been deleted is worse than not offering it.
    /// </param>
    internal RecentPicturesDialog(IReadOnlyList<string> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        Title = "Put a picture in";
        SizeToContent = SizeToContent.Height;
        Width = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Put a picture in");

        var browse = new Button
        {
            Content = "Choose a file…",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        browse.Primary();
        browse.Click += (_, _) =>
        {
            Choice = RecentPictureChoice.BrowseForOne;
            Close();
        };
        AutomationProperties.SetName(browse, "Choose a picture file from this computer");

        var cancel = new Button
        {
            Content = "Not now",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 140,
            IsCancel = true,
        };
        cancel.Action();
        cancel.Click += (_, _) => Close();
        AutomationProperties.SetName(cancel, "Do not put a picture in after all");

        var panel = new StackPanel { Margin = new Thickness(28), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = recent.Count > 0 ? "Pictures you have used before" : "Put a picture in",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (string path in recent)
        {
            panel.Children.Add(TileFor(path));
        }

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { cancel, browse },
        });

        Content = new ScrollViewer { MaxHeight = 520, Content = panel };
    }

    /// <summary>What they chose. Closing the window means <see cref="RecentPictureChoice.Cancelled"/>.</summary>
    internal RecentPictureChoice Choice { get; private set; } = RecentPictureChoice.Cancelled;

    /// <summary>Which picture, when they chose one they had used before.</summary>
    internal string? ChosenPath { get; private set; }

    /// <summary>For the tests, which cannot click.</summary>
    internal void ChooseForTest(RecentPictureChoice choice, string? path = null)
    {
        Choice = choice;
        ChosenPath = path;
    }

    private Button TileFor(string path)
    {
        var tile = new Button
        {
            FontSize = 18,
            MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock
            {
                // The file's own name, which is what somebody remembers — never the whole path,
                // which is long, and which on this audience's machines usually contains their own
                // name (PLAN.md §0).
                Text = System.IO.Path.GetFileName(path),
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        tile.Action();
        tile.Click += (_, _) =>
        {
            Choice = RecentPictureChoice.UseThisOne;
            ChosenPath = path;
            Close();
        };
        AutomationProperties.SetName(tile, "Use " + System.IO.Path.GetFileName(path) + " again");
        return tile;
    }
}
