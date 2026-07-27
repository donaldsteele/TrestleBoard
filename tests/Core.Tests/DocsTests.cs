using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The documentation gate (M15, PLAN.md §12 item 12).
///
/// The screenshot harness itself is deliberately not run by CI — gating on pixels would either
/// block every unrelated UI tweak on a re-bake, or need a tolerance, at which point it stops being
/// a gate. <b>The plumbing is gated instead</b>, and that catches the failure that actually
/// matters: a broken image on the project's front page.
///
/// These are pure file and regular-expression tests. They live in <c>Core.Tests</c> rather than
/// beside the harness precisely because they must run everywhere, on all three operating systems,
/// with no Avalonia and no Skia in the way.
/// </summary>
public sealed class DocsTests
{
    /// <summary>Docs that may reference an image. Everything the repository publishes.</summary>
    private static readonly string[] DocGlobs = ["README.md", "docs/*.md", "docs/images/*.md"];

    /// <summary>Markdown image references: <c>![alt](path)</c>.</summary>
    private static readonly Regex ImageReference =
        new(@"!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryReferencedImageExists()
    {
        List<string> missing = [];
        foreach ((string doc, Match match) in ImageReferences())
        {
            string relative = match.Groups["path"].Value.Trim();
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string resolved = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Path.Combine(Root, doc))!, relative));
            if (!File.Exists(resolved))
            {
                missing.Add($"{doc} references {relative}, which does not exist");
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The other direction, and the one that keeps the repository from growing images nobody looks
    /// at. PNGs never delta, so an unreferenced one is a full copy of dead weight in the history.
    /// </summary>
    [Fact]
    public void EveryCommittedImageIsReferenced()
    {
        HashSet<string> referenced = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string doc, Match match) in ImageReferences())
        {
            string relative = match.Groups["path"].Value.Trim();
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            referenced.Add(Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Path.Combine(Root, doc))!, relative)));
        }

        string[] orphans =
        [
            .. Images()
                .Where(path => !referenced.Contains(Path.GetFullPath(path)))
                .Select(Path.GetFileName)!,
        ];

        Assert.True(
            orphans.Length == 0,
            "No document refers to: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// Alt text is not decoration on a project built around screen readers, and "screenshot" is not
    /// alt text. A real sentence, or the image should not be there.
    /// </summary>
    [Fact]
    public void EveryImageReferenceCarriesRealAltText()
    {
        List<string> thin = [];
        foreach ((string doc, Match match) in ImageReferences())
        {
            string alt = match.Groups["alt"].Value.Trim();
            if (alt.Length < 20 || !alt.Contains(' ', StringComparison.Ordinal))
            {
                thin.Add($"{doc}: \"{alt}\" is not a sentence");
            }
        }

        Assert.True(thin.Count == 0, string.Join(Environment.NewLine, thin));
    }

    /// <summary>
    /// PLAN.md §0 rule 6. Encoders stamp software names and <b>file paths</b> into a PNG's text
    /// chunks, and a path on a maintainer's machine carries their account name. That is real
    /// personal data in a public repository, so no committed image may carry one.
    /// </summary>
    [Fact]
    public void NoCommittedImageCarriesTextChunks()
    {
        List<string> stamped = [];
        foreach (string path in Images())
        {
            string[] chunks = [.. TextChunkTypes(File.ReadAllBytes(path))];
            if (chunks.Length > 0)
            {
                stamped.Add($"{Path.GetFileName(path)} carries {string.Join(", ", chunks)}");
            }
        }

        Assert.True(stamped.Count == 0, string.Join(Environment.NewLine, stamped));
    }

    /// <summary>
    /// The harness builds a small address book in code for the People window shot. Every name in it
    /// must be obviously fictional (PLAN.md §0 rule 2) — this reads the source, because a
    /// screenshot cannot be read back and a rule nobody checks is a rule that drifts.
    /// </summary>
    [Fact]
    public void TheScreenshotHarnessUsesFictionalNamesOnly()
    {
        string source = Path.Combine(Root, "tools", "TrestleBoard.Screenshots", "Fixtures.cs");
        Assert.True(File.Exists(source), source + " is missing — was the harness moved?");

        var names = Regex.Matches(
            File.ReadAllText(source),
            @"Person\(""person-\d+"",\s*""(?<name>[^""]+)""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        Assert.NotEmpty(names);
        foreach (Match name in names)
        {
            Assert.EndsWith(
                "Placeholder",
                name.Groups["name"].Value,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The two install screenshots are still open acceptance items on M10: they are operating-system
    /// security dialogs that need a fresh Windows machine and a real Mac. M15 gives them a home and
    /// a documented standard and nothing more, so the index must still say they are not captured —
    /// and must not link them, which is what would break the front page.
    /// </summary>
    [Fact]
    public void TheReservedInstallShotsAreNamedButNotLinked()
    {
        string index = File.ReadAllText(Path.Combine(Root, "docs", "images", "README.md"));
        foreach (string reserved in new[] { "install-smartscreen", "install-gatekeeper" })
        {
            Assert.Contains(reserved, index, StringComparison.Ordinal);
            Assert.DoesNotContain($"]({reserved}.png)", index, StringComparison.Ordinal);
        }

        Assert.Contains("not yet captured", index, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Doc, Match Match)> ImageReferences()
    {
        foreach (string doc in Docs())
        {
            string relativeDoc = Path.GetRelativePath(Root, doc).Replace('\\', '/');
            foreach (Match match in ImageReference.Matches(File.ReadAllText(doc)))
            {
                yield return (relativeDoc, match);
            }
        }
    }

    private static IEnumerable<string> Docs() =>
        DocGlobs.SelectMany(glob => Directory.EnumerateFiles(
            Path.Combine(Root, Path.GetDirectoryName(glob) ?? string.Empty),
            Path.GetFileName(glob),
            SearchOption.TopDirectoryOnly));

    private static IEnumerable<string> Images()
    {
        string directory = Path.Combine(Root, "docs", "images");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>Walks a PNG's chunk stream and names any that carry text.</summary>
    private static IEnumerable<string> TextChunkTypes(byte[] png)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < signature.Length || !png.AsSpan(0, signature.Length).SequenceEqual(signature))
        {
            yield break;
        }

        int offset = signature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            if (length < 0 || offset + 12 + length > png.Length)
            {
                yield break;
            }

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type is "tEXt" or "iTXt" or "zTXt")
            {
                yield return type;
            }

            offset += length + 12;
            if (string.Equals(type, "IEND", StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }

    private static string Root { get; } = RepositoryRoot();

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
