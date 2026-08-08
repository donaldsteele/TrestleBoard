using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Container;

namespace TrestleBoard.App.Integration;

/// <summary>One of the committee's own templates, as the Start screen needs to show it.</summary>
/// <param name="Id">The file's name without its extension. Stable; never shown.</param>
/// <param name="Name">What the user called it.</param>
/// <param name="SavedAt">When it was saved, for the "saved on…" line.</param>
/// <param name="ThumbnailPng">A picture of its first page, or null if it has none.</param>
internal sealed record UserTemplate(string Id, string Name, DateTimeOffset SavedAt, byte[]? ThumbnailPng);

internal sealed record TemplateSidecar(string Name, DateTimeOffset SavedAt);

/// <summary>
/// The committee's own templates, one <c>.tboard</c> per template in AppData (PLAN.md §11 M57).
///
/// <para>The shape is <c>FileRecoveryStore</c>'s: a directory, a document file, and a small JSON
/// sidecar for the things a name and a date belong in rather than inside the newsletter. It is
/// deliberately NOT that class — recovery snapshots are the app's business and templates are the
/// user's, and the two have different lifetimes, different failure stories and different words.</para>
///
/// <para><b>Every path comes from <see cref="AppPaths"/>.</b> That is what makes the screenshot
/// harness's temporary app-state root cover this store too (§0 rule 6): the harness assigns
/// <c>AppPaths.Root</c> once, and anything computed off it follows. <c>FileRecoveryStore</c> keeps
/// a hard-coded fallback path that would bypass the redirect; this does not repeat that.</para>
/// </summary>
internal sealed class UserTemplateStore
{
    private const string DocumentExtension = ".tboard";
    private const string SidecarExtension = ".json";
    private const string ThumbnailEntry = "page-1.png";

    private readonly string _directory;

    internal UserTemplateStore(string? directory = null) =>
        _directory = directory ?? AppPaths.TemplatesDirectory;

    internal string Location => _directory;

    /// <summary>True when the last write did not reach the disk (M52's rule, applied here too).</summary>
    internal bool CouldNotBeSaved { get; private set; }

    /// <summary>
    /// Everything on the shelf, newest first. Never throws: a templates folder that cannot be read
    /// costs the user their own layouts for this sitting, and the three built-ins still work.
    /// </summary>
    internal IReadOnlyList<UserTemplate> All()
    {
        var found = new List<UserTemplate>();
        try
        {
            if (!Directory.Exists(_directory))
            {
                return found;
            }

            foreach (string path in Directory.GetFiles(_directory, "*" + DocumentExtension))
            {
                if (Read(Path.GetFileNameWithoutExtension(path)) is { } template)
                {
                    found.Add(template);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return found;
        }

        return [.. found
            .OrderByDescending(t => t.SavedAt)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Writes the newsletter as a template under a name of the user's choosing. Saving twice under
    /// one name replaces it, rather than quietly making a second copy nobody can tell apart.
    /// </summary>
    internal UserTemplate? Save(TboardPackage template, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string id = IdFor(name);
        try
        {
            Directory.CreateDirectory(_directory);
            TboardContainer.SaveToFile(template, Path.Combine(_directory, id + DocumentExtension));
            WriteSidecar(id, name, now);
            CouldNotBeSaved = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CouldNotBeSaved = true;
            return null;
        }

        return Read(id);
    }

    /// <summary>The template itself, ready to open, or null when it has gone or cannot be read.</summary>
    internal TboardPackage? Open(string id)
    {
        try
        {
            string path = Path.Combine(_directory, id + DocumentExtension);
            return File.Exists(path) ? TboardContainer.LoadFromFile(path) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Renames one. The file keeps its id, so nothing pointing at it breaks.</summary>
    internal bool Rename(string id, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Read(id) is null)
        {
            return false;
        }

        try
        {
            WriteSidecar(id, name, now);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CouldNotBeSaved = true;
            return false;
        }
    }

    /// <summary>Takes one off the shelf. Both files go.</summary>
    internal bool Remove(string id)
    {
        bool removed = Delete(Path.Combine(_directory, id + DocumentExtension));
        Delete(Path.Combine(_directory, id + SidecarExtension));
        return removed;
    }

    private void WriteSidecar(string id, string name, DateTimeOffset now) =>
        File.WriteAllBytes(
            Path.Combine(_directory, id + SidecarExtension),
            JsonSerializer.SerializeToUtf8Bytes(
                new TemplateSidecar(name.Trim(), now), TemplateJsonContext.Default.TemplateSidecar));

    private UserTemplate? Read(string id)
    {
        string document = Path.Combine(_directory, id + DocumentExtension);
        if (!File.Exists(document))
        {
            return null;
        }

        string name = id;
        DateTimeOffset savedAt = DateTimeOffset.MinValue;
        try
        {
            string sidecar = Path.Combine(_directory, id + SidecarExtension);
            if (File.Exists(sidecar)
                && JsonSerializer.Deserialize(
                    File.ReadAllBytes(sidecar), TemplateJsonContext.Default.TemplateSidecar) is { } read
                && !string.IsNullOrWhiteSpace(read.Name))
            {
                name = read.Name;
                savedAt = read.SavedAt;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A lost sidecar costs the template its name, not its existence. Falling back to the id
            // keeps a layout somebody spent an evening on reachable.
        }

        return new UserTemplate(id, name, savedAt, ThumbnailOf(document));
    }

    /// <summary>
    /// The picture of the first page every saved <c>.tboard</c> already carries — the app has
    /// written <c>thumbnails/page-1.png</c> on every save since the recovery work, so a template
    /// gets its tile picture for nothing.
    /// </summary>
    private static byte[]? ThumbnailOf(string documentPath)
    {
        try
        {
            TboardPackage package = TboardContainer.LoadFromFile(documentPath);
            return package.Thumbnails.TryGetValue(ThumbnailEntry, out byte[]? png) ? png : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool Delete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A file name from what the user typed. Anything that is not a letter or a digit becomes a
    /// hyphen, so a name with a slash or a colon in it can neither escape the folder nor refuse to
    /// save.
    /// </summary>
    private static string IdFor(string name)
    {
        string id = new([.. name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);
        id = id.Trim('-');
        return id.Length == 0 ? "template" : id;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TemplateSidecar))]
internal sealed partial class TemplateJsonContext : JsonSerializerContext;
