// Emitted once by assets-src/fonts/tools/gen-font-catalog.py from
// assets-src/fonts/fonts.json, then maintained by hand. PLAN.md M14 deliberately
// rejects a source generator here: it would trade this table for a Roslyn project,
// a build-order dependency and a debugging failure mode nobody on this project has.
// Layout.Tests/FontCatalogTests holds the two halves together — set-equality in BOTH
// directions between this table and the assembly's embedded resources.
namespace TrestleBoard.Layout.Fonts;

/// <summary>How the font picker groups the bundled families. Order is display order.</summary>
public enum FontCategory
{
    SerifText,
    SansText,
    Display,
    Accent,
}

/// <summary>
/// One bundled family, with the two things the picker cannot invent: a plain-language
/// description an elderly reader can act on, and a sample line worth setting in it.
/// </summary>
public sealed record FontFamilyInfo(
    string Family,
    FontCategory Category,
    string Description,
    string SampleText,
    int SortOrder);

/// <summary>One bundled face and the embedded resource its bytes come from.</summary>
public readonly record struct BundledFace(FontKey Key, string Resource);

public static class BundledFontCatalog
{
    private static readonly FontFamilyInfo[] FamilyTable =
    [
        new FontFamilyInfo(
            "Source Serif 4",
            FontCategory.SerifText,
            "The newsletter's usual reading face. Calm, roomy, easy on tired eyes.",
            "Stated Communication, second Tuesday",
            10),
        new FontFamilyInfo(
            "EB Garamond",
            FontCategory.SerifText,
            "An old-style book face. Warm and traditional — close to what a printed lodge notice used to look like.",
            "Brethren of Indian Land Lodge 414",
            20),
        new FontFamilyInfo(
            "Crimson Pro",
            FontCategory.SerifText,
            "A slim book serif. Fits more words on a page without feeling cramped.",
            "Minutes of the previous communication",
            30),
        new FontFamilyInfo(
            "Libre Baskerville",
            FontCategory.SerifText,
            "A sturdy, wide serif made for screens as well as paper. Very legible at small sizes.",
            "Fellowcraft degree at seven",
            40),
        new FontFamilyInfo(
            "PT Serif",
            FontCategory.SerifText,
            "A newspaper serif. Plain, even-coloured, and it never draws attention to itself.",
            "Refreshment at half past six",
            50),
        new FontFamilyInfo(
            "Lora",
            FontCategory.SerifText,
            "A serif with brushed, calligraphic edges. Friendly without being informal.",
            "Widows and orphans committee report",
            60),
        new FontFamilyInfo(
            "Libre Caslon Text",
            FontCategory.SerifText,
            "The classic Caslon, cut for continuous reading. A very English, very old printing-house look.",
            "By order of the Worshipful Master",
            70),
        new FontFamilyInfo(
            "Source Sans 3",
            FontCategory.SansText,
            "The plain, no-nonsense face used for captions and small print.",
            "Photographs by the Trestle Board committee",
            110),
        new FontFamilyInfo(
            "Open Sans",
            FontCategory.SansText,
            "A wide, open sans-serif. Probably the most familiar face on this list.",
            "Please bring a covered dish",
            120),
        new FontFamilyInfo(
            "Lato",
            FontCategory.SansText,
            "A humanist sans with soft, rounded shapes. Reads as warm rather than corporate.",
            "Fraternal greetings from the East",
            130),
        new FontFamilyInfo(
            "Libre Franklin",
            FontCategory.SansText,
            "An American gothic sans with a printed, slightly condensed feel. Good for headings.",
            "Notice to all members",
            140),
        new FontFamilyInfo(
            "PT Sans",
            FontCategory.SansText,
            "A compact, businesslike sans. Useful when a table has more columns than room.",
            "Dues are payable in December",
            150),
        new FontFamilyInfo(
            "Cinzel",
            FontCategory.Display,
            "Roman inscription capitals. The masthead face this newsletter already uses.",
            "TRESTLE BOARD",
            210),
        new FontFamilyInfo(
            "Playfair Display",
            FontCategory.Display,
            "High-contrast headline serif. Elegant, and best kept large.",
            "Annual Communication",
            220),
        new FontFamilyInfo(
            "Cormorant Garamond",
            FontCategory.Display,
            "A delicate display Garamond. Beautiful in a title, too fine for body text.",
            "Indian Land Lodge No. 414",
            230),
        new FontFamilyInfo(
            "Bitter",
            FontCategory.Display,
            "A slab serif with square, solid ends. Headings that hold up on a photocopy.",
            "This Month at the Lodge",
            240),
        new FontFamilyInfo(
            "Marcellus",
            FontCategory.Display,
            "Carved Roman letterforms with lowercase. Formal, and it has only one weight.",
            "Officers for the Ensuing Year",
            250),
        new FontFamilyInfo(
            "Cinzel Decorative",
            FontCategory.Accent,
            "Cinzel with flourishes on the capitals. For a certificate line, not a paragraph.",
            "Fifty Year Award",
            310),
        new FontFamilyInfo(
            "Great Vibes",
            FontCategory.Accent,
            "A formal joined script. Use it for a compliments line and nothing else.",
            "With fraternal regards",
            320),
        new FontFamilyInfo(
            "UnifrakturMaguntia",
            FontCategory.Accent,
            "Blackletter, as seen on old charters and certificates. Hard to read in quantity — keep it short.",
            "Chartered 1948",
            330),
    ];

