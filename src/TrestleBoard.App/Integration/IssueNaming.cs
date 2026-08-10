using TrestleBoard.Core.Model;

namespace TrestleBoard.App.Integration;

/// <summary>
/// What this issue is called, wherever its name is offered to somebody: the suggested PDF name, the
/// draft copy, the Save-as name and the PDF's own Subject (PLAN.md §11 M75 (g)).
///
/// <para><b>Why this exists.</b> Three places built the same string by hand as
/// <c>$"{meta.Title} {meta.IssueYear}-{meta.IssueMonth:00}"</c>. With the issue date wrong that read
/// <c>" 2000-01.pdf"</c> — and the <b>leading space</b> in it was not the issue date at all. A
/// shipped template leaves <see cref="DocumentMetadata.Title"/> empty, so the space between an
/// absent title and the date had nothing in front of it. Fixing (a)–(d) fixes the date and leaves
/// the space.</para>
///
/// <para><b>The decision, M75 (g).</b> The title is <i>not</i> asked for. The issue date is asked
/// for because the app cannot know it and gets it catastrophically wrong when it guesses — a
/// birthday list drawn from the wrong month. A title it can get right: every one of these files is a
/// trestle board, <c>MailHandoff.Subject</c> has defaulted a blank title to "Trestle Board" since
/// M65 without anybody minding, and the newsletter names itself on its own cover in any case. A
/// second question on the way into a newsletter is a second question to get wrong, and M75's whole
/// argument is that the ask must be worth its cost. So: one fallback, used everywhere, and the user
/// who wants "Trestle Board Extra" can still type it and have it kept.</para>
/// </summary>
internal static class IssueNaming
{
    /// <summary>What a newsletter with no title of its own is called.</summary>
    internal const string DefaultTitle = "Trestle Board";

    /// <summary>The title to print, which is never blank.</summary>
    internal static string Title(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return string.IsNullOrWhiteSpace(metadata.Title) ? DefaultTitle : metadata.Title.Trim();
    }

    /// <summary>
    /// The file name without its extension — "Trestle Board 2026-07". Year-month, in that order,
    /// so a folder of them sorts into the order they were published.
    /// </summary>
    internal static string FileStem(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return $"{Title(metadata)} {metadata.IssueYear}-{metadata.IssueMonth:00}";
    }

    /// <summary>
    /// What the PDF says about itself in its own properties, which travels with every copy the
    /// lodge receives.
    /// </summary>
    internal static string PdfSubject(DocumentMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return $"Trestle board {metadata.IssueYear}-{metadata.IssueMonth:00}";
    }
}
