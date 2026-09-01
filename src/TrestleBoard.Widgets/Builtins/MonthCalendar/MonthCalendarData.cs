using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Widgets.Builtins.MonthCalendar;

/// <summary>
/// One thing happening on one day of the month (PLAN.md §11 M84).
/// </summary>
public sealed class CalendarEntry
{
    /// <summary>Which day of the month, as the user typed it. 0 until they have.</summary>
    public int Day { get; set; }

    /// <summary>What happens. Short: it prints inside a cell about an inch wide.</summary>
    public string What { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>True when nothing usable has been typed.</summary>
    [JsonIgnore]
    public bool IsBlank => Day <= 0 || string.IsNullOrWhiteSpace(What);
}

/// <summary>
/// The month on a grid (PLAN.md §11 M84) — the eighth widget.
///
/// <para><b>The district calendar is a table, and that is the right shape for six lodges and the
/// wrong one for one month.</b> A trestle board wants the month laid out the way a calendar on a
/// wall is laid out, so that somebody can see at a glance that the stated meeting and the fish fry
/// fall in the same week.</para>
///
/// <para><b>The month itself is not stored.</b> It comes from the issue, through
/// <see cref="WidgetSeed"/>, exactly as the cover banner's does — so a newsletter carried forward to
/// next month gets next month's grid without anybody re-answering anything, and the grid can never
/// disagree with the cover about which month this is. That is the M55 rule: two things that can
/// disagree eventually will.</para>
///
/// <para><b>§0: nothing personal.</b> What is stored is a day number and a short line of the
/// committee's own words. There is no field for a member, and the stated meeting is marked from the
/// document's own <c>MeetingRule</c>, which is a recurrence rule rather than anybody's name.</para>
/// </summary>
public sealed class MonthCalendarData
{
    /// <summary>Printed above the grid. Blank prints no heading at all.</summary>
    public string Heading { get; set; } = "";

    /// <summary>
    /// The issue's month, copied from the seed when the widget is made and refreshed on every
    /// re-edit. Stored so the layouter has it without reaching for the document, which is a layer
    /// it cannot see.
    /// </summary>
    public int Month { get; set; }

    public int Year { get; set; }

    /// <summary>
    /// The stated communication's recurrence, copied from the document ("1st Tuesday"), or blank.
    /// A rule rather than a date: it is what the lodge's by-laws say, and the date follows from it
    /// and the month.
    /// </summary>
    public string MeetingRule { get; set; } = "";

    /// <summary>What the committee has added, one line each.</summary>
    public List<CalendarEntry> Entries { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>The entries with something in them. A blank row is a stray Enter, not an entry.</summary>
    public IReadOnlyList<CalendarEntry> FilledEntries() => [.. Entries.Where(e => !e.IsBlank)];

    /// <summary>
    /// Whether this widget knows which month it is for. Month 0 is what a widget made before the
    /// issue date was answered carries, and a grid for no month is a grid of empty boxes.
    /// </summary>
    [JsonIgnore]
    public bool KnowsTheMonth => Month is >= 1 and <= 12 && Year > 0;
}
