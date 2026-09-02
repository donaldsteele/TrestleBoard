namespace TrestleBoard.Screenshots;

/// <summary>
/// "If a milestone slips, M15 degrades rather than blocks" (PLAN.md §11, sizing notes), mechanised.
///
/// Each shot names the milestone it needs. The gate asks the runtime whether that milestone's
/// defining type is actually present, so a build without it prints a reason and omits the shot —
/// and the README section that would have carried it — rather than leaving a broken image on the
/// project's front page. It resolves by NAME on purpose: a direct type reference would turn a
/// missing milestone into a compile error, which is exactly the blocking behaviour this exists to
/// avoid.
/// </summary>
internal static class MilestoneGate
{
    private static readonly Dictionary<string, (string TypeName, string What)> Requirements =
        new(StringComparer.Ordinal)
        {
            ["M11"] = ("TrestleBoard.Editing.Actions.ActionCatalog, TrestleBoard.Editing",
                "the action panel"),
            ["M12"] = ("TrestleBoard.Roster.RosterService, TrestleBoard.Roster",
                "the lodge address book"),
            ["M14"] = ("TrestleBoard.Layout.Fonts.BundledFontCatalog, TrestleBoard.Layout",
                "the font catalog"),
            ["M18"] = ("TrestleBoard.Core.Commands.ReplaceImageCommand, TrestleBoard.Core",
                "putting a picture into a frame that is already on the page"),
            ["M19"] = ("TrestleBoard.Widgets.Roster.OfficersRosterProjection, TrestleBoard.Widgets",
                "filling the officers table in from the address book"),
            ["M63"] = ("TrestleBoard.App.Dialogs.HelpWindow, TrestleBoard.App",
                "the how-do-I window and the first-run tour"),
            ["M64"] = ("TrestleBoard.App.Dialogs.BringInPackWindow, TrestleBoard.App",
                "packing everything up for a successor"),
            ["M65"] = ("TrestleBoard.App.Dialogs.EmblemPickerWindow, TrestleBoard.App",
                "the emblem shelf"),
            ["M67"] = ("TrestleBoard.App.Dialogs.PdfPageWindow, TrestleBoard.App",
                "bringing in a page from a PDF"),
            ["M77"] = ("TrestleBoard.App.Dialogs.ProblemCard, TrestleBoard.App",
                "the card shown when something goes wrong"),
            ["M78"] = ("TrestleBoard.Rendering.PageFooterRenderer, TrestleBoard.Rendering",
                "the line along the bottom of every page"),
            ["M79"] = ("TrestleBoard.Core.Model.PageLooks, TrestleBoard.Core",
                "borders, shading and a line across the page"),
            ["M80"] = ("TrestleBoard.App.Dialogs.RecentPicturesDialog, TrestleBoard.App",
                "the pictures used before"),
            ["M83"] = ("TrestleBoard.Core.Commands.SetColumnCountCommand, TrestleBoard.Core",
                "two columns"),
            ["M84"] = ("TrestleBoard.Widgets.Builtins.MonthCalendar.MonthCalendarDefinition, TrestleBoard.Widgets",
                "the month calendar"),
            ["M85"] = ("TrestleBoard.App.Integration.ArchiveSearch, TrestleBoard.App",
                "looking through earlier newsletters"),
        };

    /// <summary>
    /// True if the shot can be taken. <paramref name="reason"/> is the plain sentence printed when
    /// it cannot.
    /// </summary>
    public static bool IsPresent(string? milestone, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(milestone))
        {
            return true;
        }

        if (!Requirements.TryGetValue(milestone, out (string TypeName, string What) requirement))
        {
            return true;
        }

        if (Type.GetType(requirement.TypeName, throwOnError: false) is not null)
        {
            return true;
        }

        reason = $"{milestone} is not in this build, so {requirement.What} does not exist yet.";
        return false;
    }
}
