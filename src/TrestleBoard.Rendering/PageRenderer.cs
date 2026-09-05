using SkiaSharp;
using TrestleBoard.Layout;

namespace TrestleBoard.Rendering;

public sealed record PageRenderOptions
{
    public uint BackgroundArgb { get; init; } = 0xFFFFFFFF;

    /// <summary>Dev overlay only; must stay off for snapshots.</summary>
    public bool DrawExclusionDebug { get; init; }
}

/// <summary>
/// Walks a LayoutResult and draws to ANY SKCanvas — screen surface or PDF page, identically.
/// Zero re-shaping: glyph ids and positions come straight from the layout output.
/// </summary>
public sealed class PageRenderer
{
    private readonly PageRenderOptions _options;

    public PageRenderer(PageRenderOptions? options = null)
    {
        _options = options ?? new PageRenderOptions();
    }

    public void Render(SKCanvas canvas, LayoutResult result)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(result);
        canvas.DrawColor(new SKColor(_options.BackgroundArgb));
        foreach (FrameLayout frame in result.Frames)
        {
            RenderFrame(canvas, frame);
        }
    }

    public static void RenderFrame(SKCanvas canvas, FrameLayout frame)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(frame);
        foreach (LineBox line in frame.Lines)
        {
            foreach (LineSegment segment in line.Segments)
            {
                foreach (PositionedGlyphRun run in segment.Runs)
                {
                    DrawRun(canvas, run);
                }
            }
        }
    }

    /// <summary>
    /// Draws one positioned run. Public since M7: widget draw lists carry the same runs and must
    /// rasterise through exactly this path, or widget text and body text would not match.
    /// </summary>
    public static void DrawRun(SKCanvas canvas, PositionedGlyphRun run)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(run);
        if (run.Glyphs.Count == 0)
        {
            return;
        }

        // Deterministic font settings (docs/M1-spec.md §5): antialias, no hinting, subpixel.
        using var font = new SKFont(run.Font.Typeface, run.SizePt)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
            Subpixel = true,
        };
        using var builder = new SKTextBlobBuilder();
        SKPositionedRunBuffer buffer = builder.AllocatePositionedRun(font, run.Glyphs.Count);
        Span<ushort> glyphs = buffer.Glyphs;
        Span<SKPoint> positions = buffer.Positions;
        for (int i = 0; i < run.Glyphs.Count; i++)
        {
            glyphs[i] = run.Glyphs[i];
            positions[i] = run.GlyphOffsets[i];
        }

        using SKTextBlob? blob = builder.Build();
        if (blob is null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(run.ColorArgb),
        };
        canvas.DrawText(blob, run.OriginX, run.BaselineY, paint);

        if (run.Underline)
        {
            DrawUnderline(canvas, run, font, paint);
        }
    }

    /// <summary>
    /// The line under an underlined run (PLAN.md §11 M86, delivered M102).
    ///
    /// <para><b>Position and thickness come from the face, never from a number chosen here.</b>
    /// Skia reads them out of the font's <c>post</c> table, which is where the type designer put
    /// them: an underline under Source Serif sits at that face's own depth, and changing a style's
    /// font moves it with no arithmetic anywhere in this app. A hand-picked offset would be wrong
    /// for every face except the one it was eyeballed against, and wrong at every size but one.</para>
    ///
    /// <para><b>The fallbacks are proportions of the type size, not constants.</b> A face may
    /// simply not carry the metrics — several of the emblem and symbol faces do not — and a fixed
    /// 1pt line under 30pt type reads as a mistake. One fourteenth of the size below the baseline
    /// and one eighteenth thick are the ratios the common text faces use anyway.</para>
    ///
    /// <para>Drawn in the run's own colour, because an underline is part of the writing rather than
    /// a mark on top of it — the same paint, so a red underlined heading is red underneath too.</para>
    /// </summary>
    /// <param name="facePosition">
    /// The face's own underline offset from its <c>post</c> table, or null when it carries none.
    /// Passed in rather than read from an <c>SKFontMetrics</c> here because that type's fields are
    /// read-only, and a rule about what to do when a face has no metrics cannot be tested if the
    /// only way to reach it is to find a real font that lacks them.
    /// </param>
    internal static SKRect UnderlineRectFor(
        float? facePosition,
        float? faceThickness,
        float sizePt,
        float originX,
        float baselineY,
        float advanceWidthPt)
    {
        // The fallbacks are proportions of the type size, never constants: a face may carry no
        // metrics at all — several of the symbol faces do not — and a fixed 1pt line under 30pt
        // type reads as a mistake. These ratios are what the common text faces use anyway.
        float position = facePosition ?? (sizePt / 14f);
        float thickness = faceThickness ?? (sizePt / 18f);

        // Skia reports the position as a DOWNWARD offset from the baseline. A face that stores it
        // the other way round would otherwise draw a strike-through, so the sign is taken here
        // rather than trusted.
        float top = baselineY + Math.Abs(position);

        return SKRect.Create(originX, top, advanceWidthPt, Math.Max(thickness, 0.1f));
    }

    private static void DrawUnderline(SKCanvas canvas, PositionedGlyphRun run, SKFont font, SKPaint paint)
    {
        using var line = new SKPaint
        {
            // The run's own colour: an underline is part of the writing rather than a mark on top
            // of it, so a red underlined heading is red underneath too.
            Color = paint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        canvas.DrawRect(
            UnderlineRectFor(
                font.Metrics.UnderlinePosition,
                font.Metrics.UnderlineThickness,
                run.SizePt,
                run.OriginX,
                run.BaselineY,
                run.AdvanceWidthPt),
            line);
    }
}
