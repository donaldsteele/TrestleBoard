using System;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;

namespace TrestleBoard.Core.Workflow;

/// <summary>
/// Turning this month's newsletter into a template to start from next time (PLAN.md §11 M57).
///
/// <para>"Start from last month" serves the steady state. A special issue — an installation of
/// officers, a past masters' night, a memorial issue — meant rebuilding a layout or overwriting the
/// working lineage; and a committee that had evolved its layout had no way to bless the result as
/// the new starting point. In a volunteer committee of elderly members, succession matters, and a
/// template is the only thing here that outlives the person who made it.</para>
///
/// <para><b>The reset is shared with <see cref="CarryForward"/>, not copied.</b> PLAN.md's
/// acceptance says so, and the reason is that the two must never drift: the day carry-forward
/// learns to clear something new and this does not, a template starts carrying last month's words
/// into every future issue built from it.</para>
/// </summary>
public static class NewsletterTemplate
{
    /// <summary>
    /// A copy of the newsletter with everything that belongs to one particular month taken out.
    ///
    /// <para>Kept: the layout, the styles, the widgets, the pictures — everything the committee
    /// spent the evening on. Cleared: the articles (reset to the same prompts carry-forward uses),
    /// the meeting dates in the cover banner, and the issue date.</para>
    ///
    /// <para>The source is never touched. The copy goes through the same Save/Load path a real file
    /// does, so a future model change cannot make it silently incomplete.</para>
    /// </summary>
    public static TboardPackage From(
        TboardPackage newsletter,
        string articlePrompt = CarryForward.DefaultArticlePrompt)
    {
        ArgumentNullException.ThrowIfNull(newsletter);
        ArgumentException.ThrowIfNullOrEmpty(articlePrompt);

        TboardPackage template = CarryForward.DeepCopy(newsletter);

        CarryForward.ResetArticleProse(template.Document, articlePrompt);
        CarryForward.ClearMeetingDates(template.Document);
        ClearIssueDate(template.Document.Metadata);
        template.Manifest.IsTemplate = true;

        return template;
    }

    /// <summary>
    /// True when this package was written as a template rather than as an issue.
    ///
    /// <para>The flag has been in the manifest since M2 and, until M57, nothing ever set it — it
    /// was a promise the format made and the app never kept.</para>
    /// </summary>
    public static bool IsTemplate(TboardPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return package.Manifest.IsTemplate;
    }

    /// <summary>
    /// Puts the issue date back to the model's own defaults, which is what "no issue yet" looks
    /// like in this document model.
    ///
    /// <para><see cref="DocumentMetadata.IssueMonth"/> and <see cref="DocumentMetadata.IssueYear"/>
    /// are plain <c>int</c>s with defaults of 1 and 2000, so there is no null to write. Making them
    /// nullable would be a format change reaching every widget, every projection and every
    /// snapshot for the sake of a state only a template is ever in — and the template is about to
    /// ask the user for a date the moment they start an issue from it.</para>
    /// </summary>
    private static void ClearIssueDate(DocumentMetadata metadata)
    {
        var fresh = new DocumentMetadata();
        metadata.IssueMonth = fresh.IssueMonth;
        metadata.IssueYear = fresh.IssueYear;
    }
}
