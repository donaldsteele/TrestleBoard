using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;

namespace TrestleBoard.Widgets.Roster;

/// <summary>
/// What a re-sync would do, worked out before anything is changed (PLAN.md §11 M13).
/// </summary>
/// <param name="Additions">Rows that would appear, in the order they would be appended.</param>
/// <param name="Removals">Rows that would go, because the address book no longer puts them here.</param>
/// <param name="Updates">Generated rows whose printed text would change — usually a renamed brother.</param>
/// <param name="KeptManual">Rows somebody typed or edited. These are never touched.</param>
/// <param name="Result">The list as it would be afterwards. Nothing has been applied yet.</param>
/// <param name="Fingerprint">The address book's contribution to this month, hashed.</param>
public sealed record BirthdayProjection(
    IReadOnlyList<BirthdayEntry> Additions,
    IReadOnlyList<BirthdayEntry> Removals,
    IReadOnlyList<BirthdayEntry> Updates,
    IReadOnlyList<BirthdayEntry> KeptManual,
    BirthdayListData Result,
    string Fingerprint)
{
    /// <summary>
    /// False when pressing "Update the list" would print exactly the same page. The provenance
    /// fields may still differ — a fingerprint refresh alone is not worth an undo step.
    /// </summary>
    public bool ChangesAnything => Additions.Count > 0 || Removals.Count > 0 || Updates.Count > 0;
}

/// <summary>
/// The one road from the lodge address book to a widget (PLAN.md §5's roster projection rule, §11 M13).
///
/// A pure function with the member list passed in explicitly. There is deliberately no ambient or
/// static roster accessor anywhere in <c>TrestleBoard.Core</c> or <c>TrestleBoard.Widgets</c>: that
/// is what makes §0's privacy property structural rather than merely tested, and it is why
/// <c>WidgetSeed</c> can stay non-personal and <c>TemplateTests</c> can keep passing untouched.
///
/// The result is a <b>materialised snapshot</b>. What ends up in <see cref="BirthdayListData.Entries"/>
/// is the printed truth and stays in the document; reopening March's issue in December must not
/// silently reprint December's birthdays.
/// </summary>
public static class BirthdayRosterProjection
{
    /// <summary>
    /// The ASCII unit separator between fingerprint fields. Without it "person-12" + "Al"
    /// and "person-1" + "2Al" produce the same bytes, so a real change to somebody's
    /// details can hash identical to the stored fingerprint and the list reports itself up
    /// to date when it is not (review §14.2). Same reason, same character, as the officers
    /// projection.
    /// </summary>
    private const string Unit = "\u001f";

    /// <summary>The undo step one sync produces. Named once, so the Edit menu and the panel agree.</summary>
    public const string UndoLabel = "Update birthdays from the address book";

    /// <summary>
    /// Works out what a sync would do, without doing it. Never mutates
    /// <paramref name="current"/> — the caller decides, and the caller commits.
    /// </summary>
    /// <param name="current">The list as it stands today.</param>
    /// <param name="members">The address book, passed in by the shell. Never fetched from here.</param>
    /// <param name="month">The issue month, 1–12.</param>
    public static BirthdayProjection Plan(BirthdayListData current, IReadOnlyList<Member> members, int month)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(members);

        // A removal was a decision about one month's list. Carry-forward moves the issue to a new
        // month, where that decision means nothing — so the suppressions reset with the month rather
        // than following a brother around the year.
        bool sameMonthAsBefore = current.Source == BirthdayListSource.Roster && current.SourceMonth == month;
        List<string> removedIds = [.. sameMonthAsBefore ? current.RemovedMemberIds : []];

        List<Member> candidates = Candidates(members, month, removedIds);
        var candidateById = new Dictionary<string, Member>(StringComparer.Ordinal);
        foreach (Member member in candidates)
        {
            candidateById[member.Id] = member;
        }

        var additions = new List<BirthdayEntry>();
        var removals = new List<BirthdayEntry>();
        var updates = new List<BirthdayEntry>();
        var keptManual = new List<BirthdayEntry>();
        var entries = new List<BirthdayEntry>();
        var accountedFor = new HashSet<string>(StringComparer.Ordinal);

        // Survivors keep their positions. The stored order is the user's, and the layouter sorts
        // for printing anyway (docs/M7-spec.md §9.2), so shuffling it here would only be rude.
        foreach (BirthdayEntry entry in current.Entries)
        {
            if (entry.MemberId is { Length: > 0 } id)
            {
                accountedFor.Add(id);
            }

            if (!IsGenerated(entry))
            {
                keptManual.Add(entry);
                entries.Add(Clone(entry));
                continue;
            }

            if (!candidateById.TryGetValue(entry.MemberId!, out Member? member))
            {
                removals.Add(entry);
                continue;
            }

            BirthdayEntry refreshed = FromMember(member);
            if (Prints(refreshed) != Prints(entry))
            {
                updates.Add(refreshed);
            }

            entries.Add(refreshed);
        }

