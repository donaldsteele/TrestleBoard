using System;
using System.Collections.Generic;
using System.Linq;

namespace TrestleBoard.Spelling;

/// <summary>
/// One question — "is this a word?" — and one offer — "did you mean?" (PLAN.md §11 M52).
///
/// <para>The bundled dictionary answers first; the personal dictionary overrides it, never the
/// other way round. A user who has said "it's a name, never ask again" has settled the matter.</para>
/// </summary>
public sealed class SpellChecker
{
    private readonly PersonalDictionary _personal;

    public SpellChecker(PersonalDictionary personal) =>
        _personal = personal ?? throw new ArgumentNullException(nameof(personal));

    /// <summary>Words the checker refuses to have an opinion about, whatever the dictionary says.</summary>
    public static bool IsWorthChecking(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length < 2)
        {
            return false;
        }

        // Numbers, dates, times, ordinals, telephone numbers: anything with a digit in it is not
        // spelling. "1st", "2026", "555-0100" are all correct and none of them is in a dictionary.
        if (word.Any(char.IsDigit))
        {
            return false;
        }

        // ALL CAPS is nearly always an abbreviation a lodge newsletter is entitled to use — F&AM,
        // OES, PM. Underlining them all would be the wall of red this milestone exists to avoid.
        if (word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            return false;
        }

        return word.Any(char.IsLetter);
    }

    /// <summary>True when nothing is wrong with the word as far as anyone here can tell.</summary>
    public bool IsSpelledRight(string word)
    {
        if (!IsWorthChecking(word))
        {
            return true;
        }

        string trimmed = word.Trim();
        return _personal.Contains(trimmed) || BundledDictionary.WordList.Check(trimmed);
    }

    /// <summary>
    /// What it might have been, best first, capped. Three is the cap because the wizard puts each
    /// one on its own big button and a screen of nine is a screen nobody reads.
    ///
    /// <para>Names the user has taught us come first when they are one keystroke away. A surname
    /// misspelled by one letter is the newsletter mistake that stings — the brother notices — and
    /// the general dictionary can never suggest a fix for it, because it has never heard the name.
    /// It only ever offers a word already in the personal dictionary, so it cannot invent a name.</para>
    /// </summary>
    public IReadOnlyList<string> Suggest(string word, int most = 3)
    {
        if (string.IsNullOrWhiteSpace(word) || most <= 0)
        {
            return [];
        }

        string trimmed = word.Trim();
        var offered = new List<string>();
        foreach (string known in _personal.Words)
        {
            if (offered.Count < most && IsOneKeystrokeAway(trimmed, known))
            {
                offered.Add(known);
            }
        }

        foreach (string guess in BundledDictionary.WordList.Suggest(trimmed))
        {
            if (offered.Count >= most)
            {
                break;
            }

            if (!offered.Contains(guess, StringComparer.OrdinalIgnoreCase))
            {
                offered.Add(guess);
            }
        }

        return offered;
    }

    /// <summary>
    /// One insertion, one deletion or one substitution apart, case-insensitively. Bounded on
    /// purpose: a full edit distance over a personal dictionary of two hundred surnames, run for
    /// every unknown word, would offer half the lodge as a suggestion for anything.
    /// </summary>
    internal static bool IsOneKeystrokeAway(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1)
        {
            return false;
        }

        // Same length: at most one letter may differ.
        if (a.Length == b.Length)
        {
            int differences = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (char.ToUpperInvariant(a[i]) != char.ToUpperInvariant(b[i]) && ++differences > 1)
                {
                    return false;
                }
            }

            return differences == 1;
        }

        // Different lengths: the longer must be the shorter with one letter put back in.
        string shorter = a.Length < b.Length ? a : b;
        string longer = a.Length < b.Length ? b : a;
        int s = 0, l = 0;
        bool skipped = false;
        while (s < shorter.Length && l < longer.Length)
        {
            if (char.ToUpperInvariant(shorter[s]) == char.ToUpperInvariant(longer[l]))
            {
                s++;
                l++;
                continue;
            }

            if (skipped)
            {
                return false;
            }

            skipped = true;
            l++;
        }

        return true;
    }

    /// <summary>"It's a name — never ask again."</summary>
    public bool NeverAskAgain(string word) => _personal.Add(word);

    /// <summary>
    /// True when the last word added did not reach the disk (PLAN.md §11 M73(e)). The flag has
    /// existed on <see cref="PersonalDictionary"/> since M52 and nobody read it, so teaching the
    /// checker eleven surnames on a folder it cannot write to was answered eleven times with
    /// "added to your own list of words" — and every one of them was asked about again next month.
    /// </summary>
    public bool CouldNotBeSaved => _personal.CouldNotBeSaved;
}
