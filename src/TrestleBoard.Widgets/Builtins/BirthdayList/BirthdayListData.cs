using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.BirthdayList;

/// <summary>Where the printed list came from (PLAN.md §11 M13).</summary>
public static class BirthdayListSource
{
    /// <summary>Somebody typed it. The only value a v1 payload can migrate to.</summary>
    public const string Manual = "manual";

    /// <summary>It was projected from the lodge address book and can be brought up to date.</summary>
    public const string Roster = "roster";

    /// <summary>
    /// A value written by a newer TrestleBoard is neither of these. Staleness and re-sync ask this
    /// rather than comparing to <see cref="Roster"/>, so an unknown source is simply left alone.
    /// </summary>
    public static bool IsKnown(string? source) => source is Manual or Roster;
}

/// <summary>
/// One birthday. Month and day only — no year, no age, by design (the source data is a member
/// roster, and PLAN.md §0 keeps the widget from ever printing anyone's age).
/// </summary>
public sealed class BirthdayEntry
{
    public string Name { get; set; } = "";

    public int Month { get; set; }

    public int Day { get; set; }

    /// <summary>
    /// The address-book id this row was generated from, or null for a row somebody typed (M13).
    /// It is what lets a re-sync recognise the same brother after he has been renamed.
    /// </summary>
    public string? MemberId { get; set; }

    /// <summary>
    /// Set the moment a human touches any field of this row, by the setter lambdas the wizard and
    /// the grid editor share. A manual row is never rewritten and never removed by a re-sync — hand
    /// edits win, always (PLAN.md §11 M13).
    /// </summary>
    public bool IsManual { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>
/// The every-issue narrow birthday column (wiki: birthday-list).
///
/// From M13 the list can be projected from the address book, but it stays a <b>materialised
/// snapshot</b>: <see cref="Entries"/> is the printed truth, stored in the document. A live query
/// would print December's birthdays when March's issue is reopened in December.
/// </summary>
public sealed class BirthdayListData
{
    public string Heading { get; set; } = "Birthdays";

    /// <summary>Stored in whatever order the user typed it — the layouter sorts, never the data
    /// (docs/M7-spec.md §9.2), so re-editing shows the user their own order back.</summary>
    public List<BirthdayEntry> Entries { get; set; } = [];

    /// <summary>Optional line printed after the list, e.g. "Miss anyone? Let us know."</summary>
    public string? ClosingNote { get; set; }

    // ---- provenance (dataVersion 2, M13) -------------------------------------------------------

    /// <summary>One of <see cref="BirthdayListSource"/>. A v1 payload migrates to "manual".</summary>
    public string Source { get; set; } = BirthdayListSource.Manual;

    /// <summary>The issue month this list was generated for; null for a typed list.</summary>
    public int? SourceMonth { get; set; }

    /// <summary>When the projection last ran, as a round-trip UTC string.</summary>
    public string? GeneratedUtc { get; set; }

    /// <summary>
    /// A hash of everything in the address book that contributes to this month's list. It changes
    /// if and only if a contributing field changes, which is how "the address book has changed
    /// since this list was made" is decided without storing a copy of anybody's details.
    /// </summary>
    public string? RosterFingerprint { get; set; }

    /// <summary>
    /// People the user deliberately took off this list. Without it every re-sync would resurrect
    /// the brother they just removed — the single most annoying thing a generated list can do.
    /// </summary>
    public List<string> RemovedMemberIds { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
