using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Container;

/// <summary>
/// One store inside a successor pack, as the manifest records it (PLAN.md §11 M64).
/// </summary>
/// <param name="Id">Which store this is — see <see cref="SuccessorPackParts"/>.</param>
/// <param name="FileCount">How many files it contributed. Used to spot an empty part, not shown.</param>
/// <param name="Summary">
/// What is in it, in plain language and in the packing app's own words — "84 people", "6 templates".
/// Written at packing time so the receiving computer can describe the pack without opening and
/// parsing every store in it, and so a pack written by a newer TrestleBoard still describes itself.
///
/// <para><b>Never a name.</b> This is the one free-text field in the manifest and the pack is the
/// most concentrated personal data the app produces (§0 rule 7), so it carries counts only.</para>
/// </param>
public sealed record SuccessorPackPart(string Id, int FileCount, string Summary);

/// <summary>
/// <c>manifest.json</c> of a <c>.tbpack</c>: format identity, version gates, and what is inside
/// (PLAN.md §11 M64). Deliberately the same shape as <see cref="TboardManifest"/> — the pack is
/// versioned and forward-migratable exactly like the document format, because it will outlive the
/// committee that wrote it and be opened by an app nobody has written yet.
/// </summary>
public sealed class SuccessorPackManifest
{
    /// <summary>Not "trestleboard": a pack is not a newsletter, and neither may open the other.</summary>
    public const string CurrentFormatName = "trestleboard-pack";

    public const string CurrentFormatVersion = "1.0.0";

    public const string CurrentMinReaderVersion = "1.0.0";

    public string FormatName { get; set; } = CurrentFormatName;

    /// <summary>Semver of the pack format this file was written in.</summary>
    public string FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>Version of the app that wrote it (informational).</summary>
    public string GeneratorVersion { get; set; } = "0.0.0";

    /// <summary>Oldest pack format a reader must understand to open this file.</summary>
    public string MinReaderVersion { get; set; } = CurrentMinReaderVersion;

    /// <summary>
    /// The day it was packed, so the receiving side can say "packed on 9 August 2026". A successor
    /// is often handed two or three of these and has to tell which is the recent one.
    /// </summary>
    public DateTimeOffset WrittenOn { get; set; }

    /// <summary>What is in the pack, in <see cref="SuccessorPackParts.InOrder"/> order.</summary>
    public List<SuccessorPackPart> Parts { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