    private static readonly BundledFace[] FaceTable =
    [
        new BundledFace(
            new FontKey("Source Serif 4", FontWeight.Regular, FontStyleSlant.Normal),
            "SourceSerif4-Regular.ttf"),
        new BundledFace(
            new FontKey("Source Serif 4", FontWeight.Bold, FontStyleSlant.Normal),
            "SourceSerif4-Bold.ttf"),
        new BundledFace(
            new FontKey("Source Serif 4", FontWeight.Regular, FontStyleSlant.Italic),
            "SourceSerif4-It.ttf"),
        new BundledFace(
            new FontKey("Source Serif 4", FontWeight.Bold, FontStyleSlant.Italic),
            "SourceSerif4-BoldIt.ttf"),
        new BundledFace(
            new FontKey("EB Garamond", FontWeight.Regular, FontStyleSlant.Normal),
            "EBGaramond-Regular.ttf"),
        new BundledFace(
            new FontKey("EB Garamond", FontWeight.Bold, FontStyleSlant.Normal),
            "EBGaramond-Bold.ttf"),
        new BundledFace(
            new FontKey("EB Garamond", FontWeight.Regular, FontStyleSlant.Italic),
            "EBGaramond-Italic.ttf"),
        new BundledFace(
            new FontKey("EB Garamond", FontWeight.Bold, FontStyleSlant.Italic),
            "EBGaramond-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Crimson Pro", FontWeight.Regular, FontStyleSlant.Normal),
            "CrimsonPro-Regular.ttf"),
        new BundledFace(
            new FontKey("Crimson Pro", FontWeight.Bold, FontStyleSlant.Normal),
            "CrimsonPro-Bold.ttf"),
        new BundledFace(
            new FontKey("Crimson Pro", FontWeight.Regular, FontStyleSlant.Italic),
            "CrimsonPro-Italic.ttf"),
        new BundledFace(
            new FontKey("Crimson Pro", FontWeight.Bold, FontStyleSlant.Italic),
            "CrimsonPro-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Libre Baskerville", FontWeight.Regular, FontStyleSlant.Normal),
            "LibreBaskerville-Regular.ttf"),
        new BundledFace(
            new FontKey("Libre Baskerville", FontWeight.Bold, FontStyleSlant.Normal),
            "LibreBaskerville-Bold.ttf"),
        new BundledFace(
            new FontKey("Libre Baskerville", FontWeight.Regular, FontStyleSlant.Italic),
            "LibreBaskerville-Italic.ttf"),
        new BundledFace(
            new FontKey("PT Serif", FontWeight.Regular, FontStyleSlant.Normal),
            "PTSerif-Regular.ttf"),
        new BundledFace(
            new FontKey("PT Serif", FontWeight.Bold, FontStyleSlant.Normal),
            "PTSerif-Bold.ttf"),
        new BundledFace(
            new FontKey("PT Serif", FontWeight.Regular, FontStyleSlant.Italic),
            "PTSerif-Italic.ttf"),
        new BundledFace(
            new FontKey("PT Serif", FontWeight.Bold, FontStyleSlant.Italic),
            "PTSerif-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Lora", FontWeight.Regular, FontStyleSlant.Normal),
            "Lora-Regular.ttf"),
        new BundledFace(
            new FontKey("Lora", FontWeight.Bold, FontStyleSlant.Normal),
            "Lora-Bold.ttf"),
        new BundledFace(
            new FontKey("Lora", FontWeight.Regular, FontStyleSlant.Italic),
            "Lora-Italic.ttf"),
        new BundledFace(
            new FontKey("Lora", FontWeight.Bold, FontStyleSlant.Italic),
            "Lora-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Libre Caslon Text", FontWeight.Regular, FontStyleSlant.Normal),
            "LibreCaslonText-Regular.ttf"),
        new BundledFace(
            new FontKey("Libre Caslon Text", FontWeight.Bold, FontStyleSlant.Normal),
            "LibreCaslonText-Bold.ttf"),
        new BundledFace(
            new FontKey("Libre Caslon Text", FontWeight.Regular, FontStyleSlant.Italic),
            "LibreCaslonText-Italic.ttf"),
        new BundledFace(
            new FontKey("Libre Caslon Text", FontWeight.Bold, FontStyleSlant.Italic),
            "LibreCaslonText-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Source Sans 3", FontWeight.Regular, FontStyleSlant.Normal),
            "SourceSans3-Regular.ttf"),
        new BundledFace(
            new FontKey("Source Sans 3", FontWeight.Bold, FontStyleSlant.Normal),
            "SourceSans3-Bold.ttf"),
        new BundledFace(
            new FontKey("Source Sans 3", FontWeight.Regular, FontStyleSlant.Italic),
            "SourceSans3-It.ttf"),
        new BundledFace(
            new FontKey("Source Sans 3", FontWeight.Bold, FontStyleSlant.Italic),
            "SourceSans3-BoldIt.ttf"),
        new BundledFace(
            new FontKey("Open Sans", FontWeight.Regular, FontStyleSlant.Normal),
            "OpenSans-Regular.ttf"),
        new BundledFace(
            new FontKey("Open Sans", FontWeight.Bold, FontStyleSlant.Normal),
            "OpenSans-Bold.ttf"),
        new BundledFace(
            new FontKey("Open Sans", FontWeight.Regular, FontStyleSlant.Italic),
            "OpenSans-Italic.ttf"),
        new BundledFace(
            new FontKey("Open Sans", FontWeight.Bold, FontStyleSlant.Italic),
            "OpenSans-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Lato", FontWeight.Regular, FontStyleSlant.Normal),
            "Lato-Regular.ttf"),
        new BundledFace(
            new FontKey("Lato", FontWeight.Bold, FontStyleSlant.Normal),
            "Lato-Bold.ttf"),
        new BundledFace(
            new FontKey("Lato", FontWeight.Regular, FontStyleSlant.Italic),
            "Lato-Italic.ttf"),
        new BundledFace(
            new FontKey("Lato", FontWeight.Bold, FontStyleSlant.Italic),
            "Lato-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Libre Franklin", FontWeight.Regular, FontStyleSlant.Normal),
            "LibreFranklin-Regular.ttf"),
        new BundledFace(
            new FontKey("Libre Franklin", FontWeight.Bold, FontStyleSlant.Normal),
            "LibreFranklin-Bold.ttf"),
        new BundledFace(
            new FontKey("Libre Franklin", FontWeight.Regular, FontStyleSlant.Italic),
            "LibreFranklin-Italic.ttf"),
        new BundledFace(
            new FontKey("Libre Franklin", FontWeight.Bold, FontStyleSlant.Italic),
            "LibreFranklin-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("PT Sans", FontWeight.Regular, FontStyleSlant.Normal),
            "PTSans-Regular.ttf"),
        new BundledFace(
            new FontKey("PT Sans", FontWeight.Bold, FontStyleSlant.Normal),
            "PTSans-Bold.ttf"),
        new BundledFace(
            new FontKey("PT Sans", FontWeight.Regular, FontStyleSlant.Italic),
            "PTSans-Italic.ttf"),
        new BundledFace(
            new FontKey("PT Sans", FontWeight.Bold, FontStyleSlant.Italic),
            "PTSans-BoldItalic.ttf"),
        new BundledFace(
            new FontKey("Cinzel", FontWeight.Regular, FontStyleSlant.Normal),
            "Cinzel-Regular.ttf"),
        new BundledFace(
            new FontKey("Cinzel", FontWeight.Bold, FontStyleSlant.Normal),
            "Cinzel-Bold.ttf"),
        new BundledFace(
            new FontKey("Playfair Display", FontWeight.Regular, FontStyleSlant.Normal),
            "PlayfairDisplay-Regular.ttf"),
        new BundledFace(
            new FontKey("Playfair Display", FontWeight.Bold, FontStyleSlant.Normal),
            "PlayfairDisplay-Bold.ttf"),
        new BundledFace(
            new FontKey("Cormorant Garamond", FontWeight.Regular, FontStyleSlant.Normal),
            "CormorantGaramond-Regular.ttf"),
        new BundledFace(
            new FontKey("Cormorant Garamond", FontWeight.Bold, FontStyleSlant.Normal),
            "CormorantGaramond-Bold.ttf"),
        new BundledFace(
            new FontKey("Bitter", FontWeight.Regular, FontStyleSlant.Normal),
            "Bitter-Regular.ttf"),
        new BundledFace(
            new FontKey("Bitter", FontWeight.Bold, FontStyleSlant.Normal),
            "Bitter-Bold.ttf"),
        new BundledFace(
            new FontKey("Marcellus", FontWeight.Regular, FontStyleSlant.Normal),
            "Marcellus-Regular.ttf"),
        new BundledFace(
            new FontKey("Cinzel Decorative", FontWeight.Regular, FontStyleSlant.Normal),
            "CinzelDecorative-Regular.ttf"),
        new BundledFace(
            new FontKey("Great Vibes", FontWeight.Regular, FontStyleSlant.Normal),
            "GreatVibes-Regular.ttf"),
        new BundledFace(
            new FontKey("UnifrakturMaguntia", FontWeight.Regular, FontStyleSlant.Normal),
            "UnifrakturMaguntia-Regular.ttf"),
    ];

    /// <summary>Every bundled family, in the order the picker lists them.</summary>
    public static IReadOnlyList<FontFamilyInfo> Families => FamilyTable;

    /// <summary>Every bundled face, grouped by family in <see cref="Families"/> order.</summary>
    public static IReadOnlyList<BundledFace> Faces => FaceTable;

    /// <summary>Family names only, in display order. The set a document is audited against.</summary>
    public static IReadOnlyList<string> FamilyNames { get; } =
        FamilyTable.Select(f => f.Family).ToArray();

    private static readonly Dictionary<string, FontFamilyInfo> ByName =
        FamilyTable.ToDictionary(f => f.Family, StringComparer.Ordinal);

    /// <summary>True when this build bundles the named family.</summary>
    public static bool Contains(string family) => ByName.ContainsKey(family);

    /// <summary>The family's metadata, or null when this build does not bundle it.</summary>
    public static FontFamilyInfo? Find(string family) =>
        ByName.TryGetValue(family, out FontFamilyInfo? info) ? info : null;

    /// <summary>The families in one picker group, in display order.</summary>
    public static IEnumerable<FontFamilyInfo> InCategory(FontCategory category) =>
        FamilyTable.Where(f => f.Category == category);

    /// <summary>The faces one family bundles, in Regular/Bold/Italic/BoldItalic order.</summary>
    public static IEnumerable<BundledFace> FacesOf(string family) =>
        FaceTable.Where(f => f.Key.Family == family);

    /// <summary>The plain-language heading the picker puts above a group (PLAN.md §6).</summary>
    public static string CategoryLabel(FontCategory category) => category switch
    {
        FontCategory.SerifText => "Fonts for reading",
        FontCategory.SansText => "Plain fonts, without the little feet",
        FontCategory.Display => "Fonts for titles and headings",
        FontCategory.Accent => "Fancy fonts, for a line or two",
        _ => "Other fonts",
    };
}
