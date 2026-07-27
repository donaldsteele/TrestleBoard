using System.Text.RegularExpressions;
using Xunit;

namespace TrestleBoard.Roster.Tests;

/// <summary>
/// PLAN.md §0 rule 5 and §12 gate 7, as tests rather than as good intentions.
///
/// M12 is the milestone that creates the hazard: the app now writes a file holding real members'
/// names, birthdays, telephone numbers and email addresses. Two properties are worth checking on
/// every build, because both fail silently and both are unfixable once pushed:
///
/// <list type="number">
/// <item><description><b>No roster file exists anywhere in the repository</b> except the fixtures in
/// this project. A user's "Save as a spreadsheet…" can land in any folder they browse to, including
/// a clone of this one.</description></item>
/// <item><description><b>Every person in those fixtures is fictional.</b> Emails end
/// <c>@example.invalid</c>, telephone numbers are 555 numbers, and the surnames say what they
/// are.</description></item>
/// </list>
/// </summary>
public sealed class RosterPrivacyTests
{
    private static readonly string[] FixtureSurnames =
        ["Placeholder", "Sample", "Fictitious", "Testcase", "Anonymous"];

    /// <summary>The one folder in the repository allowed to hold roster-shaped files.</summary>
    private static readonly string FixtureFolder =
        Path.Combine("tests", "Roster.Tests", "Fixtures");

    [Fact]
    public void NoRosterFileShipsAnywhereInTheRepository()
    {
        string root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path);
            if (IsBuildOutputOrIgnored(relative))
            {
                continue;
            }

            string name = Path.GetFileName(path);
            bool rosterShaped =
                name.StartsWith("roster", StringComparison.OrdinalIgnoreCase)
                    && (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                || name.EndsWith(".roster.bak.json", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Lodge-address-book-", StringComparison.OrdinalIgnoreCase);

            if (rosterShaped)
            {
                offenders.Add(relative);
            }
        }

        Assert.True(
            offenders.Count == 0,
            "roster files must never be committed (PLAN.md §0 rule 5): " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Roster fixtures exist in exactly one folder. A stray one elsewhere is the actual exposure
    /// §0 rule 5 is written about — graphify and llm-wiki scan the tree, and AppData is not what
    /// they would find.
    /// </summary>
    [Fact]
    public void MemberFixturesExistOnlyInThisProject()
    {
        string root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "members-*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path);
            if (IsBuildOutputOrIgnored(relative) || relative.StartsWith(FixtureFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            offenders.Add(relative);
        }

        Assert.True(
            offenders.Count == 0,
            "member fixtures belong only in tests/Roster.Tests/Fixtures: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Every person in the fixtures is obviously invented. Checked rather than trusted, because a
    /// fixture is exactly the sort of file somebody would helpfully "make more realistic".
    /// </summary>
    [Fact]
    public void EveryPersonInTheFixturesIsFictional()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var problems = new List<string>();

        foreach (string path in Directory.EnumerateFiles(folder, "*.csv"))
        {
            string text = File.ReadAllText(path);
            string name = Path.GetFileName(path);

            foreach (Match match in Regex.Matches(text, @"[\w.+-]+@[\w.-]+", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                if (!match.Value.EndsWith("@example.invalid", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{name}: email {match.Value} is not @example.invalid");
                }
            }

            // 555-01xx is the reserved fictional range; anything else looks like somebody's number.
            foreach (Match match in Regex.Matches(
                         text, @"\(?\d{3}\)?[ .-]?\d{3}[ .-]?\d{4}", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                if (!match.Value.Contains("555-0", StringComparison.Ordinal)
                    && !match.Value.Contains("555 0", StringComparison.Ordinal))
                {
                    problems.Add($"{name}: {match.Value} is not a 555 number");
                }
            }
        }

        string hundred = File.ReadAllText(Path.Combine(folder, "members-100.csv"));
        Assert.All(
            hundred.Split('\n').Skip(1).Where(line => line.Trim().Length > 0),
            line => Assert.Contains(
                FixtureSurnames,
                surname => line.Contains(surname, StringComparison.Ordinal)));

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    private static bool IsBuildOutputOrIgnored(string relative)
    {
        string normalised = relative.Replace('\\', '/');
        return normalised.Contains("/bin/", StringComparison.Ordinal)
            || normalised.Contains("/obj/", StringComparison.Ordinal)
            || normalised.StartsWith("bin/", StringComparison.Ordinal)
            || normalised.StartsWith("obj/", StringComparison.Ordinal)
            || normalised.StartsWith(".git/", StringComparison.Ordinal)

            // Gitignored development aids, which absorb the example PDFs' real data by design and
            // are never repository artifacts (PLAN.md §0 rule 3).
            || normalised.StartsWith("wiki/", StringComparison.Ordinal)
            || normalised.StartsWith("raw/", StringComparison.Ordinal)
            || normalised.StartsWith("graphify-out/", StringComparison.Ordinal)
            || normalised.StartsWith("Examples/", StringComparison.Ordinal);
    }

    /// <summary>Walks up from the test binaries until the folder holding PLAN.md and .git is found.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PLAN.md"))
                && Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be found from " + AppContext.BaseDirectory);
    }
}
