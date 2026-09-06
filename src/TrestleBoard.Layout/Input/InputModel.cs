using TrestleBoard.Layout.Fonts;

namespace TrestleBoard.Layout.Input;

public enum TextAlign
{
    Left,
    Center,
    Right,
}

public readonly record struct FrameRect(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;

    public float Height => Bottom - Top;
}

/// <summary>A rectangle text must avoid; the engine inflates <see cref="Rect"/> by <see cref="WrapMargin"/>.</summary>
public readonly record struct ExclusionRect(FrameRect Rect, float WrapMargin, int ZOrder);

public readonly record struct CharacterStyle(
    string FontFamily,
    FontWeight Weight,
    FontStyleSlant Slant,
    float SizePt,
    uint ColorArgb,

    /// <summary>
    /// M102: whether a line is drawn under the words. Optional and last, so every existing
    /// construction of this struct keeps compiling and keeps meaning what it did.
    /// </summary>
    bool Underline = false);

/// <param name="MarkerText">
/// M61: the bullet or number printed before a list paragraph, or empty for ordinary writing.
///
/// <para>The engine measures it in <see cref="DefaultRun"/> and hangs the whole paragraph by that
/// width, so wrapped lines align under the words rather than under the marker. It is NOT part of
/// the story's text: it is never selectable, never deletable, and never counted in a character
/// offset — which is what lets a numbered list renumber itself without editing the document.</para>
/// </param>
public readonly record struct ParagraphStyle(
    float LineSpacing,
    float SpaceBeforePt,
    float SpaceAfterPt,
    float FirstLineIndentPt,
    TextAlign Align,
    CharacterStyle DefaultRun,
    string MarkerText = "",

    /// <summary>
    /// M109: how far in from the frame's left edge EVERY line starts. Optional and last, so every
    /// existing construction of this struct keeps compiling and keeps meaning what it did.
    /// </summary>
    float LeftIndentPt = 0f,

    /// <summary>M109: how far in from the frame's right edge every line stops.</summary>
    float RightIndentPt = 0f);

public sealed record LayoutRun(string Text, CharacterStyle Style);

public sealed record LayoutParagraph(ParagraphStyle Style, IReadOnlyList<LayoutRun> Runs);

public sealed record LayoutStory(string StoryId, IReadOnlyList<LayoutParagraph> Paragraphs);

/// <summary>One frame in a story's frame chain. ColumnCount is 1 in M1 (multi-column deferred).</summary>
public sealed record LayoutFrame(FrameRect Rect, IReadOnlyList<ExclusionRect> Exclusions, int ColumnCount = 1);

public sealed record LayoutRequest(LayoutStory Story, IReadOnlyList<LayoutFrame> Frames);
