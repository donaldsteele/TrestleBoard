using System.Globalization;
using System.Text;

namespace TrestleBoard.Roster.Import;

/// <summary>
/// Deciding whether two written names are the same man (PLAN.md §11 M12, merge policy).
///
/// The rule this type exists to keep is that <b>only an exact match is ever acted on</b>. Everything
/// this file can tell you about two names that are merely similar is a question for the user, never
/// a decision: merging two brothers would corrupt the address book quietly and print the wrong name
/// in the newsletter, which is the most conspicuous mistake this app can make.
/// </summary>
public static class NameMatching
{
    private static readonly string[] Suffixes = ["jr", "sr", "ii", "iii", "iv", "jnr", "snr"];

    /// <summary>
    /// The form two names are compared in: trimmed, whitespace collapsed, case-folded, punctuation
    /// dropped, generational suffixes removed, and <c>Last, First</c> turned round into
    /// <c>First Last</c>. Culture-invariant throughout, so two machines agree.
    /// </summary>
    public static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string value = name.Trim();

        // "Placeholder, Aaron" → "Aaron Placeholder". Only the first comma counts: a second one is
        // "Placeholder, Aaron, Jr" and the suffix drops out below anyway.
        int comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma > 0)
        {
            string last = value[..comma];
            string rest = value[(comma + 1)..];
            value = rest.Trim() + " " + last.Trim();
        }

        var letters = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                letters.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c) || c is '-' or '\'' or '.')
            {
                letters.Append(' ');
            }
        }

        string[] words = letters.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => !Suffixes.Contains(w, StringComparer.Ordinal))
            .ToArray();

        return string.Join(' ', words);
    }

    /// <summary>Emails are compared trimmed and case-folded; the local part's case is not meaningful in practice.</summary>
    public static string NormaliseEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim().ToLowerInvariant();

    /// <summary>
    /// Are these close enough to be worth <em>asking</em> about? Close means: the same family name
    /// and a first name that starts the same way, or one written name away from the other by a
    /// couple of characters.
    ///
    /// Deliberately not used to merge anything. It only ever raises the question
    /// <em>"Are these the same person?"</em>.
    /// </summary>
    public static bool CouldBeTheSamePerson(string? left, string? right)
    {
        string a = Normalise(left);
        string b = Normalise(right);
        if (a.Length == 0 || b.Length == 0 || a == b)
        {
            return false;
        }

        string[] wordsA = a.Split(' ');
        string[] wordsB = b.Split(' ');

        bool sameFamilyName = string.Equals(wordsA[^1], wordsB[^1], StringComparison.Ordinal);
        if (sameFamilyName && wordsA.Length > 1 && wordsB.Length > 1)
        {
            string firstA = wordsA[0];
            string firstB = wordsB[0];

            // "A Placeholder" against "Aaron Placeholder": an initial, which is how half a lodge's
            // records are written.
            if (firstA[0] == firstB[0]
                && (firstA.Length == 1 || firstB.Length == 1
                    || firstA.StartsWith(firstB, StringComparison.Ordinal)
                    || firstB.StartsWith(firstA, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return Distance(a, b) <= 2;
    }

    /// <summary>Ordinary edit distance, bounded by the shorter string — these are names, not documents.</summary>
    internal static int Distance(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return Math.Max(a.Length, b.Length);
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>
    /// "Placeholder, Aaron" — the filing order the People window lists by when the user has not
    /// said otherwise. Returns null when the name is a single word, where there is nothing to swap.
    /// </summary>
    public static string? SuggestSortName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        string[] words = displayName.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2)
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{words[^1]}, {string.Join(' ', words[..^1])}");
    }
}
