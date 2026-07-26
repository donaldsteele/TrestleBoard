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
    }
}
