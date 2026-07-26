using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.BirthdayList;

/// <summary>
/// One birthday. Month and day only — no year, no age, by design (the source data is a member
/// roster, and PLAN.md §0 keeps the widget from ever printing anyone's age).
/// </summary>
public sealed class BirthdayEntry
{
    public string Name { get; set; } = "";

    public int Month { get; set; }

    public int Day { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>The every-issue narrow birthday column (wiki: birthday-list).</summary>
public sealed class BirthdayListData
{
    public string Heading { get; set; } = "Birthdays";

    /// <summary>Stored in whatever order the user typed it — the layouter sorts, never the data
    /// (docs/M7-spec.md §9.2), so re-editing shows the user their own order back.</summary>
    public List<BirthdayEntry> Entries { get; set; } = [];

    /// <summary>Optional line printed after the list, e.g. "Miss anyone? Let us know."</summary>
    public string? ClosingNote { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
