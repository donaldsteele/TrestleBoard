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

/// <summary>
/// 1.0.0 → 1.1.0 (M72): nothing to do, and that is the point of writing it down.
///
/// <para>1.1.0 adds one thing — a <c>"type": "vector"</c> block — and adds it <b>additively</b>. A
/// newsletter written before M72 contains no such block, every other part of the file means exactly
/// what it always meant, and there is nothing to rewrite. In particular a baked emblem stays the
/// ordinary <c>ImageFrame</c> it has always been: the emblem's id was deliberately not stored with
/// the frame (docs/M65-spec.md §9), so recognising one would take pixel matching, and this step
/// would then be silently rewriting the user's document on open. It does not.</para>
///
/// <para>The step exists because <see cref="MigrationRunner"/> refuses to open a file it has no
/// route from — bumping <see cref="TboardManifest.CurrentFormatVersion"/> without registering this
/// would throw <see cref="UnsupportedFormatException"/> on every newsletter in existence. This is
/// the chain's first live step since it was scaffolded in M2.</para>
/// </summary>
public sealed class VectorBlocksMigration : IDocumentMigration
{
    public string FromVersion => TboardManifest.BaseFormatVersion;

    public string ToVersion => TboardManifest.VectorFormatVersion;

    public void Apply(JsonObject manifest, JsonObject documentBody, JsonObject styles)
    {
        // Deliberately empty. See the type's doc comment: the change is additive, so a 1.0.0
        // document IS a valid 1.1.0 document, and touching it would be the bug.
    }
}

public static class MigrationRunner
{
    private static readonly List<IDocumentMigration> Chain = [new VectorBlocksMigration()];

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
