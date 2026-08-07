using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TrestleBoard.Roster;
using TrestleBoard.App.Theme;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "Restore an earlier version…" (PLAN.md §11 M12).
///
/// The address book gets one level of undo and then this. Snapshot restore is the right undo idiom
/// for this audience anyway — it is the one they already understand from photographs and documents —
/// and each kept copy is listed by <b>when it was taken and how many people were in it</b>, because
/// those two facts together are how somebody recognises the version they want.
/// </summary>
public sealed class RosterRestoreDialog : Window
{
    private readonly IReadOnlyList<RosterBackup> _backups;
    private readonly ListBox _list;

    public RosterRestoreDialog(IReadOnlyList<RosterBackup> backups)
    {
        ArgumentNullException.ThrowIfNull(backups);
        _backups = backups;

        Title = "Restore an earlier version";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Restore an earlier version of your address book");

        _list = new ListBox
        {
            FontSize = 18,
            MaxHeight = 320,
            ItemsSource = backups.Select(Describe).ToList(),
            SelectedIndex = backups.Count > 0 ? 0 : -1,
        };
        AutomationProperties.SetName(_list, "Earlier versions of your address book");

        var restore = new Button
        {
            Content = "Use this version",
            FontSize = 20,
            MinHeight = 44,
            MinWidth = 200,
            IsDefault = true,
        };
        AutomationProperties.SetName(restore, "Use this version");
        restore.Click += (_, _) =>
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
                    Text = "TrestleBoard keeps a copy of your address book every time it changes.",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    TextWrapping = TextWrapping.Wrap,
                },
                _list,
                new TextBlock
                {
                    FontSize = 18,
                    TextWrapping = TextWrapping.Wrap,
                    Text = "Nothing changes until you press Use this version — and if you pick the "
                        + "wrong one, Undo the last change puts it back.",
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, restore },
                },
            },
        };
    }

    /// <summary>The version the user chose, or null if they cancelled.</summary>
    public RosterBackup? Chosen { get; private set; }

    /// <summary>"27 July 2026 at 09:14 — 118 people". The two facts that identify a version.</summary>
    internal static string Describe(RosterBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);

        string when = backup.SavedAt.ToLocalTime()
            .ToString("d MMMM yyyy 'at' HH:mm", CultureInfo.CurrentCulture);
        string people = backup.MemberCount == 1 ? "1 person" : $"{backup.MemberCount} people";
        return $"{when} — {people}";
    }
}
