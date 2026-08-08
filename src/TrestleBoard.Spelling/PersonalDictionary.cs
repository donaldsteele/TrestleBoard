using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrestleBoard.Spelling;

/// <summary>
/// The words this user has told us are not mistakes (PLAN.md §11 M52).
///
/// <para><b>PLAN.md §0 rule 7 governs this file.</b> "It's a name — never ask again" is how a
/// committee member will answer most of what the checker asks, so within a month this holds the
/// surnames of the lodge. It lives in AppData beside the address book, its name is gitignored, and
/// no fixture anywhere may contain a real one.</para>
///
/// <para>Plain text, one word per line, sorted, UTF-8. Not JSON: this is a file a person may
/// reasonably open in Notepad to take a word back out, and asking them to mind the commas would be
/// the sort of small cruelty this app exists to avoid. Unknown lines are ignored rather than
/// rejected, so an edit that goes wrong loses a word rather than the file.</para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "A spelling dictionary is what this is, in the words the user and "
        + "PLAN.md §11 M52 both use. CA1711 reserves the suffix for IDictionary "
        + "implementations; renaming this to a word nobody says would cost more than "
        + "the rule protects.")]
public sealed class PersonalDictionary
{
    private readonly string _path;
    private readonly SortedSet<string> _words = new(StringComparer.OrdinalIgnoreCase);

    public PersonalDictionary(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        Load();
    }

    /// <summary>What the user has added, in the order the file holds them.</summary>
    public IReadOnlyCollection<string> Words => _words;

    /// <summary>Where it lives, for the successor pack and for a test to point at.</summary>
    public string Path => _path;

    /// <summary>
    /// True when the last write did not reach the disk. The word is still known for as long as the
    /// app is open, so the user is not asked about it twice in one sitting — but it will be gone
    /// tomorrow, and the shell says so rather than letting them find out next month. Silently
    /// accepting "never ask again" and then asking again is the kind of small betrayal that makes
    /// somebody stop trusting a program.
    /// </summary>
    public bool CouldNotBeSaved { get; private set; }

    public bool Contains(string word) => !string.IsNullOrWhiteSpace(word) && _words.Contains(word.Trim());

    /// <summary>
    /// Adds a word and writes the file. Returns false when it was already known, so a caller can
    /// tell "we learned something" from "you have told us twice".
    /// </summary>
    public bool Add(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || !_words.Add(word.Trim()))
        {
            return false;
        }

        Save();
        return true;
    }

    /// <summary>Takes a word back out — the file is the user's, and so is changing their mind.</summary>
    public bool Remove(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || !_words.Remove(word.Trim()))
        {
            return false;
        }

        Save();
        return true;
    }

    /// <summary>
    /// First run: put the craft's own vocabulary in, plus whatever names the caller hands over from
    /// the address book, so the first newsletter is not a wall of underlines.
    ///
    /// <para>Only words the bundled dictionary actually rejects are kept. "Chapter", "Marshal" and
    /// "Steward" are ordinary English; adding them would make the file a list of words we guessed
    /// at rather than words we needed. Returns how many were added.</para>
    ///
    /// <para>Does nothing when the file already holds something. A user who has emptied it on
    /// purpose has said what they want.</para>
    /// </summary>
    public int SeedIfEmpty(IEnumerable<string>? namesFromTheAddressBook = null)
    {
        if (_words.Count > 0 || File.Exists(_path))
        {
            return 0;
        }

        int added = 0;
        foreach (string word in LodgeVocabulary.Words.Concat(namesFromTheAddressBook ?? []))
        {
            string trimmed = (word ?? "").Trim();
            if (trimmed.Length == 0 || BundledDictionary.WordList.Check(trimmed) || !_words.Add(trimmed))
            {
                continue;
            }

            added++;
        }

        Save();
        return added;
    }

    /// <summary>
    /// Teaches the checker the names in the address book without touching what the user has
    /// decided. Called when the roster changes; a name the dictionary already knows ("Baker",
    /// "Young") is left out, because it would never have been asked about.
    /// </summary>
    public int Learn(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        int added = 0;
        foreach (string name in names)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0 || BundledDictionary.WordList.Check(trimmed) || !_words.Add(trimmed))
            {
                continue;
            }

            added++;
        }

        if (added > 0)
        {
            Save();
        }

        return added;
    }

    /// <summary>
    /// Never throws. A personal dictionary that cannot be read costs the user some underlines;
    /// refusing to open the newsletter would cost them the evening.
    /// </summary>
    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(_path))
            {
                string word = line.Trim();
                if (word.Length > 0 && !word.StartsWith('#'))
                {
                    _words.Add(word);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Best effort, on the settings precedent. A word that cannot be written is a nuisance.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            File.WriteAllLines(_path, _words);
            CouldNotBeSaved = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            CouldNotBeSaved = true;
        }
    }
}
