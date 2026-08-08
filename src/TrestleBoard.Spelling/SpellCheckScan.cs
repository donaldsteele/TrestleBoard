using System;
using System.Collections.Generic;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Text;

namespace TrestleBoard.Spelling;

/// <summary>
/// One word the checker does not know, and enough around it for a person to judge it
/// (PLAN.md §11 M52).
/// </summary>
/// <param name="StoryId">Which piece of writing it is in.</param>
/// <param name="ParagraphIndex">Which paragraph of that story.</param>
/// <param name="Offset">Where the word starts, in characters from the start of the paragraph.</param>
/// <param name="Word">The word itself.</param>
/// <param name="Sentence">
/// The sentence around it. The wizard shows this, because "chruch" means nothing on its own and
/// everything in "the chruch on Main Street".
/// </param>
public sealed record Misspelling(string StoryId, int ParagraphIndex, int Offset, string Word, string Sentence)
{
    public int Length => Word.Length;
}

/// <summary>
/// The newsletter walked word by word (PLAN.md §11 M52).
///
/// <para>Pure, like <c>ReviewChecklist</c> and for the same reason: everything the spell check can
/// ever say about a document can then be asserted without a window, and nothing in the walk is able
/// to change the thing it is reading.</para>
///
/// <para>Widget payloads are deliberately not walked, matching <c>StoryFinder</c>'s decision. The
/// officers table and the birthday list are generated from the address book; asking the user
/// whether a brother's surname is spelled right, in a list the app filled in itself, would be
/// asking them to proofread the computer.</para>
/// </summary>
public static class SpellCheckScan
{
    /// <summary>
    /// Every word in the document the checker does not know, in the order the reader meets them.
    /// </summary>
    public static IReadOnlyList<Misspelling> Run(Document document, SpellChecker checker)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(checker);

        var found = new List<Misspelling>();
        foreach (string storyId in StoryFinder.StoryOrder(document))
        {
            if (!document.TryGetStory(storyId, out Story? story))
            {
                continue;
            }

            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                string text = StoryNavigator.GetParagraphText(story.Paragraphs[p]);
                foreach ((int start, string word) in WordsIn(text))
                {
                    if (!checker.IsSpelledRight(word))
                    {
                        found.Add(new Misspelling(storyId, p, start, word, SentenceAround(text, start)));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Words and where they start. Uses <see cref="StoryNavigator"/>'s classifier, so "brother's"
    /// is one word rather than two and a half — the same rule the caret already moves by.
    /// </summary>
    public static IEnumerable<(int Start, string Word)> WordsIn(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int at = 0;
        while (at < text.Length)
        {
            if (!char.IsLetter(text[at]) && text[at] != '\'' && text[at] != '’')
            {
                at++;
                continue;
            }

            (int start, int end) = StoryNavigator.WordAt(text, at);
            if (end <= start)
            {
                at++;
                continue;
            }

            yield return (start, text[start..end]);
            at = end;
        }
    }

    /// <summary>
    /// The sentence the word sits in. Deliberately simple — a full stop, question mark or
    /// exclamation followed by a space ends a sentence, and that is that.
    ///
    /// <para>It will occasionally cut after "Bro." or "St." and show half a sentence. That is a
    /// cosmetic wrong answer in a box whose whole job is to give the reader their bearings, and it
    /// is a far better trade than the abbreviation table it would take to fix, which would be wrong
    /// in ways nobody could predict. The word being asked about is always inside what is shown.</para>
    /// </summary>
    public static string SentenceAround(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return "";
        }

        offset = Math.Clamp(offset, 0, text.Length - 1);

        int start = 0;
        for (int i = offset - 1; i > 0; i--)
        {
            if (IsEnd(text[i - 1]) && char.IsWhiteSpace(text[i]))
            {
                start = i + 1;
                break;
            }
        }

        int end = text.Length;
        for (int i = offset; i < text.Length; i++)
        {
            if (IsEnd(text[i]) && (i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1])))
            {
                end = i + 1;
                break;
            }
        }

        return text[start..end].Trim();
    }

    private static bool IsEnd(char c) => c is '.' or '?' or '!';
}
