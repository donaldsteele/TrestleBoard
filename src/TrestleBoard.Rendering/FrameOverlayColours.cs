namespace TrestleBoard.Rendering;

/// <summary>
/// The five colours the selection chrome is drawn in (M33 review §14.3).
///
/// <para>These were five <c>const uint</c> literals inside <see cref="FrameOverlayRenderer"/>, and
/// they were the one part of the interface that did not follow the theme. Everything else in the
/// window moved to palette tokens at M16 — for the exact reason PLAN.md §6 gives, that High
/// Contrast raises every contrast floor to 7:1 — while the selection outline, the resize handles,
/// the snap guides and the overset badge stayed a mid blue, a mid pink and a mid red on a black
/// page. A user who turned High Contrast on because they could not see the interface still could
/// not see the part of it that says <em>what is selected</em>.</para>
///
/// <para>They are passed in rather than read from a palette here because
/// <c>TrestleBoard.Rendering</c> knows nothing about Avalonia or about themes, and PLAN.md §1 keeps
/// it that way: the shell knows which theme is on, so the shell says.</para>
/// </summary>
/// <param name="Selection">The outline round the chosen frame, and the link badge.</param>
/// <param name="HandleFill">Inside the eight resize handles; the outline strokes their edges.</param>
/// <param name="SnapGuide">The dashed line shown while a drag is snapping.</param>
/// <param name="Overset">The badge saying more text is in this frame than fits.</param>
/// <param name="LinkTarget">The wash over frames a link could be completed onto.</param>
public readonly record struct FrameOverlayColours(
    uint Selection,
    uint HandleFill,
    uint SnapGuide,
    uint Overset,
    uint LinkTarget)
{
    /// <summary>Light and Dark, which share these: the page under them is white in both.</summary>
    public static FrameOverlayColours Default { get; } = new(
        FrameOverlayRenderer.SelectionArgb,
        FrameOverlayRenderer.HandleFillArgb,
        FrameOverlayRenderer.SnapGuideArgb,
        FrameOverlayRenderer.OversetArgb,
        FrameOverlayRenderer.LinkTargetArgb);

    /// <summary>
    /// High Contrast. Black and white only, because that is what the mode means — and every one of
    /// these marks is a SHAPE as well as a colour (a rectangle, eight squares, a dashed line, a
    /// badge with a glyph in it), so nothing is lost by removing the hue. That is PLAN.md §6's
    /// "colour is never the only signal" doing the work it was written for.
    ///
    /// <para>The handles invert — black fill inside the white outline — so eight solid white
    /// squares on a white outline do not read as one thick line.</para>
    /// </summary>
    public static FrameOverlayColours HighContrast { get; } = new(
        Selection: 0xFFFFFFFF,
        HandleFill: 0xFF000000,
        SnapGuide: 0xFFFFFFFF,
        Overset: 0xFFFFFFFF,
        LinkTarget: 0x66FFFFFF);
}
