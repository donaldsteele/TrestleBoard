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

    /// <summary>
    /// Still a member of the lodge. The birthday projection has filtered on this since M13.
    ///
    /// <para>M55 leaves its meaning alone and adds <see cref="PassedOn"/> beside it, because "no
    /// longer a member" and "has died" are different facts about a person and the second one is
    /// not something to record by un-ticking a box. Ask <see cref="IsInTheNewsletter"/> rather
    /// than either of them.</para>
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// The date a brother was called to the Celestial Lodge, as an ISO date string, or null for
    /// everybody else (M55).
    ///
    /// <para><b>This one nullable field is the whole of "status".</b> An enum beside
    /// <see cref="IsActive"/> would have made two properties that can disagree, and the first
    /// disagreement would put a deceased brother back in the birthday list — the single worst error
    /// this product can ship. A date is also the thing the committee actually knows, and it is what
    /// a memorial notice needs.</para>
    ///
    /// <para>An ISO string rather than a <c>DateOnly</c>, for the reason <see cref="DegreeDate"/>
    /// is: a spreadsheet round trip cannot re-interpret it.</para>
    /// </summary>
    public string? PassedOn { get; init; }

    /// <summary>
    /// Which lists this person belongs to — "Gets the newsletter by email", "Gets a printed copy",
    /// and whatever else the committee needs (M55). Free text for the same reason
    /// <see cref="Office"/> is: a lodge that wants a group we did not think of should not have to
    /// keep that list somewhere worse.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

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

    /// <summary>True when this brother has been recorded as passed (M55).</summary>
    [JsonIgnore]
    public bool HasPassed => !string.IsNullOrWhiteSpace(PassedOn);

    /// <summary>
    /// Whether anything the app generates should mention this person — the birthday list, the
    /// officers table, a mailing group (M55).
    ///
    /// <para>Deliberately one property rather than two checks at each call site. A projection that
    /// forgot one of them would be the error this milestone exists to prevent.</para>
    /// </summary>
    [JsonIgnore]
    public bool IsInTheNewsletter => IsActive && !HasPassed;

    /// <summary>Is this person in that group? Case and surrounding space are ignored.</summary>
    public bool IsIn(string group) =>
        Groups.Any(g => string.Equals(g?.Trim(), group?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Two members are the same when every field is (M55).
    ///
    /// <para><b>Written out by hand, because the compiler's version was wrong the moment
    /// <see cref="Groups"/> arrived.</b> A record compares each field with <c>==</c>, and on an
    /// <c>IReadOnlyList&lt;string&gt;</c> that is reference equality — so two members with identical
    /// group lists in different arrays compared as different. That is not a cosmetic bug: the
    /// import merge decides whether anything changed by comparing members, so re-importing the same
    /// spreadsheet twice reported every single person as edited. Gate 9 caught it within a minute
    /// of the property being added.</para>
    ///
    /// <para><c>MemberEqualityTests</c> fails if a property is added and not compared here.</para>
    /// </summary>
    public bool Equals(Member? other) =>
        other is not null
        && Id == other.Id
        && DisplayName == other.DisplayName
        && SortName == other.SortName
        && BirthMonth == other.BirthMonth
        && BirthDay == other.BirthDay
        && Phone == other.Phone
        && Email == other.Email
        && Office == other.Office
        && DegreeDate == other.DegreeDate
        && DegreeKind == other.DegreeKind
        && IsActive == other.IsActive
        && PassedOn == other.PassedOn
        && Notes == other.Notes
        && Groups.SequenceEqual(other.Groups, StringComparer.Ordinal)
        && SameExtras(ExtraProperties, other.ExtraProperties);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(Id);
        hash.Add(DisplayName);
        hash.Add(SortName);
        hash.Add(BirthMonth);
        hash.Add(BirthDay);
        hash.Add(Phone);
        hash.Add(Email);
        hash.Add(Office);
        hash.Add(DegreeDate);
        hash.Add(DegreeKind);
        hash.Add(IsActive);
        hash.Add(PassedOn);
        hash.Add(Notes);
        foreach (string group in Groups)
        {
            hash.Add(group);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Unknown properties from a newer TrestleBoard count towards sameness, or a round trip through
    /// this version would look like an edit to a file it did not actually change.
    /// </summary>
    private static bool SameExtras(
        Dictionary<string, JsonElement>? a,
        Dictionary<string, JsonElement>? b)
    {
        if (a is null || a.Count == 0)
        {
            return b is null || b.Count == 0;
        }

        if (b is null || a.Count != b.Count)
        {
            return false;
        }

        foreach ((string key, JsonElement value) in a)
        {
            if (!b.TryGetValue(key, out JsonElement other)
                || !string.Equals(value.GetRawText(), other.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Clamped rather than rejected. A hand-edited roster.json with month 13 in it should lose one
    /// birthday, not stop the address book from opening.
    /// </summary>
    public Member Normalised()
    {
        bool monthOk = BirthMonth is >= 1 and <= 12;
        bool dayOk = BirthDay is >= 1 and <= 31;

        // M55: the two cannot be allowed to disagree. A brother recorded as passed is off the
        // rolls, whatever a hand-edited file or an older TrestleBoard left in the other field.
        bool passed = HasPassed;
        return this with
        {
            DisplayName = (DisplayName ?? string.Empty).Trim(),
            BirthMonth = monthOk && dayOk ? BirthMonth : null,
            BirthDay = monthOk && dayOk ? BirthDay : null,
            DegreeKind = Roster.DegreeKind.IsKnown(DegreeKind) ? DegreeKind : null,
            IsActive = IsActive && !passed,
            PassedOn = passed ? PassedOn!.Trim() : null,
            Groups = TidyGroups(Groups),
        };
    }

    /// <summary>
    /// Trimmed, de-duplicated case-insensitively, empties dropped, original order kept. A group
    /// list that quietly holds "Officers" and "officers" would show a person twice in one mailing.
    /// </summary>
    private static List<string> TidyGroups(IReadOnlyList<string>? groups)
    {
        if (groups is null || groups.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tidy = new List<string>();
        foreach (string group in groups)
        {
            string trimmed = (group ?? string.Empty).Trim();
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                tidy.Add(trimmed);
            }
        }

        return tidy;
    }
}
