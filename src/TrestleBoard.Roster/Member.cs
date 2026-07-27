using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Roster;

/// <summary>How a brother's degree date is recorded (PLAN.md §11 M12).</summary>
public static class DegreeKind
{
    public const string Raised = "raised";
    public const string Initiated = "initiated";

    public static bool IsKnown(string? kind) =>
        kind is null || kind == Raised || kind == Initiated;
}

/// <summary>
/// One person in the lodge address book (PLAN.md §11 M12).
///
/// Seven fields the user ever types, and three deliberate absences:
/// <list type="bullet">
/// <item><description><b>No birth year.</b> The newsletter prints month and day, and asking a man
/// his age to print his birthday is a question the app has no business asking.</description></item>
/// <item><description><b>One date plus a kind</b> rather than separate raised/initiated fields,
/// which is what keeps the add-a-person form to a single screen.</description></item>
/// <item><description><b><see cref="Office"/> is free text, not an enum.</b> Titles drift, lodges
/// abbreviate differently, and a value the app refuses to store is a value the user retypes
/// somewhere worse.</description></item>
/// </list>
///
/// <see cref="Id"/> is stable and never reused, so a person survives being renamed — that is what
/// makes export → edit in Excel → re-import lossless.
/// </summary>
public sealed record Member
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>"Placeholder, A." — set only when the user wants a different filing order.</summary>
    public string? SortName { get; init; }

    public int? BirthMonth { get; init; }

    public int? BirthDay { get; init; }

    public string? Phone { get; init; }

    public string? Email { get; init; }

    public string? Office { get; init; }

    /// <summary>Stored as an ISO date string so a spreadsheet round trip cannot re-interpret it.</summary>
    public string? DegreeDate { get; init; }

    /// <summary>One of <see cref="DegreeKind"/>, or null.</summary>
    public string? DegreeKind { get; init; }

    public bool IsActive { get; init; } = true;

    public string? Notes { get; init; }

    /// <summary>
    /// Anything a newer TrestleBoard wrote that this one does not know about, preserved verbatim —
    /// the same forward-compatibility contract the document model keeps (PLAN.md §2).
    ///
    /// <c>set</c> rather than <c>init</c>, alone among these properties: System.Text.Json refuses an
    /// extension-data property it cannot bind outside a constructor, and on a record every
    /// init-only property is a constructor parameter.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>Has this person a birthday the newsletter could print?</summary>
    [JsonIgnore]
    public bool HasBirthday => BirthMonth is >= 1 and <= 12 && BirthDay is >= 1 and <= 31;

    /// <summary>"3/14", or an empty string. The one place the app formats a birthday for reading.</summary>
    [JsonIgnore]
    public string BirthdayText => HasBirthday
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{BirthMonth}/{BirthDay}")
        : string.Empty;

    /// <summary>
    /// Clamped rather than rejected. A hand-edited roster.json with month 13 in it should lose one
    /// birthday, not stop the address book from opening.
    /// </summary>
    public Member Normalised()
    {
        bool monthOk = BirthMonth is >= 1 and <= 12;
        bool dayOk = BirthDay is >= 1 and <= 31;
        return this with
        {
            DisplayName = (DisplayName ?? string.Empty).Trim(),
            BirthMonth = monthOk && dayOk ? BirthMonth : null,
            BirthDay = monthOk && dayOk ? BirthDay : null,
            DegreeKind = Roster.DegreeKind.IsKnown(DegreeKind) ? DegreeKind : null,
        };
    }
}
