using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.OfficersTable;

namespace TrestleBoard.Widgets.Roster;

/// <summary>One brother the address book puts forward for an office.</summary>
/// <param name="MemberId">His stable address-book id, so a rename does not lose the link.</param>
/// <param name="Name">What would be printed in the name column.</param>
/// <param name="Phone">What would be printed in the phone column, or null.</param>
public sealed record OfficerCandidate(string MemberId, string Name, string? Phone);

/// <summary>
/// One row the sync would touch, worked out before anything changes.
///
/// <see cref="Candidates"/> holds nobody when the office would go back to being a printed vacancy,
/// one man in the ordinary case, and two or more when the address book says two people hold the same
/// office. <b>Two claimants is never resolved here.</b> The user chooses, or nothing happens to that
/// row (PLAN.md §11 M19).
/// </summary>
/// <param name="Position">One of <see cref="OfficersTableData.StandardPositions"/>.</param>
/// <param name="CurrentName">What the table prints today, or an empty string for a vacancy.</param>
/// <param name="CurrentPhone">The phone the table prints today, or null.</param>
/// <param name="Candidates">Who the address book puts forward. Empty means "back to vacant".</param>
public sealed record OfficerProposal(
    string Position,
    string CurrentName,
    string? CurrentPhone,
    IReadOnlyList<OfficerCandidate> Candidates)
{
    /// <summary>Two or more claimants. Nothing may be applied to this row until the user picks one.</summary>
    public bool IsAmbiguous => Candidates.Count > 1;

    /// <summary>The address book no longer puts anybody here, so the office would print as vacant.</summary>
    public bool IsVacancy => Candidates.Count == 0;

    /// <summary>The one candidate, when there is exactly one. Null for a vacancy or a contest.</summary>
    public OfficerCandidate? Only => Candidates.Count == 1 ? Candidates[0] : null;
}

/// <summary>
/// Somebody whose <c>Office</c> is filled in with words this build does not recognise. His row is
/// never guessed at and never touched — he is simply shown to the user by name (PLAN.md §11 M19).
/// </summary>
/// <param name="Name">The brother, as the address book spells him.</param>
/// <param name="Office">What is actually typed in his office field.</param>
public sealed record UnrecognisedOffice(string Name, string Office);

/// <summary>
/// What a re-sync would do to the officers table, worked out before anything is changed
/// (PLAN.md §11 M19, modelled line for line on <see cref="BirthdayProjection"/>).
/// </summary>
/// <param name="Proposals">Rows that would change, in the printed order of the twelve offices.</param>
/// <param name="KeptManual">Rows somebody typed or edited. These are never touched.</param>
/// <param name="Unrecognised">Office strings this build could not place. Nothing is done with them.</param>
/// <param name="Fingerprint">The address book's contribution to this table, hashed.</param>
public sealed record OfficersProjection(
    IReadOnlyList<OfficerProposal> Proposals,
    IReadOnlyList<OfficerEntry> KeptManual,
    IReadOnlyList<UnrecognisedOffice> Unrecognised,
    string Fingerprint)
{
    /// <summary>
    /// False when pressing the button would print exactly the same page. An ambiguous row does not
    /// count on its own: until the user picks between the claimants there is nothing to apply, and a
    /// dialog that says "here is what would change" and then changes nothing would be a lie — but it
    /// IS still worth showing, which is why <see cref="HasAnythingToSay"/> exists separately.
    /// </summary>
    public bool ChangesAnything => Proposals.Any(p => !p.IsAmbiguous);

    /// <summary>Anything at all worth putting a window in front of the user for.</summary>
    public bool HasAnythingToSay => Proposals.Count > 0 || Unrecognised.Count > 0;

    /// <summary>
    /// What the dialog starts with ticked: every row where the address book gives one answer.
    /// A contested row starts with no choice made, because the app has no business making it.
    /// </summary>
    public IReadOnlyList<OfficerDecision> DefaultDecisions =>
        [.. Proposals.Where(p => !p.IsAmbiguous).Select(p => new OfficerDecision(p.Position, p.Only))];
}

/// <summary>
/// One row the user has agreed to. <paramref name="Chosen"/> null means "let this office print as a
/// vacancy" — which is a decision the user made, not an absence of one.
/// </summary>
public sealed record OfficerDecision(string Position, OfficerCandidate? Chosen);

