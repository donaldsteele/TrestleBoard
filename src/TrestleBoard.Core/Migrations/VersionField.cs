using System.Text.Json.Nodes;

namespace TrestleBoard.Core.Migrations;

/// <summary>
/// Reads a semver out of a manifest without trusting it to be one.
///
/// <para>A damaged or hand-edited file can carry <c>"1.0.0-beta"</c>, a number, an empty string or a
/// JSON object where a version belongs. Every one of those escapes as a raw
/// <see cref="FormatException"/> or <see cref="InvalidOperationException"/> unless it is caught here
/// — so the corrupt file, the exact case the plain-language contract exists for, would be the one
/// case that produced an unhandled-exception dialog instead of a sentence (review §14.2).</para>
///
/// <para>Shared by the newsletter format and the successor pack (M64) rather than written twice.
/// The <b>message</b> is the caller's, because "this newsletter file is damaged" is the wrong thing
/// to tell somebody who opened a pack.</para>
/// </summary>
internal static class VersionField
{
    internal static string Read(JsonObject manifest, string property, string fallback, string damagedMessage)
    {
        if (manifest[property] is not { } node)
        {
            return fallback;
        }

        string? text;
        try
        {
            text = node.GetValue<string>();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            throw new UnsupportedFormatException(damagedMessage);
        }

        if (string.IsNullOrWhiteSpace(text) || !Version.TryParse(text, out _))
        {
            throw new UnsupportedFormatException(damagedMessage);
        }

        return text;
    }
}
