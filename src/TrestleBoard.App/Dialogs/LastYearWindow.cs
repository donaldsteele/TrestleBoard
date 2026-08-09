using System;
using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// Last year's issue of this month, beside the one being written (PLAN.md §11 M59).
///
/// <para><b>Read-only by construction, not by discipline.</b> This window is handed a
/// <c>TboardPackage</c>, some strings and a callback. It never builds a <c>DocumentSession</c>, so
/// there is no undo stack to confuse with the editor's, nothing for autosave to find, and no
/// command that could reach the old file. The pages are shown as pictures — a picture cannot be
/// edited — and the only thing that leaves this window is text, through the one callback.</para>
///
/// <para>That is the answer to PLAN.md's acceptance that "two documents open means the action
/// catalog, undo stack and autosave are provably scoped to the editable one". There are not two
/// documents open in any sense the app understands. There is one newsletter and one photograph of
/// an old one.</para>
/// </summary>
internal sealed class LastYearWindow : Window
{
    private readonly List<Bitmap> _pictures = [];

    internal LastYearWindow(
        PastIssue issue,
        int month,
        int year,
        Func<byte[]?> firstPagePicture,
        Action<PastArticle> copyAcross)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(firstPagePicture);
        ArgumentNullException.ThrowIfNull(copyAcross);

        Title = $"{MonthName(month)} {year - 1}";
        Width = 620;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        AutomationProperties.SetName(this, $"Last year's {MonthName(month)}, to look at");

        var body = new StackPanel { Spacing = 16, Margin = new Avalonia.Thickness(24) };
        body.Children.Add(new TextBlock
        {
            Text = $"{MonthName(month)} {year - 1}",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "This is last year's issue, to look at. Nothing you do here can change it — it "
                + "is not open for writing, only for reading.",
            FontSize = 18,
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap,
        });

        if (firstPagePicture() is { Length: > 0 } png)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(png);
                var picture = new Bitmap(stream);
                _pictures.Add(picture);
                var image = new Image
                {
                    Source = picture,
                    Width = 260,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(image, $"The front page of {MonthName(month)} {year - 1}");
                body.Children.Add(image);
            }
            catch (ArgumentException)
            {
                // No picture is a smaller loss than no window.
            }
        }

        var articles = new StackPanel { Spacing = 12 };
        if (issue.Articles.Count == 0)
        {
            articles.Children.Add(new TextBlock
            {
                Text = "There is no writing in last year's issue that TrestleBoard can copy across.",
                FontSize = 18,
                MaxWidth = 520,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        foreach (PastArticle article in issue.Articles)
        {
            PastArticle chosen = article;
            articles.Children.Add(new TextBlock
            {
                Text = article.Heading,
                FontSize = 18,
                MaxWidth = 520,
                TextWrapping = TextWrapping.Wrap,
            });

            var copy = new Button
            {
                Content = "Copy this into this month",
                FontSize = 18,
                MinHeight = 44,
                MinWidth = 260,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            AutomationProperties.SetName(copy, $"Copy “{article.Heading}” into this month");
            copy.Action();
            copy.Click += (_, _) => copyAcross(chosen);
            articles.Children.Add(copy);
        }

        body.Children.Add(new TextBlock
        {
            Text = "What was in it",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
        });
        body.Children.Add(new ScrollViewer { Content = articles, Height = 260 });

        var close = new Button
        {
            Content = "Close",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 140,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(close, "Close");
        close.Action();
        close.Click += (_, _) => Close();
        body.Children.Add(close);

        Content = new ScrollViewer { Content = body };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Closed += (_, _) =>
        {
            foreach (Bitmap picture in _pictures)
            {
                picture.Dispose();
            }
        };
    }

    internal IReadOnlyList<Button> ButtonsForTest
    {
        get
        {
            var all = new List<Button>();
            Collect(Content as Control, all);
            return all;
        }
    }

    private static void Collect(Control? control, List<Button> into)
    {
        switch (control)
        {
            case Button button:
                into.Add(button);
                break;
            case ScrollViewer scroller:
                Collect(scroller.Content as Control, into);
                break;
            case Panel panel:
                foreach (Control child in panel.Children)
                {
                    Collect(child, into);
                }

                break;
        }
    }

    private static string MonthName(int month) =>
        month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)
            : "That month";
}
