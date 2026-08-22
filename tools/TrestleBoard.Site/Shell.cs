using System.Globalization;
using System.Text;

namespace TrestleBoard.Site;

/// <summary>
/// The frame every page is written into: the head, the masthead, the left rail and the footer.
///
/// <para><b>THE RAIL IS THE SITE'S SIGNATURE AND IT IS BORROWED FROM THE APPLICATION.</b> M76 gave
/// the editor a left-hand rail of miniature pages with the current one marked by an accent bar
/// carrying the lodge's gold notch. The site navigates the same way and marks the current section
/// the same way, under the same rule the palette has enforced since M16: the gold is 2.41:1 on
/// white and may never carry meaning on a light ground, so it appears only ON the navy. A site that
/// documents an application ought to move the way the application moves.</para>
///
/// <para>Marked three ways, because the app's own rule is that colour is never the only signal: the
/// bar is a shape as well as a colour, the label goes bold, and the anchor carries
/// <c>aria-current="page"</c> so a screen reader is told the same fact.</para>
/// </summary>
internal static class Shell
{
    internal static string Render(SitePage page, string body)
    {
        ArgumentNullException.ThrowIfNull(page);

        string root = page.ToRoot;
        var html = new StringBuilder();

        html.Append(
            CultureInfo.InvariantCulture,
            $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Html.Escape(page.Title)} — TrestleBoard</title>
            <meta name="description" content="{Html.Escape(page.Description)}">
            <meta name="color-scheme" content="light dark">
            <link rel="icon" href="{root}images/favicon.png">
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Cinzel:wght@400..700&family=EB+Garamond:ital,wght@0,400..600;1,400&family=Source+Sans+3:ital,wght@0,300..700;1,400&display=swap">
            <link rel="stylesheet" href="{root}assets/site.css">
            </head>
            <body>
            <a class="skip" href="#main">Skip to the page</a>

            <header class="masthead">
              <a class="wordmark" href="{root}">{Emblems.Svg("square-and-compasses", "wordmark-emblem")}<span>TrestleBoard</span></a>
              <nav class="masthead-links" aria-label="Elsewhere">
                <a class="download-pill" href="{root}install/">Download</a>
                <a href="{Site.Repository}">Source on GitHub</a>
              </nav>
            </header>

            <div class="frame">
            <nav class="rail" aria-label="Sections of this site">
              <p class="rail-heading">This site</p>
              <ul>

            """);

        foreach (SitePage railed in Site.Railed)
        {
            bool current =
                string.Equals(railed.Slug, page.Slug, StringComparison.Ordinal)
                || string.Equals(railed.Slug, page.Parent, StringComparison.Ordinal);

            html.Append(
                CultureInfo.InvariantCulture,
                $"""
                    <li class="{(current ? "rail-item is-current" : "rail-item")}">
                      <a href="{root}{railed.Slug}{(railed.Slug.Length == 0 ? "" : "/")}"{(current ? " aria-current=\"page\"" : "")}>
                        <span class="rail-mark" aria-hidden="true"></span>
                        <span class="rail-label">{Html.Escape(railed.RailLabel!)}</span>
                      </a>
                    </li>

                """);
        }

        html.Append(
            CultureInfo.InvariantCulture,
            $"""
              </ul>
            </nav>

            <main id="main">
            <h1 class="page-title">{Html.Escape(page.Heading)}</h1>
            {body}
            </main>
            </div>

            <footer class="foot">
              <p class="foot-line">
                TrestleBoard is free for lodges, churches, schools, charities and people at home,
                under the <a href="{Site.Repository}/blob/main/LICENSE">PolyForm Noncommercial
                Licence 1.0.0</a>. Making money with it needs a separate licence —
                <a href="{Site.Repository}/issues">ask for one</a>.
              </p>
              <p class="foot-line foot-quiet">
                Built for Indian Land Lodge No. 414. Every name, telephone number and photograph on
                this site is invented — the screenshots are generated against fictional records, and
                no member's details have ever been in this repository.
              </p>
            </footer>

            <script src="{root}assets/site.js" defer></script>
            </body>
            </html>

            """);

        return html.ToString();
    }
}
