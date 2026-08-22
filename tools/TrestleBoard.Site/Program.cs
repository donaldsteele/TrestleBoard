using System.Globalization;

namespace TrestleBoard.Site;

/// <summary>
/// Builds the project site into <c>site/_site</c> (PLAN.md has no milestone for this; it is the
/// project's public face rather than part of the application).
///
/// <code>
///   dotnet run --project tools/TrestleBoard.Site
///   dotnet run --project tools/TrestleBoard.Site -- --out some/other/folder
/// </code>
///
/// <para>Output is a plain folder of HTML, CSS, one small script and the screenshots. No server, no
/// framework, nothing to install: open <c>site/_site/index.html</c> in a browser and the whole site
/// works, which is how it gets checked before it is deployed.</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string repoRoot = FindRepositoryRoot();
            string source = Path.Combine(repoRoot, "site");
            string output = OutputFolder(args, source);

            if (!Directory.Exists(source))
            {
                Console.Error.WriteLine($"No site source at {source}.");
                return 2;
            }

            Fresh(output);

            int written = 0;
            foreach (SitePage page in Site.Pages)
            {
                string body = page.ContentFile is { Length: > 0 } file
                    ? Fragment(Path.Combine(source, "content", file), page.ToRoot)
                    : Reference.Build();

                string path = Path.Combine(output, page.OutputPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Shell.Render(page, body));
                written++;
                Console.WriteLine($"  page  {page.OutputPath}");
            }

            int assets = CopyFolder(Path.Combine(source, "assets"), Path.Combine(output, "assets"));

            // The screenshots are the site's best argument and they already exist, generated
            // against fictional records by tools/TrestleBoard.Screenshots (PLAN.md §0 rule 6).
            // Copied rather than linked, because a deployed site cannot reach out of its own folder.
            int images = CopyFolder(
                Path.Combine(repoRoot, "docs", "images"),
                Path.Combine(output, "images"),
                "*.png");

            // The tab icon is the application's own, linked from assets-src the way the app itself
            // links it (see TrestleBoard.App.csproj) rather than a second drawing of the same mark.
            string icon = Path.Combine(repoRoot, "assets-src", "icons", "trestleboard.png");
            if (File.Exists(icon))
            {
                File.Copy(icon, Path.Combine(output, "images", "favicon.png"), overwrite: true);
            }

            // GitHub Pages runs Jekyll over an uploaded folder unless told not to, and Jekyll
            // silently drops files and folders whose names begin with an underscore. Nothing here
            // starts with one today; this costs one empty file and removes the whole class of
            // "it worked locally" from the deployment.
            File.WriteAllText(Path.Combine(output, ".nojekyll"), string.Empty);

            Console.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{written} pages, {assets} assets, {images} screenshots → {output}"));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string OutputFolder(string[] args, string source)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--out", StringComparison.Ordinal))
            {
                return Path.GetFullPath(args[i + 1]);
            }
        }

        return Path.Combine(source, "_site");
    }

    /// <summary>
    /// A fragment is hand-written HTML with no head, no navigation and no footer — just the body of
    /// one page. Prose stays prose, in a file somebody can edit without reading any C#.
    ///
    /// <para><c>{{root}}</c> becomes the relative path back to the site root. Fragments are shared
    /// by pages at different depths — <c>/tour/</c> and <c>/help/pictures/</c> — and a hand-written
    /// "../images/" would be correct in one and broken in the other. Relative rather than absolute
    /// so the built folder can be opened straight off the disk, with no web server, which is how it
    /// gets checked before it is deployed.</para>
    /// </summary>
    private static string Fragment(string path, string root) =>
        File.Exists(path)
            ? File.ReadAllText(path).Trim().Replace("{{root}}", root, StringComparison.Ordinal)
            : throw new InvalidOperationException(
                $"The page fragment {path} is declared in Site.Pages and does not exist.");

    /// <summary>
    /// Empties the output folder before writing it.
    ///
    /// <para>Without this, a page renamed in <see cref="Site.Pages"/> would leave its old URL live
    /// and stale for as long as anybody had it bookmarked — the site's version of the comment that
    /// promises code which no longer exists.</para>
    /// </summary>
    private static void Fresh(string output)
    {
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
    }

    private static int CopyFolder(string from, string to, string pattern = "*")
    {
        if (!Directory.Exists(from))
        {
            return 0;
        }

        Directory.CreateDirectory(to);
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(from, pattern, SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Walks up from the running assembly until it finds the solution, so the tool works from any
    /// working directory — the same trick the screenshot harness uses.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? here = new(AppContext.BaseDirectory);
        while (here is not null)
        {
            if (File.Exists(Path.Combine(here.FullName, "TrestleBoard.slnx")))
            {
                return here.FullName;
            }

            here = here.Parent;
        }

        throw new InvalidOperationException(
            "Could not find TrestleBoard.slnx above the running assembly, so the repository root is "
            + "unknown. Run this from inside the repository.");
    }
}
