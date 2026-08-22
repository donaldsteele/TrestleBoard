namespace TrestleBoard.Site;

/// <summary>
/// One page of the project site.
///
/// <para><b>Every page is declared here and nowhere else.</b> A static site's usual failure is that
/// the navigation is copied into every file and then drifts — one page gains a section and six
/// forget it. Here the rail is built from this list, so a page that is not in it cannot be linked
/// to and a page that is in it cannot be missed.</para>
/// </summary>
/// <param name="Slug">
/// The directory under the site root, "" for the home page. Written out as <c>slug/index.html</c>
/// so every URL ends in a slash and no visitor ever sees ".html".
/// </param>
/// <param name="Title">What the browser tab says, before " — TrestleBoard" is appended.</param>
/// <param name="Heading">The page's own h1. Often shorter than the title.</param>
/// <param name="Description">
/// One sentence for the meta description and for the rail's own tooltip. Written from the reader's
/// side of the screen — what they will find, not what the page contains.
/// </param>
/// <param name="RailLabel">
/// What the left rail calls this page, or null for a page that is reachable but not railed — the
/// help topics, which have their own list on the help page.
/// </param>
/// <param name="Parent">
/// The railed page this one belongs under, so a help topic marks "Help" as the current section.
/// Null for a top-level page.
/// </param>
/// <param name="ContentFile">
/// The hand-written fragment under <c>site/content/</c>, or null for a page whose body is generated
/// — which is exactly one page, the command reference.
/// </param>
internal sealed record SitePage(
    string Slug,
    string Title,
    string Heading,
    string Description,
    string? RailLabel = null,
    string? Parent = null,
    string? ContentFile = null)
{
    /// <summary>Where this page is written, relative to the output root.</summary>
    internal string OutputPath =>
        Slug.Length == 0 ? "index.html" : $"{Slug}/index.html";

    /// <summary>The href another page uses to reach this one, from the site root.</summary>
    internal string Href => Slug.Length == 0 ? "/" : $"/{Slug}/";

    /// <summary>
    /// How many directories deep this page sits, which is how many "../" a relative asset link
    /// needs. Relative rather than absolute so the whole site can be opened from a folder — which
    /// is how it gets checked before it is deployed, and how somebody with no web server reads it.
    /// </summary>
    internal string ToRoot =>
        Slug.Length == 0 ? "./" : string.Concat(Enumerable.Repeat("../", Slug.Count(c => c == '/') + 1));
}
