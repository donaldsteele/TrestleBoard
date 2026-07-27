using SkiaSharp;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Shaping;

namespace TrestleBoard.Rendering;

/// <summary>One line of preview text, rasterised at a fixed size in a specific bundled face.</summary>
public readonly record struct FontPreview(byte[] Png, int WidthPx, int HeightPx);

/// <summary>
/// Rasterises a line of text in a bundled face, for the font picker (PLAN.md M14).
/// <para>
/// Previews are rendered by our own engine, not by Avalonia. Avalonia's
/// <c>EmbeddedFontCollection</c> wants <c>avares://</c> resources, but the fonts are
/// <c>EmbeddedResource</c>s in Layout — supporting both would ship every byte twice. Going
/// through HarfBuzz and Skia costs nothing extra and shows the exact face that will print,
/// which is this project's whole thesis.
/// </para>
/// <para>
/// Theme colours arrive as PARAMETERS, never read from a static: the caller caches previews per
/// key and drops the cache when the theme changes.
/// </para>
/// </summary>
public static class FontPreviewRenderer
{
    /// <summary>Padding around the text, in points, so a descender never touches the edge.</summary>
    private const float PaddingPt = 4f;

    public static FontPreview Render(
        FontStore store,
        FontKey key,
        float sizePt,
        string text,
        uint foregroundArgb,
        uint backgroundArgb,
        float scale = 1f,
        float? maxWidthPt = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizePt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);

        ResolvedFont font = store.Resolve(key);
        ShapedRun run = HarfBuzzShaper.Shape(
            font, sizePt, foregroundArgb, text, 0, text.Length, new ShapeOptions(true, true));

        FontMetrics metrics = font.GetMetrics(sizePt);
        float advance = run.Glyphs.Sum(g => g.XAdvancePt);
        float widthPt = MathF.Max(1f, advance) + (PaddingPt * 2f);
        if (maxWidthPt is > 0f)
        {
            widthPt = MathF.Min(widthPt, maxWidthPt.Value);
        }

        float heightPt = metrics.AscentPt + metrics.DescentPt + (PaddingPt * 2f);
        int widthPx = (int)MathF.Ceiling(widthPt * scale);
        int heightPx = (int)MathF.Ceiling(heightPt * scale);

        var info = new SKImageInfo(
            widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using SKSurface surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create CPU raster surface for a font preview.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(new SKColor(backgroundArgb));
        canvas.Scale(scale);

        DrawRun(canvas, run, PaddingPt, PaddingPt + metrics.AscentPt);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, quality: 100)
            ?? throw new InvalidOperationException("PNG encode failed for a font preview.");
        return new FontPreview(data.ToArray(), widthPx, heightPx);
    }

    /// <summary>
    /// The first <paramref name="maxChars"/> characters of the user's own text, tidied onto one
    /// line. "How it will look" answers "will this wreck my newsletter?" far better than a
    /// pangram does — but only if it is the reader's own words.
    /// </summary>
    public static string SampleFrom(string? documentText, string fallback, int maxChars = 40)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return fallback;
        }

        string flat = string.Join(' ', documentText.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (flat.Length == 0)
        {
            return fallback;
        }

        if (flat.Length <= maxChars)
        {
            return flat;
        }

        int cut = flat.LastIndexOf(' ', Math.Min(maxChars, flat.Length - 1));
        return (cut > maxChars / 2 ? flat[..cut] : flat[..maxChars]) + "…";
    }

    private static void DrawRun(SKCanvas canvas, ShapedRun run, float originX, float baselineY)
    {
        // The same deterministic font settings PageRenderer uses (docs/M1-spec.md §5), so the
        // preview is the printed shape and not an approximation of it.
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
