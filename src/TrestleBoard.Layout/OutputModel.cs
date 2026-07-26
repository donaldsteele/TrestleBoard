using SkiaSharp;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Input;

namespace TrestleBoard.Layout;

public readonly record struct FloatInterval(float Left, float Right)
{
    public float Width => Right - Left;
}

/// <summary>
/// Maps layout output back to source text. Char indices are paragraph-relative (into the
/// paragraph's concatenated run text); callers recover (runIndex, offsetInRun) from run lengths.
/// </summary>
public readonly record struct SourceSpan(string StoryId, int ParagraphIndex, int StartChar, int EndChar);

/// <summary>
/// A drawable run: one font/size/color at one baseline. Glyph offsets are relative to
/// (OriginX, BaselineY), y-down. Carries everything a renderer needs to build an SKTextBlob
/// with zero re-shaping, and the cluster map M4 hit-testing needs.
/// </summary>
public sealed class PositionedGlyphRun
{
    internal PositionedGlyphRun(
        ResolvedFont font,
        float sizePt,
        uint colorArgb,
        float originX,
        float baselineY,
        IReadOnlyList<ushort> glyphs,
        IReadOnlyList<SKPoint> glyphOffsets,
        IReadOnlyList<int> clusters,
        SourceSpan source,
        float advanceWidthPt)
    {
        Font = font;
        SizePt = sizePt;
        ColorArgb = colorArgb;
        OriginX = originX;
        BaselineY = baselineY;
        Glyphs = glyphs;
        GlyphOffsets = glyphOffsets;
        Clusters = clusters;
        Source = source;
        AdvanceWidthPt = advanceWidthPt;
    }

    public ResolvedFont Font { get; }

    public float SizePt { get; }

    public uint ColorArgb { get; }

    public float OriginX { get; }

    public float BaselineY { get; }

    public IReadOnlyList<ushort> Glyphs { get; }

    public IReadOnlyList<SKPoint> GlyphOffsets { get; }

    public IReadOnlyList<int> Clusters { get; }

    public SourceSpan Source { get; }

    public float AdvanceWidthPt { get; }
}

public sealed class LineSegment
{
    internal LineSegment(FloatInterval xRange, TextAlign align, IReadOnlyList<PositionedGlyphRun> runs)
    {
        XRange = xRange;
        Align = align;
        Runs = runs;
    }

    public FloatInterval XRange { get; }

    public TextAlign Align { get; }

    public IReadOnlyList<PositionedGlyphRun> Runs { get; }
}

public sealed class LineBox
{
    internal LineBox(
        int paragraphIndex,
        bool isParagraphStart,
        float baselineY,
        float bandTop,
        float bandBottom,
        float lineHeight,
        float maxAscentPt,
        float maxDescentPt,
        IReadOnlyList<LineSegment> segments,
        SourceSpan source)
    {
        ParagraphIndex = paragraphIndex;
        IsParagraphStart = isParagraphStart;
        BaselineY = baselineY;
        BandTop = bandTop;
        BandBottom = bandBottom;
        LineHeight = lineHeight;
        MaxAscentPt = maxAscentPt;
        MaxDescentPt = maxDescentPt;
        Segments = segments;
        Source = source;
    }

    public int ParagraphIndex { get; }

    public bool IsParagraphStart { get; }

    public float BaselineY { get; }

    public float BandTop { get; }

    public float BandBottom { get; }

    public float LineHeight { get; }

    public float MaxAscentPt { get; }

    public float MaxDescentPt { get; }

    public IReadOnlyList<LineSegment> Segments { get; }

    public SourceSpan Source { get; }
}

public sealed class FrameLayout
{
    internal FrameLayout(int frameIndex, FrameRect frame, IReadOnlyList<LineBox> lines)
    {
        FrameIndex = frameIndex;
        Frame = frame;
        Lines = lines;
    }

    public int FrameIndex { get; }

    public FrameRect Frame { get; }

    public IReadOnlyList<LineBox> Lines { get; }
}

/// <summary>Where layout stopped when the frame chain ran out of room.</summary>
public readonly record struct OversetInfo(int LastFrameIndex, int ParagraphIndex, int CharIndex);

public sealed class LayoutResult
{
    internal LayoutResult(IReadOnlyList<FrameLayout> frames, OversetInfo? overflow)
    {
        Frames = frames;
        Overflow = overflow;
    }

    public IReadOnlyList<FrameLayout> Frames { get; }

    public bool IsOverset => Overflow is not null;

    public OversetInfo? Overflow { get; }
}
