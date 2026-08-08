using System;
using System.Collections.Generic;
using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>
/// One sentence of the newsletter, and where it is (PLAN.md §11 M58).
/// </summary>
/// <param name="StoryId">The piece of writing it belongs to.</param>
/// <param name="ParagraphIndex">Which paragraph of that story.</param>
/// <param name="Offset">Where it starts, in characters from the start of the paragraph.</param>
/// <param name="Text">The sentence itself, trimmed.</param>
public sealed record Sentence(string StoryId, int ParagraphIndex, int Offset, string Text)
{
    public int Length => Text.Length;
}

/// <summary>
/// The newsletter cut into sentences, in the order a reader meets them (PLAN.md §11 M58).
///
/// <para>The committee's best proofreading tool for aging eyes is hearing the text — errors the eye
/// slides over, the ear catches. Whether a voice is available or not, the unit both the reading and
/// the silent walk-through move by is the sentence, so this is where both begin.</para>
///
/// <para><b>Deliberately simple, and the simplicity is the design.</b> A full-stop, question mark or
/// exclamation ends a sentence; a run of them counts once; and a few abbreviations that a lodge
/// newsletter uses constantly are excused, because "Bro. Smith gave the charge" read as two
/// sentences is a stumble in the middle of every issue. What is NOT attempted is general
/// abbreviation handling: it fails in ways nobody can predict, and the cost of a wrong split here is
/// a pause in the wrong place, not a wrong word.</para>
/// </summary>
public static class Sentences
{
    /// <summary>
    /// Abbreviations whose full stop does not end a sentence. Short, and every one of them earns
    /// its place in a trestle board — the alternative is a reading that stops dead after "Bro."
    /// </summary>
    private static readonly string[] NotAnEnding =
    [
        "bro", "br", "mr", "mrs", "ms", "dr", "st", "rev", "hon", "jr", "sr",
        "no", "vol", "pp", "approx", "etc", "e.g", "i.e", "w", "p",
    ];

    /// <summary>Every sentence in the newsletter, page by page, frame by frame.</summary>
    public static IReadOnlyList<Sentence> In(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var found = new List<Sentence>();
        foreach (string storyId in StoryFinder.StoryOrder(document))
        {
            if (!document.TryGetStory(storyId, out Story? story))
            {
                continue;
            }

            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                string text = StoryNavigator.GetParagraphText(story.Paragraphs[p]);
                foreach ((int offset, string sentence) in Split(text))
                {
                    found.Add(new Sentence(storyId, p, offset, sentence));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// One paragraph cut into sentences, each with where it starts. A paragraph with no ending
    /// punctuation at all is one sentence — a heading is still something to read out.
    /// </summary>
    public static IEnumerable<(int Offset, string Text)> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsEnding(text[i]) || EndsAnAbbreviation(text, i))
            {
                continue;
            }

            // Take the run: "Really?!" ends once, not twice.
            int end = i;
            while (end + 1 < text.Length && IsEnding(text[end + 1]))
            {
                end++;
            }

            // A closing quote or bracket belongs to the sentence it closes.
            while (end + 1 < text.Length && text[end + 1] is '"' or '\'' or '”' or '’' or ')' or ']')
            {
                end++;
            }

            if (Emit(text, start, end + 1) is { } sentence)
            {
                yield return sentence;
            }

            start = end + 1;
            i = end;
        }

        if (Emit(text, start, text.Length) is { } last)
        {
            yield return last;
        }
    }

    private static (int Offset, string Text)? Emit(string text, int from, int to)
    {
        if (to <= from)
        {
            return null;
        }

        string slice = text[from..to];
        int lead = 0;
        while (lead < slice.Length && char.IsWhiteSpace(slice[lead]))
        {
            lead++;
        }

        string trimmed = slice[lead..].TrimEnd();
        return trimmed.Length == 0 ? null : (from + lead, trimmed);
    }

    private static bool IsEnding(char c) => c is '.' or '?' or '!';

    /// <summary>
    /// True when the full stop at <paramref name="at"/> is part of "Bro." rather than the end of
    /// something. Only full stops can abbreviate; a question mark never does.
    /// </summary>
    private static bool EndsAnAbbreviation(string text, int at)
    {
        if (text[at] != '.')
        {
            return false;
        }

        int start = at;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        if (start == at)
        {
            return false;
        }

        string word = text[start..at].TrimEnd('.').ToLowerInvariant();
        foreach (string abbreviation in NotAnEnding)
        {
            if (string.Equals(word, abbreviation, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // A single capital letter followed by a stop is an initial — "A. Placeholder" is one name,
        // not the end of a sentence.
        return at - start == 1 && char.IsUpper(text[start]);
    }
}
