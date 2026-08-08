using System.Collections.Generic;

namespace TrestleBoard.Spelling;

/// <summary>
/// The words a Masonic newsletter uses that a general English dictionary has never heard of
/// (PLAN.md §11 M52).
///
/// <para>Without this the first run is a wall of underlines: "Tyler", "Worshipful" and
/// "Fellowcraft" appear in nearly every issue, and an app that marks the lodge's own vocabulary as
/// a mistake has told the committee, on page one, that it does not know what it is looking at. The
/// point of a spell checker for this audience is that the few things it does underline are worth
/// looking at.</para>
///
/// <para>These are seeded into the personal dictionary on first use, and only the ones the bundled
/// dictionary actually rejects are kept — see <see cref="PersonalDictionary.SeedIfEmpty"/>. That
/// keeps the file honest: it holds the words we had to add, not a list we hoped we had to add.</para>
///
/// <para><b>§0 rule 2:</b> vocabulary only. Not one name of a real member, a real officer or a real
/// lodge beyond the app's own. Members' surnames reach the personal dictionary from the address
/// book at runtime, on the user's own machine, and never from here.</para>
/// </summary>
public static class LodgeVocabulary
{
    /// <summary>Craft vocabulary, offices, bodies and the words of the ritual that reach print.</summary>
    public static readonly IReadOnlyList<string> Words =
    [
        // Offices and the people in them.
        "Tyler", "Tiler", "Worshipful", "Almoner", "Fellowcraft", "Fellowcrafts",
        "Sojourner", "Sojourners", "Preceptor", "Proxy", "Junior", "Senior", "Wardens",
        "Deacons", "Stewards", "Brethren", "Brother's", "Brethren's",

        // The craft itself.
        "Freemason", "Freemasons", "Freemasonry", "Masonry", "Masonic", "Masonically",
        "Lodge's", "lodgeroom", "trestleboard", "Trestleboard", "appendant", "concordant",
        "cowan", "cowans", "cabletow", "cabletows", "tessellated", "lambskin",
        "landmarks", "obligations", "Craftsman", "operative", "speculative",

        // Bodies a trestle board announces alongside the lodge.
        "Commandery", "Consistory", "Preceptory", "Chapter's", "Shriner", "Shriners",
        "DeMolay", "Rainbow", "Eastern", "Amaranth", "Cryptic", "Scottish", "York",
        "Jurisdiction", "Grand", "District", "Deputy",

        // Names from the ritual and the temple, which appear in essays every year.
        "Hiram", "Abiff", "Solomon's", "Boaz", "Jachin", "Zerubbabel", "Enoch",
        "Sanhedrin", "Tabernacle", "Sinai", "Ophir",

        // The lodge this app was written for, and its town.
        "TrestleBoard", "Indian", "Land",
    ];
}
