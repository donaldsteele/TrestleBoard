using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Core.Commands;

/// <summary>Builds command lists for range edits (docs/M4-spec.md §5.4/§5.6). The children are
/// order-sensitive: each sees the document as mutated by its predecessors.</summary>
public static class TextEditBuilder
{
    /// <summary>
    /// Deletes [range.Start, range.End]. Same-paragraph → one DeleteTextCommand. Spanning
    /// paragraphs → truncate first tail, empty interiors, truncate last head, then merge the
    /// gap closed. A story always retains at least one paragraph.
    /// </summary>
    public static IReadOnlyList<IDocumentCommand> BuildDeleteRange(Story story, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (range.IsEmpty)
        {
            return [];
        }

        string sid = range.StoryId;
        int p1 = range.Start.ParagraphIndex;
        int p2 = range.End.ParagraphIndex;
        int o1 = range.Start.Offset;
        int o2 = range.End.Offset;

        if (p1 == p2)
        {
            return [new DeleteTextCommand(sid, p1, o1, o2 - o1)];
        }

        var commands = new List<IDocumentCommand>();
        int firstLength = story.Paragraphs[p1].Length;
        if (firstLength > o1)
        {
            commands.Add(new DeleteTextCommand(sid, p1, o1, firstLength - o1));
        }

        for (int i = p1 + 1; i < p2; i++)
        {
            int length = story.Paragraphs[i].Length;
            if (length > 0)
            {
                commands.Add(new DeleteTextCommand(sid, i, 0, length));
            }
        }

        if (o2 > 0)
        {
            commands.Add(new DeleteTextCommand(sid, p2, 0, o2));
        }

        for (int i = 0; i < p2 - p1; i++)
        {
            commands.Add(new MergeParagraphCommand(sid, p1));
        }

        return commands;
    }

    /// <summary>
    /// Inserts multi-paragraph text at a position: split, fill head tail and interior chunks
    /// (docs/M4-spec.md §5.6). chunks must be the '\n'-split pieces, length >= 2.
    /// </summary>
    public static IReadOnlyList<IDocumentCommand> BuildMultiParagraphInsert(
        Story story, TextPosition at, IReadOnlyList<string> chunks)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunks.Count, 2);

        string sid = at.StoryId;
        int p = at.ParagraphIndex;
        var commands = new List<IDocumentCommand>
        {
            new SplitParagraphCommand(sid, p, at.Offset),
        };
        int headLength = at.Offset;
        if (chunks[0].Length > 0)
        {
            commands.Add(new InsertTextCommand(sid, p, headLength, chunks[0]));
        }

        // Middle chunks: split at the end of the previous paragraph creates an empty paragraph
        // pushing the tail down, then fill it.
        int previousLength = headLength + chunks[0].Length;
        for (int k = 1; k < chunks.Count - 1; k++)
        {
            commands.Add(new SplitParagraphCommand(sid, p + k - 1, previousLength));
            if (chunks[k].Length > 0)
            {
                commands.Add(new InsertTextCommand(sid, p + k, 0, chunks[k]));
            }

            previousLength = chunks[k].Length;
        }

        string last = chunks[^1];
        if (last.Length > 0)
        {
            commands.Add(new InsertTextCommand(sid, p + chunks.Count - 1, 0, last));
        }

        return commands;
    }
}
