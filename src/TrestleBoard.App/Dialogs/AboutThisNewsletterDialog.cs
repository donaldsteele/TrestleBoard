using System;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using TrestleBoard.App.Theme;
using DocumentMetadata = TrestleBoard.Core.Model.DocumentMetadata;

namespace TrestleBoard.App.Dialogs;

/// <summary>
/// "What this newsletter is called…" (PLAN.md §11 M105).
///
/// <para><b>Three plain questions, not a properties sheet.</b> Every field here is something a
/// secretary can answer without being told what it is for, which is why the window is not called
/// Document Properties: that names a filing cabinet drawer, and this asks about the lodge.</para>
///
/// <para><b>Each field says where it shows up.</b> A form whose fields do nothing visible is a form
/// people leave blank, and all three of these have been silently blank on real newsletters — the
/// lodge name printed in every footer, the title naming the exported file, the meeting rule that
/// works out next month's date.</para>
///
/// <para><b>The issue date is shown and not asked.</b> It is answered on the cover heading and
/// mirrored on the banner, so a second field here would be a second source of truth for one
/// fact.</para>
/// </summary>
public sealed class AboutThisNewsletterDialog : Window
{
    private readonly TextBox _lodge;
    private readonly TextBox _title;
    private readonly TextBox _meeting;

    public AboutThisNewsletterDialog(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        Title = "What this newsletter is called";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _lodge = Field(metadata.LodgeName, "The lodge's name");
        _title = Field(metadata.Title, "What this newsletter is called");
        _meeting = Field(metadata.MeetingRule, "When the lodge meets");

        var save = new Button
        {
            Content = "Save these",
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 160,
            IsDefault = true,
        };
        save.Primary();
        Avalonia.Automation.AutomationProperties.SetName(save, "Save these");
        save.Click += (_, _) =>
        {
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
        cancel.Action();
        Avalonia.Automation.AutomationProperties.SetName(cancel, "Cancel, change nothing");
        cancel.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 8,
            Children =
            {
                Ask("The lodge's name"),
                Because("This prints in the line along the bottom of every page, goes out as the "
                    + "author of the PDF, and is part of the subject line when you send it."),
                _lodge,
                Ask("What this newsletter is called"),
                Because("TrestleBoard suggests this as the file name when you make the PDF. "
                    + "“Trestle Board” is the usual answer."),
                _title,
                Ask("When the lodge meets"),
                Because("Written the way it is said — “1st Tuesday”, “3rd Monday”. "
                    + "TrestleBoard works out next month's meeting dates from this when you start "
                    + "next month's newsletter from this one."),
                _meeting,
                new TextBlock
                {
                    Text = IssueLine(metadata),
                    FontSize = 15,
                    Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480,
                }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Margin = new Avalonia.Thickness(0, 12, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancel, save },
                },
            },
        };
    }

    /// <summary>False when the window was cancelled — nothing is changed then.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Trimmed, because a name with a stray space is a name that will not match itself.</summary>
    public string LodgeName => (_lodge.Text ?? string.Empty).Trim();

    public string NewsletterTitle => (_title.Text ?? string.Empty).Trim();

    public string MeetingRule => (_meeting.Text ?? string.Empty).Trim();

    /// <summary>
    /// Which issue this is, and where to change it. Shown rather than asked: see the class remark.
    /// </summary>
    internal static string IssueLine(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!metadata.HasIssueDate)
        {
            return "Nobody has said which issue this is yet. Use “Which issue is this?” on "
                + "the File menu, which asks on the cover heading.";
        }

        string month = new DateTime(metadata.IssueYear, metadata.IssueMonth, 1)
            .ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        return $"This is the {month} issue. To change that, use “Which issue is this?” on "
            + "the File menu, which asks on the cover heading.";
    }

    private static TextBox Field(string value, string name)
    {
        var box = new TextBox
        {
            Text = value,
            FontSize = 18,
            MinHeight = 44,
            MinWidth = 480,
        };
        Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    private static TextBlock Ask(string text) => new()
    {
        Text = text,
        FontSize = 18,
        FontWeight = FontWeight.Bold,
        Margin = new Avalonia.Thickness(0, 10, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 480,
    };

    private static TextBlock Because(string text) => new TextBlock
    {
        Text = text,
        FontSize = 15,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 480,
    }.Token(TextBlock.ForegroundProperty, Tokens.ChromeMuted);
}
