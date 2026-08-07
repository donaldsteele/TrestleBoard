using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

/// <summary>
/// Splits a paragraph at a paragraph-relative offset (Enter). The tail becomes a new paragraph
/// at index+1 inheriting ParagraphStyleRef; run styles are preserved and both halves stay
/// canonical. Revert restores the single pre-split clone (docs/M4-spec.md §5.1).
/// </summary>
public sealed class SplitParagraphCommand(string storyId, int paragraphIndex, int offset) : IDocumentCommand
{
    private StoryParagraph? _before;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public int Offset { get; } = offset;

    public string Description => "Split paragraph";

    public ChangeScope Scope => new(ChangeKind.StoryStructure, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StoryParagraph> paragraphs = document.GetStory(StoryId).Paragraphs;
        _before = paragraphs[ParagraphIndex].Clone();
        (StoryParagraph head, StoryParagraph tail) = StoryText.SplitAt(paragraphs[ParagraphIndex], Offset);
        paragraphs[ParagraphIndex] = head;
        paragraphs.Insert(ParagraphIndex + 1, tail);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StoryParagraph> paragraphs = document.GetStory(StoryId).Paragraphs;
        paragraphs.RemoveAt(ParagraphIndex + 1);
        paragraphs[ParagraphIndex] = (_before ?? throw new InvalidOperationException("Revert before Apply.")).Clone();
    }

    // Enter is always its own undo step.
    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Merges paragraph index+1 into index (Backspace at paragraph start / Delete at paragraph
/// end). The survivor keeps index's ParagraphStyleRef; the seam is canonicalized. Revert
/// restores both clones.
/// </summary>
public sealed class MergeParagraphCommand(string storyId, int paragraphIndex) : IDocumentCommand
{
    private StoryParagraph? _beforeFirst;
    private StoryParagraph? _beforeSecond;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public string Description => "Join paragraphs";

    public ChangeScope Scope => new(ChangeKind.StoryStructure, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StoryParagraph> paragraphs = document.GetStory(StoryId).Paragraphs;
        _beforeFirst = paragraphs[ParagraphIndex].Clone();
        _beforeSecond = paragraphs[ParagraphIndex + 1].Clone();
        paragraphs[ParagraphIndex].Runs.AddRange(paragraphs[ParagraphIndex + 1].Runs.Select(r => r.Clone()));
        StoryText.Canonicalize(paragraphs[ParagraphIndex]);
        paragraphs.RemoveAt(ParagraphIndex + 1);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<StoryParagraph> paragraphs = document.GetStory(StoryId).Paragraphs;
        paragraphs[ParagraphIndex] = (_beforeFirst ?? throw new InvalidOperationException("Revert before Apply.")).Clone();
        paragraphs.Insert(ParagraphIndex + 1, (_beforeSecond ?? throw new InvalidOperationException("Revert before Apply.")).Clone());
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>Applies (or clears, with null) a named character style over a paragraph-relative
/// range. Clone-based revert; canonical form maintained by StoryText.SetCharacterStyle.</summary>
public sealed class ApplyCharacterStyleCommand(
    string storyId, int paragraphIndex, int offset, int length, string? characterStyleRef) : IDocumentCommand
{
    private List<StoryRun>? _before;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public int Offset { get; } = offset;

    public int Length { get; } = length;

    public string? CharacterStyleRef { get; } = characterStyleRef;

    public string Description => "Change text style";

    public ChangeScope Scope => new(ChangeKind.Text, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        _before = para.Runs.ConvertAll(r => r.Clone());
        StoryText.SetCharacterStyle(para, Offset, Length, CharacterStyleRef);
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        para.Runs = (_before ?? throw new InvalidOperationException("Revert before Apply.")).ConvertAll(r => r.Clone());
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

public sealed class ApplyParagraphStyleCommand(string storyId, int paragraphIndex, string paragraphStyleRef) : IDocumentCommand
{
    private string? _old;

    public string StoryId { get; } = storyId;

    public int ParagraphIndex { get; } = paragraphIndex;

    public string ParagraphStyleRef { get; } = paragraphStyleRef;

    public string Description => "Change paragraph style";

    public ChangeScope Scope => new(ChangeKind.Text, StoryId: StoryId);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        StoryParagraph para = document.GetStory(StoryId).Paragraphs[ParagraphIndex];
        _old = para.ParagraphStyleRef;
        para.ParagraphStyleRef = ParagraphStyleRef;
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.GetStory(StoryId).Paragraphs[ParagraphIndex].ParagraphStyleRef =
            _old ?? throw new InvalidOperationException("Revert before Apply.");
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// One undo step spanning several primitives (replace-selection, multi-paragraph delete,
/// paste, style over a multi-paragraph selection). Children apply in order and revert in
/// reverse; each child re-captures its own pre-state, so redo is correct.
/// </summary>
public sealed class CompositeCommand(
    string description, ChangeScope scope, IReadOnlyList<IDocumentCommand> children) : IDocumentCommand
{
    public IReadOnlyList<IDocumentCommand> Children { get; } = children;

    public string Description { get; } = description;

    public ChangeScope Scope { get; } = scope;

    /// <summary>
    /// All of the children, or none of them.
    ///
    /// <para>A composite is the app's unit of "one undoable thing" — adding a text frame is a story
    /// and a block, filling in the officers is a dozen row rewrites. If a child throws part way
    /// through, the ones before it have already mutated the document, and
    /// <see cref="DocumentSession.Execute"/> never reaches the line that pushes the command onto
    /// the undo stack. The result was a half-applied change the user could not take back, because
    /// as far as the stack was concerned it never happened (review §14.2).</para>
    ///
    /// <para>So a failure unwinds what it managed to do first. The unwind is best-effort — a
    /// Revert that throws while cleaning up after another throw has nothing useful to say, and
    /// letting it escape would replace the real exception with a confusing one — but the original
    /// exception always reaches the caller, which is what tells the shell to say something.</para>
    /// </summary>
    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        int applied = 0;
        try
        {
            for (; applied < Children.Count; applied++)
            {
                Children[applied].Apply(document);
            }
        }
        catch
        {
            for (int i = applied - 1; i >= 0; i--)
            {
                try
                {
                    Children[i].Revert(document);
                }
                catch (Exception cleanup) when (cleanup is InvalidOperationException
                    or KeyNotFoundException
                    or ArgumentOutOfRangeException)
                {
                    // Nothing to be done, and the exception below is the one worth reporting.
                }
            }

            throw;
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Children[i].Revert(document);
        }
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}

/// <summary>
/// Adds a CharacterStyleDef to the stylesheet if absent (bold/italic toggling reaching a
/// variant a template didn't define). Revert removes only what it added. Always used inside a
/// CompositeCommand next to the ApplyCharacterStyleCommand that needs it.
/// </summary>
public sealed class EnsureCharacterStyleCommand(CharacterStyleDef style) : IDocumentCommand
{
    private bool _added;

    public CharacterStyleDef Style { get; } = style;

    public string Description => "Add text style";

    public ChangeScope Scope => new(ChangeKind.Metadata);

    public void Apply(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _added = document.StyleSheet.CharacterStyles.TrueForAll(s => s.Name != Style.Name);
        if (_added)
        {
            document.StyleSheet.CharacterStyles.Add(Style);
        }
    }

    public void Revert(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_added)
        {
            document.StyleSheet.CharacterStyles.RemoveAll(s => s.Name == Style.Name);
        }
    }

    public bool TryMerge(IDocumentCommand newer) => false;
}
