using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrestleBoard.Core.Model;

public sealed class DocumentMetadata
{
    public string LodgeName { get; set; } = "";

    public int IssueMonth { get; set; } = 1;

    public int IssueYear { get; set; } = 2000;

    /// <summary>
    /// M75: has a person actually said which issue this is?
    ///
    /// <para><b>Why a nullable bool and not a nullable month.</b> The obvious fix for "January 2000
    /// means nobody has said" is to make <see cref="IssueMonth"/> an <c>int?</c>. That was rejected
    /// at <c>NewsletterTemplate.cs</c>: it is a format change reaching every widget seed, every
    /// projection and every snapshot, for the sake of a state only a template is ever in. This flag
    /// is additive and nothing but the catalog, the start paths and the cover wizard ever reads
    /// it.</para>
    ///
    /// <para><b>Three states, on purpose.</b> <c>true</c> and <c>false</c> are answers this build
    /// wrote down. <c>null</c> is a file written before M75, which has no flag in it at all — and
    /// those are read through <see cref="HasIssueDate"/>'s fallback rather than being assumed
    /// unanswered, because a real July 2026 issue saved in July 2026 has a perfectly good issue date
    /// and must not be interrogated about it on open.</para>
    /// </summary>
    public bool? IssueDateChosen { get; set; }

    /// <summary>
    /// M75: whether this newsletter knows which issue it is.
    ///
    /// <para>For a file with no flag the answer falls back to the model's own defaults: month 1 of
    /// year 2000 is what <c>new DocumentMetadata()</c> produces and what
    /// <c>NewsletterTemplate.ClearIssueDate</c> writes, and the first release of TrestleBoard was
    /// 2026 — so no real newsletter is a genuine January 2000 issue.</para>
    /// </summary>
    [JsonIgnore]
    public bool HasIssueDate => IssueDateChosen ?? !(IssueMonth == 1 && IssueYear == 2000);

    public string Title { get; set; } = "";

    /// <summary>Recurrence rule for the stated communication, e.g. "1st Tuesday" (drives M9 date bumping).</summary>
    public string MeetingRule { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraProperties { get; set; }

    /// <summary>
    /// A field-for-field copy (M75).
    ///
    /// <para><c>SetMetadataCommand</c> replaces the whole object, so a caller changing one thing has
    /// to carry the rest across — and a hand-written copy at the call site is a property waiting to
    /// be dropped the next time one is added. It lives here, beside the properties, so it fails to
    /// compile rather than failing quietly.</para>
    /// </summary>
    public DocumentMetadata Clone() => new()
    {
        LodgeName = LodgeName,
        IssueMonth = IssueMonth,
        IssueYear = IssueYear,
        IssueDateChosen = IssueDateChosen,
        Title = Title,
        MeetingRule = MeetingRule,
        ExtraProperties = ExtraProperties is null ? null : new Dictionary<string, JsonElement>(ExtraProperties),
    };
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

    /// <summary>
    /// Non-throwing story lookup, the counterpart of <see cref="TryFindBlock"/> and there for the
    /// same reason (M24 review §14.2): undo can remove a story out from under something still
    /// holding its id. Adding a text frame is one composite command over a story and a block, so
    /// its revert drops both — and the open text session finds out by asking.
    /// </summary>
    public bool TryGetStory(
        string storyId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Story? story)
    {
        story = Stories.Find(s => s.Id == storyId);
        return story is not null;
    }

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