/// <summary>
/// The second road from the lodge address book to a widget (PLAN.md §5's roster projection rule,
/// §11 M19). It **formally overturns M13's officer-generation deferral**, on the deferral's own
/// stated condition, and keeps the sentence that deferral was written to protect: silent-wrong beats
/// loud-absent nowhere in this app, and least of all on page 2.
///
/// <para>A pure function with the member list passed in explicitly, exactly as
/// <see cref="BirthdayRosterProjection"/> is. There is deliberately no ambient roster accessor
/// anywhere in <c>TrestleBoard.Widgets</c>: that is what makes PLAN.md §0's privacy property
/// structural rather than merely tested, and it is why <c>WidgetSeed</c> stays non-personal and
/// <c>TemplateTests</c> keeps passing untouched.</para>
///
/// <para>The result is a <b>materialised snapshot</b>. What ends up in
/// <see cref="OfficersTableData.Officers"/> is the printed truth and stays in the document;
/// reopening last year's issue must not silently reprint this year's line of officers.</para>
/// </summary>
public static class OfficersRosterProjection
{
    /// <summary>The undo step one sync produces. Named once, so the Edit menu and the panel agree.</summary>
    public const string UndoLabel = "Fill in the officers from the address book";

    /// <summary>
    /// Works out what a sync would do, without doing it. Never mutates <paramref name="current"/> —
    /// the caller decides, the user confirms, and the caller commits.
    /// </summary>
    /// <param name="current">The table as it stands today.</param>
    /// <param name="members">The address book, passed in by the shell. Never fetched from here.</param>
    public static OfficersProjection Plan(OfficersTableData current, IReadOnlyList<Member> members)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(members);

        Dictionary<string, List<OfficerCandidate>> claims = Claims(members, out List<UnrecognisedOffice> unrecognised);

        var proposals = new List<OfficerProposal>();
        var keptManual = new List<OfficerEntry>();

        foreach (string position in OfficersTableData.StandardPositions)
        {
            OfficerEntry? row = current.Officers.FirstOrDefault(
                o => string.Equals(o.Position, position, StringComparison.Ordinal));

            // A hand-edited row is the user's. It is listed so the dialog can promise out loud that
            // it will be left alone, and then it is left alone.
            if (row is { IsManual: true })
            {
                keptManual.Add(row);
                continue;
            }

            List<OfficerCandidate> candidates = claims.TryGetValue(position, out List<OfficerCandidate>? list)
                ? list
                : [];

            string currentName = row?.Name ?? string.Empty;
            string? currentPhone = row?.Phone;

            // Already showing one of the men the address book puts forward, down to the phone
            // number and the id behind it? Then there is nothing to propose — and for a CONTESTED
            // office that is what makes "sync twice equals sync once" true: once the user has picked
            // between two claimants, the next run stops asking.
            if (candidates.Any(c => Matches(row, currentName, currentPhone, c)))
            {
                continue;
            }

            if (candidates.Count == 0)
            {
                // Nobody claims this office. Only a row THIS projection filled in is taken back:
                // a name with no member id behind it was put there by a human, and clearing it
                // would be the app deleting somebody's typing.
                if (row?.MemberId is not { Length: > 0 } || currentName.Length == 0)
                {
                    continue;
                }
            }

            proposals.Add(new OfficerProposal(position, currentName, currentPhone, candidates));
        }

