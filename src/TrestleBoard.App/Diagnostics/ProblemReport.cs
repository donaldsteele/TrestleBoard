using System.Globalization;
using System.Text;

namespace TrestleBoard.App.Diagnostics;

/// <summary>
/// Everything a problem report is allowed to say, gathered by the shell and handed here as plain
/// values (PLAN.md §11 M77).
///
/// <para>A record rather than a pile of arguments so that the privacy rule has something to point
/// at: <b>this is the whole of what leaves the machine.</b> There is no field for the newsletter,
/// no field for the address book, and no field for a file name — not because the composer would
/// refuse them, but because there is nowhere to put them.</para>
/// </summary>
/// <param name="AppVersion">Which TrestleBoard this is.</param>
/// <param name="OperatingSystem">Which computer it is running on.</param>
/// <param name="UiScalePercent">How big the user has made the menus and buttons.</param>
/// <param name="Theme">Which look they are using.</param>
/// <param name="WhatWentWrong">The exception, or null when nobody crashed and the user just asked.</param>
/// <param name="RecentActions">The last few commands, oldest first.</param>
internal sealed record ProblemReportFacts(
    string AppVersion,
    string OperatingSystem,
    int UiScalePercent,
    string Theme,
    Exception? WhatWentWrong,
    IReadOnlyList<TrailEntry> RecentActions);

/// <summary>
/// The report the user sends to whoever looks after TrestleBoard for the lodge (PLAN.md §11 M77).
///
/// <para><b>The point of this milestone is the file, and the point of the file is that it can be
/// read by a person who was not there.</b> This audience cannot describe a defect — "it went away"
/// is what the maintainer gets — and until M77 there was no unhandled-exception handler anywhere in
/// the app, no log, and therefore nothing to look at afterwards.</para>
///
/// <para><b>What it may never contain</b> (PLAN.md §0): a member's name, a birthday, a telephone
/// number or an email; a word of the newsletter; the name of any file; or any path under the
/// user's home directory. The last one is the subtle one and the reason
/// <see cref="Scrub"/> exists: a .NET stack trace carries the source paths of the machine the app
/// was built on, and an exception message from a failed open carries the path it failed to open —
/// a path under Documents names a person, a computer and a document all at once. Every string that
/// came from outside this file goes through the scrubber before it is written.</para>
///
/// <para>Pure and static: a report can be composed and asserted on in a test with no Avalonia
/// session, which is what lets the privacy rule be checked by machine rather than by eye.</para>
/// </summary>
internal static class ProblemReport
{
    /// <summary>How many commands the report prints. PLAN.md §11 M77 says the last twenty.</summary>
    internal const int ActionsShown = 20;

    /// <summary>What a scrubbed path is replaced by. Recognisable, and not a name.</summary>
    internal const string Redacted = "(a folder on this computer)";

    /// <summary>
    /// The suggested file name. No date on it: two reports in a day is two files, and the save
    /// dialog is where a person decides what to call the second one.
    /// </summary>
    internal const string SuggestedFileName = "TrestleBoard problem report.txt";

    /// <summary>
    /// The report, as the person who receives it will read it. Plain text with headings a human can
    /// skim, because the person who receives it is a volunteer with the app installed, not a
    /// support desk with a parser.
    /// </summary>
    internal static string Compose(ProblemReportFacts facts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var text = new StringBuilder();
        text.AppendLine("TrestleBoard problem report");
        text.AppendLine("===========================");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Written:      {now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"TrestleBoard: {Scrub(facts.AppVersion)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Computer:     {Scrub(facts.OperatingSystem)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Menu size:    {facts.UiScalePercent}%");
        text.AppendLine(CultureInfo.InvariantCulture, $"Look:         {Scrub(facts.Theme)}");
        text.AppendLine();

        text.AppendLine("What went wrong");
        text.AppendLine("---------------");
        text.AppendLine(facts.WhatWentWrong is { } error
            ? Scrub(error.ToString())
            : "Nothing crashed. The person using TrestleBoard asked for this report.");

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"The last {ActionsShown} things asked for");
        text.AppendLine("--------------------------------");
        IReadOnlyList<TrailEntry> recent = facts.RecentActions;
        if (recent.Count == 0)
        {
            text.AppendLine("Nothing yet this time.");
        }
        else
        {
            foreach (TrailEntry entry in recent)
            {
                text.AppendLine(entry.Describe());
            }
        }

        text.AppendLine();
        text.AppendLine("This report holds no names, no telephone numbers, no email addresses and");
        text.AppendLine("nothing from the newsletter itself. It is safe to send on.");
        return text.ToString();
    }

    /// <summary>
    /// Takes the user's own folders out of a string (PLAN.md §0, §11 M77).
    ///
    /// <para><b>Whole-prefix replacement rather than a pattern.</b> A regular expression for "a path"
    /// would have to decide what a path looks like on three operating systems and would still miss
    /// the case that matters — a home directory named after the person. Instead the folders this app
    /// can name are asked for by name and struck out wherever they appear, longest first so that a
    /// nested one does not leave the tail of its parent behind.</para>
    ///
    /// <para>Comparison is case-insensitive because Windows paths are, and a stack trace built on
    /// one machine and read on another may differ in case alone.</para>
    /// </summary>
    internal static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string scrubbed = text;
        foreach (string folder in SensitiveFolders())
        {
            scrubbed = scrubbed.Replace(folder, Redacted, StringComparison.OrdinalIgnoreCase);
        }

        return scrubbed;
    }

    /// <summary>
    /// The folders whose names identify the person, longest first.
    ///
    /// <para>The user profile is the load-bearing one — everything else on the list is inside it on
    /// a normal installation, and the ordering means the profile is struck out first and the rest
    /// never match. They are listed anyway for the installations where they are not: a redirected
    /// Documents folder on a network share is common in exactly the sort of place that lends a
    /// laptop to a lodge secretary.</para>
    /// </summary>
    private static IEnumerable<string> SensitiveFolders() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        }
        .Where(folder => !string.IsNullOrWhiteSpace(folder))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(folder => folder.Length);
}
