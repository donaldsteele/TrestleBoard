using TrestleBoard.Core.Model;

namespace TrestleBoard.Editing;

/// <summary>
/// Makes a content copy of a block (PLAN.md §11 M91) — everything except where it sits.
///
/// <para><b>Lifted out of <c>FrameEditorController.DuplicateSelected</c>, which has owned this
/// switch since M81.</b> It was welded to "the same page, twenty-four points down", which is what
/// made copy-and-paste-onto-another-page impossible to build without writing the switch a second
/// time. Two copies of a per-type switch is exactly the shape that goes wrong when a seventh block
/// type arrives: one of them learns about it and the other silently does the wrong thing.</para>
///
/// <para><b>Geometry belongs to the caller.</b> <see cref="Block.FrameRect"/> and
/// <see cref="Block.ZOrder"/> are deliberately left at their defaults here, because the whole
/// difference between "make another like this" and "put it on page four" is where it lands — and
/// the target page's paper may not even be the same size as the one it came from.</para>
/// </summary>
internal static class BlockCopier
{
    /// <summary>
    /// A copy of <paramref name="original"/> carrying <paramref name="newId"/>, or <c>null</c> for
    /// a kind of block nobody has wired up.
    ///
    /// <para><b>Null rather than a partial copy.</b> M72's lesson: a new block type that "appears
    /// to work" while quietly dropping half of itself is worse than one that refuses, because the
    /// refusal is noticed on the first try and the silent loss is noticed months later, in a
    /// newsletter that has already gone out.</para>
    /// </summary>
    /// <param name="newStoryId">
    /// The story the copy's words will live in — required for a <see cref="TextBlock"/> and unused
    /// by every other kind. The caller mints it because only the caller knows what is already taken
    /// in the batch it is building.
    /// </param>
    internal static Block? Copy(Block original, string newId, string? newStoryId)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentException.ThrowIfNullOrEmpty(newId);

        Block copy;

        switch (original)
        {
            case TextBlock text:
            {
                ArgumentException.ThrowIfNullOrEmpty(newStoryId);
                copy = new TextBlock
                {
                    Id = newId,

                    // Its own story, never the same one: two blocks sharing a story is what a LINK
                    // is, and a copy is not a continuation.
                    StoryRef = newStoryId,
                    ColumnCount = text.ColumnCount,
                    VerticalAlign = text.VerticalAlign,

                    // Deliberately NOT copied: a copy continues nothing.
                    LinkNext = null,
                };
                break;
            }

            case ImageFrame image:
                copy = new ImageFrame
                {
                    Id = newId,

                    // The bytes are already in the package, and a second copy of a three-megabyte
                    // photograph would double the file for no reason a reader could see.
                    AssetRef = image.AssetRef,
                    Recipe = image.Recipe.Clone(),
                    Fit = image.Fit,
                    Caption = image.Caption,
                    AltText = image.AltText,
                    SourcePdfAssetRef = image.SourcePdfAssetRef,
                    SourcePdfPage = image.SourcePdfPage,
                };
                break;

            case WidgetBlock widget:
                copy = new WidgetBlock
                {
                    Id = newId,
                    WidgetType = widget.WidgetType,
                    DataVersion = widget.DataVersion,
                    Data = widget.Data,
                    TableStyleRef = widget.TableStyleRef,
                };
                break;

            case VectorBlock vector:
                copy = new VectorBlock
                {
                    Id = newId,
                    ViewBoxWidth = vector.ViewBoxWidth,
                    ViewBoxHeight = vector.ViewBoxHeight,
                    Parts = [.. vector.Parts.Select(part => new VectorPart
                    {
                        PathData = part.PathData,
                        StrokeWidth = part.StrokeWidth,
                    })],
                    InkArgb = vector.InkArgb,
                    AltText = vector.AltText,
                    Caption = vector.Caption,
                    EmblemId = vector.EmblemId,
                    EmblemFingerprint = vector.EmblemFingerprint,
                };
                break;

            case ShapeBlock shape:
                copy = new ShapeBlock
                {
                    Id = newId,
                    Kind = shape.Kind,
                    StrokeArgb = shape.StrokeArgb,
                    StrokeWidthPt = shape.StrokeWidthPt,
                    FillArgb = shape.FillArgb,
                };
                break;

            default:
                return null;
        }

        copy.WrapMode = original.WrapMode;
        copy.WrapMarginPt = original.WrapMarginPt;
        copy.FrameStyleRef = original.FrameStyleRef;

        // Deliberately NOT copied: a copy the user has just asked for is a copy they are about to
        // move, and one that arrived pinned would refuse the very next thing they do.
        copy.Locked = false;

        return copy;
    }

    /// <summary>A paragraph and its runs, detached from the story they came out of.</summary>
    internal static StoryParagraph CopyParagraph(StoryParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        return new StoryParagraph
        {
            ParagraphStyleRef = paragraph.ParagraphStyleRef,
            ListKind = paragraph.ListKind,
            Runs = [.. paragraph.Runs.Select(run => new StoryRun
            {
                Text = run.Text,
                CharacterStyleRef = run.CharacterStyleRef,
            })],
        };
    }
}
