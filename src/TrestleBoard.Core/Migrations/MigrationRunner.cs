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

        string fileVersion = VersionField.Read(
            manifest, "formatVersion", TboardManifest.CurrentFormatVersion, DamagedMessage("formatVersion"));
        string minReader = VersionField.Read(
            manifest, "minReaderVersion", TboardManifest.CurrentMinReaderVersion, DamagedMessage("minReaderVersion"));

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

    private static string DamagedMessage(string property) =>
        "This newsletter file is damaged — the part of it that says which version of TrestleBoard "
        + $"made it ({property}) cannot be read. If you have an earlier copy of the newsletter, or "
        + "a `.bak` beside it, open that one instead.";

    private static Version Parse(string semver) => Version.Parse(semver);
}
