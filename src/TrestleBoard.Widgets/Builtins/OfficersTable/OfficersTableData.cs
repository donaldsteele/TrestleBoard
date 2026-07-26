using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.OfficersTable;

/// <summary>
/// One office. A blank <see cref="Name"/> is a real, printed vacancy — never a row to collapse
/// (docs/M7-spec.md §9.1). Phone is optional; some offices carry none.
/// </summary>
public sealed class OfficerEntry
{
    public string Position { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Phone { get; set; }

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

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
