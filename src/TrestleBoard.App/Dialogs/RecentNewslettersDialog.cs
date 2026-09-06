using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>What the user chose in <see cref="RecentNewslettersDialog"/> (M106).</summary>
internal enum RecentNewsletterChoice
{
    /// <summary>The window was closed without choosing.</summary>
    Cancelled,

    /// <summary>Open the one at <see cref="RecentNewslettersDialog.ChosenPath"/>.</summary>
    OpenThisOne,

    /// <summary>Go and look for one, the ordinary way.</summary>
    BrowseForOne,
}

/// <summary>
/// "Newsletters you had open…" (PLAN.md §11 M106).
///
/// <para><b>Shown by name, never by path.</b> The full path is the user's own filing (§0), it is
/// usually longer than the window, and it is not what tells one issue from another —
/// "Trestle Board 2026-07" does. The folder is shown underneath only as its own name, which is what
/// separates two Septembers kept in different places.</para>
///
/// <para><b>An empty list is answered here rather than by a refusal.</b> On a first run there is
/// nothing to offer, and a greyed-out menu item whose reason is "you have not opened anything yet"
/// tells somebody off for being new. The window says so and offers the file dialog.</para>
/// </summary>
internal sealed class RecentNewslettersDialog : Window
{
    private readonly ListBox _list;
    private readonly IReadOnlyList<string> _paths;

    internal RecentNewslettersDialog(IReadOnlyList<string> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        _paths = recent;
        Title = "Newsletters you had open";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _list = new ListBox { MinWidth = 460, MaxHeight = 340, IsVisible = recent.Count > 0 };
        foreach (string path in recent)
        {
            var row = new StackPanel
            {
                MinHeight = 44,
                Children =
                {
                    new TextBlock { Text = Path.GetFileNameWithoutExtension(path), FontSize = 18 },
                    new TextBlock
                    {
                        Text = FolderLine(path),
                        FontSize = 14,
                        TextWrapping = TextWrapping.NoWrap,
                    }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                },
            };

            Avalonia.Automation.AutomationProperties.SetName(
                row, $"{Path.GetFileNameWithoutExtension(path)}, {FolderLine(path)}");
            _list.Items.Add(row);
        }

        if (recent.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.DoubleTapped += (_, _) => Accept();
        }

        var open = new Button
        {
            Content = "Open this one",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 180,
            IsDefault = true,
            IsVisible = recent.Count > 0,
        };
        open.Primary();
        Avalonia.Automation.AutomationProperties.SetName(open, "Open this one");
        open.Click += (_, _) => Accept();

        var browse = new Button
        {
            Content = "Go and find one…",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 180,
        };
        browse.Action();
        Avalonia.Automation.AutomationProperties.SetName(browse, "Go and find a newsletter");
        browse.Click += (_, _) =>
        {
            Choice = RecentNewsletterChoice.BrowseForOne;
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
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel, open nothing");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = recent.Count > 0
                        ? "Newsletters you had open"
                        : "You have not had a newsletter open on this computer yet.",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                _list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, browse, open },
                },
            },
        };
    }

    internal RecentNewsletterChoice Choice { get; private set; } = RecentNewsletterChoice.Cancelled;

    /// <summary>The newsletter to open, when <see cref="Choice"/> says to open one.</summary>
    internal string? ChosenPath { get; private set; }

    /// <summary>Stands in for a click, so the shell path can be tested without a screen.</summary>
    internal void ChooseForTest(RecentNewsletterChoice choice, string? path = null)
    {
        Choice = choice;
        ChosenPath = path;
    }

    /// <summary>
    /// The folder's own name, not the whole path — enough to tell two Septembers apart, and not the
    /// user's filing spelled out in a window (§0).
    /// </summary>
    internal static string FolderLine(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            return "In this computer";
        }

        string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? "In this computer" : $"In {name}";
    }

    private void Accept()
    {
        int index = _list.SelectedIndex;
        if (index < 0 || index >= _paths.Count)
        {
            return;
        }

        ChosenPath = _paths[index];
        Choice = RecentNewsletterChoice.OpenThisOne;
        Close();
    }
}
