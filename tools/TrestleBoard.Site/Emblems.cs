using System.Globalization;
using System.Text;
using TrestleBoard.Emblems;

namespace TrestleBoard.Site;

/// <summary>
/// The craft's emblems, as SVG, from the same geometry the application draws onto a page.
///
/// <para><b>Nothing here is clip art and nothing here is a second drawing.</b>
/// <see cref="EmblemLibrary"/> holds nineteen emblems as path data in source — no SVG files, no
/// PNGs — because that makes them diffable, hashable like a font face, and drawable at any size with
/// no new dependency. A committee inserting the square and compasses onto a cover gets these exact
/// paths. So does this site. If the emblem on the masthead ever differs from the one in the program,
/// it will be because somebody changed the program.</para>
///
/// <para>It is the same argument the command reference makes about <c>ActionCatalog</c>, applied to
/// the other thing a site about a Masonic newsletter editor has to get right: the mark itself.</para>
/// </summary>
internal static class Emblems
{
    /// <summary>
    /// One emblem as an inline <c>&lt;svg&gt;</c>, painted in <c>currentColor</c>.
    ///
    /// <para><b>currentColor rather than a fill</b>, so an emblem takes the colour of whatever it
    /// stands in — navy on the masthead, the muted rule colour on a divider, and the light accent
    /// when the reader's system is dark. That is the site's half of "colour is a token, never a
    /// literal": there is no hex value here at all.</para>
    /// </summary>
    /// <param name="id">An id from <see cref="EmblemLibrary"/>.</param>
    /// <param name="cssClass">The class the stylesheet sizes it by.</param>
    /// <param name="title">
    /// What a screen reader should say, or null to hide the emblem from one entirely.
    ///
    /// <para>Null is the right answer more often than it looks. On the masthead the emblem sits
    /// beside the word "TrestleBoard", and an emblem announced there would make every page of this
    /// site begin "square and compasses, TrestleBoard". PLAN.md §6's rule is that an icon never
    /// appears without its label — it does not say the label has to be read twice.</para>
    /// </param>
    internal static string Svg(string id, string cssClass, string? title = null)
    {
        Emblem emblem = EmblemLibrary.Find(id)
            ?? throw new InvalidOperationException(
                $"No emblem '{id}' on the shelf. The site draws the application's own emblems, so a "
                + "name it does not know is a mistake here rather than a missing file.");

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg class=\"{cssClass}\" viewBox=\"0 0 ");
        svg.Append(CultureInfo.InvariantCulture, $"{Number(emblem.Width)} {Number(emblem.Height)}\"");

        svg.Append(title is null
            ? " aria-hidden=\"true\" focusable=\"false\">"
            : $" role=\"img\" focusable=\"false\"><title>{Html.Escape(title)}</title>");

        foreach (EmblemPart part in emblem.Parts)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{Html.Escape(part.PathData)}\"");

            // Zero means fill; anything else is a pen width. Most of the craft's tools are drawn
            // rather than filled — a plumb rule is a line and a weight — so both cases are real.
            svg.Append(part.StrokeWidth > 0
                ? $" fill=\"none\" stroke=\"currentColor\" stroke-width=\"{Number(part.StrokeWidth)}\""
                    + " stroke-linecap=\"round\" stroke-linejoin=\"round\""
                : " fill=\"currentColor\"");

            svg.Append("/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    /// <summary>
    /// The divider between sections of a page: the shelf's own <c>rule-diamond</c>.
    ///
    /// <para>A trestle board is dressed with rules like this one, which is why it is on the shelf at
    /// all — and why it is the right thing to separate sections of a page about trestle boards with,
    /// rather than a border-top. It is decoration, so it is hidden from screen readers: the heading
    /// after it already says a new section has begun.</para>
    /// </summary>
    internal static string Divider() =>
        $"<div class=\"ornament\">{Svg("rule-diamond", "ornament-rule")}</div>";

    /// <summary>
    /// Invariant, and trimmed of a trailing ".0" — the paths in the library are written by hand and
    /// a viewBox reading "1000.0 1000.0" would be this generator's fingerprint on somebody's
    /// geometry.
    /// </summary>
    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}
