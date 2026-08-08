using System;
using System.Linq;
using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Shaping;

namespace TrestleBoard.Rendering;

/// <summary>
/// The diagonal across a draft copy (PLAN.md §11 M53).
///
/// <para>The Master reads the newsletter before it goes out, and until now the review copy and the
/// final copy were the same PDF with different filenames — which means the only thing standing
/// between a draft and sixty inboxes was somebody remembering which was which. A page that says
/// <i>DRAFT — not for sending</i> across it cannot be sent by mistake.</para>
///
/// <para><b>Drawn by the same renderer as everything else</b>, through HarfBuzz and the bundled
/// faces, with the deterministic font settings <c>PageRenderer</c> uses. It is not chrome: it is
/// meant to print, and it is the one thing in this project that is deliberately added to the
/// exported page. Everything about it therefore has to be as reproducible as the newsletter under
/// it — same text, same face, same size, same place, on all four RIDs.</para>
/// </summary>
public static class WatermarkRenderer
{
    /// <summary>What a draft copy says. Two claims: what it is, and what not to do with it.</summary>
    public const string DraftText = "DRAFT — not for sending";

    /// <summary>
    /// Light enough to read the newsletter through, dark enough that nobody can miss it. Grey
    /// rather than red: this is not an error, and a red page reads as one.
    /// </summary>
    private const uint InkArgb = 0x28000000;

    /// <summary>The angle the words run at, bottom-left to top-right.</summary>
    private const float DegreesAntiClockwise = -30f;

    /// <summary>How much of the page's diagonal the words are asked to span.</summary>
    private const float ShareOfTheDiagonal = 0.86f;

    /// <summary>
    /// Draws the diagonal over a page that has already been rendered. The canvas is in page points
    /// with no transform, exactly as the exporter hands it over.
    /// </summary>
    public static void Draw(SKCanvas canvas, SizePt page, FontStore fonts, string text = DraftText)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        if (page.Width <= 0f || page.Height <= 0f)
        {
            return;
        }

        // Sans, because a display face at this size turns the two words into a pattern; bold,
        // because it is being read through a page of body text.
        var key = new FontKey(BundledFonts.SansFamily, FontWeight.Bold, FontStyleSlant.Normal);
        if (!fonts.TryResolve(key, out ResolvedFont? font) || font is null)
        {
            // Bundled fonts only, and a store without the sans family is a broken build rather than
            // a user's problem. A draft without its diagonal would be a draft that can be sent by
            // mistake, so say so rather than exporting one quietly.
            throw new InvalidOperationException(
                $"The draft watermark needs the bundled face {BundledFonts.SansFamily} Bold.");
        }

        // Measure once at a nominal size, then scale — one shaping pass, and the scale factor is
        // exact arithmetic rather than a search that could land differently on another machine.
        const float NominalPt = 100f;
        ShapedRun run = HarfBuzzShaper.Shape(
            font, NominalPt, InkArgb, text, 0, text.Length, new ShapeOptions(true, true));
        float nominalWidth = run.Glyphs.Sum(g => g.XAdvancePt);
        if (nominalWidth <= 0f)
        {
            return;
        }

        float diagonal = MathF.Sqrt((page.Width * page.Width) + (page.Height * page.Height));
        float sizePt = NominalPt * (diagonal * ShareOfTheDiagonal / nominalWidth);

        ShapedRun sized = HarfBuzzShaper.Shape(
            font, sizePt, InkArgb, text, 0, text.Length, new ShapeOptions(true, true));
        float width = sized.Glyphs.Sum(g => g.XAdvancePt);
        FontMetrics metrics = font.GetMetrics(sizePt);

        canvas.Save();
        try
        {
            canvas.Translate(page.Width / 2f, page.Height / 2f);
            canvas.RotateDegrees(DegreesAntiClockwise);

            // Centred on the middle of the page: half the run to the left, and the baseline set so
            // the letters straddle the centre line rather than hanging below it.
            float originX = -width / 2f;
            float baselineY = (metrics.AscentPt - metrics.DescentPt) / 2f;
            DrawRun(canvas, sized, originX, baselineY);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// The same deterministic font settings <c>PageRenderer</c> and <c>FontPreviewRenderer</c> use
    /// (docs/M1-spec.md §5). This one prints, so it matters more here than in either of them.
    /// </summary>
    private static void DrawRun(SKCanvas canvas, ShapedRun run, float originX, float baselineY)
    {
        using var skFont = new SKFont(run.Font.Typeface, run.SizePt)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
            Subpixel = true,
        };
        using var paint = new SKPaint { Color = new SKColor(run.ColorArgb), IsAntialias = true };
        using var builder = new SKTextBlobBuilder();

        SKPositionedRunBuffer buffer = builder.AllocatePositionedRun(skFont, run.Glyphs.Count);
        Span<ushort> glyphs = buffer.Glyphs;
        Span<SKPoint> positions = buffer.Positions;
        float pen = originX;
        for (int i = 0; i < run.Glyphs.Count; i++)
        {
            ShapedGlyph glyph = run.Glyphs[i];
            glyphs[i] = glyph.GlyphId;
            positions[i] = new SKPoint(pen + glyph.XOffsetPt, baselineY - glyph.YOffsetPt);
            pen += glyph.XAdvancePt;
        }

        using SKTextBlob? blob = builder.Build();
        if (blob is not null)
        {
            canvas.DrawText(blob, 0f, 0f, paint);
        }
    }
}
