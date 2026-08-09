namespace TrestleBoard.Emblems;

/// <summary>
/// The cabinet (PLAN.md §11 M65).
///
/// <para>Until now a committee wanting the square and compasses on the cover had to find an image
/// somewhere, judge whether they were allowed to use it, and get it into the app. Every one of
/// those three steps is a place to go wrong, and the third one is where a 90-pixel JPEG from a
/// search result ends up printed four inches wide.</para>
///
/// <para><b>Deliberately wide rather than minimal</b> — the owner's direction. A shelf with three
/// things on it sends people back to the search engine, which defeats the point.</para>
///
/// <para><b>Every emblem is geometry in this file.</b> No SVG files, no PNGs in the repository:
/// each one is a handful of path strings, which is what makes them diffable, hashable like a font
/// face (gate 22) and renderable at any size with no new dependency. It is also what makes the
/// provenance simple to state honestly — see <c>assets-src/emblems/EMBLEMS-PROVENANCE.txt</c>.</para>
/// </summary>
public static class EmblemLibrary
{
    /// <summary>The tools of the craft, as a trestle board uses them.</summary>
    public const string WorkingTools = "The working tools";

    /// <summary>What stands in the lodge room.</summary>
    public const string TheLodge = "The lodge";

    /// <summary>Light, and the things that mark a season.</summary>
    public const string LightsAndSeasons = "Lights and seasons";

    /// <summary>Rules, borders and corners — for dressing a page rather than saying anything.</summary>
    public const string Ornaments = "Ornaments";

    // ---- shared geometry ----------------------------------------------------------------------------
    //
    // The compasses and the square appear alone and together, and the emblem everybody knows is the
    // two of them interlocked. Drawn once each, so the pair can never drift from its halves.

    private static IReadOnlyList<EmblemPart> Compasses { get; } =
    [
        new EmblemPart("M500,150 m-62,0 a62,62 0 1,0 124,0 a62,62 0 1,0 -124,0"),
        new EmblemPart("M500,170 L268,790", 58),
        new EmblemPart("M500,170 L732,790", 58),
        new EmblemPart("M232,760 L268,860 L304,760 Z"),
        new EmblemPart("M696,760 L732,860 L768,760 Z"),
    ];

    private static IReadOnlyList<EmblemPart> Square { get; } =
    [
        new EmblemPart("M150,480 L500,865 L850,480", 76),
    ];

    private static IReadOnlyList<EmblemPart> LetterG { get; } =
    [
        new EmblemPart("M598,491 A120,120 0 1,0 598,629 L598,566 L516,566", 38),
    ];

