using System;
using System.Globalization;
using System.Linq;
using SkiaSharp;
using TrestleBoard.Core.Model;
using TrestleBoard.Layout.Fonts;
using TrestleBoard.Layout.Shaping;

namespace TrestleBoard.Rendering;

/// <summary>
/// The line along the bottom of every page (PLAN.md §11 M78).
///
/// <para><b>Every issue from the old tool had one and this one did not.</b> "Indian Land Lodge 414 ·
/// September 2026 · page 3 of 6" is what a reader who prints the newsletter double-sided needs, and
/// every fact in it is already known to the app — the lodge name and the issue date have been in
/// the document since M2 and M75, and the page numbers are counting.</para>
///
/// <para><b>One wording, editable nowhere.</b> There is no footer dialog, no field for the words and
/// no way to put something else there. A committee that could edit the footer would be a committee
/// that could delete half of it, and what it says is not a preference — it is the three facts a
/// reader needs to know which page of which issue they are holding.</para>
///
/// <para><b>Inside the margin, and that is load-bearing.</b> M47 draws the margin on the canvas and
/// tells the user it is the edge to keep inside. A footer outside it would make the app's own
/// furniture the one thing that broke the app's own rule, which is exactly the kind of quiet
/// contradiction this audience reads as the program being unreliable.</para>
///
/// <para><b>Not a block.</b> It is drawn, never placed. A footer that existed as a frame would be
/// clickable, and a frame the user can click but cannot delete or move is a support call — so it
/// is not on the page at all in the sense the canvas means, and nothing can select it.</para>
/// </summary>
public static class PageFooterRenderer
{
    /// <summary>What separates the three facts. A middle dot, not a hyphen: a hyphen inside a date
    /// range reads as subtraction and this line already carries numbers.</summary>
    public const string Separator = " · ";

    /// <summary>Small, and no smaller. This has to be readable in print by the audience §6 is about,
    /// and 9pt is the floor at which a serif face survives a photocopier.</summary>
    private const float SizePt = 9f;

    /// <summary>Grey rather than black: it is furniture, and it must not compete with the writing.</summary>
    private const uint InkArgb = 0xFF555555;

    /// <summary>How far above the bottom margin the baseline sits.</summary>
    private const float AboveTheMarginPt = 4f;

    /// <summary>
    /// The words, built from what the document already knows.
    ///
    /// <para>A pure function of four facts, so what the footer SAYS can be asserted without a
    /// canvas — which is the half of this that a plain-language test cares about.</para>
    /// </summary>
    /// <param name="lodgeName">Blank on a newsletter nobody has named; the clause is then left out.</param>
    /// <param name="issue">The issue's month and year, or null when nobody has said which issue it is.</param>
    /// <param name="pageNumber">One-based.</param>
    /// <param name="pageCount">How many pages the newsletter has.</param>
    public static string Compose(string? lodgeName, DateOnly? issue, int pageNumber, int pageCount)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(lodgeName))
        {
            parts.Add(lodgeName.Trim());
        }

        if (issue is { } when)
        {
            parts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(when.Month)} {when.Year}"));
        }

        // Always. A newsletter with no name and no date still has pages, and "page 3 of 6" alone is
        // the fact the reader is most likely to be looking for.
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"page {pageNumber} of {pageCount}"));
        return string.Join(Separator, parts);
    }

    /// <summary>
    /// Draws the line on a page that has already been rendered. The canvas is in page points with
    /// no transform, exactly as the exporter and the editor canvas both hand it over — which is why
    /// the footer on screen and the footer in the PDF cannot drift apart.
    /// </summary>
    /// <param name="marginBottomPt">
    /// Where the text area ends. The footer sits just above this line, inside the margin M47 draws.
    /// </param>
    public static void Draw(
        SKCanvas canvas, FontStore fonts, string text, float pageWidthPt, float marginBottomPt)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(fonts);

        if (string.IsNullOrWhiteSpace(text) || pageWidthPt <= 0f)
        {
            return;
        }

        var key = new FontKey(BundledFonts.SansFamily, FontWeight.Regular, FontStyleSlant.Normal);
        if (!fonts.TryResolve(key, out ResolvedFont? font) || font is null)
        {
            // Bundled fonts only (PLAN.md §1). A store without the sans family is a broken build,
            // and a missing footer is a smaller failure than a wrong one — so this says nothing
            // rather than reaching for a system face and printing a page nobody can reproduce.
            return;
        }

        ShapedRun run = HarfBuzzShaper.Shape(
            font, SizePt, InkArgb, text, 0, text.Length, new ShapeOptions(true, true));
        float width = run.Glyphs.Sum(g => g.XAdvancePt);
        if (width <= 0f)
        {
            return;
        }

        // Centred on the page rather than on the text area: the two are the same on every master
        // this app ships, and where they are not, a footer centred on the paper is what a reader
        // expects to find at the bottom of a sheet.
        float originX = (pageWidthPt - width) / 2f;
        float baselineY = marginBottomPt - AboveTheMarginPt;
        DrawRun(canvas, run, originX, baselineY);
    }

    /// <summary>
    /// The same deterministic font settings <c>PageRenderer</c> and <c>WatermarkRenderer</c> use
    /// (docs/M1-spec.md §5). This prints, so byte-identical output across the three operating
    /// systems is the promise being kept here.
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
            canvas.DrawText(blob, 0, 0, paint);
        }
    }
}
