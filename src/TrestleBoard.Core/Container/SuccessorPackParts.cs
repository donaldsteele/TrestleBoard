namespace TrestleBoard.Core.Container;

/// <summary>
/// The five things a successor pack can carry, named once (PLAN.md §11 M64).
///
/// <para>The ids are the folder names inside the zip, so the pack is legible to somebody who
/// unzips it with no TrestleBoard at all — which is the only real insurance a volunteer lodge has
/// if this app ever stops being maintained. <c>roster/roster.json</c> is a readable address book
/// whatever happens to the program.</para>
///
/// <para><b>The plain wording lives here and not in the window,</b> for M11's reason: the restore
/// window, the "what you already have" side and the after-the-event summary must all call the
/// address book the same thing, and three copies of a sentence become three different sentences.
/// Core carries user-facing plain English already — every message
/// <see cref="Migrations.UnsupportedFormatException"/> is thrown with is one.</para>
/// </summary>
public static class SuccessorPackParts
{
    /// <summary>The address book: <c>roster.json</c> and the ten-deep backup ring beside it.</summary>
    public const string Roster = "roster";

    /// <summary>The committee's own saved layouts (M57), one <c>.tboard</c> and sidecar each.</summary>
    public const string Templates = "templates";

    /// <summary>The phrase shelf (M54) — the wordings kept for hard news.</summary>
    public const string Phrases = "phrases";

    /// <summary>Words the spell checker was told to accept (M52), mostly surnames.</summary>
    public const string Dictionary = "dictionary";

    /// <summary>Chrome preferences (M9): theme, text size, where the panel sits.</summary>
    public const string Settings = "settings";

    /// <summary>
    /// Every part, in the order a person should be asked about them: the irreplaceable first, the
    /// merely convenient last. Somebody who stops reading half way down has still restored the
    /// address book.
    /// </summary>
    public static IReadOnlyList<string> InOrder { get; } =
        [Roster, Templates, Phrases, Dictionary, Settings];

    private static readonly Dictionary<string, (string Title, string What)> Words =
        new(StringComparer.Ordinal)
        {
            [Roster] = ("Your address book",
                "The lodge's members, their birthdays, and how to reach them — and the ten "
                + "day-by-day backups of it."),
            [Templates] = ("Your templates",
                "The layouts the committee saved for itself, so next month starts from your own "
                + "shape and not the app's."),
            [Phrases] = ("Your saved wordings",
                "The paragraphs kept on the shelf — the memorial wordings and the notices that get "
                + "reused."),
            [Dictionary] = ("Words you told the spell checker to accept",
                "Mostly surnames. Without these the spell checker will underline half the lodge."),
            [Settings] = ("How you like TrestleBoard set up",
                "The colours, the text size and where the panel sits. Worth taking only if the new "
                + "computer is being set up the same way."),
        };

    /// <summary>The short name for a part — the line a person reads down.</summary>
    public static string TitleOf(string id) =>
        Words.TryGetValue(id, out (string Title, string What) w) ? w.Title : id;

    /// <summary>The sentence under the title, saying what taking it would actually mean.</summary>
    public static string DescriptionOf(string id) =>
        Words.TryGetValue(id, out (string Title, string What) w) ? w.What : "";

    /// <summary>Whether an id is one this build knows. A newer pack may carry parts it does not.</summary>
    public static bool IsKnown(string id) => Words.ContainsKey(id);
}
