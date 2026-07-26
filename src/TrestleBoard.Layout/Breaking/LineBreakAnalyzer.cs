using System.Globalization;

namespace TrestleBoard.Layout.Breaking;

public enum BreakKind
{
    Allowed,
    Mandatory,
}

/// <summary>
/// A position at which a line may (Allowed) or must (Mandatory) end. <see cref="TextIndex"/> is
/// the char index at which the NEXT line would start (exclusive end of the upper line).
/// </summary>
public readonly record struct BreakOpportunity(int TextIndex, BreakKind Kind, bool KeepHyphenBefore);

/// <summary>
/// UAX#14-lite break analysis (PLAN.md §3): spaces, hyphens, narrow post-punctuation rule,
/// mandatory newlines. No hyphenation, no dictionary. Indices may fall inside a HarfBuzz
/// cluster; the layout engine snaps them forward to cluster boundaries.
/// </summary>
public static class LineBreakAnalyzer
{
    public static IReadOnlyList<BreakOpportunity> Analyze(string paragraphText)
    {
        ArgumentNullException.ThrowIfNull(paragraphText);
        var result = new List<BreakOpportunity>();
        int n = paragraphText.Length;
        for (int i = 0; i < n; i++)
        {
            char c = paragraphText[i];
            if (IsMandatoryBreak(c))
            {
                // CRLF counts as one break, after the LF.
                if (c == '\r' && i + 1 < n && paragraphText[i + 1] == '\n')
                {
                    i++;
                }

                result.Add(new BreakOpportunity(i + 1, BreakKind.Mandatory, KeepHyphenBefore: false));
                continue;
            }

            if (IsBreakableSpace(c))
            {
                // Break after the maximal whitespace run (trailing spaces hang on the upper line).
                int j = i;
                while (j + 1 < n && IsBreakableSpace(paragraphText[j + 1]))
                {
                    j++;
                }

                if (j + 1 < n)
                {
                    result.Add(new BreakOpportunity(j + 1, BreakKind.Allowed, KeepHyphenBefore: false));
                }

                i = j;
                continue;
            }

            if (IsBreakAfterHyphen(c) && i + 1 < n && !IsBreakableSpace(paragraphText[i + 1]))
            {
                result.Add(new BreakOpportunity(i + 1, BreakKind.Allowed, KeepHyphenBefore: true));
                continue;
            }

            if (IsNarrowBreakAfter(c) && i + 1 < n)
            {
                char next = paragraphText[i + 1];
                if (!IsBreakableSpace(next) && !IsNarrowBreakAfter(next) && !IsOpeningPunct(next)
                    && CharUnicodeInfo.GetUnicodeCategory(next) != UnicodeCategory.NonSpacingMark)
                {
                    result.Add(new BreakOpportunity(i + 1, BreakKind.Allowed, KeepHyphenBefore: false));
                }
            }
        }

        return result;
    }

    // LF, CR, FF, U+2028 LINE SEPARATOR, U+2029 PARAGRAPH SEPARATOR.
    internal static bool IsMandatoryBreak(char c) =>
        c is '\n' or '\r' or '\f' or '\u2028' or '\u2029';

    // Zs spaces break, except U+00A0 NBSP and U+202F NNBSP which never do.
    internal static bool IsBreakableSpace(char c)
    {
        if (c is '\u00A0' or '\u202F')
        {
            return false;
        }

        return c is ' ' or '\t' or '\u1680'
            || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
    }

    // HYPHEN-MINUS, U+2010 HYPHEN, U+2012 FIGURE DASH, U+2013 EN DASH, U+2014 EM DASH.
    // U+2011 NON-BREAKING HYPHEN and U+00AD SOFT HYPHEN are deliberately absent.
    private static bool IsBreakAfterHyphen(char c) =>
        c is '-' or '‐' or '‒' or '–' or '—';

    private static bool IsNarrowBreakAfter(char c) =>
        c is ')' or ']' or '}' or '/';

    private static bool IsOpeningPunct(char c) =>
        c is '(' or '[' or '{';
}
