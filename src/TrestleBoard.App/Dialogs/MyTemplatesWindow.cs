using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrestleBoard.App.Integration;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// The committee's own templates: rename one, take one off the shelf, or hand one to a successor
/// (PLAN.md §11 M57).
///
/// <para>Deliberately separate from the Start screen. The Start screen's job is to get somebody
/// into a newsletter in one press, and putting rename and remove buttons on the tiles there would
/// mean a person reaching for "start from this" and finding "delete this" under their finger. The
/// Start screen lists templates; this window is where they are managed.</para>
/// </summary>
internal sealed class MyTemplatesWindow : Window
{
    private readonly UserTemplateStore _store;
    private readonly StackPanel _list;
    private readonly TextBlock _status;
    private readonly List<Bitmap> _pictures = [];

    internal MyTemplatesWindow(UserTemplateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Title = "My templates";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "My templates");

        _list = new StackPanel { Spacing = 16 };
        _status = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(_status, "What just happened");
        AutomationProperties.SetLiveSetting(_status, Avalonia.Automation.AutomationLiveSetting.Polite);

        var close = new Button
        {
            Content = "Close",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 140,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        close.Action();
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Close();

        Content = new DockPanel { Margin = new Avalonia.Thickness(24) };
        var body = new StackPanel { Spacing = 16 };
        body.Children.Add(new TextBlock
        {
            Text = "My templates",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = "These are the layouts you have saved. Starting a newsletter from one of them "
                + "keeps the layout and leaves the writing to you.",
            FontSize = 18,
            MaxWidth = 620,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new ScrollViewer { Content = _list, Height = 360 });
        body.Children.Add(_status);
        body.Children.Add(close);
        Content = body;

        Closed += (_, _) =>
        {
            foreach (Bitmap picture in _pictures)
            {
                picture.Dispose();
            }
        };

        Refresh();
    }

    /// <summary>A template the user asked to hand on, picked up by the shell after this closes.</summary>
    internal string? ExportRequestedFor { get; private set; }

    internal string? RenameAnswerForTest { get; set; }

    internal bool? RemoveAnswerForTest { get; set; }

    internal IReadOnlyList<Button> ButtonsForTest
    {
        get
        {
            var all = new List<Button>();
            Collect(_list, all);
            return all;
        }
    }

    internal string StatusForTest => _status.Text ?? "";

    private static void Collect(Panel panel, List<Button> into)
    {
        foreach (Control child in panel.Children)
        {
            switch (child)
            {
                case Button button:
                    into.Add(button);
                    break;
                case Panel nested:
                    Collect(nested, into);
                    break;
            }
        }
    }

    private void Refresh()
    {
        _list.Children.Clear();
        IReadOnlyList<UserTemplate> templates = _store.All();

        if (templates.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "You have not saved any templates yet. When a newsletter's layout is how you "
                    + "want it, choose File, then “Save this as one of my templates”.",
                FontSize = 18,
                MaxWidth = 620,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (UserTemplate template in templates)
        {
            _list.Children.Add(Row(template));
        }
    }

    private Border Row(UserTemplate template)
    {
        var row = new StackPanel { Spacing = 8 };

        if (template.ThumbnailPng is { Length: > 0 } png)
        {
            try
            {
                using var stream = new System.IO.MemoryStream(png);
                var picture = new Bitmap(stream);
                _pictures.Add(picture);
                var image = new Image
                {
                    Source = picture,
                    Width = 150,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                AutomationProperties.SetName(image, $"A picture of the first page of {template.Name}");
                row.Children.Add(image);
            }
            catch (ArgumentException)
            {
                // A thumbnail that will not decode is not worth a word to the user; the template
                // itself is still perfectly usable.
            }
        }

        row.Children.Add(new TextBlock
        {
            Text = template.Name,
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 600,
        });

        if (template.SavedAt > DateTimeOffset.MinValue)
        {
            row.Children.Add(new TextBlock
            {
                Text = "Saved " + template.SavedAt.LocalDateTime.ToString("d MMMM yyyy", CultureInfo.CurrentCulture),
                FontSize = 16,
                Foreground = this.FindResource(Tokens.ChromeMuted) as IBrush,
            });
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        buttons.Children.Add(Small("Rename…", () => RenameAsync(template)));
        buttons.Children.Add(Small("Hand it on…", () =>
        {
            ExportRequestedFor = template.Id;
            Close();
            return System.Threading.Tasks.Task.CompletedTask;
        }));

        Button remove = Small("Remove…", () => RemoveAsync(template));
        remove.Destructive();
        buttons.Children.Add(remove);
        row.Children.Add(buttons);

        return new Border
        {
            Padding = new Avalonia.Thickness(0, 0, 0, 12),
            Child = row,
        };
    }

    private async System.Threading.Tasks.Task RenameAsync(UserTemplate template)
    {
        string? name = RenameAnswerForTest ?? await AskForNameAsync(template.Name);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _status.Text = _store.Rename(template.Id, name, DateTimeOffset.Now)
            ? $"That template is now called “{name.Trim()}”."
            : "TrestleBoard could not rename that template.";
        Refresh();
    }

    private async System.Threading.Tasks.Task RemoveAsync(UserTemplate template)
    {
        bool confirmed = RemoveAnswerForTest ?? await ConfirmRemoveAsync(template);
        if (!confirmed)
        {
            return;
        }

        _status.Text = _store.Remove(template.Id)
            ? $"“{template.Name}” is off your list of templates."
            : "TrestleBoard could not remove that template.";
        Refresh();
    }

    private async System.Threading.Tasks.Task<bool> ConfirmRemoveAsync(UserTemplate template)
    {
        bool confirmed = false;
        var dialog = new Window
        {
            Title = "Remove this template?",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var yes = new Button { Content = "Yes, remove it", FontSize = 20, MinHeight = 44, MinWidth = 200 };
        yes.Destructive();
        var no = new Button
        {
            Content = "No, keep it",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
            IsCancel = true,
        };
        no.Action();
        AutomationProperties.SetName(yes, "Yes, remove it");
        AutomationProperties.SetName(no, "No, keep it");
        yes.Click += (_, _) => { confirmed = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Remove “{template.Name}” from your templates? Newsletters you "
                        + "already made from it will not change. This cannot be undone, so if you "
                        + "may want it again, hand it on to a file first.",
                    FontSize = 20,
                    MaxWidth = 520,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes },
                },
            },
        };

        AutomationProperties.SetName(dialog, "Remove this template?");
        await dialog.ShowDialog(this);
        return confirmed;
    }

    private async System.Threading.Tasks.Task<string?> AskForNameAsync(string current)
    {
        string? answer = null;
        var dialog = new Window
        {
            Title = "Rename this template",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var box = new TextBox
        {
            Text = current,
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(box, "What would you like to call it?");

        var keep = new Button
        {
            Content = "Rename it",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 160,
            IsDefault = true,
        };
        keep.Action();
        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 160,
            IsCancel = true,
        };
        cancel.Action();
        AutomationProperties.SetName(keep, "Rename it");
        AutomationProperties.SetName(cancel, "Cancel");
        keep.Click += (_, _) => { answer = box.Text; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "What would you like to call it?",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    MaxWidth = 480,
                    TextWrapping = TextWrapping.Wrap,
                },
                box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Children = { keep, cancel },
                },
            },
        };

        AutomationProperties.SetName(dialog, "Rename this template");
        await dialog.ShowDialog(this);
        return answer;
    }

    private static Button Small(string content, Func<System.Threading.Tasks.Task> run)
    {
        var button = new Button
        {
            Content = content,
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 150,
        };
        AutomationProperties.SetName(button, content);
        button.Click += async (_, _) => await run();
        return button.Action();
    }
}
