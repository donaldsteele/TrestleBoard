using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;

namespace TrestleBoard.Core.Migrations;

/// <summary>
/// One step in the format upgrade chain, operating on raw JSON so old shapes never need
/// live CLR types. Scaffold only in M2 — the chain is empty until the format first changes.
/// </summary>
public interface IDocumentMigration
{
    /// <summary>Format version this migration upgrades from (exact match).</summary>
    string FromVersion { get; }

    /// <summary>Format version produced.</summary>
    string ToVersion { get; }

    void Apply(JsonObject manifest, JsonObject documentBody, JsonObject styles);
}

public sealed class UnsupportedFormatException(string message, Exception? inner = null)
    : Exception(message, inner);

public static class MigrationRunner
{
    private static readonly List<IDocumentMigration> Chain = [];

    /// <summary>
    /// Brings raw file JSON up to <see cref="TboardManifest.CurrentFormatVersion"/>.
    /// Throws <see cref="UnsupportedFormatException"/> (plain-language message, PLAN.md §6)
    /// when the file requires a newer reader than this build.
    /// </summary>
    public static void Run(JsonObject manifest, JsonObject documentBody, JsonObject styles)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(documentBody);
        ArgumentNullException.ThrowIfNull(styles);

        string fileVersion = ReadVersionText(manifest, "formatVersion", TboardManifest.CurrentFormatVersion);
        string minReader = ReadVersionText(manifest, "minReaderVersion", TboardManifest.CurrentMinReaderVersion);

        if (Parse(minReader) > Parse(TboardManifest.CurrentFormatVersion))
        {
            throw new UnsupportedFormatException(
                "This newsletter was saved by a newer version of TrestleBoard. " +
                "Please update TrestleBoard, then open the file again.");
        }

        Version current = Parse(fileVersion);
        Version target = Parse(TboardManifest.CurrentFormatVersion);
        while (current < target)
        {
            IDocumentMigration? step = Chain.Find(m => Parse(m.FromVersion) == current);
            if (step is null)
            {
                throw new UnsupportedFormatException(
                    $"This newsletter uses format version {fileVersion}, which this version of " +
                    "TrestleBoard does not know how to upgrade.");
            }

            step.Apply(manifest, documentBody, styles);
            current = Parse(step.ToVersion);
            manifest["formatVersion"] = step.ToVersion;
        }
    }

    /// <summary>
    /// Reads a version string from the manifest without trusting it to be one.
    ///
    /// <para>A damaged or hand-edited file can carry <c>"1.0.0-beta"</c>, a number, an empty string
    /// or a JSON object where a version belongs. Every one of those used to escape as a raw
    /// <see cref="FormatException"/> or <see cref="InvalidOperationException"/> — so the corrupt
    /// file, the exact case this class's plain-language contract exists for, was the one case that
    /// produced an unhandled-exception dialog instead of a sentence (review §14.2).</para>
    /// </summary>
    private static string ReadVersionText(JsonObject manifest, string property, string fallback)
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
            throw new UnsupportedFormatException(DamagedMessage(property));
        }

        if (string.IsNullOrWhiteSpace(text) || !Version.TryParse(text, out _))
        {
            throw new UnsupportedFormatException(DamagedMessage(property));
        }

        return text;
    }

    private static string DamagedMessage(string property) =>
        "This newsletter file is damaged — the part of it that says which version of TrestleBoard "
        + $"made it ({property}) cannot be read. If you have an earlier copy of the newsletter, or "
        + "a `.bak` beside it, open that one instead.";

    private static Version Parse(string semver) => Version.Parse(semver);
}
