using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.OfficersTable;

/// <summary>Where the printed table came from (PLAN.md §11 M19, M13's idiom exactly).</summary>
public static class OfficersTableSource
{
    /// <summary>Somebody typed it, or picked the names one screen at a time. The only value a v1
    /// payload can migrate to.</summary>
    public const string Manual = "manual";

    /// <summary>It was filled in from the lodge address book and can be brought up to date.</summary>
    public const string Roster = "roster";

    /// <summary>
    /// A value written by a newer TrestleBoard is neither of these. Staleness and re-sync ask this
    /// rather than comparing to <see cref="Roster"/>, so an unknown source is simply left alone.
    /// </summary>
    public static bool IsKnown(string? source) => source is Manual or Roster;
}

/// <summary>
/// One office. A blank <see cref="Name"/> is a real, printed vacancy — never a row to collapse
/// (docs/M7-spec.md §9.1). Phone is optional; some offices carry none.
/// </summary>
public sealed class OfficerEntry
{
    public string Position { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Phone { get; set; }

    // ---- provenance (dataVersion 2, M19) -------------------------------------------------------

    /// <summary>
    /// The address-book id this row was filled in from, or null for a row somebody typed (M19).
    /// It is what lets a re-sync recognise the same brother after he has been renamed.
    /// </summary>
    public string? MemberId { get; set; }

    /// <summary>
    /// Set the moment a human touches this row's name or phone, by the setter lambdas the wizard
    /// and the grid editor share. A hand-edited row is never rewritten by a re-sync — hand edits
    /// win, always (PLAN.md §11 M13, carried over verbatim to M19).
    /// </summary>
    public bool IsManual { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>The every-issue officers table (wiki: officers-table).</summary>
public sealed class OfficersTableData
{
    /// <summary>The twelve offices, in the order they are printed. Never reordered, never trimmed.</summary>
    public static readonly IReadOnlyList<string> StandardPositions =
    [
        "Worshipful Master",
        "Senior Warden",
        "Junior Warden",
        "Senior Deacon",
        "Junior Deacon",
        "Senior Steward",
        "Junior Steward",
        "Treasurer",
        "Secretary",
        "Chaplain",
        "Tiler",
        "Marshall",
    ];

    public string Heading { get; set; } = "Lodge Officers";

    public List<OfficerEntry> Officers { get; set; } = [];

    /// <summary>Printed in the name column when an office is unfilled.</summary>
    public string VacantText { get; set; } = "(vacant)";

    // ---- provenance (dataVersion 2, M19) -------------------------------------------------------

    /// <summary>One of <see cref="OfficersTableSource"/>. A v1 payload migrates to "manual".</summary>
    public string Source { get; set; } = OfficersTableSource.Manual;

    /// <summary>When the address book was last drawn on, as a round-trip UTC string.</summary>
    public string? GeneratedUtc { get; set; }

    /// <summary>
    /// A hash of everything in the address book that contributes to this table — who holds each of
    /// the twelve offices, what he is called and his phone number. It changes if and only if a
    /// contributing field changes, which is how "the address book has changed since this table was
    /// filled in" is decided without storing a copy of anybody's details.
    /// </summary>
    public string? RosterFingerprint { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
