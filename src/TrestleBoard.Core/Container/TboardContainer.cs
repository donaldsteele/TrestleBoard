using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrestleBoard.Core.Migrations;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Serialization;

namespace TrestleBoard.Core.Container;

/// <summary>
/// A document plus its container-level payloads: ORIGINAL untouched image bytes (assets/) and
/// regenerable page thumbnails (thumbnails/). Keeping originals forever is what makes every
/// image re-croppable months later with zero quality loss (PLAN.md §2).
/// </summary>
public sealed class TboardPackage
{
    public TboardManifest Manifest { get; set; } = new();

    public Document Document { get; set; } = new();

    /// <summary>Entry name (e.g. "img-01hzy.jpg") → original bytes, stored under assets/.</summary>
    public Dictionary<string, byte[]> Assets { get; } = [];

    /// <summary>Entry name (e.g. "page-1.png") → PNG bytes, stored under thumbnails/.</summary>
    public Dictionary<string, byte[]> Thumbnails { get; } = [];
}

/// <summary>.tboard zip container read/write (PLAN.md §2). Deterministic output: fixed entry
/// timestamps, sorted entry order, invariant JSON.</summary>
public static class TboardContainer
{
    private const string ManifestEntry = "manifest.json";
    private const string DocumentEntry = "document.json";
    private const string StylesEntry = "styles.json";
    private const string AssetsPrefix = "assets/";
    private const string ThumbnailsPrefix = "thumbnails/";

    /// <summary>Fixed zip timestamp so identical content yields identical bytes.</summary>
    private static readonly DateTimeOffset EntryTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static void Save(TboardPackage package, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(stream);

        var body = new DocumentBodyFile
        {
            Metadata = package.Document.Metadata,
            PageMasters = package.Document.PageMasters,
            Pages = package.Document.Pages,
            Stories = package.Document.Stories,
            ExtraProperties = package.Document.ExtraProperties,
        };
        var styles = new StylesFile
        {
            Theme = package.Document.Theme,
            StyleSheet = package.Document.StyleSheet,
            ExtraProperties = package.Document.StylesFileExtraProperties,
        };

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteJson(zip, ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(package.Manifest, TboardJsonContext.Default.TboardManifest));
        WriteJson(zip, DocumentEntry, JsonSerializer.SerializeToUtf8Bytes(body, TboardJsonContext.Default.DocumentBodyFile));
        WriteJson(zip, StylesEntry, JsonSerializer.SerializeToUtf8Bytes(styles, TboardJsonContext.Default.StylesFile));
        foreach ((string name, byte[] bytes) in package.Assets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            WriteBinary(zip, AssetsPrefix + name, bytes);
        }

        foreach ((string name, byte[] bytes) in package.Thumbnails.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            WriteBinary(zip, ThumbnailsPrefix + name, bytes);
        }
    }

    /// <summary>Atomic file save: write temp beside target, then rename over it (PLAN.md §4).</summary>
    public static void SaveToFile(TboardPackage package, string path)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrEmpty(path);
        string temp = path + ".tmp";
        using (FileStream fs = File.Create(temp))
        {
            Save(package, fs);
        }

        File.Move(temp, path, overwrite: true);
    }

    public static TboardPackage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        JsonObject manifestNode = ReadJsonObject(zip, ManifestEntry);
        JsonObject bodyNode = ReadJsonObject(zip, DocumentEntry);
        JsonObject stylesNode = ReadJsonObject(zip, StylesEntry);

        if (!string.Equals(manifestNode["formatName"]?.GetValue<string>(), TboardManifest.CurrentFormatName, StringComparison.Ordinal))
        {
            throw new UnsupportedFormatException("This file is not a TrestleBoard newsletter.");
        }

        MigrationRunner.Run(manifestNode, bodyNode, stylesNode);

        TboardManifest manifest = Deserialize(manifestNode, TboardJsonContext.Default.TboardManifest);
        DocumentBodyFile body = Deserialize(bodyNode, TboardJsonContext.Default.DocumentBodyFile);
        StylesFile styles = Deserialize(stylesNode, TboardJsonContext.Default.StylesFile);

        var package = new TboardPackage
        {
            Manifest = manifest,
            Document = new Document
            {
                Metadata = body.Metadata,
                PageMasters = body.PageMasters,
                Pages = body.Pages,
                Stories = body.Stories,
                Theme = styles.Theme,
                StyleSheet = styles.StyleSheet,
                ExtraProperties = body.ExtraProperties,
                StylesFileExtraProperties = styles.ExtraProperties,
            },
        };

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (entry.FullName.StartsWith(AssetsPrefix, StringComparison.Ordinal) && entry.Name.Length > 0)
            {
                package.Assets[entry.FullName[AssetsPrefix.Length..]] = ReadAllBytes(entry);
            }
            else if (entry.FullName.StartsWith(ThumbnailsPrefix, StringComparison.Ordinal) && entry.Name.Length > 0)
            {
                package.Thumbnails[entry.FullName[ThumbnailsPrefix.Length..]] = ReadAllBytes(entry);
            }
        }

        return package;
    }

    public static TboardPackage LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream fs = File.OpenRead(path);
        return Load(fs);
    }

    private static T Deserialize<T>(JsonObject node, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        node.Deserialize(typeInfo)
            ?? throw new UnsupportedFormatException("This newsletter file is damaged and could not be read.");

    private static JsonObject ReadJsonObject(ZipArchive zip, string entryName)
    {
        ZipArchiveEntry entry = zip.GetEntry(entryName)
            ?? throw new UnsupportedFormatException(
                $"This newsletter file is missing its '{entryName}' part and could not be opened.");
        using Stream s = entry.Open();
        return JsonNode.Parse(s) as JsonObject
            ?? throw new UnsupportedFormatException("This newsletter file is damaged and could not be read.");
    }

    private static void WriteJson(ZipArchive zip, string name, byte[] utf8Bytes)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryTimestamp;
        using Stream s = entry.Open();
        s.Write(utf8Bytes);
    }

    private static void WriteBinary(ZipArchive zip, string name, byte[] bytes)
    {
        // Originals are typically already-compressed JPEG/PNG; store uncompressed for speed.
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = EntryTimestamp;
        using Stream s = entry.Open();
        s.Write(bytes);
    }

    private static byte[] ReadAllBytes(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream(capacity: (int)entry.Length);
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
