using System.Globalization;
using System.Text;

namespace TrestleBoard.Site;

/// <summary>
/// The marks beside the three download choices.
///
/// <para><b>Deliberately not the Apple logo, the Microsoft flag or Tux.</b> Every one of those is
/// somebody's mark: Apple's guidelines restrict its logo, the Windows flag is Microsoft's, and Tux
/// is Larry Ewing's drawing and carries an attribution requirement. This project keeps
/// <c>assets-src/emblems/EMBLEMS-PROVENANCE.txt</c> so it can say plainly that it ships no imported
/// artwork, and the README ends by disclaiming any right in anybody's names or arms. Putting three
/// corporate logos on the download page to save a reader half a second would be the one place the
/// project quietly did the opposite of what it says.</para>
///
/// <para>So these are generic: a four-pane window, a laptop, a penguin. <b>Each sits beside the
/// operating system's name in text, never instead of it</b> (PLAN.md §6: an icon never appears
/// without its label). The mark is there to be found at a glance by somebody scanning the page; the
/// word is what actually tells them which is which, and the word alone would still work.</para>
/// </summary>
internal static class Platforms
{
    private static readonly Dictionary<string, (double W, double H, string[] Fills, string[] Strokes)> Marks =
        new(StringComparer.Ordinal)
        {
            // A window with four panes. Generic joinery, not a flag in perspective.
            ["windows"] = (100, 100, [],
            [
                "M14,18 H86 V82 H14 Z",
                "M50,18 V82",
                "M14,50 H86",
            ]),

            // A laptop, open, seen from the front. What a Mac is, rather than whose it is.
            ["mac"] = (100, 100, ["M8,76 H92 L96,86 H4 Z"],
            [
                "M22,20 H78 V70 H22 Z",
            ]),

            // A penguin: body, beak, feet. Drawn here, and not Tux — Tux is a particular drawing by
            // a particular person, and borrowing it would need his attribution on this page.
            ["linux"] = (100, 100,
            [
                "M50,12 C66,12 74,26 74,42 C74,52 80,62 80,72 C80,84 66,90 50,90 C34,90 20,84 20,72 C20,62 26,52 26,42 C26,26 34,12 50,12 Z",
                "M44,88 C40,94 30,96 26,92 C30,88 38,86 44,88 Z",
                "M56,88 C60,94 70,96 74,92 C70,88 62,86 56,88 Z",
                "M50,34 L58,42 L50,48 L42,42 Z",
            ], []),
        };

    /// <summary>One platform mark as inline SVG, hidden from screen readers.</summary>
    /// <remarks>
    /// Hidden on purpose: the name of the operating system is written beside it as a heading, and an
    /// announced mark would make a screen-reader user hear "image, windows, Windows" on the one page
    /// where clarity decides whether somebody ends up with a working program.
    /// </remarks>
    internal static string Svg(string id)
    {
        if (!Marks.TryGetValue(id, out (double W, double H, string[] Fills, string[] Strokes) mark))
        {
            throw new InvalidOperationException(
                $"No platform mark '{id}'. The download page draws one per operating system it "
                + "offers a build for, so a name that is not here is a mistake rather than a gap.");
        }

        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"os-mark\" viewBox=\"0 0 {mark.W} {mark.H}\" aria-hidden=\"true\" focusable=\"false\">");

        foreach (string d in mark.Fills)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<path d=\"{d}\" fill=\"currentColor\"/>");
        }

        foreach (string d in mark.Strokes)
        {
            svg.Append(CultureInfo.InvariantCulture,
                $"<path d=\"{d}\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"8\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }
}
