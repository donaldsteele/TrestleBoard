namespace TrestleBoard.Editing.Review;

/// <summary>What kind of thing the checklist noticed (PLAN.md §11 M51).</summary>
public enum ReviewFindingKind
{
    /// <summary>A story still holds the sentence the template put there.</summary>
    UnwrittenWords,

    /// <summary>More writing than fits — M43's overset, asked about rather than badged.</summary>
    WritingThatRanOut,

    /// <summary>A picture frame with no picture in it.</summary>
    EmptyPictureFrame,

    /// <summary>A picture with nothing printed underneath it.</summary>
    PictureWithoutCaption,

    /// <summary>A picture a blind reader would meet in silence.</summary>
    PictureWithoutDescription,

    /// <summary>A month named in the writing that is not this issue's month. A guess, always asked.</summary>
    DateFromAnotherMonth,

    /// <summary>The last station: look at each page with your own eyes.</summary>
    LookAtThePage,
}

/// <summary>
/// One screen of the review (PLAN.md §11 M51). A finding is a question, never an accusation: the
/// checklist does not know what the committee meant, and several of these checks are heuristics.
///
/// <para>
/// This record is data, not behaviour, and it is produced by <see cref="ReviewChecklist"/> without
/// touching the document. Nothing here can change a newsletter; the remedy is an
/// <c>ActionId</c> for the shell to run if the user asks for it.
/// </para>
/// </summary>
/// <param name="Kind">Which check produced it.</param>
/// <param name="PageNumber">The page as the user counts them, from 1.</param>
/// <param name="BlockId">What to select when the user says "Take me there", if anything.</param>
/// <param name="Heading">The short line at the top of the screen.</param>
/// <param name="Question">What the screen asks, in plain words.</param>
/// <param name="RemedyActionId">The command that would deal with it, if there is one.</param>
public sealed record ReviewFinding(
    ReviewFindingKind Kind,
    int PageNumber,
    string? BlockId,
    string Heading,
    string Question,
    string? RemedyActionId = null);
