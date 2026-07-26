using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Commands;

public enum ChangeKind
{
    Text,
    BlockGeometry,
    BlockContent,
    PageStructure,
    StoryStructure,
    Metadata,
}

/// <summary>Dirty scope for layout invalidation and repaint (PLAN.md §4). Null ids = broad change.</summary>
public readonly record struct ChangeScope(ChangeKind Kind, string? PageId = null, string? BlockId = null, string? StoryId = null);

/// <summary>
/// The ONLY way the model mutates (PLAN.md §4). Apply must capture whatever pre-state Revert
/// needs, and must re-capture on every call so redo works. Description is plain language for
/// the Edit menu ("Undo Move photo").
/// </summary>
public interface IDocumentCommand
{
    string Description { get; }

    ChangeScope Scope { get; }

    void Apply(Document document);

    void Revert(Document document);

    /// <summary>
    /// Attempts to absorb <paramref name="newer"/> (already applied to the document) into this
    /// command so one Undo covers both. Returns false to keep them separate. The session only
    /// offers merges within the coalescing time window.
    /// </summary>
    bool TryMerge(IDocumentCommand newer);
}
