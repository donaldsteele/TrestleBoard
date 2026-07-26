using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

public sealed class DocumentMetadata
{
    public string LodgeName { get; set; } = "";

    public int IssueMonth { get; set; } = 1;

    public int IssueYear { get; set; } = 2000;

    public string Title { get; set; } = "";

    /// <summary>Recurrence rule for the stated communication, e.g. "1st Tuesday" (drives M9 date bumping).</summary>
    public string MeetingRule { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }
}

/// <summary>
/// Root of the document tree (PLAN.md §2). Plain CLR, UI-agnostic; mutated ONLY through
/// <c>IDocumentCommand</c> executed by <c>DocumentSession</c> (PLAN.md §4).
/// </summary>
public sealed class Document
{
    public DocumentMetadata Metadata { get; set; } = new();

    public Theme Theme { get; set; } = new();

    public StyleSheet StyleSheet { get; set; } = new();

    public List<PageMaster> PageMasters { get; set; } = [];

    public List<Page> Pages { get; set; } = [];

    public List<Story> Stories { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>Unknown top-level properties of styles.json, preserved for round-trip (never serialized here;
    /// the container splits the document across document.json/styles.json).</summary>
    [JsonIgnore]
    public Dictionary<string, JsonElement>? StylesFileExtraProperties { get; set; }

    public Page GetPage(string pageId) =>
        Pages.Find(p => p.Id == pageId)
            ?? throw new KeyNotFoundException($"Page not found: {pageId}");

    public Story GetStory(string storyId) =>
        Stories.Find(s => s.Id == storyId)
            ?? throw new KeyNotFoundException($"Story not found: {storyId}");

    public PageMaster GetMaster(string masterId) =>
        PageMasters.Find(m => m.Id == masterId)
            ?? throw new KeyNotFoundException($"Page master not found: {masterId}");

    /// <summary>
    /// Non-throwing lookup, for callers that must survive a reference to a block that is no longer
    /// there — a linkNext left dangling by a deleted page (docs/M8-spec.md §3.1).
    /// </summary>
    public bool TryFindBlock(
        string blockId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Page? page,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Block? block)
    {
        foreach (Page candidate in Pages)
        {
            Block? found = candidate.Blocks.Find(b => b.Id == blockId);
            if (found is not null)
            {
                page = candidate;
                block = found;
                return true;
            }
        }

        page = null;
        block = null;
        return false;
    }

    /// <summary>Finds a block anywhere in the document; throws if absent.</summary>
    public (Page Page, Block Block) FindBlock(string blockId)
    {
        foreach (Page page in Pages)
        {
            Block? block = page.Blocks.Find(b => b.Id == blockId);
            if (block is not null)
            {
                return (page, block);
            }
        }

        throw new KeyNotFoundException($"Block not found in document: {blockId}");
    }
}