    /// <summary>
    /// Every emblem, grouped in the order the picker shows them: the ones somebody came for first.
    /// </summary>
    public static IReadOnlyList<Emblem> All { get; } =
    [
        // ---- The working tools ------------------------------------------------------------------
        new("square-and-compasses-g",
            "Square and compasses, with the G",
            "The square and compasses with the letter G between them.",
            WorkingTools,
            ["square", "compass", "compasses", "emblem", "logo", "g", "masonic", "freemason"],
            1000, 1000,
            [
                .. Compasses,
                .. Square,
                .. LetterG,
            ]),

        new("square-and-compasses",
            "Square and compasses",
            "The square and compasses.",
            WorkingTools,
            ["square", "compass", "compasses", "emblem", "logo", "masonic", "freemason"],
            1000, 1000,
            [
                .. Compasses,
                .. Square,
            ]),

        new("square",
            "The square",
            "A mason's square.",
            WorkingTools,
            ["square", "tool", "right angle"],
            1000, 1000,
            [
                new EmblemPart("M170,150 H830 V300 H320 V850 H170 Z"),
            ]),

        new("compasses",
            "The compasses",
            "A pair of compasses.",
            WorkingTools,
            ["compass", "compasses", "dividers", "tool"],
            1000, 1000,
            [.. Compasses]),

        new("plumb",
            "The plumb",
            "A plumb rule, with its line and weight.",
            WorkingTools,
            ["plumb", "plumb rule", "upright", "tool", "vertical"],
            1000, 1000,
            [
                new EmblemPart("M250,130 H750 V220 H250 Z"),
                new EmblemPart("M500,220 V690", 26),
                new EmblemPart("M430,690 H570 L500,880 Z"),
            ]),

        new("level",
            "The level",
            "A level: an A-frame with a plumb line hanging from its point.",
            WorkingTools,
            ["level", "equality", "tool", "a-frame"],
            1000, 1000,
            [
                new EmblemPart("M170,790 L500,220 L830,790", 58),
                new EmblemPart("M320,560 H680", 46),
                new EmblemPart("M500,250 V700", 20),
                new EmblemPart("M440,700 H560 L500,860 Z"),
            ]),

        new("trowel",
            "The trowel",
            "A trowel.",
            WorkingTools,
            ["trowel", "cement", "tool", "spread"],
            1000, 1000,
            [
                new EmblemPart("M100,600 L470,250 L800,470 L430,880 Z"),
                new EmblemPart("M610,390 L830,180", 58),
                new EmblemPart("M880,130 m-78,0 a78,78 0 1,0 156,0 a78,78 0 1,0 -156,0"),
            ]),

        new("gavel",
            "The gavel",
            "A gavel.",
            WorkingTools,
            ["gavel", "hammer", "mallet", "master", "tool"],
            1000, 1000,
            [
                new EmblemPart("M195,185 H805 L755,390 H245 Z"),
                new EmblemPart("M500,390 V870", 62),
            ]),

        new("gauge",
            "The twenty-four inch gauge",
            "A twenty-four inch gauge, marked off along its length.",
            WorkingTools,
            ["gauge", "rule", "ruler", "measure", "twenty-four", "24", "tool"],
            1000, 400,
            [
                new EmblemPart("M40,140 H960 V300 H40 Z", 26),
                new EmblemPart("M155,140 V215", 20),
                new EmblemPart("M270,140 V240", 20),
                new EmblemPart("M385,140 V215", 20),
                new EmblemPart("M500,140 V255", 20),
                new EmblemPart("M615,140 V215", 20),
                new EmblemPart("M730,140 V240", 20),
                new EmblemPart("M845,140 V215", 20),
            ]),

        // ---- The lodge --------------------------------------------------------------------------
        new("ashlars",
            "The two ashlars",
            "The rough ashlar and the perfect ashlar, side by side.",
            TheLodge,
            ["ashlar", "ashlars", "stone", "rough", "perfect", "cube"],
            1000, 1000,
            [
                new EmblemPart("M115,435 L305,470 L280,810 L140,745 Z", 30),
                new EmblemPart("M115,435 L245,290 L460,330 L305,470 Z", 30),
                new EmblemPart("M305,470 L460,330 L475,660 L280,810 Z", 30),
                new EmblemPart("M175,545 L235,600 L190,665", 22),
                new EmblemPart("M545,295 H890 V755 H545 Z", 32),
                new EmblemPart("M545,295 L620,215 H965 L890,295", 32),
                new EmblemPart("M890,755 L965,680 V215", 32),
            ]),

        new("pillars",
            "The two pillars",
            "The two pillars of the porch, each crowned with a globe.",
            TheLodge,
            ["pillar", "pillars", "column", "columns", "boaz", "jachin", "porch"],
            1000, 1000,
            [
                new EmblemPart("M250,205 m-82,0 a82,82 0 1,0 164,0 a82,82 0 1,0 -164,0"),
                new EmblemPart("M110,300 H390 V380 H110 Z"),
                new EmblemPart("M165,380 H335 V820 H165 Z", 28),
                new EmblemPart("M85,820 H415 V900 H85 Z"),

                new EmblemPart("M750,205 m-82,0 a82,82 0 1,0 164,0 a82,82 0 1,0 -164,0"),
                new EmblemPart("M610,300 H890 V380 H610 Z"),
                new EmblemPart("M665,380 H835 V820 H665 Z", 28),
                new EmblemPart("M585,820 H915 V900 H585 Z"),
            ]),

        new("mosaic-pavement",
            "The mosaic pavement",
            "A chequered border of light and dark squares.",
            TheLodge,
            ["mosaic", "pavement", "chequer", "checker", "floor", "border", "squares"],
            1000, 200,
            [
                new EmblemPart("M20,40 H980 V180 H20 Z", 16),
                new EmblemPart("M20,40 H140 V180 H20 Z"),
                new EmblemPart("M260,40 H380 V180 H260 Z"),
                new EmblemPart("M500,40 H620 V180 H500 Z"),
                new EmblemPart("M740,40 H860 V180 H740 Z"),
                new EmblemPart("M140,40 V180", 16),
                new EmblemPart("M380,40 V180", 16),
                new EmblemPart("M620,40 V180", 16),
                new EmblemPart("M860,40 V180", 16),
            ]),

        // ---- Lights and seasons -------------------------------------------------------------------
        new("all-seeing-eye",
            "The all-seeing eye",
            "An eye within a triangle.",
            LightsAndSeasons,
            ["eye", "all seeing", "triangle", "providence", "watch"],
            1000, 1000,
            [
                new EmblemPart("M500,130 L900,820 H100 Z", 44),
                new EmblemPart("M320,560 Q500,410 680,560 Q500,710 320,560 Z", 30),
                new EmblemPart("M500,560 m-58,0 a58,58 0 1,0 116,0 a58,58 0 1,0 -116,0"),
            ]),

        new("blazing-star",
            "The blazing star",
            "A five-pointed star.",
            LightsAndSeasons,
            ["star", "blazing", "five", "point", "light"],
            1000, 1000,
            [
                new EmblemPart(
                    "M500,100 L594,371 L880,376 L652,549 L735,824 L500,660 L265,824 L348,549 "
                    + "L120,376 L406,371 Z"),
            ]),

        new("sun",
            "The sun",
            "The sun, with its rays.",
            LightsAndSeasons,
            ["sun", "day", "light", "summer", "rays"],
            1000, 1000,
            [
                new EmblemPart("M500,500 m-230,0 a230,230 0 1,0 460,0 a230,230 0 1,0 -460,0"),
                new EmblemPart("M500,60 V190", 34),
                new EmblemPart("M500,810 V940", 34),
                new EmblemPart("M60,500 H190", 34),
                new EmblemPart("M810,500 H940", 34),
                new EmblemPart("M189,189 L281,281", 34),
                new EmblemPart("M719,719 L811,811", 34),
                new EmblemPart("M811,189 L719,281", 34),
                new EmblemPart("M281,719 L189,811", 34),
            ]),

        new("moon-and-stars",
            "The moon and stars",
            "A crescent moon with three small stars.",
            LightsAndSeasons,
            ["moon", "crescent", "night", "stars", "winter"],
            1000, 1000,
            [
                // Drawn as one outline rather than a circle with a circle taken out of it. Two
                // circles is the textbook way and it was wrong twice here: an arc bulges where its
                // radius says it must, and the shape that results is a ring with a bite rather than
                // a crescent. The two edges are stated directly instead, so the drawing is the
                // thing that was intended and not the outcome of an arithmetic guess.
                new EmblemPart(
                    "M640,170 C300,220 130,340 130,500 C130,660 300,780 640,830 "
                    + "C420,700 420,300 640,170 Z"),
                new EmblemPart("M810,220 L838,300 L918,300 L853,349 L878,428 L810,380 L742,428 "
                    + "L767,349 L702,300 L782,300 Z"),
                new EmblemPart("M880,520 L899,574 L954,574 L910,607 L927,661 L880,628 L833,661 "
                    + "L850,607 L806,574 L861,574 Z"),
                new EmblemPart("M770,760 L786,806 L834,806 L795,834 L810,880 L770,852 L730,880 "
                    + "L745,834 L706,806 L754,806 Z"),
            ]),

        // ---- Ornaments ------------------------------------------------------------------------------
        new("rule-diamond",
            "A rule with a diamond",
            "A horizontal rule with a diamond at its centre.",
            Ornaments,
            ["rule", "line", "divider", "separator", "diamond", "ornament"],
            1000, 140,
            [
                new EmblemPart("M20,70 H390", 16),
                new EmblemPart("M610,70 H980", 16),
                new EmblemPart("M500,12 L578,70 L500,128 L422,70 Z"),
                new EmblemPart("M395,70 m-24,0 a24,24 0 1,0 48,0 a24,24 0 1,0 -48,0"),
                new EmblemPart("M605,70 m-24,0 a24,24 0 1,0 48,0 a24,24 0 1,0 -48,0"),
            ]),

        new("rule-plain",
            "A plain rule",
            "A horizontal rule, thick above and thin below.",
            Ornaments,
            ["rule", "line", "divider", "separator", "ornament", "plain"],
            1000, 100,
            [
                new EmblemPart("M20,36 H980", 22),
                new EmblemPart("M20,72 H980", 8),
            ]),

        new("corner-ornament",
            "A corner ornament",
            "A curling ornament for the corner of a page.",
            Ornaments,
            ["corner", "ornament", "flourish", "scroll", "decoration"],
            1000, 1000,
            [
                new EmblemPart("M90,910 Q90,90 910,90", 42),
                new EmblemPart("M250,910 Q250,250 910,250", 20),
                new EmblemPart("M910,90 m-46,0 a46,46 0 1,0 92,0 a46,46 0 1,0 -92,0"),
                new EmblemPart("M90,910 m-46,0 a46,46 0 1,0 92,0 a46,46 0 1,0 -92,0"),
            ]),
    ];

    /// <summary>Every category, in the order the picker lays them out.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        [WorkingTools, TheLodge, LightsAndSeasons, Ornaments];

    /// <summary>One emblem by id, or null when it is not on the shelf.</summary>
    public static Emblem? Find(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// What matches what the user typed, in shelf order. An empty box returns everything, because
    /// the picker doubles as a way to see what is on the shelf at all.
    ///
    /// <para>All the words must match: someone typing "corner ornament" means both.</para>
    /// </summary>
    public static IReadOnlyList<Emblem> Search(string? query)
    {
        string[] words = Words(query);
        return words.Length == 0
            ? All
            : [.. All.Where(e => words.All(w => Matches(e, w)))];
    }

    private static bool Matches(Emblem emblem, string word) =>
        Words(emblem.Name).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        || emblem.SearchWords.Any(s => Words(s).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase)))
        || Words(emblem.Category).Any(w => w.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    private static string[] Words(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(
                [' ', '\t', '\n', '\r', ',', '.', ';', ':', '-', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
