using System.Globalization;
using System.Text.Json;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;

namespace TrestleBoard.App.Integration;

/// <summary>
/// One place the words were found in an old newsletter (PLAN.md §11 M85).
/// </summary>
/// <param name="Path">The file it is in, so it can be opened.</param>
/// <param name="Issue">Which issue, as a person names it: "September 2025".</param>
/// <param name="PageNumber">One-based, so the answer is "page 3" rather than "block 7".</param>
/// <param name="Sentence">The sentence the words are in, which is what makes the hit recognisable.</param>
internal sealed record ArchiveHit(string Path, string Issue, int PageNumber, string Sentence)
{
    /// <summary>How a hit reads in the results list.</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"{Issue}, page {PageNumber}: {Sentence}");
}

/// <summary>What came back, and what to say if it was nothing.</summary>
/// <param name="Hits">Newest issue first.</param>
/// <param name="IssuesRead">How many newsletters were opened, for the sentence afterwards.</param>
/// <param name="Problem">A sentence when the search could not be done at all, else null.</param>
internal sealed record ArchiveSearchResult(
    IReadOnlyList<ArchiveHit> Hits,
    int IssuesRead,
    string? Problem);

/// <summary>
/// "When did we last mention the fish fry?" (PLAN.md §11 M85).
///
/// <para>M59 put last year's same-month issue beside this one, which answers the annual question —
/// the picnic, the awards night, the installation notice. It does not answer the other one, which
/// is the committee wondering whether a thing has been written about at all, and when. That is a
/// quarterly question rather than a monthly one, and until now the only way to answer it was to
/// open eleven files one at a time.</para>
///
/// <para><b>Read-only, structurally.</b> Every newsletter this touches is loaded, searched and
/// dropped. Nothing is written, nothing is kept open, and the open newsletter is not involved at
/// all — which is the same guarantee M51's checklist makes and for the same reason.</para>
///
/// <para><b>Every failure is a sentence.</b> A folder that has gone, a file a newer TrestleBoard
/// wrote, a file another program is holding: none of them stops the search, and none of them throws.
/// The ones that could not be read are counted and the rest are searched, because a committee
/// looking for the fish fry is better served by nine issues than by an error.</para>
/// </summary>
internal static class ArchiveSearch
{
    /// <summary>How many hits are worth showing. Beyond this the answer is "it is everywhere".</summary>
    internal const int MostHits = 50;

    /// <summary>
    /// Looks through every newsletter in <paramref name="folder"/> for <paramref name="words"/>.
    /// </summary>
    /// <param name="exceptPath">
    /// The newsletter already open, left out of the results. Finding the words in the issue on
    /// screen is what the ordinary Find is for, and listing it here would bury the answer.
    /// </param>
    internal static ArchiveSearchResult Search(
        string? folder, string words, bool matchCase, string? exceptPath = null)
    {
        if (string.IsNullOrWhiteSpace(words))
        {
            return new ArchiveSearchResult([], 0, "Type the words to look for first.");
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return new ArchiveSearchResult(
                [],
                0,
                "TrestleBoard does not know where you keep your old newsletters yet. Show it the "
                + "folder once and it will remember.");
        }

        if (!Directory.Exists(folder))
        {
            return new ArchiveSearchResult(
                [],
                0,
                "The folder TrestleBoard was told to look in is not there any more. "
                + "Show it the folder again.");
        }

        string[] candidates;
        try
        {
            candidates = Directory.GetFiles(folder, "*.tboard", SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ArchiveSearchResult([], 0, $"TrestleBoard could not look inside that folder. ({e.Message})");
        }

        StringComparison how = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var hits = new List<(DateOnly When, ArchiveHit Hit)>();
        int read = 0;

        foreach (string path in candidates.OrderBy(p => p, StringComparer.Ordinal))
        {
            if (exceptPath is not null
                && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(exceptPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TboardPackage package;
            try
            {
                package = TboardContainer.LoadFromFile(path);
            }
            catch (Exception e) when (e is Core.Migrations.UnsupportedFormatException
                or InvalidDataException
                or NotSupportedException
                or IOException
                or JsonException
                or UnauthorizedAccessException)
            {
                // Counted by its absence and otherwise ignored. A committee looking for the fish
                // fry is better served by nine issues than by an error about the tenth.
                _ = e;
                continue;
            }

            read++;
            Document document = package.Document;
            DateOnly when = IssueDateOf(document);

            foreach (ArchiveHit hit in HitsIn(document, path, words, how))
            {
                hits.Add((when, hit));
                if (hits.Count >= MostHits)
                {
                    break;
                }
            }

            if (hits.Count >= MostHits)
            {
                break;
            }
        }

        // Newest first: "when did we last mention it" is a question about the most recent time.
        return new ArchiveSearchResult(
            [.. hits.OrderByDescending(h => h.When).Select(h => h.Hit)],
            read,
            null);
    }

    /// <summary>
    /// Every place the words appear in one newsletter, with the page each is on.
    /// </summary>
    private static IEnumerable<ArchiveHit> HitsIn(
        Document document, string path, string words, StringComparison how)
    {
        string issue = IssueName(document);

        // A story reported ONCE, however many frames its chain runs through, and named by the page
        // it STARTS on — which is where the reader begins it. This is M51's rule, and it is not
        // tidiness: the sample newsletter's body story runs through a frame on page one and another
        // on page two, so without this every article in a two-page chain came back twice. The first
        // version of this search did exactly that, and the tests caught it.
        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < document.Pages.Count; i++)
        {
            foreach (Block block in document.Pages[i].Blocks)
            {
                if (block is not Core.Model.TextBlock frame
                    || !reported.Add(frame.StoryRef)
                    || !document.TryGetStory(frame.StoryRef, out Story? story))
                {
                    continue;
                }

                foreach (StoryParagraph paragraph in story.Paragraphs)
                {
                    string text = string.Concat(paragraph.Runs.Select(r => r.Text));
                    if (text.Contains(words, how))
                    {
                        yield return new ArchiveHit(path, issue, i + 1, Shorten(text, words, how));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The words in their sentence, trimmed to something that fits a list row.
    ///
    /// <para>What makes a hit recognisable is the words around it, not the words themselves — the
    /// committee already knows what they typed. So the match is kept in view with room either side
    /// rather than the paragraph being truncated from its start.</para>
    /// </summary>
    private static string Shorten(string text, string words, StringComparison how)
    {
        const int Room = 40;
        string collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        int at = collapsed.IndexOf(words, how);
        if (at < 0)
        {
            return collapsed.Length <= Room * 2 ? collapsed : collapsed[..(Room * 2)] + "…";
        }

        int start = Math.Max(0, at - Room);
        int end = Math.Min(collapsed.Length, at + words.Length + Room);
        return (start > 0 ? "…" : string.Empty)
            + collapsed[start..end]
            + (end < collapsed.Length ? "…" : string.Empty);
    }

    /// <summary>"September 2025", or the file's own name when the issue never said.</summary>
    private static string IssueName(Document document)
    {
        DocumentMetadata meta = document.Metadata;
        return meta.HasIssueDate
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(meta.IssueMonth)} {meta.IssueYear}")
            : "A newsletter with no month on it";
    }

    /// <summary>For sorting. A newsletter that never said which issue it is sorts oldest.</summary>
    private static DateOnly IssueDateOf(Document document)
    {
        DocumentMetadata meta = document.Metadata;
        return meta.HasIssueDate && meta.IssueMonth is >= 1 and <= 12
            ? new DateOnly(meta.IssueYear, meta.IssueMonth, 1)
            : DateOnly.MinValue;
    }
}
