using System.Globalization;
using System.Text;
using TrestleBoard.Widgets.Builtins.OfficersTable;

namespace TrestleBoard.Widgets.Roster;

/// <summary>
/// Turns the free text somebody typed into <c>Member.Office</c> into one of the twelve
/// <see cref="OfficersTableData.StandardPositions"/>, or into nothing at all (PLAN.md §11 M19).
///
/// <para><b>It never guesses.</b> M13 deferred officer generation precisely because "Sr. Warden",
/// "SW" and "Senior Warden" are the same office while "Past Master" and "Historian" are not offices
/// this table prints, and page 2 is the most conspicuous place in the newsletter for a wrong name.
/// The answer is not cleverness but a table: every non-exact mapping below is <em>data, in one
/// place</em>, and anything not in it comes back null and is shown to the user as "we didn't
/// recognise this". No fuzzy distance, no prefix matching, no "contains" heuristics — those are how
/// a Junior Deacon quietly becomes a Junior Steward.</para>
///
/// <para>Pure and static, with no roster or document state: the same office string gives the same
/// answer on every machine, which is what lets the fingerprint mean anything.</para>
/// </summary>
public static class OfficeMatcher
{
    /// <summary>
    /// The abbreviation table, keyed by the <see cref="Normalise"/> form. The twelve position names
    /// themselves are matched first and are deliberately NOT repeated here — one spelling of an
    /// office lives in <see cref="OfficersTableData.StandardPositions"/> and nowhere else.
    ///
    /// <para>"Master" alone maps to Worshipful Master; "Past Master" does not appear, and must not:
    /// a Past Master is not the Master, and a table that printed him as one would be wrong in the
    /// most embarrassing possible way.</para>
    /// </summary>
    private static readonly Dictionary<string, string> AbbreviationTable = new(StringComparer.Ordinal)
    {
        ["wm"] = "Worshipful Master",
        ["w m"] = "Worshipful Master",
        ["master"] = "Worshipful Master",
        ["worshipful"] = "Worshipful Master",

        ["sw"] = "Senior Warden",
        ["s w"] = "Senior Warden",
        ["sr warden"] = "Senior Warden",

        ["jw"] = "Junior Warden",
        ["j w"] = "Junior Warden",
        ["jr warden"] = "Junior Warden",

        ["sd"] = "Senior Deacon",
        ["s d"] = "Senior Deacon",
        ["sr deacon"] = "Senior Deacon",

        ["jd"] = "Junior Deacon",
        ["j d"] = "Junior Deacon",
        ["jr deacon"] = "Junior Deacon",

        ["ss"] = "Senior Steward",
        ["s s"] = "Senior Steward",
        ["sr steward"] = "Senior Steward",

        ["js"] = "Junior Steward",
        ["j s"] = "Junior Steward",
        ["jr steward"] = "Junior Steward",

        ["treas"] = "Treasurer",
        ["tres"] = "Treasurer",

        ["sec"] = "Secretary",
        ["secy"] = "Secretary",
        ["sect"] = "Secretary",

        // "Sec'y" — the apostrophe is a separator like every other punctuation mark, so the written
        // form arrives here as two words. Both spellings a secretary uses for his own office.
        ["sec y"] = "Secretary",

        ["chap"] = "Chaplain",

        // Spelled both ways in lodge minutes for two centuries; neither is a typo.
        ["tyler"] = "Tiler",

        ["marshal"] = "Marshall",
    };

    /// <summary>Every office string this build recognises, for the round-trip test.</summary>
    public static IReadOnlyDictionary<string, string> Abbreviations => AbbreviationTable;

    /// <summary>
    /// Which of the twelve offices this text names, or null when the answer is "we don't know".
    /// Null is a first-class answer here, not a failure: it is what the sync dialog prints as
    /// "We didn't recognise this — the row stays as it is".
    /// </summary>
    public static string? Match(string? office)
    {
        string key = Normalise(office);
        if (key.Length == 0)
        {
            return null;
        }

        foreach (string position in OfficersTableData.StandardPositions)
        {
            if (string.Equals(Normalise(position), key, StringComparison.Ordinal))
            {
                return position;
            }
        }

        return AbbreviationTable.TryGetValue(key, out string? matched) ? matched : null;
    }

    /// <summary>
    /// Lower-cased, punctuation removed, runs of whitespace collapsed to one space, trimmed. So
    /// "Sr. Warden", "SR WARDEN" and "sr  warden" are one key, and the table needs one row for the
    /// three of them.
    ///
    /// <para>Invariant culture on purpose: a Turkish machine lower-casing "I" to a dotless ı would
    /// otherwise give a different answer from every other machine, and the fingerprint would then
    /// disagree across the committee's laptops.</para>
    /// </summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
                continue;
            }

            // Everything that is not a letter or a digit — spaces, full stops, apostrophes, hyphens
            // — is a separator. "Sr.Warden" and "Sr. Warden" must not be two different offices.
            pendingSpace = true;
        }

        return builder.ToString();
    }

    /// <summary>
    /// "Senior Warden" for a matched string, and the user's own words back for one that is not
    /// recognised. The dialog prints this, so it must never be an empty string.
    /// </summary>
    public static string Describe(string? office) =>
        Match(office) ?? (string.IsNullOrWhiteSpace(office)
            ? "no office"
            : office.Trim().ToString(CultureInfo.InvariantCulture));
}
