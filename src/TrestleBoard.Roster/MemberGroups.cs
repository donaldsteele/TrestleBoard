using System;
using System.Collections.Generic;
using System.Linq;

namespace TrestleBoard.Roster;

/// <summary>
/// The groups the app itself knows the meaning of (PLAN.md §11 M55).
///
/// <para>Groups are free text, so a lodge can invent any list it needs. These three are different
/// only in that the app itself reads them: M56 sends the newsletter to the email group. Naming
/// them here keeps a feature from guessing at a spelling.</para>
///
/// <para>They are offered as tick boxes rather than typed, because a group that is nearly spelled
/// right is a brother who quietly stops receiving his newsletter.</para>
/// </summary>
public static class MemberGroups
{
    /// <summary>M56 fills the BCC line from this group.</summary>
    public const string ByEmail = "Gets the newsletter by email";

    /// <summary>
    /// M56 counts these, so the user is told how many copies to run off.
    ///
    /// <para>It is a count and nothing more. M62 would have addressed their envelopes and was
    /// dropped on 2026-08-08, for two reasons: the roster held no postal address, and this is not a
    /// membership management tool.</para>
    ///
    /// <para><b>The first of those reasons stopped being true in M88</b>, which stores a postal
    /// address for every member because the lodge's own system already holds one and keeping it in
    /// two places is what the app exists to stop. The second reason stands, and so does the
    /// decision: printing labels is still not something this app does. Knowing "eleven printed" is
    /// what the committee needs at the copier.</para>
    /// </summary>
    public const string Printed = "Gets a printed copy";

    /// <summary>Not the officers table — that is driven by each member's office — but a mailing list.</summary>
    public const string Officers = "Officers";

    /// <summary>The ones offered as tick boxes before the user has invented any of their own.</summary>
    public static readonly IReadOnlyList<string> Suggested = [ByEmail, Printed, Officers];

    /// <summary>
    /// Every group name in use, the suggested ones first and the lodge's own after, each appearing
    /// once. This is what the People window offers and what the import wizard matches against.
    /// </summary>
    public static IReadOnlyList<string> InUse(IEnumerable<Member> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        var seen = new HashSet<string>(Suggested, StringComparer.OrdinalIgnoreCase);
        var all = new List<string>(Suggested);
        foreach (string group in members.SelectMany(m => m.Groups))
        {
            string trimmed = (group ?? string.Empty).Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                all.Add(trimmed);
            }
        }

        return all;
    }

    /// <summary>Everybody in that group who should still be hearing from the lodge.</summary>
    public static IReadOnlyList<Member> Members(IEnumerable<Member> members, string group)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);

        return [.. members.Where(m => m.IsInTheNewsletter && m.IsIn(group))];
    }
}
