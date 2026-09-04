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
    /// The fictional export in the shape a lodge's member-management system writes (M88): its real
    /// column headers, and invented people under them.
    /// </summary>
    public static string MembersFullCsv()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "members-full.csv");
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException(
                "The fictional lodge-export fixture is missing from the harness output. It is linked "
                + "from tests/Roster.Tests/Fixtures — check the project file.", path);
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

    /// <summary>
    /// The same fictional book with two awkward cases added, for the officers-sync shot (M19): one
    /// brother whose office is written in words TrestleBoard does not recognise, and one who claims
    /// an office somebody else already claims. Those two sections are the whole point of that
    /// dialog, and neither belongs in the People window shot — which is why this is a second list
    /// rather than two more rows in the first.
    /// </summary>
    public static IReadOnlyList<Member> PeopleWithAwkwardOffices =>
    [
        .. People,
        Person("person-11", "Kenrick Placeholder", "Historian", 12, 12, "555-0111"),
        Person("person-12", "Lucian Placeholder", "Senior Warden", 1, 15, "555-0112"),
    ];

    /// <summary>
    /// A fictional lodge of twenty-four, built to look like a real one (M88).
    ///
    /// <para><b>Why it is this long and this varied.</b> Screenshots are the only documentation
    /// somebody reads before they trust the program, and a list of ten identical rows photographs a
    /// layout rather than a lodge. So the sample carries what a real address book carries and what a
    /// committee will recognise: brethren with a mobile and no home number and brethren the other way
    /// round, two men whose post comes back, a Fellowcraft and two Entered Apprentices among the
    /// Master Masons, wives on some cards and not on others, a brother who has moved away and come
    /// off the mailing, one called to the Celestial Lodge, and birthdays that cluster in some months
    /// and skip others — because a birthday list with exactly one name in every month is a screenshot
    /// nobody believes.</para>
    ///
    /// <para><b>Every person is invented</b> (PLAN.md §0 rule 2). The surnames are the five this
    /// project has always used for fictional people — Placeholder, Sample, Fictitious, Testcase,
    /// Anonymous — every telephone number is 555-01xx or 555-09xx, the streets are called Example and
    /// Sample, and the email domain is <c>example.invalid</c>, which cannot resolve. The first ten
    /// keep the ids, names, offices, birthdays and telephone numbers they have had since M15, so
    /// every shot taken before this milestone still shows the same men in the same order.</para>
    /// </summary>
    private static IReadOnlyList<Member> People =>
    [
        // The officers first, in the order the officers table prints them.
        Person("person-1", "Aaron Placeholder", "Worshipful Master", 2, 2, "555-0101") with
        {
            MemberNumber = "98501",
            MasonicTitle = "PM",
            Degree = Degree.MasterMason,
            DegreeDate = "1995-05-01",
            BirthYear = 1957,
            AddressLine1 = "1 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0901",
            MobilePhone = "555-0902",
            SpouseName = "Alice Placeholder",
            SpouseEmail = "alice.placeholder@example.invalid",
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
            Notes = "Prefers a telephone call to an email.",
        },
        Person("person-2", "Bertram Placeholder", "Senior Warden", 3, 3, "555-0102") with
        {
            MemberNumber = "98502",
            Degree = Degree.MasterMason,
            DegreeDate = "1998-06-02",
            BirthYear = 1962,
            AddressLine1 = "2 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0903",
            SpouseName = "Beatrice Placeholder",
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
        },
        Person("person-3", "Cedric Placeholder", "Junior Warden", 4, 4, "555-0103") with
        {
            MemberNumber = "98503",
            Degree = Degree.MasterMason,
            DegreeDate = "2003-07-03",
            BirthYear = 1971,
            AddressLine1 = "3 Example Street",
            City = "Nexttown",
            State = "SC",
            Zip = "29888",
            HomePhone = "555-0904",

            // The one whose post keeps coming back. A committee knows at least one.
            AddressUndeliverable = true,
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
        },
        Person("person-4", "Desmond Placeholder", "Treasurer", 5, 5, "555-0104") with
        {
            MemberNumber = "98504",
            MasonicTitle = "PM",
            Degree = Degree.MasterMason,
            DegreeDate = "1975-08-04",
            BirthYear = 1948,
            AddressLine1 = "4 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0905",
            SpouseName = "Delia Placeholder",
            SpousePhone = "555-0906",
            Groups = [MemberGroups.Printed, MemberGroups.Officers],
        },
        Person("person-5", "Ewan Placeholder", "Secretary", 6, 6, "555-0105") with
        {
            MemberNumber = "98505",
            Degree = Degree.MasterMason,
            DegreeDate = "2001-09-05",
            BirthYear = 1983,
            AddressLine1 = "5 Example Street",
            AddressLine2 = "Apartment 2",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0907",
            WorkPhone = "555-0908",
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
        },
        Person("person-6", "Fergus Placeholder", "Senior Deacon", 7, 7, "555-0106") with
        {
            MemberNumber = "98506",
            Degree = Degree.MasterMason,
            DegreeDate = "2010-10-06",
            BirthYear = 1990,
            AddressLine1 = "6 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0909",
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
        },
        Person("person-7", "Gideon Placeholder", "Junior Deacon", 8, 8, "555-0107") with
        {
            MemberNumber = "98507",
            Degree = Degree.MasterMason,
            DegreeDate = "2014-11-07",
            BirthYear = 1994,
            AddressLine1 = "7 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0910",
            Groups = [MemberGroups.ByEmail, MemberGroups.Officers],
        },
        Person("person-8", "Hollis Placeholder", "Chaplain", 9, 9, "555-0108") with
        {
            MemberNumber = "98508",
            MasonicTitle = "PM",
            Degree = Degree.MasterMason,
            DegreeDate = "1993-01-09",
            BirthYear = 1968,
            AddressLine1 = "8 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0911",
            SpouseName = "Harriet Placeholder",
            SpouseEmail = "harriet.placeholder@example.invalid",
            Groups = [MemberGroups.Printed, MemberGroups.Officers],
        },
        Person("person-9", "Ivor Placeholder", "Tyler", 10, 10, "555-0109") with
        {
            MemberNumber = "98509",
            Degree = Degree.MasterMason,
            DegreeDate = "2001-02-10",
            BirthYear = 1976,
            AddressLine1 = "9 Sample Lane",
            City = "Nexttown",
            State = "SC",
            Zip = "29888",
            HomePhone = "555-0912",
            Groups = [MemberGroups.Printed, MemberGroups.Officers],
        },

        // …and the brethren who hold no office, which in every lodge is most of them.
        Person("person-10", "Jarvis Placeholder", null, 11, 11, "555-0110") with
        {
            MemberNumber = "98510",
            Degree = Degree.MasterMason,
            DegreeDate = "1988-03-11",
            BirthYear = 1959,
            AddressLine1 = "10 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0913",
            SpouseName = "Josephine Placeholder",
            Groups = [MemberGroups.Printed],
        },
        Person("person-13", "Marcus Sample", null, 2, 14, "555-0113") with
        {
            MemberNumber = "98513",
            Degree = Degree.MasterMason,
            DegreeDate = "2006-04-12",
            BirthYear = 1980,
            AddressLine1 = "12 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0914",
            SpouseName = "Miriam Sample",
            SpousePhone = "555-0915",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-14", "Nathan Sample", null, 2, 21, "555-0114") with
        {
            MemberNumber = "98514",
            Degree = Degree.MasterMason,
            DegreeDate = "1999-05-13",
            BirthYear = 1965,
            AddressLine1 = "14 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0916",
            WorkPhone = "555-0917",
            Groups = [MemberGroups.Printed],
        },
        Person("person-15", "Oswald Sample", null, 3, 6, "555-0115") with
        {
            MemberNumber = "98515",
            MasonicTitle = "PM",
            Degree = Degree.MasterMason,
            DegreeDate = "1972-06-14",
            BirthYear = 1944,
            AddressLine1 = "15 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0918",
            SpouseName = "Olivia Sample",
            Notes = "Fifty years a Mason in 2022.",
            Groups = [MemberGroups.Printed],
        },
        Person("person-16", "Peregrine Sample", null, 3, 19, "555-0116") with
        {
            MemberNumber = "98516",

            // The Fellowcraft. Before M88 he had nowhere to sit: the app knew only "raised or
            // initiated", and he is neither.
            Degree = Degree.Fellowcraft,
            DegreeDate = "2025-10-06",
            BirthYear = 1997,
            AddressLine1 = "16 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0919",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-17", "Quentin Fictitious", null, 3, 27, "555-0117") with
        {
            MemberNumber = "98517",
            Degree = Degree.EnteredApprentice,
            DegreeDate = "2026-02-11",
            BirthYear = 2001,
            AddressLine1 = "17 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0920",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-18", "Rupert Fictitious", null, 4, 8, "555-0118") with
        {
            MemberNumber = "98518",
            Degree = Degree.EnteredApprentice,
            DegreeDate = "2026-05-20",
            BirthYear = 1999,
            AddressLine1 = "18 Example Street",
            City = "Nexttown",
            State = "SC",
            Zip = "29888",
            MobilePhone = "555-0921",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-19", "Silas Fictitious", null, 5, 2, "555-0119") with
        {
            MemberNumber = "98519",
            Degree = Degree.MasterMason,
            DegreeDate = "1991-07-16",
            BirthYear = 1961,
            AddressLine1 = "19 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0922",
            SpouseName = "Sarah Fictitious",
            SpouseEmail = "sarah.fictitious@example.invalid",
            Groups = [MemberGroups.Printed],
        },
        Person("person-20", "Tobias Fictitious", null, 6, 23, "555-0120") with
        {
            MemberNumber = "98520",
            Degree = Degree.MasterMason,
            DegreeDate = "2004-08-17",
            BirthYear = 1978,
            AddressLine1 = "20 Sample Lane",
            City = "Farvale",
            State = "NC",
            Zip = "28777",

            // Moved out of state, and his post comes back. He stays in the book: "we cannot reach
            // him" and "we have forgotten him" are different facts.
            AddressUndeliverable = true,
            MobilePhone = "555-0923",
            Groups = [],
        },
        Person("person-21", "Ulric Testcase", null, 7, 30, "555-0121") with
        {
            MemberNumber = "98521",
            Degree = Degree.MasterMason,
            DegreeDate = "1996-09-18",
            BirthYear = 1970,
            AddressLine1 = "21 Example Street",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0924",
            MobilePhone = "555-0925",
            Groups = [MemberGroups.ByEmail, MemberGroups.Printed],
        },
        Person("person-22", "Vaughn Testcase", null, 9, 4, "555-0122") with
        {
            MemberNumber = "98522",
            Degree = Degree.MasterMason,
            DegreeDate = "2012-10-19",
            BirthYear = 1986,
            AddressLine1 = "22 Example Street",
            AddressLine2 = "Unit B",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0926",
            SpouseName = "Verity Testcase",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-23", "Wendell Testcase", null, 9, 15, "555-0123") with
        {
            MemberNumber = "98523",
            Degree = Degree.MasterMason,
            DegreeDate = "1984-11-20",
            BirthYear = 1954,
            AddressLine1 = "23 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0927",
            Groups = [MemberGroups.Printed],
        },
        Person("person-24", "Xavier Anonymous", null, 11, 3, "555-0124") with
        {
            MemberNumber = "98524",
            Degree = Degree.MasterMason,
            DegreeDate = "2008-12-21",
            BirthYear = 1982,
            AddressLine1 = "24 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            MobilePhone = "555-0928",
            SpouseName = "Xenia Anonymous",
            SpousePhone = "555-0929",
            Groups = [MemberGroups.ByEmail],
        },
        Person("person-25", "Yorick Anonymous", null, 12, 18, "555-0125") with
        {
            MemberNumber = "98525",
            Degree = Degree.MasterMason,
            DegreeDate = "1979-01-22",
            BirthYear = 1951,
            AddressLine1 = "25 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0930",
            SpouseName = "Yvonne Anonymous",
            Groups = [MemberGroups.Printed],
        },

        // A brother called to the Celestial Lodge. He stays in the book — the lodge does not forget
        // a man because he has died — and nothing the app generates mentions him again.
        Person("person-26", "Zachary Anonymous", null, 8, 26, "555-0126") with
        {
            MemberNumber = "98526",
            MasonicTitle = "PM",
            Degree = Degree.MasterMason,
            DegreeDate = "1969-02-23",
            BirthYear = 1938,
            AddressLine1 = "26 Sample Lane",
            City = "Anytown",
            State = "SC",
            Zip = "29999",
            HomePhone = "555-0931",
            SpouseName = "Zelda Anonymous",
            PassedOn = "2026-06-14",
            IsActive = false,
        },
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
