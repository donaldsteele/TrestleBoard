using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Migrations;
using TrestleBoard.Core.Serialization;

namespace TrestleBoard.Core.Container;

/// <summary>
/// Everything one committee has accumulated, ready to be written to a single file (PLAN.md §11 M64).
///
/// <para>Files are keyed by their path inside the pack — <c>roster/roster.json</c>,
/// <c>templates/stated-communication.tboard</c> — and the first segment is the part id, which is
/// what makes a partial restore possible without the reader knowing what any of these files mean.
/// </para>
/// </summary>
public sealed class SuccessorPackage
{
    public SuccessorPackManifest Manifest { get; set; } = new();

    /// <summary>Pack-relative path → the store's bytes, exactly as they were on disk.</summary>
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>The files belonging to one part, in a stable order.</summary>
    public IEnumerable<KeyValuePair<string, byte[]>> FilesOf(string partId)
    {
        ArgumentException.ThrowIfNullOrEmpty(partId);
        string prefix = partId + "/";
        return Files
            .Where(f => f.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(f => f.Key, StringComparer.Ordinal);
    }

    /// <summary>The manifest's record of one part, or null if the pack does not carry it.</summary>
    public SuccessorPackPart? PartOf(string partId) =>
        Manifest.Parts.FirstOrDefault(p => string.Equals(p.Id, partId, StringComparison.Ordinal));
}

/// <summary>
/// <c>.tbpack</c> zip read/write (PLAN.md §11 M64).
///
/// <para><b>The <c>.tboard</c> discipline, deliberately repeated rather than shared.</b> Fixed entry
/// timestamps and sorted entry order, so the same stores pack to the same bytes; a manifest with a
/// format name, a format version and a minimum reader version; a migration chain in front of the
/// deserialize; temp-then-rename with an fsync on the way out. Not merged with
/// <see cref="TboardContainer"/> because a newsletter has three known JSON parts and a pack has an
/// open-ended set of opaque store files — one class doing both would be a switch on which it is.
/// </para>
///
/// <para><b>Store bytes are copied, never re-serialized.</b> A pack round-trips
/// <c>roster.json</c> byte-for-byte, which is the acceptance test and also the honest thing: the
/// pack is a removal van, not an editor, and a pack step that re-wrote the roster would be a pack
/// step that could corrupt it. It also means a pack written by an older TrestleBoard hands the
/// newer one a file its own migrations then upgrade, exactly as opening an old newsletter does.
/// </para>
/// </summary>
public static class SuccessorPackContainer
{
    /// <summary>The extension. §0 rule 7: gitignored, and only ever written where the user browsed.</summary>
    public const string Extension = ".tbpack";

    private const string ManifestEntry = "manifest.json";

    /// <summary>Fixed zip timestamp so identical content yields identical bytes.</summary>
    private static readonly DateTimeOffset EntryTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Save(SuccessorPackage package, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(stream);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Write(
            zip,
            ManifestEntry,
            JsonSerializer.SerializeToUtf8Bytes(package.Manifest, TboardJsonContext.Default.SuccessorPackManifest),
            CompressionLevel.Optimal);

        foreach ((string name, byte[] bytes) in package.Files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // Compressed, unlike a .tboard's assets: these are JSON, plain text and zips. The zips
            // (templates) gain nothing and lose nothing, and the JSON — a roster and ten backups of
            // it — is the bulk of the file and compresses to a fraction, which matters when the way
            // this file reaches the successor is an email attachment.
            Write(zip, name, bytes, CompressionLevel.Optimal);
        }
    }

    /// <summary>
    /// Atomic file save: temp beside the target, flushed to the device, then renamed over
    /// (<see cref="TboardContainer.SaveToFile"/>'s reasoning — a half-written pack that overwrote a
    /// good one would be the worst possible outcome of a milestone about not losing things).
    /// </summary>
    public static void SaveToFile(SuccessorPackage package, string path)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrEmpty(path);

        string temp = path + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Save(package, fs);
                fs.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The original failure is the one worth reporting.
            }

            throw;
        }
    }

    public static SuccessorPackage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        JsonObject manifestNode = ReadManifest(zip);
        if (!string.Equals(
                manifestNode["formatName"]?.GetValue<string>(),
                SuccessorPackManifest.CurrentFormatName,
                StringComparison.Ordinal))
        {
            throw new UnsupportedFormatException(
                "This is not a TrestleBoard pack. If somebody sent you a newsletter, open it with "
                + "“Open a newsletter” instead.");
        }

        SuccessorPackMigrations.Run(manifestNode);

        var package = new SuccessorPackage
        {
            Manifest = manifestNode.Deserialize(TboardJsonContext.Default.SuccessorPackManifest)
                ?? throw new UnsupportedFormatException(DamagedPack),
        };

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            // A directory entry has an empty Name; the manifest is not a store file. Everything
            // else is taken as it comes, including parts this build has never heard of — throwing
            // them away on load would mean a pack silently shrank by being opened.
            if (entry.Name.Length == 0 || string.Equals(entry.FullName, ManifestEntry, StringComparison.Ordinal))
            {
                continue;
            }

            package.Files[entry.FullName] = ReadAllBytes(entry);
        }

        return package;
    }

    public static SuccessorPackage LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }

    private const string DamagedPack =
        "This pack is damaged and could not be read. If the person who sent it still has "
        + "TrestleBoard, ask them to pack it up again.";

    private static JsonObject ReadManifest(ZipArchive zip)
    {
        ZipArchiveEntry entry = zip.GetEntry(ManifestEntry)
            ?? throw new UnsupportedFormatException(
                "This file is missing the part that says what is inside it, so TrestleBoard cannot "
                + "tell what it is. It may not be a TrestleBoard pack.");

        using Stream s = entry.Open();
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(s);
        }
        catch (JsonException e)
        {
            throw new UnsupportedFormatException(DamagedPack, e);
        }

        return parsed as JsonObject ?? throw new UnsupportedFormatException(DamagedPack);
    }

    private static void Write(ZipArchive zip, string name, byte[] bytes, CompressionLevel level)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        entry.LastWriteTime = EntryTimestamp;
        using Stream s = entry.Open();
        s.Write(bytes);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();

        // Capacity is a hint, and entry.Length is a number a hostile or damaged archive chooses.
        // Casting it straight to int overflows negative above 2 GB and throws out of the
        // MemoryStream constructor — an unhandled exception where a plain sentence is promised.
        // Clamped, an absurd length costs a few reallocations instead.
        using var ms = new MemoryStream(capacity: (int)Math.Clamp(entry.Length, 0, 64 * 1024 * 1024));
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
