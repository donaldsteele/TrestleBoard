using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrestleBoard.App.Settings;
using TrestleBoard.Core.Phrases;

namespace TrestleBoard.App.Integration;

/// <summary>One paragraph the user saved, as it sits in the file.</summary>
internal sealed record SavedPhrase
{
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";

    public string Text { get; init; } = "";
}

/// <summary>
/// The bundled paragraphs and the user's own, in one list (PLAN.md §11 M54).
///
/// <para><b>§0 rule 7 governs the file.</b> A committee member saving "the words we used for
/// Brother Smith last March" is saving a real name, so the shelf lives in AppData beside the
/// address book, its name is gitignored, and no fixture may hold a real one. The bundled half
/// carries nothing but blanks (§0 rule 2).</para>
///
/// <para>Load never throws and save is best-effort, the <see cref="AppSettings"/> pattern: losing a
/// saved paragraph is a nuisance, and refusing to open the newsletter over one would not be.</para>
/// </summary>
internal sealed class PhraseShelf
{
    private readonly string _path;
    private readonly List<SavedPhrase> _mine = [];

    internal PhraseShelf(string? path = null)
    {
        _path = path ?? AppPaths.PhraseShelfFile;
        Load();
    }

    internal string Path => _path;

    /// <summary>True when the last write did not reach the disk, so the shell can say so (M52's rule).</summary>
    internal bool CouldNotBeSaved { get; private set; }

    /// <summary>
    /// Everything on the shelf: what shipped, then what the user added. Theirs come second because
    /// a list that reorders itself as you use it is a list you cannot learn.
    /// </summary>
    internal IReadOnlyList<Phrase> All() =>
    [
        .. PhraseLibrary.Bundled,
        .. _mine.Select(m => PhraseLibrary.Mine(m.Id, m.Title, m.Text)),
    ];

    /// <summary>
    /// Saves a paragraph of the user's own. The id is derived from the title and made unique, so
    /// saving twice under one name replaces rather than quietly making a second copy nobody can
    /// tell apart.
    /// </summary>
    internal Phrase Save(string title, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        string id = "mine-" + new string([.. title.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')]);
        _mine.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal));
        _mine.Add(new SavedPhrase { Id = id, Title = title.Trim(), Text = text.Trim() });
        Write();
        return PhraseLibrary.Mine(id, title.Trim(), text.Trim());
    }

    /// <summary>Takes one of the user's own back off the shelf. Bundled ones cannot be removed.</summary>
    internal bool Forget(string id)
    {
        if (_mine.RemoveAll(m => string.Equals(m.Id, id, StringComparison.Ordinal)) == 0)
        {
            return false;
        }

        Write();
        return true;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            SavedPhrase[]? saved = JsonSerializer.Deserialize(
                File.ReadAllBytes(_path), PhraseShelfJsonContext.Default.SavedPhraseArray);
            foreach (SavedPhrase phrase in saved ?? [])
            {
                if (!string.IsNullOrWhiteSpace(phrase.Id) && !string.IsNullOrWhiteSpace(phrase.Text))
                {
                    _mine.Add(phrase);
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(
                _mine.ToArray(), PhraseShelfJsonContext.Default.SavedPhraseArray));
            CouldNotBeSaved = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CouldNotBeSaved = true;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(SavedPhrase[]))]
internal sealed partial class PhraseShelfJsonContext : JsonSerializerContext;
