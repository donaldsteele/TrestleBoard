using HarfBuzzSharp;
using TrestleBoard.Layout.Fonts;
using HbBuffer = HarfBuzzSharp.Buffer;

namespace TrestleBoard.Layout.Shaping;

public readonly record struct ShapedGlyph(
    ushort GlyphId,
    int Cluster,
    float XAdvancePt,
    float XOffsetPt,
    float YOffsetPt);

public sealed class ShapedRun
{
    internal ShapedRun(
        ResolvedFont font,
        float sizePt,
        uint colorArgb,
        string text,
        int paragraphCharStart,
        IReadOnlyList<ShapedGlyph> glyphs)
    {
        Font = font;
        SizePt = sizePt;
        ColorArgb = colorArgb;
        Text = text;
        ParagraphCharStart = paragraphCharStart;
        Glyphs = glyphs;
    }

    public ResolvedFont Font { get; }

    public float SizePt { get; }

    public uint ColorArgb { get; }

    public string Text { get; }

    public int ParagraphCharStart { get; }

    public IReadOnlyList<ShapedGlyph> Glyphs { get; }

    /// <summary>
    /// Where the cluster beginning at <paramref name="cluster"/> ENDS, in paragraph coordinates.
    ///
    /// <para>A cluster is not one character. "fi" shaped with standard ligatures on — which is the
    /// default — is a single glyph whose cluster is the index of the "f" and which covers two
    /// characters. Callers that treated the end as <c>cluster + 1</c> were therefore one or two
    /// characters short whenever a run ended in a ligature, and the caret and selection geometry
    /// built on top of that span stopped short of the text (review §14.2).</para>
    ///
    /// <para>The answer is the next cluster boundary in this run, or the run's own end when there
    /// is none. It is asked of the whole shaped run rather than of the glyphs placed on one line,
    /// because the boundary may be a glyph that landed on the following line — and that is still
    /// where this cluster stops.</para>
    /// </summary>
    public int ClusterEnd(int cluster)
    {
        int next = int.MaxValue;
        foreach (ShapedGlyph glyph in Glyphs)
        {
            if (glyph.Cluster > cluster && glyph.Cluster < next)
            {
                next = glyph.Cluster;
            }
        }

        return next == int.MaxValue ? ParagraphCharStart + Text.Length : next;
    }
}

public readonly record struct ShapeOptions(bool Ligatures, bool Kerning);

public static class HarfBuzzShaper
{
    public const int HbScale = ResolvedFont.HbScale;

    private static readonly Tag LigaTag = new('l', 'i', 'g', 'a');
    private static readonly Tag KernTag = new('k', 'e', 'r', 'n');
    private static readonly Tag DligTag = new('d', 'l', 'i', 'g');
    private static readonly Tag HligTag = new('h', 'l', 'i', 'g');

    /// <summary>
    /// Shapes one style run of a paragraph. Clusters in the result are paragraph-relative.
    /// Segment properties are set explicitly (never guessed) so no locale can perturb output.
    /// </summary>
    public static ShapedRun Shape(
        ResolvedFont font,
        float sizePt,
        uint colorArgb,
        string paragraphText,
        int runStart,
        int runLength,
        ShapeOptions options)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(paragraphText);
        ArgumentOutOfRangeException.ThrowIfNegative(runStart);
        ArgumentOutOfRangeException.ThrowIfNegative(runLength);

        using var buffer = new HbBuffer();
        buffer.AddUtf16(paragraphText.AsSpan(runStart, runLength));
        buffer.Direction = Direction.LeftToRight;
        buffer.Script = Script.Latin;
        buffer.Language = new Language("en");

        Feature[] features =
        [
            new Feature(LigaTag, options.Ligatures ? 1u : 0u),
            new Feature(KernTag, options.Kerning ? 1u : 0u),
            new Feature(DligTag, 0u),
            new Feature(HligTag, 0u),
        ];
        font.HbFont.Shape(buffer, features);

        ReadOnlySpan<GlyphInfo> infos = buffer.GetGlyphInfoSpan();
        ReadOnlySpan<GlyphPosition> positions = buffer.GetGlyphPositionSpan();
        float f = sizePt / HbScale;
        var glyphs = new ShapedGlyph[infos.Length];
        for (int i = 0; i < infos.Length; i++)
        {
            glyphs[i] = new ShapedGlyph(
                (ushort)infos[i].Codepoint,
                (int)infos[i].Cluster + runStart,
                positions[i].XAdvance * f,
                positions[i].XOffset * f,
                positions[i].YOffset * f);
        }

        string runText = paragraphText.Substring(runStart, runLength);
        return new ShapedRun(font, sizePt, colorArgb, runText, runStart, glyphs);
    }
}
