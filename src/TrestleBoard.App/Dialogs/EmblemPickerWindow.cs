using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using TrestleBoard.App.Emblems;
using TrestleBoard.App.Theme;
using TrestleBoard.Emblems;
using TrestleBoard.Rendering;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Add an emblem…" (PLAN.md §11 M65): the cabinet, opened.
///
/// <para><b>Named, searchable and walkable by keyboard</b> — the acceptance in three words, and the
/// order matters. Every tile carries its name in writing beside the picture rather than as a
/// hover tip, because a tip is invisible to a screen reader and to anybody not using a mouse, and
/// because "the one that looks like a hammer" is how somebody will describe what they are after.
/// </para>
///
/// <para>Thumbnails are drawn at a modest size and thrown away with the window, and from M72 they
/// are the <b>only</b> pixels in the emblem story: choosing one puts path data on the page, not a
/// picture of it. They are drawn by <see cref="VectorArtRenderer"/>, the same routine that paints
/// the page and the PDF, so a tile cannot come to disagree with what it puts there.</para>
/// </summary>
internal sealed class EmblemPickerWindow : Window
{
    private const int ThumbnailPixels = 128;

    private readonly TextBox _search;
    private readonly TextBlock _count;
    private readonly StackPanel _shelf;
    private readonly List<Bitmap> _thumbnails = [];

    internal EmblemPickerWindow()
    {
        Title = "Add an emblem";
        Width = 760;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = "Add an emblem",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        };

        _search = new TextBox
        {
            FontSize = 22,
            MinHeight = 50,
            Watermark = "Search, or leave empty to see them all",
        };
        AutomationProperties.SetName(_search, "Search for an emblem");
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Fill();
            }
        };

        _count = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_count, "How many emblems were found");
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);

        _shelf = new StackPanel { Spacing = 18 };

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
                    Children = { heading, _search, _count },
                },
                new StackPanel
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                    Children = { cancel },
                },
                new ScrollViewer
                {
                    Content = _shelf,
                    Margin = new Avalonia.Thickness(0, 16, 0, 0),
                },
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

        Opened += (_, _) => _search.Focus();
        Closed += (_, _) => Forget();
        Fill();
    }

    /// <summary>What the user picked, or null if they closed the window.</summary>
    internal Emblem? Chosen { get; private set; }

    internal string CountTextForTest => _count.Text ?? "";

    internal void TypeForTest(string what) => _search.Text = what;

    internal IReadOnlyList<Button> TilesForTest =>
        [.. _shelf.Children.OfType<StackPanel>()
            .SelectMany(group => group.Children.OfType<WrapPanel>())
            .SelectMany(wrap => wrap.Children.OfType<Button>())];

    internal void ChooseForTest(string emblemId)
    {
        Chosen = EmblemLibrary.Find(emblemId);
        Close();
    }

    private void Fill()
    {
        Forget();
        _shelf.Children.Clear();

        IReadOnlyList<Emblem> found = EmblemLibrary.Search(_search.Text);
        _count.Text = found.Count switch
        {
            0 => "Nothing on the shelf matches those words. Try a different one, or empty the box.",
            1 => "One emblem.",
            _ when string.IsNullOrWhiteSpace(_search.Text) => $"All {found.Count} emblems.",
            _ => $"{found.Count} emblems.",
        };

        // Grouped under their headings, in shelf order, so the window doubles as a way to see what
        // is there at all — the same reason the help window's empty box lists everything.
        foreach (string category in EmblemLibrary.Categories)
        {
            List<Emblem> here = [.. found.Where(e => e.Category == category)];
            if (here.Count == 0)
            {
                continue;
            }

            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (Emblem emblem in here)
            {
                wrap.Children.Add(Tile(emblem));
            }

            _shelf.Children.Add(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = category,
                        FontSize = 18,
                        FontWeight = FontWeight.Bold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    wrap,
                },
            });
        }
    }

    private Button Tile(Emblem emblem)
    {
        var picture = new Image
        {
            Source = Thumbnail(emblem),
            Width = 96,
            Height = 96,
            Stretch = Stretch.Uniform,
        };

        var button = new Button
        {
            Width = 168,
            MinHeight = 168,
            Margin = new Avalonia.Thickness(0, 0, 12, 12),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    picture,
                    new TextBlock
                    {
                        Text = emblem.Name,
                        FontSize = 15,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 148,
                    },
                },
            },
        };

        button.Tag = emblem.Id;

        // The name and the sentence both: a screen reader user is choosing a picture they cannot
        // see, so "Square and compasses" alone leaves them to guess what will land on the page.
        AutomationProperties.SetName(button, $"{emblem.Name}. {emblem.Description}");
        button.Click += (_, _) =>
        {
            Chosen = emblem;
            Close();
        };

        return button.Action();
    }

    /// <summary>
    /// A tile's picture, drawn through <see cref="VectorArtRenderer"/> — the SAME routine that paints
    /// the emblem onto the page and into the PDF (M72 (c)).
    ///
    /// <para>That is deliberate and it is the deliverable. An <c>EmblemRenderer.Draw</c> kept alive
    /// here for thumbnails, beside the renderer's own path drawing, would be the "second renderer"
    /// M65 rasterised the emblem to avoid — two routines drawing the same artwork, free to drift, so
    /// that one day the tile stops looking like what lands on the page. There is one, and this is a
    /// caller of it.</para>
    ///
    /// <para>These pixels are the only ones left in the emblem story, and they never leave the
    /// window: they are a picture OF the drawing, thrown away when the picker closes.</para>
    /// </summary>
    private Bitmap Thumbnail(Emblem emblem)
    {
        byte[] png = VectorArtRenderer.ToPng(
            EmblemGeometry.SpecsOf(emblem),
            emblem.Width,
            emblem.Height,
            ThumbnailPixels,

            // The ink the inserted block will use, taken from the block rather than restated — a
            // tile that did not match what lands on the page would be the drift M72 (c) is about.
            new SKColor(Core.Model.VectorBlock.DefaultInkArgb));
        var bitmap = new Bitmap(new MemoryStream(png));
        _thumbnails.Add(bitmap);
        return bitmap;
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
