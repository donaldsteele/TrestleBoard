using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;

namespace TrestleBoard.Core.Migrations;

/// <summary>
/// One step in the successor-pack upgrade chain, on raw JSON so an old manifest never needs a live
/// CLR type (PLAN.md §11 M64). Empty at M64 — the chain exists from the first version, because a
/// format that gains its migration story later has already shipped files nobody can upgrade.
/// </summary>
public interface IPackMigration
{
    /// <summary>Pack format version this upgrades from (exact match).</summary>
    string FromVersion { get; }

    /// <summary>Pack format version produced.</summary>
    string ToVersion { get; }

    /// <summary>
    /// Rewrites the manifest in place. Only the manifest: the store files inside a pack are opaque
    /// bytes owned by the store that wrote them, and each of those has its own version and its own
    /// upgrade path on the way back in.
    /// </summary>
    void Apply(JsonObject manifest);
}

public static class SuccessorPackMigrations
{
    private static readonly List<IPackMigration> Chain = [];

    /// <summary>
    /// Brings a pack manifest up to <see cref="SuccessorPackManifest.CurrentFormatVersion"/>, or
    /// refuses in plain language (PLAN.md §6).
    ///
    /// <para>The refusals say what to <b>do</b>, not what went wrong. Somebody opening a pack is on
    /// a new computer, on their first day of a job they did not ask for, holding a file they cannot
    /// inspect — "update TrestleBoard, then open it again" is the entire useful content.</para>
    /// </summary>
    public static void Run(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        string packVersion = VersionField.Read(
            manifest, "formatVersion", SuccessorPackManifest.CurrentFormatVersion, DamagedMessage("formatVersion"));
        string minReader = VersionField.Read(
            manifest, "minReaderVersion", SuccessorPackManifest.CurrentMinReaderVersion, DamagedMessage("minReaderVersion"));

        if (Parse(minReader) > Parse(SuccessorPackManifest.CurrentFormatVersion))
        {
            throw new UnsupportedFormatException(
                "This pack was made by a newer version of TrestleBoard. Please update TrestleBoard, "
                + "then bring the pack in again.");
        }

        Version current = Parse(packVersion);
        Version target = Parse(SuccessorPackManifest.CurrentFormatVersion);
        while (current < target)
        {
            IPackMigration? step = Chain.Find(m => Parse(m.FromVersion) == current);
            if (step is null)
            {
                throw new UnsupportedFormatException(
                    $"This pack uses version {packVersion}, which this version of TrestleBoard does "
                    + "not know how to bring up to date.");
            }

            step.Apply(manifest);
            current = Parse(step.ToVersion);
            manifest["formatVersion"] = step.ToVersion;
        }
    }

    private static string DamagedMessage(string property) =>
        "This pack is damaged — the part of it that says which version of TrestleBoard made it "
        + $"({property}) cannot be read. If the person who sent it still has TrestleBoard, ask them "
        + "to pack it up again.";

    private static Version Parse(string semver) => Version.Parse(semver);
}
