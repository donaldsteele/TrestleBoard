using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout.Documents;

/// <summary>
/// Where a picture's caption goes and what it is set in (PLAN.md §11 M18).
///
/// <para><see cref="ImageFrame.Caption"/> has been collected by the insert dialog since M6, stored
/// in the document, carried through every save and reload — and never drawn. This is the only part
/// of M17–M21 allowed to move a rendered pixel, and it is the reason: a caption the user typed and
/// could not see was a promise the file format kept and the page did not.</para>
///
/// <para><b>The caption is laid out as an ordinary story</b>, through the same engine and the same
/// styles as everything else, in a band under the frame. That is what makes it print in the PDF
/// exactly as it appears on screen without a second code path — the WYSIWYG guarantee is structural
/// (PLAN.md §1), and a caption drawn by a bespoke text routine would have been the first hole in
/// it.</para>
/// </summary>
public static class CaptionLayout
{
    /// <summary>The name of the paragraph style a caption is set in, when the document defines one.</summary>
    public const string StyleName = "caption";

    /// <summary>Breathing room between the bottom of the picture and the first caption baseline's band.</summary>
    public const float GapPt = 4f;

    /// <summary>
    /// How many lines a caption may take. A caption is a line under a photograph, not an article;
    /// three is generous enough that nothing sensible is cut off and small enough that a paragraph
    /// pasted in by accident cannot silently push the page's text half a column down.
    /// </summary>
    public const int MaxLines = 3;

    /// <summary>The caption text of a block, or null when there is nothing to print.</summary>
    public static string? TextOf(Block block) =>
        block is ImageFrame { Caption: { } caption } && caption.Trim().Length > 0
            ? caption.Trim()
            : null;

    /// <summary>
    /// The band the caption is laid out in: full frame width, starting a gap below the frame, and
    /// tall enough for <see cref="MaxLines"/>. The band is where the caption MAY go; how much of it
    /// the caption actually uses comes back from the engine as line boxes, and that — not this — is
    /// what the text around the picture has to flow past.
    /// </summary>
    public static RectPt Band(Document document, RectPt frameRect)
    {
        ArgumentNullException.ThrowIfNull(document);

        (ParagraphStyleDef paragraph, CharacterStyleDef character) = ResolveStyle(document);
        float lineHeight = character.SizePt * Math.Max(1f, paragraph.LineSpacing);

        // 1.6 covers ascent, descent and leading for any bundled face at this size; the band is only
        // an upper bound, so overshooting it costs nothing and undershooting would clip a line.
        return new RectPt(
            frameRect.X,
            frameRect.Bottom + GapPt,
            frameRect.Width,
            (lineHeight * 1.6f * MaxLines) + paragraph.SpaceAfterPt);
    }

    /// <summary>
    /// The layout request for one caption, or null when the block has none. The caller runs it
    /// through the same <see cref="TextLayoutEngine"/> every story goes through.
    /// </summary>
    public static LayoutRequest? Request(Document document, Block block, RectPt frameRect)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(block);

        if (TextOf(block) is not { } caption)
        {
            return null;
        }

        (ParagraphStyleDef paragraph, CharacterStyleDef character) = ResolveStyle(document);
        CharacterStyle run = DocumentLayoutAdapter.MapCharacter(character);
        var style = new ParagraphStyle(
            paragraph.LineSpacing,
            SpaceBeforePt: 0f,   // the gap under the picture already provides it
            paragraph.SpaceAfterPt,
            FirstLineIndentPt: 0f,
            DocumentLayoutAdapter.MapAlign(paragraph.Align),
            run);

        var story = new LayoutStory(
            $"caption:{block.Id}",
            [new LayoutParagraph(style, [new LayoutRun(caption, run)])]);

        RectPt band = Band(document, frameRect);
        return new LayoutRequest(
            story,
            [new LayoutFrame(new FrameRect(band.X, band.Y, band.Right, band.Bottom), [])]);
    }

    /// <summary>
    /// The caption style if the document has one, the body style if not, and a last-resort default
    /// if it has neither. A document written by hand in a test does not have to carry a full
    /// stylesheet to be drawable — throwing here would turn a missing style into a blank page.
    /// </summary>
    private static (ParagraphStyleDef Paragraph, CharacterStyleDef Character) ResolveStyle(Document document)
    {
        ParagraphStyleDef? paragraph =
            document.StyleSheet.ParagraphStyles.Find(s => s.Name == StyleName)
            ?? document.StyleSheet.ParagraphStyles.Find(s => s.Name == "body")
            ?? document.StyleSheet.ParagraphStyles.FirstOrDefault();

        CharacterStyleDef? character = paragraph is null
            ? null
            : document.StyleSheet.CharacterStyles.Find(s => s.Name == paragraph.CharacterStyleRef);

        character ??= document.StyleSheet.CharacterStyles.Find(s => s.Name == StyleName)
            ?? document.StyleSheet.CharacterStyles.FirstOrDefault();

        return (
            paragraph ?? new ParagraphStyleDef
            {
                Name = StyleName,
                CharacterStyleRef = StyleName,
                LineSpacing = 1.2f,
                SpaceAfterPt = 4f,
            },
            character ?? new CharacterStyleDef
            {
                Name = StyleName,
                FontFamily = Fonts.BundledFonts.SansFamily,
                SizePt = 10f,
            });
    }
}
