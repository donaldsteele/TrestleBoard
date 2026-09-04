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
/// How far a brother has come (PLAN.md §11 M88).
///
/// <para><b>This is a different question from <see cref="DegreeKind"/>, which is why it is a
/// different field.</b> "Raised or initiated" says which ceremony <see cref="Member.DegreeDate"/>
/// records; this says which degree he holds today. The lodge's own export carries the second for
/// every member — 103 Master Masons, 8 Entered Apprentices and one Fellowcraft — and a Fellowcraft
/// had nowhere to sit in the old pair at all.</para>
/// </summary>
public static class Degree
{
    public const string EnteredApprentice = "enteredApprentice";
    public const string Fellowcraft = "fellowcraft";
    public const string MasterMason = "masterMason";

    public static bool IsKnown(string? degree) =>
        degree is null || degree == EnteredApprentice || degree == Fellowcraft || degree == MasterMason;
}

/// <summary>
/// One person in the lodge address book (PLAN.md §11 M12, widened by M88).
///
/// <para><b>M12 stored seven fields and argued for the absence of the rest; the owner reversed that
/// on 2026-09-04.</b> The lodge's member system already holds a postal address for every brother,
/// three telephone numbers, a member number, his degree and his spouse — and a book that drops them
/// makes the secretary keep a second list, which is the thing this app exists to stop. What M12's
/// reasoning was actually protecting was the <em>form</em>, not the record: that is now two tabs, and
/// the seven fields a person edits in a hurry are still the first thing they see.</para>
///
/// One M12 absence survives, and one is deliberately narrowed:
/// <list type="bullet">
/// <item><description><b><see cref="BirthYear"/> is stored and never printed (M88).</b> The
/// newsletter still prints month and day only — <see cref="BirthdayText"/> cannot say otherwise —
/// and a source test keeps the year's name out of every project that can reach a page.</description></item>
/// <item><description><b>One date plus a kind</b> rather than separate raised/initiated fields.
/// M88 adds <see cref="Degree"/> beside them for the different question of how far he has
/// come.</description></item>
/// <item><description><b><see cref="Office"/> is free text, not an enum.</b> Titles drift, lodges
/// abbreviate differently, and a value the app refuses to store is a value the user retypes
/// somewhere worse. <see cref="State"/> and <see cref="MasonicTitle"/> follow it.</description></item>
/// <item><description><b>No first/middle/last/suffix.</b> Splitting a name is a different product;
/// the father-and-son problem it would have solved is solved by
/// <see cref="MemberNumber"/> instead.</description></item>
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

    // ---- M88: what the lodge's own member system holds ------------------------------------------

    /// <summary>
    /// The number the lodge gave him, as text (M88).
    ///
    /// <para>Text and never a number, though every one of them looks like one: it is an identifier,
    /// the app never does arithmetic on it, and a leading zero that a numeric type would eat is
    /// somebody's real member number. It is also the merge's second match key, which is what stops a
    /// father and a son sharing a card.</para>
    /// </summary>
    public string? MemberNumber { get; init; }

    /// <summary>
    /// The year he was born, stored and <b>never printed</b> (M88).
    ///
    /// <para>M12 refused the year outright, on the ground that asking a man his age to print his
    /// birthday is a question the app has no business asking. The question is not asked here either:
    /// the secretary's system already knows it, the app only keeps what it is handed, and the
    /// newsletter still gets month and day. <see cref="BirthdayText"/> is unchanged, so "never
    /// printed" holds by construction rather than by care — and a source test keeps this property's
    /// name out of Widgets, Editing, Rendering and PdfPages entirely.</para>
    /// </summary>
    public int? BirthYear { get; init; }

    /// <summary>One of <see cref="Roster.Degree"/>, or null (M88).</summary>
    public string? Degree { get; init; }

    /// <summary>The letters after his name — "PM", "PDDGM". Free text (M88).</summary>
    public string? MasonicTitle { get; init; }

    public string? AddressLine1 { get; init; }

    public string? AddressLine2 { get; init; }

    public string? City { get; init; }

    /// <summary>Free text, like <see cref="Office"/>: a lodge with a member in another country
    /// should not meet a list of American states.</summary>
    public string? State { get; init; }

    /// <summary>Named for the word the audience says, not "postal code" (M88).</summary>
    public string? Zip { get; init; }

    /// <summary>Post to this address comes back (M88). A fact the secretary already tracks, and the
    /// difference between a printed copy that arrives and one nobody reads.</summary>
    public bool AddressUndeliverable { get; init; }

    /// <summary>
    /// The other numbers (M88). <see cref="Phone"/> stays "the one printed in the newsletter" and is
    /// the only number anything generated ever reads; these three are the book's own record.
    ///
    /// <para>Three fields rather than one, because 24 of the lodge's 112 members have both a home
    /// and a mobile number and a single field silently drops one of them.</para>
    /// </summary>
    public string? HomePhone { get; init; }

    /// <inheritdoc cref="HomePhone"/>
    public string? MobilePhone { get; init; }

    /// <inheritdoc cref="HomePhone"/>
    public string? WorkPhone { get; init; }

    /// <summary>
    /// His wife's name, and how to reach her (M88).
    ///
    /// <para><b>Fields on his card, and deliberately not people in the book.</b> A spouse is not a
    /// member: giving her a <see cref="Member"/> row would put her one un-ticked box away from the
    /// birthday list, the officers table and a mailing count. There is no row, so there is nothing
    /// for a projection to find.</para>
    /// </summary>
    public string? SpouseName { get; init; }

    /// <inheritdoc cref="SpouseName"/>
    public string? SpouseEmail { get; init; }

    /// <inheritdoc cref="SpouseName"/>
    public string? SpousePhone { get; init; }

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
    /// Any number we hold for him, the printed one first (M88).
    ///
    /// <para><b>For showing a person their own data, and nothing else.</b> The import review screen
    /// uses it so a row does not read as blank when the lodge's file has only a mobile column. It
    /// must never reach a projection: the officers table's stored fingerprint hashes
    /// <see cref="Phone"/>, and widening what that hash sees would make every synced table in every
    /// saved newsletter report itself stale. A test asserts this property's name appears nowhere in
    /// TrestleBoard.Widgets.</para>
    /// </summary>
    [JsonIgnore]
    public string? AnyPhone => Phone ?? MobilePhone ?? HomePhone ?? WorkPhone;

    /// <summary>Have we somewhere to post to? (M88)</summary>
    [JsonIgnore]
    public bool HasMailingAddress =>
        !string.IsNullOrWhiteSpace(AddressLine1) || !string.IsNullOrWhiteSpace(City);

    /// <summary>Do we know his wife's name? (M88)</summary>
    [JsonIgnore]
    public bool HasSpouse => !string.IsNullOrWhiteSpace(SpouseName);

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
        && MemberNumber == other.MemberNumber
        && BirthYear == other.BirthYear
        && Degree == other.Degree
        && MasonicTitle == other.MasonicTitle
        && AddressLine1 == other.AddressLine1
        && AddressLine2 == other.AddressLine2
        && City == other.City
        && State == other.State
        && Zip == other.Zip
        && AddressUndeliverable == other.AddressUndeliverable
        && HomePhone == other.HomePhone
        && MobilePhone == other.MobilePhone
        && WorkPhone == other.WorkPhone
        && SpouseName == other.SpouseName
        && SpouseEmail == other.SpouseEmail
        && SpousePhone == other.SpousePhone
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
        hash.Add(MemberNumber);
        hash.Add(BirthYear);
        hash.Add(Degree);
        hash.Add(MasonicTitle);
        hash.Add(AddressLine1);
        hash.Add(AddressLine2);
        hash.Add(City);
        hash.Add(State);
        hash.Add(Zip);
        hash.Add(AddressUndeliverable);
        hash.Add(HomePhone);
        hash.Add(MobilePhone);
        hash.Add(WorkPhone);
        hash.Add(SpouseName);
        hash.Add(SpouseEmail);
        hash.Add(SpousePhone);
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

        // M88. "Raised" IS the Master Mason degree, so a book written before this milestone can say
        // so without inventing anything, and the old field is emptied so the two can never disagree.
        //
        // "Initiated" is deliberately NOT migrated. A man initiated in 1998 is almost certainly a
        // Master Mason today, and writing "Entered Apprentice" on his card would be the app making a
        // claim about a real brother that nobody ever made. It stays as it was and corrects itself
        // the first time the lodge imports its own list, which carries the degree for everybody.
        string? kind = Roster.DegreeKind.IsKnown(DegreeKind) ? DegreeKind : null;
        string? degree = Roster.Degree.IsKnown(Degree) ? Degree : null;
        if (kind == Roster.DegreeKind.Raised)
        {
            degree ??= Roster.Degree.MasterMason;
            kind = null;
        }

        return this with
        {
            DisplayName = (DisplayName ?? string.Empty).Trim(),
            BirthMonth = monthOk && dayOk ? BirthMonth : null,
            BirthDay = monthOk && dayOk ? BirthDay : null,
            DegreeKind = kind,
            IsActive = IsActive && !passed,
            PassedOn = passed ? PassedOn!.Trim() : null,
            Groups = TidyGroups(Groups),

            // M88. A fixed range rather than "this year": Normalised must give the same answer on
            // every machine and in every year, or two committee members' books disagree.
            BirthYear = BirthYear is >= 1850 and <= 2100 ? BirthYear : null,
            Degree = degree,
            MemberNumber = Tidy(MemberNumber),
            MasonicTitle = Tidy(MasonicTitle),
            AddressLine1 = Tidy(AddressLine1),
            AddressLine2 = Tidy(AddressLine2),
            City = Tidy(City),
            State = Tidy(State),
            Zip = Tidy(Zip),
            HomePhone = Tidy(HomePhone),
            MobilePhone = Tidy(MobilePhone),
            WorkPhone = Tidy(WorkPhone),
            SpouseName = Tidy(SpouseName),
            SpouseEmail = Tidy(SpouseEmail),
            SpousePhone = Tidy(SpousePhone),
        };
    }

    /// <summary>
    /// Trimmed, and empty is null (M88). A field the user cleared and a field they never filled in
    /// are the same fact, and storing them differently would report an edit where there was none.
    /// </summary>
    private static string? Tidy(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
