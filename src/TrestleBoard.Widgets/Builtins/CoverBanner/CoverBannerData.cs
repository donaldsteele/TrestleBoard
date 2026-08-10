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

    /// <summary>
    /// M75 (a): "July" — which issue this newsletter is, as the cover wizard asks it.
    ///
    /// <para><b>Never saved.</b> This is not banner content; it is an answer the banner's wizard
    /// collects on the document's behalf, and its home is
    /// <c>DocumentMetadata.IssueMonth</c> — the single source of truth that drives the file name,
    /// the archive lookup and the birthday projection. It is carried on this POCO so that the ask
    /// lives in the wizard the user already meets (the owner's second ruling) and inherits, free,
    /// every bit of the wizard's validation, review read-back, big-row grid view, keyboard path and
    /// screen-reader labelling. The shell reads it off the session at commit and writes it through
    /// <c>SetMetadataCommand</c>, composed with the widget edit so one Ctrl+Z takes back both.</para>
    /// </summary>
    [JsonIgnore]
    public string IssueMonthName { get; set; } = "";

    /// <summary>M75 (a): "2026". Never saved, for the reason on <see cref="IssueMonthName"/>.</summary>
    [JsonIgnore]
    public string IssueYearText { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}
