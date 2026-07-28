using TrestleBoard.Core.Model;

namespace TrestleBoard.Core.Text;

/// <summary>One occurrence of the searched-for words, in story coordinates.</summary>
/// <param name="StoryId">The story the words are in.</param>
/// <param name="ParagraphIndex">Which paragraph of that story.</param>
/// <param name="Offset">Paragraph-relative character offset of the first character.</param>
/// <param name="Length">How many characters matched.</param>
public readonly record struct FindHit(string StoryId, int ParagraphIndex, int Offset, int Length);

/// <summary>
/// Searching the newsletter's writing (PLAN.md §11 M21).
///
/// <para>Pure and BCL-only, like everything else in Core: it walks the document's STORIES, which is
/// what makes "find reaches the second frame of a linked chain" true by construction rather than by
/// special-casing. A linked chain is one story flowing through several frames, so a hit in the
/// second frame is simply a hit further down the same paragraph list — the frame it lands in is a
/// question for the layout, asked afterwards.</para>
///
/// <para><b>Widget payloads are deliberately not searched</b> (M21 pass one). Their text lives in a
/// JSON payload rather than a story, and half-searching them — matching the officers table but not
/// being able to select or replace inside it — would be worse than not searching them at all. The
/// caller says so out loud when a search finds nothing; it never silently misses.</para>
/// </summary>
public static class StoryFinder
{
    /// <summary>
    /// The stories of the newsletter in the order a reader meets them: page by page, and within a
    /// page in the order the blocks are stored. A story flowing through several frames appears once,
    /// at its first frame.
    /// </summary>
    public static IReadOnlyList<string> StoryOrder(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Page page in document.Pages)
        {
            foreach (Block block in page.Blocks)
            {
                if (block is TextBlock text && seen.Add(text.StoryRef))
                {
                    order.Add(text.StoryRef);
                }
            }
        }

        // A story with no frame at all cannot be shown, so it is not searched: selecting a hit in it
        // would scroll to nowhere.
        return order;
    }

    /// <summary>
    /// The next occurrence after <paramref name="after"/>, wrapping round to the beginning when it
    /// runs out. Null when the words are nowhere in the newsletter's writing.
    /// </summary>
    /// <param name="document">The newsletter.</param>
    /// <param name="needle">What to look for; an empty string finds nothing.</param>
    /// <param name="after">The hit to continue from, or null to start at the beginning.</param>
    /// <param name="matchCase">Whether upper and lower case have to agree.</param>
    /// <param name="wrapped">True when the answer came from wrapping round the end.</param>
    public static FindHit? Next(
        Document document, string needle, FindHit? after, bool matchCase, out bool wrapped)
    {
        ArgumentNullException.ThrowIfNull(document);

        wrapped = false;
        if (string.IsNullOrEmpty(needle))
        {
            return null;
        }

        IReadOnlyList<FindHit> all = All(document, needle, matchCase);
        if (all.Count == 0)
        {
            return null;
        }

        if (after is not { } previous)
        {
            return all[0];
        }

        IReadOnlyList<string> order = StoryOrder(document);
        foreach (FindHit hit in all)
        {
            if (Compare(order, hit, previous) > 0)
            {
                return hit;
            }
        }

        wrapped = true;
        return all[0];
    }

    /// <summary>
    /// Every occurrence, in reading order. Used by "replace all", and by the message that says how
    /// many there are.
    /// </summary>
    public static IReadOnlyList<FindHit> All(Document document, string needle, bool matchCase)
    {
        ArgumentNullException.ThrowIfNull(document);

        var hits = new List<FindHit>();
        if (string.IsNullOrEmpty(needle))
        {
            return hits;
        }

        StringComparison comparison = matchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        foreach (string storyId in StoryOrder(document))
        {
            if (document.Stories.Find(s => s.Id == storyId) is not { } story)
            {
                continue;
            }

            for (int p = 0; p < story.Paragraphs.Count; p++)
            {
                string text = StoryNavigator.GetParagraphText(story.Paragraphs[p]);
                for (int at = 0; at <= text.Length - needle.Length;)
                {
                    int found = text.IndexOf(needle, at, comparison);
                    if (found < 0)
                    {
                        break;
                    }

                    hits.Add(new FindHit(storyId, p, found, needle.Length));

                    // Continue past this match rather than one character on, so "aa" in "aaaa"
                    // finds two occurrences and not three.
                    at = found + Math.Max(1, needle.Length);
                }
            }
        }

        return hits;
    }

    /// <summary>Reading-order comparison of two hits: story first, then paragraph, then offset.</summary>
    private static int Compare(IReadOnlyList<string> storyOrder, FindHit a, FindHit b)
    {
        int storyA = IndexOfStory(storyOrder, a.StoryId);
        int storyB = IndexOfStory(storyOrder, b.StoryId);
        if (storyA != storyB)
        {
            return storyA.CompareTo(storyB);
        }

        return a.ParagraphIndex != b.ParagraphIndex
            ? a.ParagraphIndex.CompareTo(b.ParagraphIndex)
            : a.Offset.CompareTo(b.Offset);
    }

    private static int IndexOfStory(IReadOnlyList<string> order, string storyId)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], storyId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
