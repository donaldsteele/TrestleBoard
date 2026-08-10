using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Templates;
using TrestleBoard.Core.Text;
using TrestleBoard.Editing.Actions;

namespace TrestleBoard.Editing.Review;

/// <summary>
/// "Look it over with me" (PLAN.md §11 M51): the newsletter walked top to bottom, turned into a
/// short list of questions.
///
/// <para>
/// Proofreading six pages is the hardest task in the committee's month, and it is hardest for
/// exactly the eyes doing it. Answering "is this right?" eight times is a different job from
/// finding eight things, and it is a job this audience can finish.
/// </para>
///
/// <para>
/// <b>This class reads. It never writes.</b> It takes a document and two sets of block ids that
/// only the layout knows about, and returns a list of records. Nothing here holds a session, runs a
/// command, or can mark a newsletter as edited — which is why "Make the PDF" can offer the review
/// without risking the thing it is about to export.
/// </para>
///
/// <para>
/// Several of these checks are guesses, and the wording says so. The date check in particular can
/// only ever ask: a September newsletter that mentions August may be reporting on the August picnic
/// or may be last month's file with the date changed, and no amount of cleverness here can tell
/// which. PLAN.md's acceptance is explicit — <i>the stale-date check is a heuristic and must be
/// phrased as a question, never an assertion</i>.
/// </para>
/// </summary>
public static class ReviewChecklist
{
    /// <summary>
    /// Builds the questions, in the order the reader meets them on the page.
    /// </summary>
    /// <param name="document">Read, never touched.</param>
    /// <param name="oversetTailBlockIds">
    /// From <c>DocumentRenderSource.GetOversetTailBlockIds()</c> — the last frame of every chain
    /// whose writing ran out of room. Passed in rather than computed because laying the document
    /// out is the render source's business, and a checklist that could trigger a relayout would be
    /// doing more than reading.
    /// </param>
    /// <param name="emptyPictureBlockIds">
    /// From <c>DocumentRenderSource.GetPlaceholderPictureRects</c>, gathered over every page.
    /// "Empty" there means the bytes do not decode, which is the same thing the user sees.
    /// </param>
    /// <param name="extraStations">
    /// Screens produced by something this project cannot see (M52's spelling, M58's read-aloud).
    /// They arrive already written and go in after the defects and before the page-by-page look,
    /// which is where a whole-newsletter question belongs. PLAN.md scheduled M51 first precisely so
    /// that the later milestones could add stations to this frame instead of building their own.
    /// </param>
    public static IReadOnlyList<ReviewFinding> Build(
        Document document,
        IEnumerable<string>? oversetTailBlockIds = null,
        IEnumerable<string>? emptyPictureBlockIds = null,
        IEnumerable<ReviewFinding>? extraStations = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var overset = new HashSet<string>(oversetTailBlockIds ?? [], StringComparer.Ordinal);
        var emptyPictures = new HashSet<string>(emptyPictureBlockIds ?? [], StringComparer.Ordinal);
        var findings = new List<ReviewFinding>();

        // Stories are reported once even when their chain crosses three frames: the user has one
        // piece of writing to deal with, not three.
        var storiesReported = new HashSet<string>(StringComparer.Ordinal);
        string? previousMonth = PreviousMonthName(document.Metadata.IssueMonth);

        for (int i = 0; i < document.Pages.Count; i++)
        {
            int pageNumber = i + 1;
            foreach (Block block in document.Pages[i].Blocks)
            {
                switch (block)
                {
                    case TextBlock text:
                        AddTextFindings(findings, document, text, pageNumber, overset, storiesReported, previousMonth);
                        break;
                    // M74 (f): reached through the interface, not through ImageFrame. This was
                    // `case ImageFrame`, written when a photograph was the only kind of picture
                    // there was — so from M72 a drawing whose description had been blanked was
                    // never once flagged, on the one screen in the app whose whole job is to find
                    // exactly that. Both frame types carry a caption and a description because both
                    // implement ICaptionedBlock, and that is what the review asks about.
                    case ICaptionedBlock picture:
                        AddPictureFindings(findings, block, picture, pageNumber, emptyPictures);
                        break;
                }
            }
        }

        findings.AddRange(extraStations ?? []);

        // The last station, and the only one that is not about a defect: no checklist replaces
        // looking at the thing. One screen per page, in order.
        for (int i = 0; i < document.Pages.Count; i++)
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.LookAtThePage,
                i + 1,
                BlockId: null,
                $"Page {i + 1} — have a look",
                "Take a moment to look at this page as it will print. Anything out of place?"));
        }

        return findings;
    }

    private static void AddTextFindings(
        List<ReviewFinding> findings,
        Document document,
        TextBlock frame,
        int pageNumber,
        HashSet<string> overset,
        HashSet<string> storiesReported,
        string? previousMonth)
    {
        if (overset.Contains(frame.Id))
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.WritingThatRanOut,
                pageNumber,
                frame.Id,
                $"Page {pageNumber} has more writing than fits",
                "Some of this writing is past the bottom of its box, so it will not print. Would "
                + "you like TrestleBoard to flow the rest into the space that is free, or would you "
                + "rather make the box bigger yourself?",
                ActionId.AutoFlow));
        }

        if (!storiesReported.Add(frame.StoryRef)
            || !document.TryGetStory(frame.StoryRef, out Story? story)
            || story is null)
        {
            return;
        }

        string prose = TextOf(story);

        if (PlaceholderPrompts.FirstIn(prose) is { } prompt)
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.UnwrittenWords,
                pageNumber,
                frame.Id,
                $"Page {pageNumber} still says \"{prompt}\"",
                $"This is the sentence TrestleBoard put there to hold the space. If it stays, it "
                + "prints exactly as it reads now. Would you like to write something here?"));
        }

        if (previousMonth is not null && MentionsMonth(prose, previousMonth))
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.DateFromAnotherMonth,
                pageNumber,
                frame.Id,
                $"Page {pageNumber} mentions {previousMonth}",
                $"The writing on this page names {previousMonth}, and this issue is for "
                + $"{MonthName(document.Metadata.IssueMonth)}. That is perfectly fine if you are "
                + "writing about something that has already happened. Is it what you meant?"));
        }
    }

    /// <param name="block">
    /// The frame itself, for its id. <see cref="ICaptionedBlock"/> carries the caption and the
    /// description but not the identity — a photograph and a drawing are the same thing to this
    /// method and different things to the rest of the document.
    /// </param>
    private static void AddPictureFindings(
        List<ReviewFinding> findings,
        Block block,
        ICaptionedBlock picture,
        int pageNumber,
        HashSet<string> emptyPictures)
    {
        if (emptyPictures.Contains(block.Id))
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.EmptyPictureFrame,
                pageNumber,
                block.Id,
                $"Page {pageNumber} has a picture box with no picture in it",
                "This box will print as an empty space. Would you like to choose a picture for it?",
                ActionId.ReplacePicture));

            // The two questions below are about a picture. There is no picture, so they are not
            // questions yet — asking them here would be three screens about one empty box.
            return;
        }

        if (string.IsNullOrWhiteSpace(picture.Caption))
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.PictureWithoutCaption,
                pageNumber,
                block.Id,
                $"The picture on page {pageNumber} has nothing printed under it",
                "A caption is the line of words printed under a picture, telling the reader who or "
                + "what it shows. This picture has none. Would you like to write one?",
                ActionId.CaptionPicture));
        }

        if (NeedsDescription(picture.AltText))
        {
            findings.Add(new ReviewFinding(
                ReviewFindingKind.PictureWithoutDescription,
                pageNumber,
                block.Id,
                $"The picture on page {pageNumber} has no description",
                "A description is not printed. It is what a reader who cannot see the picture is "
                + "told instead — a brother reading the newsletter with a screen reader, for "
                + "instance. Would you like to describe this picture?",
                ActionId.DescribePicture));
        }
    }

    /// <summary>
    /// A description the six-page template shipped is not a description. That prompt sits in
    /// <c>AltText</c> from the moment the template is opened, so the obvious null-or-whitespace
    /// test would call every untouched photo frame described.
    /// </summary>
    private static bool NeedsDescription(string? altText) =>
        string.IsNullOrWhiteSpace(altText) || PlaceholderPrompts.IsUntouched(altText);

    private static string TextOf(Story story) =>
        string.Join("\n", story.Paragraphs.Select(StoryNavigator.GetParagraphText));

    /// <summary>
    /// Whole-word month match. Without the word boundaries "March" would be found inside "marching"
    /// and "May" inside "Maybe", and a checklist that cries wolf is one the committee stops running.
    /// </summary>
    private static bool MentionsMonth(string prose, string month)
    {
        int at = 0;
        while ((at = prose.IndexOf(month, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startsWord = at == 0 || !char.IsLetter(prose[at - 1]);
            int after = at + month.Length;
            bool endsWord = after >= prose.Length || !char.IsLetter(prose[after]);
            if (startsWord && endsWord)
            {
                return true;
            }

            at = after;
        }

        return false;
    }

    /// <summary>
    /// The month before this issue's — the one "last month's file with the date changed" leaves
    /// behind. Months after the issue's are deliberately NOT flagged: a September newsletter
    /// announcing the October picnic is doing its job.
    /// </summary>
    private static string? PreviousMonthName(int issueMonth) =>
        issueMonth is < 1 or > 12 ? null : MonthName(issueMonth == 1 ? 12 : issueMonth - 1);

    private static string MonthName(int month) =>
        month is < 1 or > 12
            ? "this month"
            : CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
}
