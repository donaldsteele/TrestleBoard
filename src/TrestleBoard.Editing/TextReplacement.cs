using System.Collections.Generic;
using TrestleBoard.Core.Commands;

namespace TrestleBoard.Editing;

/// <summary>
/// Swapping one stretch of words for another, as a single undoable act.
///
/// <para>Find-and-replace has built this since M21; M52's spell check needs exactly the same
/// composite — delete these characters, put those there — so it moved out of
/// <see cref="FindController"/> rather than being written twice. Two copies of "one edit is one
/// undo step" is one copy too many: the day they disagree, one of the two features starts needing
/// two presses of Ctrl+Z to take back one thing the user did.</para>
/// </summary>
public static class TextReplacement
{
    /// <summary>
    /// One <see cref="IDocumentCommand"/> covering both halves, so one Ctrl+Z puts the old words
    /// back. An empty <paramref name="replacement"/> is a deletion, and skips the insert.
    /// </summary>
    public static CompositeCommand Build(
        string storyId,
        int paragraphIndex,
        int offset,
        int length,
        string replacement,
        string description) =>
        new(
            description,
            new ChangeScope(ChangeKind.Text, StoryId: storyId),
            [.. Steps(storyId, paragraphIndex, offset, length, replacement)]);

    /// <summary>
    /// The two halves on their own, for a caller gathering many replacements into one composite of
    /// its own — which is what "Replace all" does, back to front, so that each swap leaves the
    /// offsets before it alone.
    /// </summary>
    public static IEnumerable<IDocumentCommand> Steps(
        string storyId,
        int paragraphIndex,
        int offset,
        int length,
        string replacement)
    {
        yield return new DeleteTextCommand(storyId, paragraphIndex, offset, length);
        if (replacement.Length > 0)
        {
            yield return new InsertTextCommand(storyId, paragraphIndex, offset, replacement);
        }
    }
}
