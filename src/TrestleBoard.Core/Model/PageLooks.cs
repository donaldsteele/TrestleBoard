namespace TrestleBoard.Core.Model;

/// <summary>
/// The few fixed looks a committee can put on the page (PLAN.md §11 M79).
///
/// <para><b>Fixed, and that is the whole design.</b> "Put a line under that heading" and "put a box
/// round the fish-fry notice" are the two layout requests a committee actually makes, and the
/// obvious way to serve them — a colour picker and a width box — is the way that produces a
/// newsletter with four kinds of border on it. What is offered instead is a small number of looks
/// that were chosen once, here, against the white of the page.</para>
///
/// <para><b>These are page colours, not chrome colours.</b> The palette in the App layer changes
/// with the user's theme; the page does not. A newsletter looks the same whether it is being edited
/// in the dark theme or the light one, because it is going to be printed either way — so these
/// values live in the model, beside the document they describe, and no theme reaches them.</para>
/// </summary>
public static class PageLooks
{
    /// <summary>
    /// The thinnest line that survives being looked at. <b>Not half a point.</b> A hairline is
    /// invisible at the 77% zoom the app opens at and can vanish entirely through a photocopier,
    /// which is what half this newsletter's readership is holding.
    /// </summary>
    public const float MinimumStrokeWidthPt = 1f;

    /// <summary>What a border round a frame is drawn at.</summary>
    public const float BorderWidthPt = 1f;

    /// <summary>What a line across the page is drawn at — heavier, because it is the whole point of
    /// its own block rather than the edge of something else.</summary>
    public const float RuleWidthPt = 1.5f;

    /// <summary>How tall the block holding a line across the page is. The line is drawn across the
    /// middle of it; the height is what the user's pointer has to take hold of.</summary>
    public const float RuleBlockHeightPt = 8f;

    /// <summary>
    /// The grey a border and a rule are drawn in. Not black: a black box round a notice competes
    /// with the writing inside it, and every rule in the sample newsletter has been this grey since
    /// M3.
    /// </summary>
    public const uint RuleArgb = 0xFF9AA5B8;

    /// <summary>
    /// The tint behind a shaded notice. Pale enough that black body text on it clears 4.5:1 by a
    /// wide margin — the shading marks the notice, it does not have to be seen from across the
    /// room, and a tint dark enough to notice at a glance is a tint that makes the writing on it
    /// hard to read (PLAN.md §6).
    /// </summary>
    public const uint ShadeArgb = 0xFFEDF0F5;

    /// <summary>
    /// The lodge's own navy, for a heading that wants to be the lodge's rather than merely dark.
    /// The same value the chrome calls Accent, because it is the same brand colour — but written
    /// here as page ink, since a heading printed in it is not chrome and does not follow a theme.
    /// </summary>
    public const uint LodgeInkArgb = 0xFF1E335C;

    /// <summary>The name of the frame style a bordered frame points at.</summary>
    public const string BorderStyleName = "frame-border";

    /// <summary>The name of the frame style a shaded frame points at.</summary>
    public const string ShadeStyleName = "frame-shade";

    /// <summary>The name of the frame style that is both.</summary>
    public const string BorderAndShadeStyleName = "frame-border-shade";

    /// <summary>
    /// The frame style for a given combination, or null when the answer is "nothing at all" — which
    /// is stored as no reference rather than as a style that draws nothing.
    /// </summary>
    public static string? StyleNameFor(bool border, bool shade) => (border, shade) switch
    {
        (true, true) => BorderAndShadeStyleName,
        (true, false) => BorderStyleName,
        (false, true) => ShadeStyleName,
        _ => null,
    };

    /// <summary>The definition one of those three names stands for.</summary>
    public static FrameStyleDef Define(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        bool border = name is BorderStyleName or BorderAndShadeStyleName;
        bool shade = name is ShadeStyleName or BorderAndShadeStyleName;
        return new FrameStyleDef
        {
            Name = name,
            StrokeArgb = border ? RuleArgb : null,
            StrokeWidthPt = border ? BorderWidthPt : 0f,
            FillArgb = shade ? ShadeArgb : null,

            // Room between the line and the words. Without it a border sits hard against the text
            // and reads as a mistake rather than as a box.
            PaddingPt = 6f,
        };
    }

    /// <summary>Whether a frame style reference means a border is on.</summary>
    public static bool HasBorder(string? styleRef) =>
        styleRef is BorderStyleName or BorderAndShadeStyleName;

    /// <summary>Whether a frame style reference means shading is on.</summary>
    public static bool HasShade(string? styleRef) =>
        styleRef is ShadeStyleName or BorderAndShadeStyleName;
}