        foreach (Member member in candidates)
        {
            if (accountedFor.Contains(member.Id))
            {
                continue;
            }

            BirthdayEntry addition = FromMember(member);
            additions.Add(addition);
            entries.Add(addition);
        }

        string fingerprint = Fingerprint(members, month, removedIds);
        var result = new BirthdayListData
        {
            Heading = current.Heading,
            Entries = entries,
            ClosingNote = current.ClosingNote,
            Source = BirthdayListSource.Roster,
            SourceMonth = month,
            GeneratedUtc = current.GeneratedUtc,
            RosterFingerprint = fingerprint,
            RemovedMemberIds = removedIds,
            ExtraProperties = current.ExtraProperties,
        };

        return new BirthdayProjection(additions, removals, updates, keptManual, result, fingerprint);
    }

    /// <summary>
    /// How many people the address book would put in this month's list, ignoring anything already
    /// on the page. This is what decides whether the insert wizard offers its extra first screen.
    /// </summary>
    public static int CountFor(IReadOnlyList<Member> members, int month)
    {
        ArgumentNullException.ThrowIfNull(members);
        return Candidates(members, month, []).Count;
    }

    /// <summary>
    /// Has the address book changed since this list was made? False for a list somebody typed —
    /// nagging about a hand-written list would be nonsense — and false when nothing contributing
    /// has moved.
    ///
    /// <b>Asking is all this does.</b> PLAN.md §11 M13 makes it a hard rule that staleness never
    /// mutates the document: applying on open would dirty a newsletter the user opened only to look
    /// at, trip the 60-second autosave and grow the recovery snapshot.
    /// </summary>
    public static bool IsStale(BirthdayListData data, IReadOnlyList<Member> members, int month)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(members);

        if (data.Source != BirthdayListSource.Roster)
        {
            return false;
        }

        if (data.SourceMonth != month)
        {
            return true;
        }

        return !string.Equals(
            data.RosterFingerprint,
            Fingerprint(members, month, data.RemovedMemberIds),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A hash of exactly the fields that reach the page: who is in this month, what he is called and
    /// on which day. A changed phone number is not a stale birthday list, and must not claim to be —
    /// and neither is a change to somebody the user has already taken off this list, which is why the
    /// suppressions are part of the input rather than something applied afterwards.
    /// </summary>
    public static string Fingerprint(
        IReadOnlyList<Member> members, int month, IEnumerable<string>? removedMemberIds = null)
    {
        ArgumentNullException.ThrowIfNull(members);

        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"m{month}\n");
        foreach (Member member in Candidates(members, month, removedMemberIds))
        {
            builder.Append(CultureInfo.InvariantCulture, $"{member.Id}{Unit}{member.DisplayName}{Unit}{member.BirthDay}\n");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Who this month's list is drawn from: active people with a birthday in it who have not been
    /// taken off it, in one stable order so two machines produce the same fingerprint and the same
    /// list.
    /// </summary>
    private static List<Member> Candidates(
        IReadOnlyList<Member> members, int month, IEnumerable<string>? removedMemberIds)
    {
        var suppressed = new HashSet<string>(removedMemberIds ?? [], StringComparer.Ordinal);
        return members
            .Where(m => m is { IsActive: true } && m.HasBirthday && m.BirthMonth == month)
            .Where(m => !string.IsNullOrEmpty(m.Id) && !suppressed.Contains(m.Id))
            .OrderBy(m => m.BirthDay ?? 0)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A row the projection owns. A row with no member behind it, or one a human has touched, is
    /// the user's — and the user's rows are never rewritten and never taken away.
    /// </summary>
    private static bool IsGenerated(BirthdayEntry entry) =>
        entry.MemberId is { Length: > 0 } && !entry.IsManual;

    private static BirthdayEntry FromMember(Member member) => new()
    {
        Name = member.DisplayName,
        Month = member.BirthMonth ?? 0,
        Day = member.BirthDay ?? 0,
        MemberId = member.Id,
        IsManual = false,
    };

    private static BirthdayEntry Clone(BirthdayEntry entry) => new()
    {
        Name = entry.Name,
        Month = entry.Month,
        Day = entry.Day,
        MemberId = entry.MemberId,
        IsManual = entry.IsManual,
        ExtraProperties = entry.ExtraProperties,
    };

    private static string Prints(BirthdayEntry entry) =>
        string.Create(CultureInfo.InvariantCulture, $"{entry.Name}{Unit}{entry.Month}/{entry.Day}");
}
