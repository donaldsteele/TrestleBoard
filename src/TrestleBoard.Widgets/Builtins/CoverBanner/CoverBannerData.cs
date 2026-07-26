using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.CoverBanner;

/// <summary>
/// The front-page banner: lodge name, the standing "Stated Communication" heading, this month's
/// meeting date and (optionally) its times (wiki: cover-banner).
/// </summary>
public sealed class CoverBannerData
{
    public string LodgeName { get; set; } = "";

    /// <summary>Printed exactly as typed (docs/M7-spec.md §9.6) — the lodge's ALL-CAPS voice is theirs.</summary>
    public string HeadingText { get; set; } = "STATED COMMUNICATION";

    /// <summary>
    /// "1st Tuesday" — the machine-readable recurrence rule (<see cref="Core.Text.MeetingRule"/>).
    /// Kept alongside <see cref="MeetingDateText"/> rather than replacing it: v1 prints exactly what
    /// the user typed, while M9's start-from-last-month recomputes the date from this rule without
    /// re-parsing English prose.
    /// </summary>
    public string MeetingRule { get; set; } = "";

    /// <summary>What actually prints, e.g. "July 7th". M9 recomputes this from <see cref="MeetingRule"/>.</summary>
    public string MeetingDateText { get; set; } = "";

    /// <summary>"6:30". Null (not just blank) when the issue carries no dinner time.</summary>
    public string? DinnerTimeText { get; set; }

    /// <summary>"7:30". Null (not just blank) when the issue carries no lodge-opening time.</summary>
    public string? WorkTimeText { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
