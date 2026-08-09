namespace TrestleBoard.Core.Import;

/// <summary>
/// One paragraph brought in from somebody else's file (PLAN.md §11 M66).
/// </summary>
/// <param name="Text">The words, with every trace of the other program's formatting gone.</param>
/// <param name="StyleRef">
/// The name of one of this app's own paragraph styles — <c>heading</c>, <c>subheading</c>,
/// <c>quote</c>, <c>caption</c> or <c>body</c>. Not the Word style it came from: the whole point is
/// that the writing arrives dressed as a TrestleBoard newsletter and not as a Word document.
/// </param>
/// <param name="ListKind">One of <c>Model.ListKinds</c> when the paragraph was a list point (M61).</param>
public sealed record ImportedParagraph(string Text, string StyleRef, string? ListKind = null);

/// <summary>
/// A picture that came with the writing. Offered one at a time and never inserted unasked — a Word
/// document's media folder holds the author's letterhead and their signature scan as readily as the
/// photograph they meant to send.
/// </summary>
/// <param name="Name">The name it had inside the file, for the "use it?" question.</param>
/// <param name="Bytes">
/// The original bytes, untouched. They go into the container exactly as they arrived (gate 7), so a
/// picture that came through Word can still be re-cropped in three years.
/// </param>
public sealed record ImportedPicture(string Name, byte[] Bytes);

/// <summary>What a file turned out to contain.</summary>
/// <param name="Paragraphs">The writing, in order.</param>
/// <param name="Pictures">The pictures, in the order the file stored them.</param>
public sealed record ImportedWriting(
    IReadOnlyList<ImportedParagraph> Paragraphs,
    IReadOnlyList<ImportedPicture> Pictures)
{
    /// <summary>True when the file had nothing in it worth bringing in.</summary>
    public bool IsEmpty => Paragraphs.Count == 0 && Pictures.Count == 0;

    /// <summary>Every word of it, for a count the user can be shown before anything happens.</summary>
    public int WordCount =>
        Paragraphs.Sum(p => p.Text.Split(
            [' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length);
}
