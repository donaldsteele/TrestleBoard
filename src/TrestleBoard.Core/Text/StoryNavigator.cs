using System.Globalization;
using System.Text;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>
/// Grapheme-cluster and word motion over paragraph text (docs/M4-spec.md §4.1). Storage
/// offsets stay UTF-16 code units; graphemes are a motion/deletion concept computed on demand
/// via <see cref="StringInfo"/>.
/// </summary>
public static class StoryNavigator
{
    private enum CharClass
    {
        Word,
        Space,
        Punct,
    }

    public static string GetParagraphText(StoryParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return string.Concat(paragraph.Runs.Select(r => r.Text));
    }

    /// <summary>Ascending grapheme boundaries including 0 and text.Length.</summary>
    public static int[] GetGraphemeBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var boundaries = new List<int> { 0 };
        int i = 0;
        while (i < text.Length)
        {
            i += StringInfo.GetNextTextElementLength(text, i);
            boundaries.Add(i);
        }

        return [.. boundaries];
    }

    public static int NextGrapheme(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset >= text.Length)
        {
            return text.Length;
        }

        return offset + StringInfo.GetNextTextElementLength(text, offset);
    }

    public static int PreviousGrapheme(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset <= 0)
        {
            return 0;
        }

        int[] boundaries = GetGraphemeBoundaries(text);
        int previous = 0;
        foreach (int b in boundaries)
        {
            if (b >= offset)
            {
                break;
            }

            previous = b;
        }

        return previous;
    }

    /// <summary>Snaps to the nearest grapheme boundary; forward on ties when
    /// <paramref name="preferForward"/>.</summary>
    public static int SnapToGrapheme(string text, int offset, bool preferForward = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        offset = Math.Clamp(offset, 0, text.Length);
        int[] boundaries = GetGraphemeBoundaries(text);
        int best = 0;
        int bestDistance = int.MaxValue;
        foreach (int b in boundaries)
        {
            int distance = Math.Abs(b - offset);
            bool better = distance < bestDistance
                || (distance == bestDistance && preferForward && b > best);
            if (better)
            {
                best = b;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Start of the next word: skip the current class run, then any following spaces
    /// (Windows semantics). Stops at the paragraph end.</summary>
    public static int NextWordStart(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset >= text.Length)
        {
            return text.Length;
        }

        CharClass current = Classify(text, offset);
        int i = offset;
        while (i < text.Length && Classify(text, i) == current)
        {
            i = NextGrapheme(text, i);
        }

        while (i < text.Length && Classify(text, i) == CharClass.Space)
        {
            i = NextGrapheme(text, i);
        }

        return i;
    }

    /// <summary>Start of the previous word: skip preceding spaces, then the class run.</summary>
    public static int PreviousWordStart(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset <= 0)
        {
            return 0;
        }

        int i = PreviousGrapheme(text, offset);
        while (i > 0 && Classify(text, i) == CharClass.Space)
        {
            i = PreviousGrapheme(text, i);
        }

        CharClass current = Classify(text, i);
        while (i > 0)
        {
            int prev = PreviousGrapheme(text, i);
            if (Classify(text, prev) != current)
            {
                break;
            }

            i = prev;
        }

        return i;
    }

    /// <summary>The word (or class run) containing <paramref name="offset"/>; used by
    /// double-click selection.</summary>
    public static (int Start, int End) WordAt(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return (0, 0);
        }

        offset = Math.Clamp(offset, 0, text.Length);
        int probe = offset >= text.Length ? PreviousGrapheme(text, offset) : offset;
        CharClass current = Classify(text, probe);
        int start = probe;
        while (start > 0)
        {
            int prev = PreviousGrapheme(text, start);
            if (Classify(text, prev) != current)
            {
                break;
            }

            start = prev;
        }

        int end = probe;
        while (end < text.Length && Classify(text, end) == current)
        {
            end = NextGrapheme(text, end);
        }

        return (start, end);
    }

    /// <summary>Selection text extraction; '\n' between paragraphs. Used by copy/cut.</summary>
    public static string GetRangeText(Story story, TextRange range)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (range.IsEmpty)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (int p = range.Start.ParagraphIndex; p <= range.End.ParagraphIndex; p++)
        {
            string text = GetParagraphText(story.Paragraphs[p]);
            int from = p == range.Start.ParagraphIndex ? range.Start.Offset : 0;
            int to = p == range.End.ParagraphIndex ? range.End.Offset : text.Length;
            if (p > range.Start.ParagraphIndex)
            {
                sb.Append('\n');
            }

            sb.Append(text, from, to - from);
        }

        return sb.ToString();
    }

    private static CharClass Classify(string text, int offset)
    {
        char c = text[offset];
        if (char.IsWhiteSpace(c))
        {
            return CharClass.Space;
        }

        if (char.IsLetterOrDigit(c)
            || char.GetUnicodeCategory(c) is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark)
        {
            return CharClass.Word;
        }

        // Apostrophes inside a word ("brother's") stay part of the word.
        if (c is '\'' or '’'
            && offset > 0 && offset < text.Length - 1
            && char.IsLetter(text[offset - 1]) && char.IsLetter(text[offset + 1]))
        {
            return CharClass.Word;
        }

        return CharClass.Punct;
    }
}
