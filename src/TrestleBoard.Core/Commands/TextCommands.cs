using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

/// <summary>
/// Types text at a paragraph-relative offset. Reverts from a pre-Apply run-list snapshot
/// (re-captured on every Apply so redo stays correct). Merges contiguous same-paragraph
/// inserts so Undo removes word-bursts, not characters (PLAN.md §4).
/// </summary>
public sealed class InsertTextCommand(string storyId, int paragraphIndex, int offset, string text) : IDocumentCommand
{
    private List<StoryRun>? _before;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public int Offset { get; } = offset;

    public string Text { get; private set; } = text;

    public string Description => "Typing";

    public ChangeScope Scope => new(ChangeKind.Text, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        _before = para.Runs.ConvertAll(r => r.Clone());
        StoryText.Insert(para, Offset, Text);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        para.Runs = (_before ?? throw new InvalidOperationException("Revert before Apply.")).ConvertAll(r => r.Clone());
    }

    public bool TryMerge(IDocumentCommand newer)
    {
        if (newer is InsertTextCommand n
            && n.StoryId == StoryId
            && n.ParagraphIndex == ParagraphIndex
            && n.Offset == Offset + Text.Length)
        {
            Text += n.Text;
            return true;
        }

        return false;
    }
}

/// <summary>
/// Deletes [offset, offset+length). Merges forward-delete bursts (same offset) and backspace
/// bursts (ranges meeting at this command's start).
/// </summary>
public sealed class DeleteTextCommand(string storyId, int paragraphIndex, int offset, int length) : IDocumentCommand
{
    private List<StoryRun>? _before;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public int Offset { get; private set; } = offset;

    public int Length { get; private set; } = length;

    public string Description => "Delete text";

    public ChangeScope Scope => new(ChangeKind.Text, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        _before = para.Runs.ConvertAll(r => r.Clone());
        StoryText.Delete(para, Offset, Length);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        para.Runs = (_before ?? throw new InvalidOperationException("Revert before Apply.")).ConvertAll(r => r.Clone());
    }

    public bool TryMerge(IDocumentCommand newer)
    {
        if (newer is not DeleteTextCommand n || n.StoryId != StoryId || n.ParagraphIndex != ParagraphIndex)
        {
            return false;
        }

        if (n.Offset == Offset)
        {
            // Forward delete: later command removed text at the same position.
            Length += n.Length;
            return true;
        }

        if (n.Offset + n.Length == Offset)
        {
            // Backspace: later command removed text immediately before this range.
            Offset = n.Offset;
            Length += n.Length;
            return true;
        }

        return false;
    }
}
