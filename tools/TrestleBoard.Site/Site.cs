namespace TrestleBoard.Site;

/// <summary>
/// The site's table of contents, and the version it advertises.
///
/// <para>Seven railed pages and five help topics. The brief was "more than a one-pager", and the
/// reason it is more than one page is not length: the two readers want opposite things. Somebody
/// told "the lodge uses this now" wants the install page and one answer; somebody at another lodge
/// deciding whether to adopt it wants the tour. A single page serves whichever of them it was
/// written for and buries the other.</para>
/// </summary>
internal static class Site
{
    /// <summary>
    /// The release the download buttons point at.
    ///
    /// <para><b>It is a fallback, not the truth.</b> The install page asks GitHub for the latest
    /// release when it loads, because a number baked into a page is a number that goes stale the
    /// next time somebody tags. This is what a reader sees when that request cannot be made — an
    /// offline copy, a blocked corporate network — and it still reaches a real page, because the
    /// releases URL without a version always shows the newest one.</para>
    /// </summary>
    internal const string FallbackVersion = "1.8.0";

    internal const string Repository = "https://github.com/donaldsteele/TrestleBoard";

    internal const string ReleasesUrl = Repository + "/releases";

    internal const string LatestReleaseApi =
        "https://api.github.com/repos/donaldsteele/TrestleBoard/releases/latest";

    internal static IReadOnlyList<SitePage> Pages { get; } =
    [
        new(
            Slug: "",
            Title: "TrestleBoard",
            Heading: "A month of the lodge, laid out in an evening.",
            Description:
                "A free desktop program for producing a Masonic lodge's monthly trestle board "
                + "newsletter, and exporting the PDF you email round.",
            RailLabel: "Home",
            ContentFile: "home.html"),

        new(
            Slug: "tour",
            Title: "What it does",
            Heading: "What it does",
            Description:
                "The page, the words, the recurring lists, the photographs and the PDF — with a "
                + "picture of each.",
            RailLabel: "What it does",
            ContentFile: "tour.html"),

        new(
            Slug: "install",
            Title: "Download and install",
            Heading: "Download and install",
            Description:
                "Windows, macOS and Linux, with the exact words of the warning each one shows and "
                + "which button to press.",
            RailLabel: "Download",
            ContentFile: "install.html"),

        new(
            Slug: "help",
            Title: "Help",
            Heading: "Help",
            Description:
                "Short answers to the things a trestle board committee actually asks, one page each.",
            RailLabel: "Help",
            ContentFile: "help.html"),

        new(
            Slug: "help/first-newsletter",
            Title: "Your first newsletter",
            Heading: "Your first newsletter",
            Description: "From an empty screen to a PDF you can send, in the order it happens.",
            Parent: "help",
            ContentFile: "help-first-newsletter.html"),

        new(
            Slug: "help/each-month",
            Title: "How a month goes",
            Heading: "How a month goes",
            Description:
                "Start from last month, write this month's articles, check what is left, send it.",
            Parent: "help",
            ContentFile: "help-each-month.html"),

        new(
            Slug: "help/address-book",
            Title: "The address book",
            Heading: "The address book",
            Description:
                "Type a brother's name once. What the lodge list holds, bringing one in from a "
                + "spreadsheet, and where the birthday list and the officers table get their names "
                + "from.",
            Parent: "help",
            ContentFile: "help-address-book.html"),

        new(
            Slug: "help/pictures",
            Title: "Photographs",
            Heading: "Photographs",
            Description:
                "Putting a picture on the page, making the writing flow around it, and fixing one "
                + "that came out dark.",
            Parent: "help",
            ContentFile: "help-pictures.html"),

        new(
            Slug: "help/when-something-goes-wrong",
            Title: "When something goes wrong",
            Heading: "When something goes wrong",
            Description:
                "Work you thought you lost, a warning you did not expect, and how to undo almost "
                + "anything.",
            Parent: "help",
            ContentFile: "help-when-something-goes-wrong.html"),

        new(
            Slug: "reference",
            Title: "Every command",
            Heading: "Every command",
            Description:
                "All of them, with what each one does and its keyboard shortcut — generated from "
                + "the application itself.",
            RailLabel: "Every command"),

        new(
            Slug: "accessibility",
            Title: "Built for the people who use it",
            Heading: "Built for the people who use it",
            Description:
                "The committee is mostly elderly. What that changed, stated as rules the program "
                + "is tested against.",
            RailLabel: "Accessibility",
            ContentFile: "accessibility.html"),

        new(
            Slug: "about",
            Title: "About",
            Heading: "About TrestleBoard",
            Description:
                "Who it is for, what it costs, the licence, the typefaces, and how it is built.",
            RailLabel: "About",
            ContentFile: "about.html"),
    ];

    /// <summary>The pages the left rail lists, in the order it lists them.</summary>
    internal static IEnumerable<SitePage> Railed => Pages.Where(p => p.RailLabel is not null);

    /// <summary>The help topics, which the help page lists rather than the rail.</summary>
    internal static IEnumerable<SitePage> HelpTopics =>
        Pages.Where(p => string.Equals(p.Parent, "help", StringComparison.Ordinal));
}
