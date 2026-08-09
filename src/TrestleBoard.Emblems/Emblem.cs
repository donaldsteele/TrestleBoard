namespace TrestleBoard.Emblems;

/// <summary>
/// One stroke or filled shape of an emblem, in the emblem's own coordinates.
/// </summary>
/// <param name="PathData">
/// SVG path data — the one piece of SVG this app understands, parsed by Skia rather than by a
/// document reader. There is no <c>&lt;svg&gt;</c> file anywhere: an emblem is geometry in the
/// source, which is what lets it be hashed like a font face and rendered with no new dependency.
/// </param>
/// <param name="StrokeWidth">
/// Zero to fill the path; otherwise the pen width, with round caps and joins. Most of the craft's
/// tools are drawn rather than filled — a plumb rule is a line and a weight — and stroking is what
/// makes them authorable as geometry a person can read in a diff.
/// </param>
public sealed record EmblemPart(string PathData, double StrokeWidth = 0);

/// <summary>
/// One emblem: what it is called, what a screen reader should say about it, and how to draw it
/// (PLAN.md §11 M65).
/// </summary>
/// <param name="Id">Stable, never shown. The manifest key and the entry in the picker's tests.</param>
/// <param name="Name">What the picker shows — plain, and what a Mason would call it.</param>
/// <param name="Description">
/// The default alt text. Every emblem carries one so a picture inserted from the shelf is described
/// before anybody thinks to describe it — the acceptance says so, and M23's "Describe this picture"
/// remains available to change it.
/// </param>
/// <param name="Category">The picker's heading. See <see cref="EmblemLibrary"/>.</param>
/// <param name="SearchWords">What somebody might type instead of the name.</param>
/// <param name="Width">Viewbox width. Most emblems are square; the rules and borders are not.</param>
/// <param name="Height">Viewbox height.</param>
/// <param name="Parts">Drawn in order, back to front.</param>
public sealed record Emblem(
    string Id,
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> SearchWords,
    double Width,
    double Height,
    IReadOnlyList<EmblemPart> Parts)
{
    /// <summary>Width over height. The picker and the inserted frame both keep it.</summary>
    public double AspectRatio => Width / Height;
}
