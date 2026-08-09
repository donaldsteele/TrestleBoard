using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Migrations;
using Xunit;

namespace TrestleBoard.Core.Tests;

/// <summary>
/// The <c>.tbpack</c> container (PLAN.md §11 M64).
///
/// <para>Nothing here knows what a roster is. The container's whole contract is that bytes go in
/// and the same bytes come out, that the file says what it is before anything trusts it, and that a
/// file from a future TrestleBoard refuses in a sentence rather than half-loading — which is why
/// this half of the milestone is testable with no app, no Avalonia and no personal data.</para>
/// </summary>
public sealed class SuccessorPackTests
{
    [Fact]
    public void EveryByteThatGoesInComesBackOut()
    {
        SuccessorPackage packed = Sample();

        SuccessorPackage read = RoundTrip(packed);

        Assert.Equal(packed.Files.Count, read.Files.Count);
        foreach ((string name, byte[] bytes) in packed.Files)
        {
            Assert.True(read.Files.ContainsKey(name), $"{name} did not survive the round trip");
            Assert.Equal(bytes, read.Files[name]);
        }
    }

    /// <summary>
    /// The acceptance is "restores every store byte-for-byte", and a store is not text: the roster
    /// is UTF-8 JSON and a template is a zip. A container that helpfully re-encoded anything would
    /// pass a string comparison and fail the actual promise.
    /// </summary>
    [Fact]
    public void BytesThatAreNotTextSurviveUnchanged()
    {
        byte[] awkward = [0x00, 0xFF, 0x50, 0x4B, 0x03, 0x04, 0x0D, 0x0A, 0x1A, 0x00, 0xEF, 0xBB, 0xBF];
        var package = new SuccessorPackage();
        package.Files["templates/one.tboard"] = awkward;

        Assert.Equal(awkward, RoundTrip(package).Files["templates/one.tboard"]);
    }

    [Fact]
    public void TheManifestSaysWhatIsInside()
    {
        SuccessorPackage read = RoundTrip(Sample());

        Assert.Equal(SuccessorPackManifest.CurrentFormatName, read.Manifest.FormatName);
        Assert.Equal(SuccessorPackManifest.CurrentFormatVersion, read.Manifest.FormatVersion);
        Assert.Equal(2, read.Manifest.Parts.Count);
        Assert.Equal("84 people", read.PartOf(SuccessorPackParts.Roster)?.Summary);
        Assert.Null(read.PartOf(SuccessorPackParts.Settings));
    }

    /// <summary>The same stores pack to the same bytes — the <c>.tboard</c> discipline, M64's turn.</summary>
    [Fact]
    public void PackingTheSameThingTwiceProducesTheSameFile()
    {
        Assert.Equal(Bytes(Sample()), Bytes(Sample()));
    }

    /// <summary>
    /// A partial restore has to be possible without the reader understanding any of the files, and
    /// that only works if the part is legible from the entry name alone.
    /// </summary>
    [Fact]
    public void APartCanBeTakenOutWithoutReadingAnythingElse()
    {
        SuccessorPackage read = RoundTrip(Sample());

        Assert.Equal(
            ["roster/roster-backups/roster-20260101-000000-000.roster.bak.json", "roster/roster.json"],
            read.FilesOf(SuccessorPackParts.Roster).Select(f => f.Key));
        Assert.Single(read.FilesOf(SuccessorPackParts.Templates));
        Assert.Empty(read.FilesOf(SuccessorPackParts.Phrases));
    }

    // ---- refusing, in sentences -------------------------------------------------------------------

