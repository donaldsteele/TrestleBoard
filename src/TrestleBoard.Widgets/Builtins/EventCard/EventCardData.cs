using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.EventCard;

/// <summary>
/// A boxed announcement for one event — the "Lodge picnic", "Special communication" notice that runs
/// as its own card rather than inside the body prose (wiki: event-card, docs/M7-spec.md §9.5).
/// </summary>
public sealed class EventCardData
{
    /// <summary>Centred at the top of the card, e.g. "Placeholder Lodge Picnic".</summary>
    public string Title { get; set; } = "";

    /// <summary>e.g. "Saturday, 6:00 pm" — free text, prints exactly as typed. Optional: the date may
    /// not be settled yet when the box goes in.</summary>
    public string? WhenText { get; set; }

    /// <summary>e.g. "Sample Lodge 000 hall" — free text, prints exactly as typed. Optional.</summary>
    public string? WhereText { get; set; }

    /// <summary>The announcement itself, wrapped as ordinary left-aligned prose.</summary>
    public string BodyText { get; set; } = "";

    /// <summary>Whether the four-rule frame prints around the card (docs/M7-spec.md §9.5).</summary>
    public bool ShowBorder { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
