using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace TrestleBoard.PdfPages.Tests;

/// <summary>
/// PLAN.md §12 gate 22, applied to the last asset that had escaped it — the same shape as
/// <c>FontManifestTests</c> and <c>DictionaryManifestTests</c>, and for the same reason.
///
/// <para>Gate 22 has named "the PDFium native library" since it was written, and until M74 (f)
/// PDFium had no manifest entry, no recorded hash, no licence in the repository and nothing under
/// Help that mentioned it. It was missed because it is a binary rather than data: the fonts and the
/// dictionary are files a maintainer copied in and could see, and PDFium arrives inside a NuGet
/// package where nobody looks.</para>
///
/// <para>PDFium is BSD-3-Clause over eleven bundled third-party notices. BSD-3-Clause §2 and
/// Apache-2.0 §4 both require the notice to accompany a binary distribution, so what these tests
/// hold shut is not tidiness — it is the condition on which the app may be handed to anybody.</para>
/// </summary>
public sealed class PdfiumLicenceTests
{
    [Fact]
    public void EveryFileInTheManifestHashesToWhatTheManifestSays()
    {
        foreach ((string relative, string expected) in ManifestFiles())
        {
            string path = Path.Combine(NativesDirectory(), relative);
            Assert.True(File.Exists(path), $"{relative} is in the manifest and not on disk.");

            string actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    /// The direction that catches a careless commit: a licence appearing beside the others with
    /// nobody having said what it belongs to.
    /// </summary>
    [Fact]
    public void EveryFileOnDiskAppearsInTheManifest()
    {
        string root = NativesDirectory();
        HashSet<string> manifested =
            [.. ManifestFiles().Select(f => f.Relative.Replace('/', Path.DirectorySeparatorChar))];

        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, path);
            if (relative.Equals("natives.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.Contains(relative, manifested);
        }
    }

    /// <summary>
    /// <b>The clause that matters.</b> The licence ships inside the assembly, so it travels wherever
    /// the installer does — it cannot be left behind by a packaging step, and it cannot be
    /// overwritten by another package's file of the same name, which is what the copy Docnet puts in
    /// the publish root is exposed to.
    /// </summary>
    [Fact]
    public void ThePdfiumLicenceShipsInsideTheAssembly()
    {
        string[] resources = typeof(BundledPdfium).Assembly.GetManifestResourceNames();

        Assert.Contains(BundledPdfium.LicenceResource, resources);
    }

    /// <summary>The text in the assembly is the file that was hashed, not some other copy.</summary>
    [Fact]
    public void TheLicenceInTheAssemblyIsTheLicenceInTheRepository()
    {
        string onDisk = File.ReadAllText(
            Path.Combine(NativesDirectory(), "pdfium", "LICENSE-pdfium.txt"));

        Assert.Equal(
            onDisk.Replace("\r\n", "\n", StringComparison.Ordinal),
            BundledPdfium.ReadLicenceText().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// PDFium's own grant, and the notices it carries for the code inside it. Eleven of them: a
    /// packaging change that quietly swapped the full notice for the top one would pass every test
    /// above and fail this.
    /// </summary>
    [Fact]
    public void TheLicenceCarriesPdfiumsTermsAndTheNoticesUnderneathThem()
    {
        string text = BundledPdfium.ReadLicenceText();

        Assert.Contains("Copyright 2014 The PDFium Authors", text, StringComparison.Ordinal);
        Assert.Contains(
            "Redistributions in binary form must reproduce the above",
            text,
            StringComparison.Ordinal);

        foreach (string bundled in new[]
        {
            "# BEGIN PDFium license",
            "# BEGIN libpng license",
            "# BEGIN LibTIFF License",
            "# BEGIN FreeType license",
            "# BEGIN zlib license",
            "# BEGIN libjpeg-turbo license file",
            "# BEGIN ICU (International Components for Unicode) license file",
        })
        {
            Assert.Contains(bundled, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The one edit made to the upstream file, held to being the only one: the file must be valid
    /// UTF-8 from end to end, so no notice reaches a user with a replacement glyph where a copyright
    /// sign should be.
    /// </summary>
    [Fact]
    public void TheNoticeIsReadableFromEndToEnd()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(NativesDirectory(), "pdfium", "LICENSE-pdfium.txt"));

        string text = new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);

        Assert.DoesNotContain('�', text);
        Assert.Contains("©", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the binary comes from, pinned, so a future maintainer can find out what they have
    /// rather than guessing from the DLL — and so a package upgrade that changes the notice is a
    /// deliberate act. The version here and the one in <c>Directory.Packages.props</c> have to
    /// agree; a bump that forgets this manifest fails here.
    /// </summary>
    [Fact]
    public void TheUpstreamIsNamedAndPinnedToTheVersionTheBuildUses()
    {
        using JsonDocument manifest = ReadManifest();
        JsonElement source = manifest.RootElement.GetProperty("sources").EnumerateObject().First().Value;

        string package = source.GetProperty("package").GetString()!;
        string version = source.GetProperty("version").GetString()!;
        Assert.StartsWith("https://", source.GetProperty("repo").GetString()!, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("commit").GetString()));

        string props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Packages.props"));
        Assert.Contains(
            $"\"{package}\" Version=\"{version}\"",
            props,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every native named in the manifest names a licence file that is there — the clause M14 added
    /// after the fonts shipped and their OFL text did not.
    /// </summary>
    [Fact]
    public void EveryNativeNamesALicenceFileThatIsThere()
    {
        using JsonDocument manifest = ReadManifest();

        foreach (JsonElement native in manifest.RootElement.GetProperty("natives").EnumerateArray())
        {
            string licenceFile = native.GetProperty("licenceFile").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(native.GetProperty("licence").GetString()));
            Assert.True(
                File.Exists(Path.Combine(NativesDirectory(), licenceFile)),
                $"{licenceFile} is named as a licence and is not on disk.");

            // And the binaries it names are recorded per RID, because the app ships on four of
            // them and a notice that only covers the one the maintainer built on covers nothing.
            JsonElement binaries = native.GetProperty("binaries");
            Assert.True(binaries.GetArrayLength() > 0);
            foreach (JsonElement binary in binaries.EnumerateArray())
            {
                Assert.Equal(64, binary.GetProperty("sha256").GetString()!.Length);
                Assert.False(string.IsNullOrWhiteSpace(binary.GetProperty("rid").GetString()));
            }
        }
    }

    private static IEnumerable<(string Relative, string Sha256)> ManifestFiles()
    {
        using JsonDocument manifest = ReadManifest();
        foreach (JsonElement native in manifest.RootElement.GetProperty("natives").EnumerateArray())
        {
            foreach (JsonElement file in native.GetProperty("files").EnumerateArray())
            {
                yield return (file.GetProperty("name").GetString()!, file.GetProperty("sha256").GetString()!);
            }
        }
    }

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(NativesDirectory(), "natives.json")));

    private static string NativesDirectory() =>
        Path.Combine(RepositoryRoot(), "assets-src", "natives");

    private static string RepositoryRoot()
    {
        var at = new DirectoryInfo(AppContext.BaseDirectory);
        while (at is not null && !File.Exists(Path.Combine(at.FullName, "PLAN.md")))
        {
            at = at.Parent;
        }

        Assert.True(at is not null, "could not find the repository root above the test binary");
        return at!.FullName;
    }
}
