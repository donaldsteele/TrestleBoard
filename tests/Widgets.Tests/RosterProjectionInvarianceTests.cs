using TrestleBoard.Roster;
using TrestleBoard.Widgets.Builtins.BirthdayList;
using TrestleBoard.Widgets.Builtins.OfficersTable;
using TrestleBoard.Widgets.Roster;
using Xunit;

namespace TrestleBoard.Widgets.Tests;

/// <summary>
/// M88's hard constraint, as a test.
///
/// <para><b>Every synced widget in every saved newsletter carries a stored fingerprint.</b> The
/// officers table hashes who holds each office, his name and his telephone number; the birthday list
/// hashes id, name and day. Widen what either hash sees and every already-synced table in every
/// <c>.tboard</c> on the committee's laptops reports itself out of date on the next open — for a
/// change that alters not one printed character. The lodge would be told its newsletter is stale
/// because the app learned somebody's ZIP code.</para>
///
/// <para>So: M88 added sixteen properties to <see cref="Member"/>, and <em>none</em> of them may
/// reach a projection. This asserts that by setting every one of them on every member and demanding
/// the projections come back byte-identical. It is deliberately blunt — a new field wired into a
/// projection by accident fails here, whichever field it was.</para>
///
/// <para>Every person here is fictional (PLAN.md §0 rule 2), as everywhere in this suite.</para>
/// </summary>
public sealed class RosterProjectionInvarianceTests
{
    private const int March = 3;

    [Fact]
    public void TheOfficersFingerprintCannotSeeAnythingM88Added()
    {
        Assert.Equal(
            OfficersRosterProjection.Fingerprint(Plain()),
            OfficersRosterProjection.Fingerprint(WithEverythingM88Added()));
    }

    [Fact]
    public void TheBirthdayFingerprintCannotSeeAnythingM88Added()
    {
        Assert.Equal(
            BirthdayRosterProjection.Fingerprint(Plain(), March),
            BirthdayRosterProjection.Fingerprint(WithEverythingM88Added(), March));
    }

    /// <summary>
    /// The fingerprints agreeing is not enough on its own — a projection could print an address and
    /// still hash the same three fields, and the page would change without the app noticing. So the
    /// rows themselves are compared too.
    /// </summary>
    [Fact]
    public void TheProjectedRowsAreIdenticalToo()
    {
        BirthdayProjection plain = BirthdayRosterProjection.Plan(new BirthdayListData(), Plain(), March);
        BirthdayProjection wide = BirthdayRosterProjection.Plan(
            new BirthdayListData(), WithEverythingM88Added(), March);

        Assert.Equal(
            plain.Additions.Select(e => e.Name + "|" + e.Month + "/" + e.Day),
            wide.Additions.Select(e => e.Name + "|" + e.Month + "/" + e.Day));

        OfficersProjection plainOfficers = OfficersRosterProjection.Plan(new OfficersTableData(), Plain());
        OfficersProjection wideOfficers = OfficersRosterProjection.Plan(
            new OfficersTableData(), WithEverythingM88Added());

        Assert.Equal(
            plainOfficers.Proposals.Select(Describe),
            wideOfficers.Proposals.Select(Describe));
    }

    /// <summary>
    /// The telephone number that reaches the page is <see cref="Member.Phone"/> and only that one.
    /// A member who has a mobile and no <c>Phone</c> prints no number — which is what makes the
    /// import's "fill the empty Phone from the mobile" rule a visible, reviewed change rather than
    /// something the projection does behind the committee's back.
    /// </summary>
    [Fact]
    public void AMobileNumberAloneNeverReachesTheOfficersTable()
    {
        IReadOnlyList<Member> book =
        [
            new Member
            {
                Id = "person-1",
                DisplayName = "A. Placeholder",
                Office = "Worshipful Master",
                MobilePhone = "555-0199",
                HomePhone = "555-0198",
            },
        ];

        OfficersProjection plan = OfficersRosterProjection.Plan(new OfficersTableData(), book);

        Assert.All(
            plan.Proposals.SelectMany(p => p.Candidates),
            candidate => Assert.Null(candidate.Phone));
    }

    private static string Describe(OfficerProposal proposal) => string.Join(
        "|",
        proposal.Position,
        proposal.CurrentName,
        proposal.CurrentPhone,
        string.Join(";", proposal.Candidates.Select(c => c.MemberId + "," + c.Name + "," + c.Phone)));

    /// <summary>
    /// M88's promise about the birth year, checked where promises are easiest to break: in the
    /// source itself.
    ///
    /// <para>The year is stored because the lodge's own system holds it, and it is <b>never
    /// printed</b>. <c>BirthdayText</c> cannot say otherwise, and the fingerprint tests above show
    /// no projection can see it — but the next person to want a "90th birthday" notice will reach
    /// for it, and this is the sentence they should meet first. The four projects named here are
    /// every project whose output can reach a page.</para>
    /// </summary>
    [Fact]
    public void NothingThatCanReachAPageEvenMentionsTheBirthYear()
    {
        string root = RepositoryRoot();
        string[] projects = ["TrestleBoard.Widgets", "TrestleBoard.Editing", "TrestleBoard.Rendering", "TrestleBoard.PdfPages"];
        var offenders = new List<string>();

        foreach (string project in projects)
        {
            string folder = Path.Combine(root, "src", project);
            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.ReadAllText(file).Contains("BirthYear", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetRelativePath(root, file));
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "the birth year is stored and never printed (M88); these can reach a page and name it: "
            + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }

    private static IReadOnlyList<Member> Plain() =>
    [
        new Member
        {
            Id = "person-1",
            DisplayName = "A. Placeholder",
            Office = "Worshipful Master",
            Phone = "555-0101",
            BirthMonth = March,
            BirthDay = 4,
        },
        new Member
        {
            Id = "person-2",
            DisplayName = "B. Sample",
            Office = "Senior Warden",
            Phone = "555-0102",
            BirthMonth = March,
            BirthDay = 19,
        },

        // The third man has NO telephone number, and he is the reason this fixture has three people
        // in it. With everybody holding a Phone, a projection that quietly fell back to the mobile
        // could never be caught here: the fallback would never fire. He is where it would.
        new Member
        {
            Id = "person-3",
            DisplayName = "C. Fictitious",
            Office = "Junior Warden",
            BirthMonth = March,
            BirthDay = 27,
        },
    ];

    /// <summary>The same two men, with every field M88 added filled in.</summary>
    private static IReadOnlyList<Member> WithEverythingM88Added() =>
    [
        .. Plain().Select(m => m with
        {
            MemberNumber = "98" + m.Id,
            BirthYear = 1957,
            Degree = TrestleBoard.Roster.Degree.MasterMason,
            MasonicTitle = "PM",
            AddressLine1 = "1 Example Street",
            AddressLine2 = "Apartment 2",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            AddressUndeliverable = true,
            HomePhone = "555-0901",
            MobilePhone = "555-0902",
            WorkPhone = "555-0903",
            SpouseName = "C. Fictitious",
            SpouseEmail = "c.fictitious@example.invalid",
            SpousePhone = "555-0904",
            Notes = "Nothing that should ever be printed.",
        }),
    ];
}
