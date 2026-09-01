using System.Globalization;

namespace TrestleBoard.Core.Text;

/// <summary>
/// How much writing did not fit, counted in words (PLAN.md §11 M82).
///
/// <para><b>Words, and only ever words.</b> The overset marker has said since M43 that there is more
/// writing than fits; it has never said how much. The answer the committee wants is "about forty
/// words" — a quantity they can act on, because they know what forty words of their own article
/// looks like. Lines depend on the frame's width, and points are typography (M46's jargon rule): a
/// person who is told "12 points of overflow" has been told nothing they can use.</para>
///
/// <para><b>"About", never exactly.</b> The count is rounded, and the sentence says so, because the
/// exact figure changes with every character typed and a number that twitches while you read it
/// reads as the app being unsure. What the user needs is the size of the problem: a handful of
/// words, or half the article.</para>
/// </summary>
public static class OversetWords
{
    /// <summary>
    /// How many words lie after a position in a story.
    /// </summary>
    /// <param name="paragraphs">Each paragraph's whole text, in order.</param>
    /// <param name="fromParagraph">The paragraph the flow stopped in.</param>
    /// <param name="fromCharacter">Where in that paragraph it stopped.</param>
    public static int CountAfter(IReadOnlyList<string> paragraphs, int fromParagraph, int fromCharacter)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        int words = 0;
        for (int i = Math.Max(0, fromParagraph); i < paragraphs.Count; i++)
        {
            string text = paragraphs[i] ?? string.Empty;
            int start = i == fromParagraph ? Math.Clamp(fromCharacter, 0, text.Length) : 0;

            // The word the break lands INSIDE has been half shown, and counting it whole would
            // overstate by one on every frame in the newsletter. It counts as hidden only when the
            // break is at its very start.
            words += CountWords(text.AsSpan(start));
        }

        return words;
    }

    /// <summary>Words in a stretch of text — runs of non-space, which is what a person counts.</summary>
    public static int CountWords(ReadOnlySpan<char> text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }

        return words;
    }

    /// <summary>
    /// The sentence the panel shows. Rounded to something a person would say out loud.
    ///
    /// <para>Under ten it counts exactly, because "about ten words" when three are missing is
    /// alarming out of proportion; above that it rounds to the nearest ten, and above a hundred to
    /// the nearest fifty, because at that size what matters is the order of the problem.</para>
    /// </summary>
    public static string Describe(int words)
    {
        if (words <= 0)
        {
            return "All of the writing fits.";
        }

        if (words == 1)
        {
            return "About one word too long for this box.";
        }

        int rounded = words < 10 ? words
            : words < 100 ? (int)(Math.Round(words / 10.0) * 10)
            : (int)(Math.Round(words / 50.0) * 50);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"About {Math.Max(rounded, 2)} words too long for this box.");
    }
}
