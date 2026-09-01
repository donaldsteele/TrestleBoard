using TrestleBoard.App.Integration;
using TrestleBoard.Core.Container;
using TrestleBoard.Core.Model;
using TrestleBoard.Core.Samples;
using Xunit;

namespace TrestleBoard.App.HeadlessTests;

/// <summary>
/// M85: when did we last mention the fish fry?
///
/// <para>M59 puts last year's same-month issue beside this one, which answers the annual question.
/// It does not answer the other one — whether a thing has been written about at all, and when — and
/// until now the only way to answer that was to open eleven files one at a time.</para>
/// </summary>
public sealed class ArchiveSearchTests : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "trestleboard-archive-" + Guid.NewGuid().ToString("N"));

    public ArchiveSearchTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _ = e;
        }
    }

    /// <summary>
    /// <b>Newest first.</b> "When did we last mention it" is a question about the most recent time,
    /// so the answer has to lead with it rather than with the oldest issue in the folder.
    /// </summary>
    [Fact]
    public void HitsComeBackNewestFirstWithTheIssueAndThePage()
    {
        Write("2024-09.tboard", 9, 2024, "The fish fry raised two hundred dollars.");
        Write("2026-03.tboard", 3, 2026, "Tickets for the fish fry are on sale.");
        Write("2025-06.tboard", 6, 2025, "Nothing about supper at all.");

        ArchiveSearchResult result = ArchiveSearch.Search(_folder, "fish fry", matchCase: false);

        Assert.Null(result.Problem);
        Assert.Equal(3, result.IssuesRead);
        Assert.Equal(2, result.Hits.Count);
        Assert.Equal("March 2026", result.Hits[0].Issue);
        Assert.Equal("September 2024", result.Hits[1].Issue);
        Assert.All(result.Hits, h => Assert.True(h.PageNumber >= 1));
    }

    /// <summary>
    /// The words come back in their sentence, because what makes a hit recognisable is the words
    /// around it — the committee already knows what they typed.
    /// </summary>
    [Fact]
    public void AHitCarriesTheSentenceTheWordsAreIn()
    {
        Write("2026-03.tboard", 3, 2026, "Tickets for the fish fry are on sale at the door.");

        ArchiveHit hit = Assert.Single(ArchiveSearch.Search(_folder, "fish fry", false).Hits);

        Assert.Contains("Tickets", hit.Sentence, StringComparison.Ordinal);
        Assert.Contains("fish fry", hit.Sentence, StringComparison.Ordinal);
        Assert.Contains("page 1", hit.Describe(), StringComparison.Ordinal);
        Assert.StartsWith("March 2026", hit.Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Finding the words in the newsletter already on screen is what the ordinary Find is for, and
    /// listing it here would bury the answer.
    /// </summary>
    [Fact]
    public void TheNewsletterAlreadyOpenIsLeftOut()
    {
        string open = Write("2026-03.tboard", 3, 2026, "Tickets for the fish fry are on sale.");
        Write("2024-09.tboard", 9, 2024, "The fish fry raised two hundred dollars.");

        ArchiveSearchResult result = ArchiveSearch.Search(_folder, "fish fry", false, exceptPath: open);

        ArchiveHit only = Assert.Single(result.Hits);
        Assert.Equal("September 2024", only.Issue);
    }

    /// <summary>
    /// <b>A file that cannot be read does not stop the search.</b> A committee looking for the fish
    /// fry is better served by the issues that opened than by an error about the one that did not.
    /// </summary>
    [Fact]
    public void ADamagedFileIsSteppedOverRatherThanThrown()
    {
        Write("2026-03.tboard", 3, 2026, "Tickets for the fish fry are on sale.");
        File.WriteAllText(Path.Combine(_folder, "broken.tboard"), "this is not a newsletter at all");

        ArchiveSearchResult result = ArchiveSearch.Search(_folder, "fish fry", false);

        Assert.Null(result.Problem);
        Assert.Single(result.Hits);
        Assert.Equal(1, result.IssuesRead);
    }

    /// <summary>Every refusal is a sentence, and each names what the user can do about it.</summary>
    [Fact]
    public void EveryRefusalIsASentenceThatSaysWhatToDo()
    {
        Assert.Contains(
            "where you keep your old newsletters",
            ArchiveSearch.Search(null, "fish fry", false).Problem,
            StringComparison.Ordinal);

        Assert.Contains(
            "not there any more",
            ArchiveSearch.Search(Path.Combine(_folder, "gone"), "fish fry", false).Problem,
            StringComparison.Ordinal);

        Assert.Contains(
            "Type the words",
            ArchiveSearch.Search(_folder, "  ", false).Problem,
            StringComparison.Ordinal);
    }

    /// <summary>Exact capitals when asked for, and not otherwise.</summary>
    [Fact]
    public void MatchingCapitalsIsHonoured()
    {
        Write("2026-03.tboard", 3, 2026, "Tickets for the Fish Fry are on sale.");

        Assert.Single(ArchiveSearch.Search(_folder, "fish fry", matchCase: false).Hits);
        Assert.Empty(ArchiveSearch.Search(_folder, "fish fry", matchCase: true).Hits);
        Assert.Single(ArchiveSearch.Search(_folder, "Fish Fry", matchCase: true).Hits);
    }

    /// <summary>
    /// Nothing found says how many were looked through, so the user knows the search happened. An
    /// empty folder and a folder of ten issues with no match are different answers.
    /// </summary>
    [Fact]
    public void TheSentenceAfterwardsSaysWhatActuallyHappened()
    {
        Assert.Contains(
            "no earlier newsletters",
            Dialogs.FindWindow.DescribeArchive(new ArchiveSearchResult([], 0, null)),
            StringComparison.Ordinal);

        Assert.Contains(
            "not in any of the 9",
            Dialogs.FindWindow.DescribeArchive(new ArchiveSearchResult([], 9, null)),
            StringComparison.Ordinal);

        Assert.Contains(
            "Found it once",
            Dialogs.FindWindow.DescribeArchive(new ArchiveSearchResult(
                [new ArchiveHit("p", "March 2026", 1, "…")], 9, null)),
            StringComparison.Ordinal);

        // A problem is said instead of a count, never as well: two sentences about one search is
        // one more than anybody reads.
        Assert.Equal(
            "The folder has gone.",
            Dialogs.FindWindow.DescribeArchive(new ArchiveSearchResult([], 0, "The folder has gone.")));
    }

    /// <summary>
    /// <b>Read-only, structurally.</b> Every newsletter the search touches is loaded, searched and
    /// dropped — and this proves it by comparing the files afterwards, byte for byte.
    /// </summary>
    [Fact]
    public void NothingInTheArchiveIsChanged()
    {
        string path = Write("2026-03.tboard", 3, 2026, "Tickets for the fish fry are on sale.");
        byte[] before = File.ReadAllBytes(path);

        ArchiveSearch.Search(_folder, "fish fry", false);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>Writes a one-page newsletter carrying the given sentence. Fictional throughout (§0).</summary>
    private string Write(string name, int month, int year, string sentence)
    {
        TboardPackage package = SampleDocument.CreatePackage([]);
        Document document = package.Document;
        document.Metadata.IssueMonth = month;
        document.Metadata.IssueYear = year;
        document.Metadata.IssueDateChosen = true;
        document.Stories.Single(s => s.Id == "story-body").Paragraphs[0].Runs =
            [new StoryRun { Text = sentence }];

        string path = Path.Combine(_folder, name);
        TboardContainer.SaveToFile(package, path);
        return path;
    }
}