    /// <summary>A newsletter is not a pack, and the message says what to do about it.</summary>
    [Fact]
    public void ANewsletterIsNotAPack()
    {
        byte[] newsletter = Zip(("manifest.json", Utf8("""{"formatName":"trestleboard","formatVersion":"1.0.0"}""")));

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => SuccessorPackContainer.Load(new MemoryStream(newsletter)));
        Assert.Contains("Open a newsletter", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AZipThatIsNotOursSaysSoRatherThanThrowingSomethingRaw()
    {
        byte[] other = Zip(("readme.txt", Utf8("hello")));

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => SuccessorPackContainer.Load(new MemoryStream(other)));
        Assert.Contains("what is inside it", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedManifestIsADamagedPackAndNotAnUnhandledException()
    {
        byte[] damaged = Zip(("manifest.json", Utf8("""{"formatName":"trestleboard-pack",""")));

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => SuccessorPackContainer.Load(new MemoryStream(damaged)));
        Assert.Contains("damaged", e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case this whole versioning story exists for: a pack written by a TrestleBoard newer than
    /// the one being handed the file. It must say "update, then try again" — the successor cannot
    /// diagnose anything else, and half-reading it would silently drop whatever was new.
    /// </summary>
    [Fact]
    public void APackFromANewerTrestleBoardAsksForAnUpdate()
    {
        var manifest = new JsonObject
        {
            ["formatName"] = SuccessorPackManifest.CurrentFormatName,
            ["formatVersion"] = "2.0.0",
            ["minReaderVersion"] = "2.0.0",
        };

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => SuccessorPackMigrations.Run(manifest));
        Assert.Contains("update TrestleBoard", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An old pack this build has no upgrade step for is refused rather than read as though it were
    /// current. The chain is empty at M64, so this is also the test that will start passing through
    /// the day a real migration is written.
    /// </summary>
    [Fact]
    public void AVersionWithNoUpgradeStepIsRefused()
    {
        var manifest = new JsonObject
        {
            ["formatName"] = SuccessorPackManifest.CurrentFormatName,
            ["formatVersion"] = "0.9.0",
            ["minReaderVersion"] = "0.9.0",
        };

        Assert.Throws<UnsupportedFormatException>(() => SuccessorPackMigrations.Run(manifest));
    }

    [Theory]
    [InlineData("\"1.0.0-beta\"")]
    [InlineData("\"\"")]
    [InlineData("7")]
    [InlineData("{}")]
    public void AVersionThatIsNotOneIsADamagedPack(string json)
    {
        JsonObject manifest = (JsonObject)JsonNode.Parse(
            $$"""{"formatName":"trestleboard-pack","formatVersion":{{json}}}""")!;

        UnsupportedFormatException e = Assert.Throws<UnsupportedFormatException>(
            () => SuccessorPackMigrations.Run(manifest));
        Assert.Contains("damaged", e.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pack carrying a part this build has never heard of keeps it. Opening a file must never be
    /// a way of shrinking it — the successor may yet install the newer TrestleBoard that understands
    /// the part, and the pack in their hands has to still contain it.
    /// </summary>
    [Fact]
    public void APartFromTheFutureIsCarriedRatherThanDropped()
    {
        var package = Sample();
        package.Files["stationery/letterhead.svg"] = Utf8("<svg/>");

        SuccessorPackage read = RoundTrip(package);

        Assert.True(read.Files.ContainsKey("stationery/letterhead.svg"));
        Assert.False(SuccessorPackParts.IsKnown("stationery"));
    }

    // ---- the words --------------------------------------------------------------------------------

    [Fact]
    public void EveryPartHasATitleAndASentence()
    {
        foreach (string id in SuccessorPackParts.InOrder)
        {
            Assert.False(string.IsNullOrWhiteSpace(SuccessorPackParts.TitleOf(id)), id);
            Assert.False(string.IsNullOrWhiteSpace(SuccessorPackParts.DescriptionOf(id)), id);
            Assert.True(SuccessorPackParts.IsKnown(id), id);
        }
    }

    /// <summary>
    /// The order is the promise that somebody who stops reading half way down has still taken the
    /// irreplaceable thing.
    /// </summary>
    [Fact]
    public void TheAddressBookIsAskedAboutFirstAndTheSettingsLast()
    {
        Assert.Equal(SuccessorPackParts.Roster, SuccessorPackParts.InOrder[0]);
        Assert.Equal(SuccessorPackParts.Settings, SuccessorPackParts.InOrder[^1]);
    }

    // ---- plumbing ---------------------------------------------------------------------------------

    private static SuccessorPackage Sample()
    {
        var package = new SuccessorPackage
        {
            Manifest = new SuccessorPackManifest
            {
                GeneratorVersion = "1.2.3",
                WrittenOn = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero),
                Parts =
                [
                    new SuccessorPackPart(SuccessorPackParts.Roster, 2, "84 people"),
                    new SuccessorPackPart(SuccessorPackParts.Templates, 1, "1 template"),
                ],
            },
        };

        // Fictional throughout (§0 rule 2) — this file is a public artifact of a private format.
        package.Files["roster/roster.json"] = Utf8("""{"schemaVersion":1,"members":[]}""");
        package.Files["roster/roster-backups/roster-20260101-000000-000.roster.bak.json"] =
            Utf8("""{"schemaVersion":1,"members":[]}""");
        package.Files["templates/stated-communication.tboard"] = [0x50, 0x4B, 0x05, 0x06];
        return package;
    }

    private static SuccessorPackage RoundTrip(SuccessorPackage package) =>
        SuccessorPackContainer.Load(new MemoryStream(Bytes(package)));

    private static byte[] Bytes(SuccessorPackage package)
    {
        using var ms = new MemoryStream();
        SuccessorPackContainer.Save(package, ms);
        return ms.ToArray();
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] bytes) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name);
                using Stream s = entry.Open();
                s.Write(bytes);
            }
        }

        return ms.ToArray();
    }
}
