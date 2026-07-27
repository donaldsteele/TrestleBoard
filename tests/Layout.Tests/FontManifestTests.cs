using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace TrestleBoard.Layout.Tests;

/// <summary>
/// CI does not run Python, so <c>assets-src/fonts/tools/build-fonts.py</c> cannot be the
/// guarantee that the bundled fonts are what the project says they are. This is. It reads
/// <c>assets-src/fonts/fonts.json</c> off disk and holds it against the actual .ttf files, the
/// licence files, and the two generated documents.
/// </summary>
public sealed class FontManifestTests
{
    private static readonly Lazy<Manifest> Loaded = new(Manifest.Load);

    [Fact]
    public void EveryManifestEntryHashesCorrectly()
    {
        Manifest manifest = Loaded.Value;
        var wrong = new List<string>();
        foreach (Face face in manifest.Faces)
        {
            string path = Path.Combine(manifest.FontsDir, face.File.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{face.File} is in fonts.json but not on disk.");
            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            if (!string.Equals(actual, face.Sha256, StringComparison.Ordinal))
            {
                wrong.Add($"{face.File}: on disk {actual}, manifest {face.Sha256}");
            }
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryTtfOnDiskAppearsInTheManifest()
    {
        Manifest manifest = Loaded.Value;
        HashSet<string> listed = manifest.Faces
            .Select(f => Path.GetFullPath(Path.Combine(manifest.FontsDir, f.File)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] onDisk = Directory
            .GetFiles(manifest.FontsDir, "*.ttf", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();

        // An unlisted .ttf is installer weight nobody accounted for: the csproj globs this tree.
        string[] orphans = onDisk.Where(p => !listed.Contains(p)).ToArray();
        Assert.Empty(orphans);
        Assert.Equal(manifest.Faces.Count, onDisk.Length);
    }

    [Fact]
    public void EveryTtfFilenameIsGloballyUnique()
    {
        // Load-bearing for the csproj's single globbed EmbeddedResource: LogicalName is
        // "TrestleBoard.Layout.Fonts." + filename, so two same-named faces in different family
        // folders would silently collide into one embedded resource.
        string[] duplicates = Loaded.Value.Faces
            .Select(f => Path.GetFileName(f.File))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryFamilyHasANonEmptyLicenceFile()
    {
        Manifest manifest = Loaded.Value;
        foreach (Family family in manifest.Families)
        {
            string path = Path.Combine(
                manifest.FontsDir, family.LicenceFile.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"{family.Name}: {family.LicenceFile} is missing.");
            Assert.True(new FileInfo(path).Length > 0, $"{family.Name}: {family.LicenceFile} is empty.");
        }
    }

    [Fact]
    public void FrozenFacesAreNeverSubset()
    {
        // Subsetting renumbers glyph ids and WidgetDrawListDump records them, so re-subsetting
        // one of the three original families would re-bake all 42 snapshot baselines and every
        // widget golden. PLAN.md M14 makes that a rule, not a preference.
        foreach (Face face in Loaded.Value.Faces.Where(f => f.Frozen))
        {
            Assert.Null(face.Subset);
        }
    }

    [Fact]
    public void EveryFaceNamesAKnownFamilyAndSubset()
    {
        Manifest manifest = Loaded.Value;
        HashSet<string> families = manifest.Families.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (Face face in manifest.Faces)
        {
            Assert.Contains(face.Family, families);
            if (face.Subset is not null)
            {
                Assert.Contains(face.Subset, manifest.SubsetNames);
            }
        }
    }

    [Fact]
    public void TheLicenceBundleMatchesTheManifest()
    {
        Manifest manifest = Loaded.Value;
        string path = Path.Combine(manifest.FontsDir, "THIRD-PARTY-FONTS.txt");
        Assert.True(File.Exists(path), "THIRD-PARTY-FONTS.txt is missing — run build-fonts.py.");
        Assert.Equal(ComposeLicenceBundle(manifest), Normalise(File.ReadAllText(path)));
    }

    [Fact]
    public void TheFontsDocMatchesTheManifest()
    {
        Manifest manifest = Loaded.Value;
        string path = Path.Combine(manifest.RepoRoot, "docs", "FONTS.md");
        Assert.True(File.Exists(path), "docs/FONTS.md is missing — run build-fonts.py.");
        Assert.Equal(ComposeFontsDoc(manifest), Normalise(File.ReadAllText(path)));
    }

    [Fact]
    public void TheBundledLicenceTextShipsInsideTheAssembly()
    {
        // OFL §II(3): the licence must accompany the fonts in any distribution. Before M14 the
        // OFL files lived in the repo only and never reached the installer, though the fonts did.
        string text = TrestleBoard.Layout.Fonts.BundledFonts.ReadLicenceText();

        Assert.Contains("SIL OPEN FONT LICENSE", text, StringComparison.OrdinalIgnoreCase);
        foreach (Family family in Loaded.Value.Families)
        {
            Assert.Contains(family.Name, text, StringComparison.Ordinal);
        }
    }

    // ── the two composition rules, mirrored from build-fonts.py ────────────────────────────
    private static readonly string Rule = new('=', 78);

    private const string BundlePreamble = """
        TrestleBoard — third-party font licences

        TrestleBoard bundles the typefaces listed below and renders with those only. It never
        loads a font from your computer, which is what makes a page look the same on every
        machine. Each family is used under the licence reproduced in full beneath its heading.

        """;

    private static string ComposeLicenceBundle(Manifest manifest)
    {
        var parts = new List<string> { BundlePreamble };
        foreach (Family family in manifest.Families.OrderBy(f => f.SortOrder))
        {
            string text = File.ReadAllText(Path.Combine(
                manifest.FontsDir, family.LicenceFile.Replace('/', Path.DirectorySeparatorChar)));
            parts.Add(
                $"{Rule}\n{family.Name} — {family.Designer}\n{family.Licence}\n" +
                $"Upstream: {manifest.UpstreamRef(family)}\n{Rule}\n\n" +
                Normalise(text).TrimEnd() + "\n");
        }

        return string.Join("\n", parts);
    }

    private static string ComposeFontsDoc(Manifest manifest)
    {
        Dictionary<string, int> faceCount = manifest.Faces
            .GroupBy(f => f.Family, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var rows = new StringBuilder();
        rows.Append("""
            # Bundled fonts

            Generated by `assets-src/fonts/tools/build-fonts.py`; do not edit by hand.
            `tests/Layout.Tests/FontManifestTests` fails if this file and
            `assets-src/fonts/fonts.json` disagree.

            TrestleBoard renders with these faces and no others — never a font installed on
            your computer. That is what makes a page paginate identically on Windows, Linux
            and macOS. The full licence text ships with the app: Help → About → *Fonts and
            licences*.

            | Family | Faces | Designer | Licence | Upstream |
            | --- | --- | --- | --- | --- |

            """.ReplaceLineEndings("\n"));
        foreach (Family family in manifest.Families.OrderBy(f => f.SortOrder))
        {
            rows.Append(CultureInfo.InvariantCulture,
                $"| {family.Name} | {faceCount[family.Name]} | {family.Designer} | " +
                $"{family.Licence} | {manifest.UpstreamRef(family)} |\n");
        }

        rows.Append(CultureInfo.InvariantCulture,
            $"\n{manifest.Families.Count} families, {manifest.Faces.Count} faces.\n");
        return rows.ToString();
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");

    // ── manifest reading ───────────────────────────────────────────────────────────────────
    internal sealed record Face(
        string Family, string Weight, string Slant, string File,
        string Source, string? Subset, bool Frozen, string Sha256);

    internal sealed record Family(
        string Name, string Folder, string Category, string Designer,
        string LicenceFile, string Licence, string Source, string Description,
        string SampleText, int SortOrder);

    internal sealed class Manifest
    {
        public required string RepoRoot { get; init; }

        public required string FontsDir { get; init; }

        public required IReadOnlyList<Family> Families { get; init; }

        public required IReadOnlyList<Face> Faces { get; init; }

        public required IReadOnlyCollection<string> SubsetNames { get; init; }

        public required IReadOnlyDictionary<string, (string Repo, string Pin)> Sources { get; init; }

        public string UpstreamRef(Family family)
        {
            (string repo, string pin) = Sources[family.Source];
            return $"{repo} @ {pin}";
        }

        public static Manifest Load()
        {
            string root = RepositoryRoot();
            string fontsDir = Path.Combine(root, "assets-src", "fonts");
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(fontsDir, "fonts.json")));
            JsonElement r = document.RootElement;

            var sources = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            foreach (JsonProperty source in r.GetProperty("sources").EnumerateObject())
            {
                string repo = source.Value.GetProperty("repo").GetString()!;
                string pin = source.Value.TryGetProperty("tag", out JsonElement tag)
                    ? tag.GetString()!
                    : source.Value.GetProperty("commit").GetString()!;
                sources[source.Name] = (repo, pin);
            }

            return new Manifest
            {
                RepoRoot = root,
                FontsDir = fontsDir,
                Sources = sources,
                SubsetNames = r.GetProperty("subsets").EnumerateObject().Select(p => p.Name).ToArray(),
                Families = r.GetProperty("families").EnumerateArray().Select(f => new Family(
                    f.GetProperty("family").GetString()!,
                    f.GetProperty("folder").GetString()!,
                    f.GetProperty("category").GetString()!,
                    f.GetProperty("designer").GetString()!,
                    f.GetProperty("licenceFile").GetString()!,
                    f.GetProperty("licence").GetString()!,
                    f.GetProperty("source").GetString()!,
                    f.GetProperty("description").GetString()!,
                    f.GetProperty("sampleText").GetString()!,
                    f.GetProperty("sortOrder").GetInt32())).ToArray(),
                Faces = r.GetProperty("faces").EnumerateArray().Select(f => new Face(
                    f.GetProperty("family").GetString()!,
                    f.GetProperty("weight").GetString()!,
                    f.GetProperty("slant").GetString()!,
                    f.GetProperty("file").GetString()!,
                    f.GetProperty("source").GetString()!,
                    f.GetProperty("subset").ValueKind == JsonValueKind.Null
                        ? null
                        : f.GetProperty("subset").GetString(),
                    f.GetProperty("frozen").GetBoolean(),
                    f.GetProperty("sha256").GetString()!)).ToArray(),
            };
        }

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
}
