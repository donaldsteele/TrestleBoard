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
