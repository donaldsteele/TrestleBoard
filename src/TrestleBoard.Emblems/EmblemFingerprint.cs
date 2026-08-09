using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TrestleBoard.Emblems;

/// <summary>
/// The SHA-256 of an emblem's geometry (PLAN.md gate 22).
///
/// <para>A bundled TTF is hashed in <c>fonts.json</c> so a face cannot be swapped without the
/// manifest noticing. An emblem has no file to hash — the artwork <i>is</i> the source — so what is
/// hashed is the drawing itself: the id, and every path and pen width in order. Change a curve and
/// the hash moves, and <c>EmblemManifestTests</c> fails until somebody records the new artwork in
/// the manifest beside its provenance. That is the same promise the fonts make, kept the only way
/// it can be kept for artwork that lives in code.</para>
///
/// <para>Culture-invariant throughout: a pen width formatted as <c>"58,0"</c> in one country and
/// <c>"58.0"</c> in another would hash differently, and the manifest would fail for everybody whose
/// machine is not the maintainer's.</para>
/// </summary>
public static class EmblemFingerprint
{
    /// <summary>Lower-case hex SHA-256 of the emblem's drawing.</summary>
    public static string Of(Emblem emblem)
    {
        ArgumentNullException.ThrowIfNull(emblem);

        var text = new StringBuilder();
        text.Append(emblem.Id).Append('\n');
        text.Append(emblem.Width.ToString("R", CultureInfo.InvariantCulture)).Append('x')
            .Append(emblem.Height.ToString("R", CultureInfo.InvariantCulture)).Append('\n');

        foreach (EmblemPart part in emblem.Parts)
        {
            text.Append(part.PathData).Append('|')
                .Append(part.StrokeWidth.ToString("R", CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
