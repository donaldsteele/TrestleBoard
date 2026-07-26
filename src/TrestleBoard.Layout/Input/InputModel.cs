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
    uint ColorArgb);

public readonly record struct ParagraphStyle(
    float LineSpacing,
    float SpaceBeforePt,
    float SpaceAfterPt,
    float FirstLineIndentPt,
    TextAlign Align,
    CharacterStyle DefaultRun);

public sealed record LayoutRun(string Text, CharacterStyle Style);

public sealed record LayoutParagraph(ParagraphStyle Style, IReadOnlyList<LayoutRun> Runs);

public sealed record LayoutStory(string StoryId, IReadOnlyList<LayoutParagraph> Paragraphs);

/// <summary>One frame in a story's frame chain. ColumnCount is 1 in M1 (multi-column deferred).</summary>
public sealed record LayoutFrame(FrameRect Rect, IReadOnlyList<ExclusionRect> Exclusions, int ColumnCount = 1);

public sealed record LayoutRequest(LayoutStory Story, IReadOnlyList<LayoutFrame> Frames);
