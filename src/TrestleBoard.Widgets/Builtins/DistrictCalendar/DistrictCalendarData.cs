using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.DistrictCalendar;

/// <summary>
/// Which half of the district trestle board this block prints (docs/M7-spec.md §9.4). One widget,
/// not two: the static meeting-days table must survive carry-forward while the dated events are
/// replaced, and an issue frequently shows both.
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumMemberNameAttribute"/> pins each member's wire name explicitly — plain
/// <c>UseStringEnumConverter</c> on the source-generated context (§1.2) writes the C# member name
/// verbatim ("Both"), not the container's camelCase convention, so this is spelled out per member
/// rather than left to a naming policy that does not apply to enums.
/// </remarks>
public enum DistrictCalendarMode
{
    [JsonStringEnumMemberName("meetingDays")]
    MeetingDays,

    [JsonStringEnumMemberName("events")]
    Events,

    [JsonStringEnumMemberName("both")]
    Both,
}

/// <summary>One other lodge in the district and the day it meets on.</summary>
public sealed class DistrictLodgeEntry
{
    public string LodgeName { get; set; } = "";

    /// <summary>"1st Tuesday" — parsed and displayed through <see cref="Core.Text.MeetingRule"/>.
    /// An unparseable rule sorts last and prints exactly as typed.</summary>
    public string MeetingRule { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>
/// One dated district event. Dates are free text ("July 12") and print exactly as typed — they
/// cannot be parsed or sorted reliably, so the wizard offers Move up/down instead.
/// </summary>
public sealed class DistrictEventEntry
{
    public string DateText { get; set; } = "";

    public string? HostText { get; set; }

    public string Description { get; set; } = "";

    public string? TimesText { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>The 22nd-district trestle board (wiki: district-calendar). Mode picks which half prints.</summary>
public sealed class DistrictCalendarData
{
    public string Heading { get; set; } = "22nd District";

    public DistrictCalendarMode Mode { get; set; } = DistrictCalendarMode.MeetingDays;

    /// <summary>Sub-heading printed directly above the meeting-days table.</summary>
    public string LodgesHeading { get; set; } = "Meeting days";

    public List<DistrictLodgeEntry> Lodges { get; set; } = [];

    /// <summary>Sub-heading printed directly above the dated events.</summary>
    public string EventsHeading { get; set; } = "Coming up";

    public List<DistrictEventEntry> Events { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
