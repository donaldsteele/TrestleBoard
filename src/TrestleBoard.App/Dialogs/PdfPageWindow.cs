using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrestleBoard.App.Theme;
using TrestleBoard.PdfPages;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "A page from a PDF" (PLAN.md §11 M67): the pages of a chosen file, as pictures of themselves.
///
/// <para><b>Thumbnails, because a page number is not a page.</b> The committee is handed a district
/// bulletin and told "the calendar is in there somewhere". Asking them to type a number would be
/// asking them to open the file in another program first, which is the round trip this milestone
/// exists to remove.</para>
///
/// <para>Rendered small and once per window. A twenty-page bulletin at full size would be eighty
/// megabytes to look at a menu; the chosen page alone is re-rendered at printing size.</para>
/// </summary>
internal sealed class PdfPageWindow : Window
{
    private const int ThumbnailPixels = 220;

    private readonly List<Bitmap> _thumbnails = [];

    internal PdfPageWindow(byte[] pdf, IReadOnlyList<PdfPageInfo> pages, string fileName)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentNullException.ThrowIfNull(pages);

        Title = "A page from a PDF";
        Width = 760;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = pages.Count == 1 ? "Which page?" : $"Which of the {pages.Count} pages?",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        };

        var whence = new TextBlock
        {
            Text = $"From {fileName}. The page comes in as a picture — you can move it, size it and "
                + "wrap writing round it like any other.",
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
        };

        var shelf = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (PdfPageInfo page in pages)
        {
            shelf.Children.Add(Tile(pdf, page));
        }

        Button cancel = Wide("Cancel", "Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close();

        Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Spacing = 12,
                    Children = { heading, whence },
                },
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children = { cancel },
                },
                new ScrollViewer { Content = shelf, Margin = new Avalonia.Thickness(0, 16, 0, 0) },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Closed += (_, _) => Forget();
    }

    /// <summary>The page the user chose, 1-based, or null if they closed the window.</summary>
    internal int? ChosenPage { get; private set; }

    internal IReadOnlyList<Button> TilesForTest =>
        [.. this.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag is int)];

    internal void ChooseForTest(int pageNumber)
    {
        ChosenPage = pageNumber;
        Close();
    }

    private Button Tile(byte[] pdf, PdfPageInfo page)
    {
        var picture = new Avalonia.Controls.Image
        {
            Width = 150,
            Height = 190,
            Stretch = Stretch.Uniform,
            Source = Thumbnail(pdf, page.Number),
        };

        var button = new Button
        {
            Tag = page.Number,
            Width = 200,
            MinHeight = 260,
            Margin = new Avalonia.Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    picture,
                    new TextBlock
                    {
                        Text = $"Page {page.Number}",
                        FontSize = 17,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };

        // The shape as well as the number: a screen-reader user choosing between twenty pages of a
        // bulletin has nothing else to go on, and "wide" is often the whole distinction they need.
        string shape = page.WidthPt > page.HeightPt ? "on its side" : "upright";
        AutomationProperties.SetName(button, $"Page {page.Number}, {shape}");

        button.Click += (_, _) =>
        {
            ChosenPage = page.Number;
            Close();
        };

        return button.Action();
    }

    /// <summary>
    /// A small rendering of one page, or nothing if that page will not draw. A bulletin with one
    /// bad page in it is still a bulletin — refusing the whole file over it would be the app
    /// deciding for the committee that they cannot have the other nineteen.
    /// </summary>
    private Bitmap? Thumbnail(byte[] pdf, int pageNumber)
    {
        try
        {
            // The stream is disposed either way: a Bitmap constructor that throws would otherwise
            // leave it behind, and the failing page is exactly the case this method is written for.
            using var png = new MemoryStream(
                PdfPageRasterizer.RenderPage(pdf, pageNumber, ThumbnailPixels));
            var bitmap = new Bitmap(png);
            _thumbnails.Add(bitmap);
            return bitmap;
        }
        catch (Exception e) when (e is PdfPageException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void Forget()
    {
        foreach (Bitmap bitmap in _thumbnails)
        {
            bitmap.Dispose();
        }

        _thumbnails.Clear();
    }

    private static Button Wide(string content, string automationName)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        AutomationProperties.SetName(button, automationName);
        return button.Action();
    }
}
