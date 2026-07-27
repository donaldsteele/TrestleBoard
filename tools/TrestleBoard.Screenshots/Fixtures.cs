using TrestleBoard.Roster;

namespace TrestleBoard.Screenshots;

/// <summary>
/// The fictional people who appear in the address-book screenshots.
///
/// PLAN.md §0 rule 5 puts roster fixtures in exactly one place —
/// <c>tests/Roster.Tests/Fixtures</c> — so the CSV is LINKED into this project's output rather than
/// copied, the same arrangement <c>App.HeadlessTests</c> uses. The handful of members built in code
/// below exist because the People window shot wants a short list with offices and birthdays
/// showing, not a hundred rows of scrolling; every name in it is obviously a placeholder.
/// </summary>
internal static class Fixtures
{
    /// <summary>The fictional hundred-member spreadsheet the import screenshots are taken from.</summary>
    public static string MembersCsv()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "members-100.csv");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                "The fictional roster fixture is missing from the harness output. It is linked from "
                + "tests/Roster.Tests/Fixtures — check the project file.", path);
    }

    /// <summary>
    /// A roster service over a store inside the harness's TEMPORARY app-state root. It cannot be
    /// the real one: <see cref="TrestleBoard.App.Settings.AppPaths.Root"/> was redirected before
    /// Avalonia started (PLAN.md §0 rule 6).
    /// </summary>
    public static RosterService FictionalRoster(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);

        var store = new RosterStore(Path.Combine(stateRoot, "screenshot-roster.json"));
        var service = new RosterService(store);
        service.Replace(
            new RosterBook { Members = People },
            "Load the example address book");
        return service;
    }

    private static IReadOnlyList<Member> People =>
    [
        Person("person-1", "Aaron Placeholder", "Worshipful Master", 2, 2, "555-0101"),
        Person("person-2", "Bertram Placeholder", "Senior Warden", 3, 3, "555-0102"),
        Person("person-3", "Cedric Placeholder", "Junior Warden", 4, 4, "555-0103"),
        Person("person-4", "Desmond Placeholder", "Treasurer", 5, 5, "555-0104"),
        Person("person-5", "Ewan Placeholder", "Secretary", 6, 6, "555-0105"),
        Person("person-6", "Fergus Placeholder", "Senior Deacon", 7, 7, "555-0106"),
        Person("person-7", "Gideon Placeholder", "Junior Deacon", 8, 8, "555-0107"),
        Person("person-8", "Hollis Placeholder", "Chaplain", 9, 9, "555-0108"),
        Person("person-9", "Ivor Placeholder", "Tyler", 10, 10, "555-0109"),
        Person("person-10", "Jarvis Placeholder", null, 11, 11, "555-0110"),
    ];

    private static Member Person(
        string id, string name, string? office, int month, int day, string phone) =>
        new()
        {
            Id = id,
            DisplayName = name,
            Office = office,
            BirthMonth = month,
            BirthDay = day,
            Phone = phone,
            Email = name.Replace(' ', '.').ToLowerInvariant() + "@example.invalid",
            IsActive = true,
        };
}
