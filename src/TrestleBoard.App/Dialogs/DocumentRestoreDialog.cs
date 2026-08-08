using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.App.Theme;
using TrestleBoard.Editing;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// M39: offers back the copies of the newsletter that the rotating <c>.bak</c> ring kept beside the
/// user's own file (PLAN.md §4).
///
/// <para>Deliberately modelled on <see cref="RosterRestoreDialog"/>, because the two questions are
/// the same question and answering them the same way is worth more than either dialog being
/// individually clever — but the promise underneath is different, and the wording has to say so. The
/// address book's restore is undoable. This one replaces what is on screen, and its safety net is
/// that <b>nothing is written to the file</b>: the older version is opened, and the user's own file
/// is untouched until they choose to save. Closing without saving is how they change their mind.</para>
/// </summary>
public sealed class DocumentRestoreDialog : Window
{
    private readonly IReadOnlyList<DocumentBackup> _backups;
    private readonly ListBox _list;

    public DocumentRestoreDialog(IReadOnlyList<DocumentBackup> backups, string fileName)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(fileName);
        _backups = backups;

        Title = "Go back to an earlier version";
        Width = 660;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Go back to an earlier version of this newsletter");

        _list = new ListBox
        {
            FontSize = 18,
            MaxHeight = 320,
            ItemsSource = backups.Select(Describe).ToList(),
            SelectedIndex = backups.Count > 0 ? 0 : -1,
        };
        AutomationProperties.SetName(_list, "Earlier versions of this newsletter");

        var open = new Button
        {
            Content = "Open this version",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 220,
            IsDefault = true,
        };
        AutomationProperties.SetName(open, "Open this version");
        open.Click += (_, _) =>
        {
            Chosen = _list.SelectedIndex >= 0 && _list.SelectedIndex < _backups.Count
                ? _backups[_list.SelectedIndex]
                : null;
            Close();
        };

        var cancel = new Button
        {
            Content = "Cancel",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 140,
            IsCancel = true,
        };
        cancel.Action();
        AutomationProperties.SetName(cancel, "Cancel");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(28),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Every time you save {fileName}, TrestleBoard keeps a copy of the "
                        + "version it replaced.",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                },
                _list,
                new TextBlock
                {
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,

                    // The one sentence that has to be right. The user is about to lose what is on
                    // screen, and the reason that is safe is that their FILE is not being touched.
                    Text = "The older version opens on screen. Your file is not changed until you "
                        + $"save — so if it is the wrong one, close {fileName} without saving and "
                        + "nothing is lost.",
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, open },
                },
            },
        };
    }

    /// <summary>The version the user chose, or null if they cancelled.</summary>
    public DocumentBackup? Chosen { get; private set; }

    /// <summary>
    /// "The one before that — 27 July 2026 at 09:14". The generation is named in words rather than
    /// as ".bak3", which is a file extension the audience has no reason to have met.
    /// </summary>
    internal static string Describe(DocumentBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        string when = backup.SavedAt.ToLocalTime()
            .ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.CurrentCulture);
        string which = backup.Generation switch
        {
            1 => "The last one you saved over",
            2 => "The one before that",
            _ => string.Create(CultureInfo.CurrentCulture, $"{backup.Generation} saves ago"),
        };

        return $"{which} — {when}";
    }
}
