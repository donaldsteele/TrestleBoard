using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.CommitteeList;

/// <summary>
/// One committee: its name, its members (entered one per line in the wizard), and an optional
/// free-text tail for cases like "and team" (docs/M7-spec.md §9.3). A committee with no members is
/// real — the roster is thin some years — and prints the name and colon alone.
/// </summary>
public sealed class CommitteeEntry
{
    public string Name { get; set; } = "";

    public List<string> Members { get; set; } = [];

    /// <summary>Free-text tail appended after the members, e.g. "and team".</summary>
    public string? Note { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>The standing-committees list (wiki: committee-list).</summary>
public sealed class CommitteeListData
{
    public string Heading { get; set; } = "Committees";

    public string? NoticeText { get; set; } =
        "Please notify the Worshipful Master to be added or removed.";

    public List<CommitteeEntry> Committees { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