        return new OfficersProjection(proposals, keptManual, unrecognised, Fingerprint(members));
    }

    /// <summary>
    /// Builds the table the user agreed to. Every position the decisions do not mention is copied
    /// across exactly as it stands — including the twelve-row structure, the heading and the vacancy
    /// wording, none of which the address book has any opinion about.
    /// </summary>
    public static OfficersTableData Apply(
        OfficersTableData current, IReadOnlyList<OfficerDecision> decisions, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(decisions);

        var byPosition = new Dictionary<string, OfficerDecision>(StringComparer.Ordinal);
        foreach (OfficerDecision decision in decisions)
        {
            byPosition[decision.Position] = decision;
        }

        var officers = new List<OfficerEntry>();
        foreach (OfficerEntry row in current.Officers)
        {
            // A hand-edited row never reaches the decision list, so this is belt and braces: even a
            // caller that made one up cannot overwrite the user's own typing.
            if (row.IsManual || !byPosition.TryGetValue(row.Position, out OfficerDecision? decision))
            {
                officers.Add(Clone(row));
                continue;
            }

            officers.Add(new OfficerEntry
            {
                Position = row.Position,
                Name = decision.Chosen?.Name ?? string.Empty,
                Phone = decision.Chosen?.Phone,
                MemberId = decision.Chosen?.MemberId,
                IsManual = false,
                ExtraProperties = row.ExtraProperties,
            });
        }

        return new OfficersTableData
        {
            Heading = current.Heading,
            Officers = officers,
            VacantText = current.VacantText,
            Source = OfficersTableSource.Roster,
            GeneratedUtc = current.GeneratedUtc,
            RosterFingerprint = fingerprint,
            ExtraProperties = current.ExtraProperties,
        };
    }

    /// <summary>
    /// How many of the twelve offices the address book could fill in, ignoring what is on the page.
    /// This is what decides whether the action can do anything at all, and it counts a contested
    /// office once — the user still has one office's worth of work to confirm.
    /// </summary>
    public static int CountFor(IReadOnlyList<Member> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return Claims(members, out _).Count;
    }

    /// <summary>
    /// Has the address book changed since this table was filled in? False for a table somebody
    /// typed — nagging about a hand-written table would be nonsense — and false when nothing
    /// contributing has moved.
    ///
    /// <b>Asking is all this does.</b> PLAN.md §11 M13 made it a hard rule that staleness never
    /// mutates the document, and M19 keeps it: applying on open would dirty a newsletter the user
    /// opened only to look at, trip the 60-second autosave and grow the recovery snapshot.
    /// </summary>
    public static bool IsStale(OfficersTableData data, IReadOnlyList<Member> members)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(members);

        return data.Source == OfficersTableSource.Roster
            && !string.Equals(data.RosterFingerprint, Fingerprint(members), StringComparison.Ordinal);
    }

    /// <summary>
    /// A hash of exactly the fields that reach the page: who holds each of the twelve offices, what
    /// he is called and his phone number. A changed birthday is not a stale officers table, and must
    /// not claim to be.
    /// </summary>
    public static string Fingerprint(IReadOnlyList<Member> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        Dictionary<string, List<OfficerCandidate>> claims = Claims(members, out _);
        var builder = new StringBuilder();
        foreach (string position in OfficersTableData.StandardPositions)
        {
            if (!claims.TryGetValue(position, out List<OfficerCandidate>? candidates))
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture, $"{position}\n");
            foreach (OfficerCandidate candidate in candidates)
            {
                builder.Append(
                    CultureInfo.InvariantCulture, $"{candidate.MemberId}{candidate.Name}{candidate.Phone}\n");
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>
    /// Who the address book puts forward for each office, in one stable order so two machines
    /// produce the same fingerprint and the same proposals. Inactive members hold no office as far
    /// as the newsletter is concerned.
    /// </summary>
    private static Dictionary<string, List<OfficerCandidate>> Claims(
        IReadOnlyList<Member> members, out List<UnrecognisedOffice> unrecognised)
    {
        unrecognised = [];
        var claims = new Dictionary<string, List<OfficerCandidate>>(StringComparer.Ordinal);

        IEnumerable<Member> ordered = members
            .Where(m => m is { IsActive: true } && !string.IsNullOrEmpty(m.Id))
            .Where(m => !string.IsNullOrWhiteSpace(m.Office))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.Ordinal);

        foreach (Member member in ordered)
        {
            if (OfficeMatcher.Match(member.Office) is not { } position)
            {
                unrecognised.Add(new UnrecognisedOffice(member.DisplayName, member.Office!.Trim()));
                continue;
            }

            if (!claims.TryGetValue(position, out List<OfficerCandidate>? list))
            {
                list = [];
                claims[position] = list;
            }

            list.Add(new OfficerCandidate(member.Id, member.DisplayName, Blank(member.Phone)));
        }

        return claims;
    }

    /// <summary>Is the row on the page already exactly what this candidate would put there?</summary>
    private static bool Matches(
        OfficerEntry? row, string currentName, string? currentPhone, OfficerCandidate candidate) =>
        string.Equals(currentName, candidate.Name, StringComparison.Ordinal)
        && string.Equals(currentPhone ?? "", candidate.Phone ?? "", StringComparison.Ordinal)
        && string.Equals(row?.MemberId, candidate.MemberId, StringComparison.Ordinal);

    private static string? Blank(string? phone) => string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

    private static OfficerEntry Clone(OfficerEntry row) => new()
    {
        Position = row.Position,
        Name = row.Name,
        Phone = row.Phone,
        MemberId = row.MemberId,
        IsManual = row.IsManual,
        ExtraProperties = row.ExtraProperties,
    };
}
