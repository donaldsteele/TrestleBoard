using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace TrestleBoard.Spelling.Tests;

/// <summary>
/// PLAN.md §12 gate 22, the asset-provenance gate, applied to the bundled dictionary — the same
/// shape as <c>FontManifestTests</c> and for the same reason.
///
/// <para>The point is not that somebody might maliciously swap a word list. It is that a bundled
/// asset arrives once, from a download somebody did on an afternoon, and then sits in the
/// repository for years with nothing recording where it came from or what its terms were. The
/// fonts taught this project that lesson at M14, when twenty typefaces were shipping and their
/// licences were not.</para>
/// </summary>
public sealed class DictionaryManifestTests
{
    [Fact]
    public void EveryFileInTheManifestHashesToWhatTheManifestSays()
    {
        foreach ((string relative, string expected) in ManifestFiles())
        {
            string path = Path.Combine(DictionariesDirectory(), relative);
            Assert.True(File.Exists(path), $"{relative} is in the manifest and not on disk.");

            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// The direction that actually catches a careless commit: an asset appearing beside the others
    /// with nobody having said where it came from.
    /// </summary>
    [Fact]
    public void EveryFileOnDiskAppearsInTheManifest()
    {
        string root = DictionariesDirectory();
        HashSet<string> manifested = [.. ManifestFiles().Select(f => f.Relative.Replace('/', Path.DirectorySeparatorChar))];

        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path);
            if (relative.Equals("dictionaries.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.Contains(relative, manifested);
        }
    }

    /// <summary>
    /// A dictionary with no licence file beside it cannot ship, and the file it names has to exist.
    /// This is the clause M14 added after the fonts shipped without their OFL text.
    /// </summary>
    [Fact]
    public void EveryDictionaryNamesALicenceFileThatIsThere()
    {
        using JsonDocument manifest = ReadManifest();

        foreach (JsonElement dictionary in manifest.RootElement.GetProperty("dictionaries").EnumerateArray())
        {
            string licenceFile = dictionary.GetProperty("licenceFile").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(dictionary.GetProperty("licence").GetString()));
            Assert.True(
                File.Exists(Path.Combine(DictionariesDirectory(), licenceFile)),
                $"{licenceFile} is named as a licence and is not on disk.");
        }
    }

    /// <summary>The licence that ships inside the assembly is the file that was hashed.</summary>
    [Fact]
    public void TheLicenceInTheAssemblyIsTheLicenceInTheRepository()
    {
        string onDisk = File.ReadAllText(
            Path.Combine(DictionariesDirectory(), "en-US", BundledDictionary.LicenceResource));

        Assert.Equal(
            onDisk.Replace("\r\n", "\n", StringComparison.Ordinal),
            BundledDictionary.ReadLicenceText().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// The upstream is named and pinned to a commit, so a future maintainer can find out what they
    /// have rather than guessing from the word list.
    /// </summary>
    [Fact]
    public void TheUpstreamIsNamedAndPinned()
    {
        using JsonDocument manifest = ReadManifest();
        JsonElement source = manifest.RootElement.GetProperty("sources").EnumerateObject().First().Value;

        Assert.StartsWith("https://", source.GetProperty("repo").GetString()!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("commit").GetString()));
    }

    private static IEnumerable<(string Relative, string Sha256)> ManifestFiles()
    {
        using JsonDocument manifest = ReadManifest();
        foreach (JsonElement dictionary in manifest.RootElement.GetProperty("dictionaries").EnumerateArray())
        {
            foreach (JsonElement file in dictionary.GetProperty("files").EnumerateArray())
            {
                yield return (file.GetProperty("name").GetString()!, file.GetProperty("sha256").GetString()!);
            }
        }
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DictionariesDirectory(), "dictionaries.json")));

    private static string DictionariesDirectory() =>
        Path.Combine(RepositoryRoot(), "assets-src", "dictionaries");

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TrestleBoard.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
